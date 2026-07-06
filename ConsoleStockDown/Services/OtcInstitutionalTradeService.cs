using System.Globalization;
using System.Text.Json;
using ConsoleStockDown.Configuration;
using ConsoleStockDown.Models;
using ConsoleStockDown.Repository;
using Microsoft.Extensions.Logging;

namespace ConsoleStockDown.Services;

/// <summary>
/// 負責抓取 TPEX 三大法人資料、轉換欄位並寫入資料庫。
/// </summary>
public sealed class OtcInstitutionalTradeService : IOtcInstitutionalTradeService
{
    private const int MaxApiRetryCount = 3;
    private static readonly TimeSpan ApiRetryDelay = TimeSpan.FromSeconds(2);
    private delegate bool InstitutionalTradeRecordParser(
        IReadOnlyList<JsonElement> record,
        string tradeDate,
        string rawTradeDate,
        out InstitutionalTradeDaily institutionalTrade);
    private readonly IInstitutionalTradeRepository _repository;
    private readonly IStockRepository _stockRepository;
    private readonly LatestTradeDateContext _latestTradeDateContext;
    private readonly ILogger<OtcInstitutionalTradeService> _logger;
    private readonly string _apiUrlTemplate;
    private readonly string? _configuredTradeDate;

    /// <summary>
    /// 建立上櫃三大法人資料服務。
    /// </summary>
    public OtcInstitutionalTradeService(
        IInstitutionalTradeRepository repository,
        IStockRepository stockRepository,
        LatestTradeDateContext latestTradeDateContext,
        ILogger<OtcInstitutionalTradeService> logger,
        string apiUrlTemplate,
        string? configuredTradeDate)
    {
        _repository = repository;
        _stockRepository = stockRepository;
        _latestTradeDateContext = latestTradeDateContext;
        _logger = logger;
        _apiUrlTemplate = apiUrlTemplate;
        _configuredTradeDate = configuredTradeDate;
    }

    /// <summary>
    /// 依設定規則抓取最新或指定日期的上櫃三大法人資料。
    /// </summary>
    public async Task FetchAndStoreLatestAsync()
    {
        var targetTradeDate = await ResolveTargetTradeDateAsync();
        if (targetTradeDate is null)
        {
            return;
        }

        await FetchAndStoreAsync(targetTradeDate);
    }

    /// <summary>
    /// 抓取指定交易日期的上櫃三大法人資料，並保留同日已寫入的上市法人資料。
    /// </summary>
    public async Task FetchAndStoreAsync(string tradeDate)
    {
        _logger.LogInformation("Starting TPEX institutional trade sync for trade date {TradeDate}.", tradeDate);
        await _repository.InitializeDatabaseAsync();

        using var httpClient = new HttpClient();
        var payload = await FetchOtcInstitutionalTradePayloadAsync(httpClient, tradeDate);
        if (payload is null)
        {
            return;
        }

        var stocksByCode = await _stockRepository.GetStocksByTradeDateAsync(payload.TradeDate);
        if (stocksByCode.Count == 0)
        {
            _logger.LogWarning(
                "No StockDaily records found for trade date {TradeDate}. Skipping TPEX institutional trade sync.",
                payload.TradeDate);
            return;
        }

        var availableStockCodes = new HashSet<string>(stocksByCode.Keys, StringComparer.Ordinal);
        _logger.LogInformation(
            "Filtering TPEX institutional trade records by {Count} stock codes from StockDaily trade date {TradeDate}.",
            availableStockCodes.Count,
            payload.TradeDate);

        var otcItems = FilterAndParseInstitutionalTrades(
            payload.Records,
            payload.TradeDate,
            payload.RawTradeDate,
            availableStockCodes,
            "TPEX",
            TryParseOtcRecord);

        otcItems = await RetryFilterWithSupplementalStockCodesAsync(
            payload.TradeDate,
            payload,
            availableStockCodes,
            otcItems);

        if (otcItems.Count == 0)
        {
            _logger.LogWarning(
                "No TPEX institutional trade records remained after StockDaily filtering for trade date {TradeDate}.",
                payload.TradeDate);
            return;
        }

        var mergedItems = await MergeWithExistingTwseInstitutionalTradesAsync(
            payload.TradeDate,
            payload.RawTradeDate,
            otcItems);

        _logger.LogInformation(
            "Persisting {Count} institutional trade records after TPEX sync for trade date {TradeDate}.",
            mergedItems.Count,
            payload.TradeDate);

        await _repository.ReplaceByTradeDateAsync(payload.TradeDate, mergedItems);

        _logger.LogInformation(
            "Inserted {Count} institutional trade records after TPEX sync for trade date {TradeDate}.",
            mergedItems.Count,
            payload.TradeDate);
    }

    /// <summary>
    /// 決定本次抓取上櫃三大法人資料所使用的交易日期。
    /// </summary>
    private async Task<string?> ResolveTargetTradeDateAsync()
    {
        if (!string.IsNullOrWhiteSpace(_configuredTradeDate))
        {
            var configuredTradeDate = NormalizeTradeDate(_configuredTradeDate);
            if (configuredTradeDate is null)
            {
                throw new InvalidOperationException(
                    "InstitutionalTradeFetchDate must be in yyyy-MM-dd or yyyyMMdd format.");
            }

            _logger.LogInformation(
                "Using configured TPEX institutional trade date {TradeDate} from AppSettings.",
                configuredTradeDate);

            return configuredTradeDate;
        }

        if (!string.IsNullOrWhiteSpace(_latestTradeDateContext.LatestTwseTradeDate))
        {
            _logger.LogInformation(
                "No InstitutionalTradeFetchDate configured. Using TWSE stock API trade date {TradeDate} resolved from AppSettings.ApiUrl for TPEX institutional trade sync.",
                _latestTradeDateContext.LatestTwseTradeDate);

            return _latestTradeDateContext.LatestTwseTradeDate;
        }

        var latestTradeDate = await _stockRepository.GetLatestTradeDateAsync();
        if (latestTradeDate is null)
        {
            _logger.LogWarning("No stock trade date found. Skipping TPEX institutional trade sync.");
            return null;
        }

        _logger.LogWarning(
            "TWSE stock API trade date was unavailable in the current execution. Falling back to latest stock trade date {TradeDate} from database for TPEX institutional trade sync.",
            latestTradeDate);

        return latestTradeDate;
    }

    /// <summary>
    /// 抓取並整理上櫃三大法人 API 回應，保留後續過濾所需的原始資料列。
    /// </summary>
    private async Task<InstitutionalTradePayload?> FetchOtcInstitutionalTradePayloadAsync(
        HttpClient httpClient,
        string requestedTradeDate)
    {
        var apiDate = ConvertTradeDateToOtcApiDate(requestedTradeDate);
        if (apiDate is null)
        {
            _logger.LogWarning("Unable to convert trade date {TradeDate} to TPEX institutional trade API date.", requestedTradeDate);
            return null;
        }

        var apiUrl = BuildApiUrl(_apiUrlTemplate, apiDate, nameof(AppSettings.OtcInstitutionalTradeApiUrlTemplate));
        _logger.LogInformation("Calling TPEX institutional trade API: {ApiUrl}", apiUrl);

        var apiResponse = await GetApiResponseWithRetryAsync(
            httpClient,
            apiUrl,
            requestedTradeDate,
            "TPEX institutional trade",
            response => JsonSerializer.Deserialize<OtcInstitutionalTradeApiResponse>(response),
            response => string.Equals(response.Stat, "ok", StringComparison.OrdinalIgnoreCase),
            response => response.Stat);

        if (apiResponse is null)
        {
            return null;
        }

        var table = apiResponse.Tables.FirstOrDefault(item => item.Data.Count > 0);
        if (table is null)
        {
            _logger.LogWarning("No data returned from TPEX institutional trade API for requested trade date {TradeDate}.", requestedTradeDate);
            return new InstitutionalTradePayload(requestedTradeDate, string.Empty, []);
        }

        var rawTradeDate = table.Date.Trim();
        var tradeDate = apiResponse.Date.Length > 0
            ? ConvertDate(apiResponse.Date.Trim())
            : ConvertDate(rawTradeDate);
        if (!string.Equals(tradeDate, requestedTradeDate, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "TPEX institutional trade API returned trade date {ApiTradeDate}, which differs from requested trade date {RequestedTradeDate}.",
                tradeDate,
                requestedTradeDate);
        }

        return new InstitutionalTradePayload(tradeDate, rawTradeDate, table.Data);
    }

    /// <summary>
    /// 以重試機制呼叫法人 API，並在回應異常時記錄狀態與回應片段。
    /// </summary>
    private async Task<TResponse?> GetApiResponseWithRetryAsync<TResponse>(
        HttpClient httpClient,
        string apiUrl,
        string requestedTradeDate,
        string sourceName,
        Func<string, TResponse?> deserialize,
        Func<TResponse, bool> isSuccess,
        Func<TResponse, string> getStatus)
    {
        for (var attempt = 1; attempt <= MaxApiRetryCount; attempt++)
        {
            try
            {
                var response = await httpClient.GetStringAsync(apiUrl);
                try
                {
                    var apiResponse = deserialize(response);
                    if (apiResponse is not null && isSuccess(apiResponse))
                    {
                        if (attempt > 1)
                        {
                            _logger.LogInformation(
                                "{SourceName} API recovered on attempt {Attempt}/{MaxAttempts} for trade date {TradeDate}.",
                                sourceName,
                                attempt,
                                MaxApiRetryCount,
                                requestedTradeDate);
                        }

                        return apiResponse;
                    }

                    _logger.LogWarning(
                        "{SourceName} API returned an abnormal response on attempt {Attempt}/{MaxAttempts} for trade date {TradeDate}. Stat: {Stat}. ApiUrl: {ApiUrl}. ResponsePreview: {ResponsePreview}",
                        sourceName,
                        attempt,
                        MaxApiRetryCount,
                        requestedTradeDate,
                        apiResponse is null ? "(null)" : getStatus(apiResponse),
                        apiUrl,
                        CreateResponsePreview(response));
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "{SourceName} API returned invalid JSON on attempt {Attempt}/{MaxAttempts} for trade date {TradeDate}. ApiUrl: {ApiUrl}. ResponsePreview: {ResponsePreview}",
                        sourceName,
                        attempt,
                        MaxApiRetryCount,
                        requestedTradeDate,
                        apiUrl,
                        CreateResponsePreview(response));
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "{SourceName} API request failed on attempt {Attempt}/{MaxAttempts} for trade date {TradeDate}. ApiUrl: {ApiUrl}",
                    sourceName,
                    attempt,
                    MaxApiRetryCount,
                    requestedTradeDate,
                    apiUrl);
            }

            if (attempt < MaxApiRetryCount)
            {
                await Task.Delay(ApiRetryDelay);
            }
        }

        _logger.LogError(
            "{SourceName} API failed after {MaxAttempts} attempts for trade date {TradeDate}. ApiUrl: {ApiUrl}",
            sourceName,
            MaxApiRetryCount,
            requestedTradeDate,
            apiUrl);

        return default;
    }

    /// <summary>
    /// 解析並過濾上櫃法人資料列，只保留 StockDaily 已存在的股票代碼。
    /// </summary>
    private List<InstitutionalTradeDaily> FilterAndParseInstitutionalTrades(
        IReadOnlyList<List<JsonElement>> records,
        string tradeDate,
        string rawTradeDate,
        HashSet<string> availableStockCodes,
        string sourceName,
        InstitutionalTradeRecordParser parser)
    {
        var items = new List<InstitutionalTradeDaily>(records.Count);
        var failedParseCount = 0;
        var skippedCodeCount = 0;

        foreach (var record in records)
        {
            if (!parser(record, tradeDate, rawTradeDate, out var institutionalTrade))
            {
                failedParseCount++;
                continue;
            }

            if (!availableStockCodes.Contains(institutionalTrade.StockCode))
            {
                skippedCodeCount++;
                continue;
            }

            items.Add(institutionalTrade);
        }

        if (failedParseCount > 0)
        {
            _logger.LogWarning(
                "Failed to parse {Count} {SourceName} institutional trade records from API response for trade date {TradeDate}.",
                failedParseCount,
                sourceName,
                tradeDate);
        }

        if (skippedCodeCount > 0)
        {
            _logger.LogInformation(
                "Skipped {Count} {SourceName} institutional trade records because the stock code was not present in StockDaily for trade date {TradeDate}.",
                skippedCodeCount,
                sourceName,
                tradeDate);
        }

        _logger.LogInformation(
            "Prepared {Count} {SourceName} institutional trade records after StockDaily filtering for trade date {TradeDate}.",
            items.Count,
            sourceName,
            tradeDate);

        return items;
    }

    /// <summary>
    /// 當同日 <see cref="StockDaily"/> 缺少上櫃股票代碼時，補用最新交易日的股票清單重試一次上櫃法人過濾。
    /// </summary>
    private async Task<List<InstitutionalTradeDaily>> RetryFilterWithSupplementalStockCodesAsync(
        string tradeDate,
        InstitutionalTradePayload payload,
        HashSet<string> availableStockCodes,
        List<InstitutionalTradeDaily> items)
    {
        if (items.Count > 0 || payload.Records.Count == 0)
        {
            return items;
        }

        var latestStockTradeDate = await _stockRepository.GetLatestTradeDateAsync();
        if (string.IsNullOrWhiteSpace(latestStockTradeDate)
            || string.Equals(latestStockTradeDate, tradeDate, StringComparison.Ordinal))
        {
            return items;
        }

        var latestStocksByCode = await _stockRepository.GetStocksByTradeDateAsync(latestStockTradeDate);
        if (latestStocksByCode.Count == 0)
        {
            return items;
        }

        var supplementalStockCodes = new HashSet<string>(availableStockCodes, StringComparer.Ordinal);
        foreach (var stockCode in latestStocksByCode.Keys)
        {
            supplementalStockCodes.Add(stockCode);
        }

        _logger.LogWarning(
            "No TPEX institutional trade records remained after filtering by StockDaily trade date {TradeDate}. Retrying with supplemental stock codes from latest StockDaily trade date {LatestTradeDate}.",
            tradeDate,
            latestStockTradeDate);

        return FilterAndParseInstitutionalTrades(
            payload.Records,
            payload.TradeDate,
            payload.RawTradeDate,
            supplementalStockCodes,
            "TPEX fallback",
            TryParseOtcRecord);
    }

    /// <summary>
    /// 寫入上櫃法人資料前，保留同交易日已存在的上市法人資料。
    /// </summary>
    private async Task<List<InstitutionalTradeDaily>> MergeWithExistingTwseInstitutionalTradesAsync(
        string tradeDate,
        string otcRawTradeDate,
        List<InstitutionalTradeDaily> otcItems)
    {
        var existingItems = await _repository.GetByTradeDateAsync(tradeDate);
        if (existingItems.Count == 0)
        {
            return otcItems;
        }

        var preservedTwseItems = existingItems
            .Where(item => !string.Equals(item.RawDate, otcRawTradeDate, StringComparison.Ordinal))
            .ToList();

        if (preservedTwseItems.Count == 0)
        {
            return otcItems;
        }

        _logger.LogInformation(
            "Preserving {Count} institutional trade records already stored for trade date {TradeDate} while refreshing TPEX data.",
            preservedTwseItems.Count,
            tradeDate);

        return preservedTwseItems
            .Concat(otcItems)
            .ToList();
    }

    /// <summary>
    /// 將上櫃單筆法人資料列轉成 <see cref="InstitutionalTradeDaily"/> 模型。
    /// </summary>
    private static bool TryParseOtcRecord(
        IReadOnlyList<JsonElement> record,
        string tradeDate,
        string rawTradeDate,
        out InstitutionalTradeDaily institutionalTrade)
    {
        institutionalTrade = new InstitutionalTradeDaily();

        if (record.Count != 24)
        {
            return false;
        }

        var stockCode = GetElementText(record[0]).Trim();
        var stockName = GetElementText(record[1]).Trim();
        if (stockCode.Length == 0 || stockName.Length == 0)
        {
            return false;
        }

        institutionalTrade.RawDate = rawTradeDate;
        institutionalTrade.TradeDate = tradeDate;
        institutionalTrade.StockCode = stockCode;
        institutionalTrade.StockName = stockName;
        institutionalTrade.ForeignInvestorBuy = ParseLong(record[2]);
        institutionalTrade.ForeignInvestorSell = ParseLong(record[3]);
        institutionalTrade.ForeignInvestorNet = ParseLong(record[4]);
        institutionalTrade.ForeignDealerBuy = ParseLong(record[5]);
        institutionalTrade.ForeignDealerSell = ParseLong(record[6]);
        institutionalTrade.ForeignDealerNet = ParseLong(record[7]);
        institutionalTrade.InvestmentTrustBuy = ParseLong(record[11]);
        institutionalTrade.InvestmentTrustSell = ParseLong(record[12]);
        institutionalTrade.InvestmentTrustNet = ParseLong(record[13]);
        institutionalTrade.DealerNet = ParseLong(record[22]);
        institutionalTrade.DealerSelfBuy = ParseLong(record[14]);
        institutionalTrade.DealerSelfSell = ParseLong(record[15]);
        institutionalTrade.DealerSelfNet = ParseLong(record[16]);
        institutionalTrade.DealerHedgeBuy = ParseLong(record[17]);
        institutionalTrade.DealerHedgeSell = ParseLong(record[18]);
        institutionalTrade.DealerHedgeNet = ParseLong(record[19]);
        institutionalTrade.InstitutionalInvestorsNet = ParseLong(record[23]);

        return true;
    }

    /// <summary>
    /// 將 API 日期統一轉成 <c>yyyy-MM-dd</c> 格式。
    /// </summary>
    private static string ConvertDate(string rawDate)
    {
        if (DateTime.TryParseExact(rawDate, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (rawDate.Length == 9
            && int.TryParse(rawDate.Substring(0, 3), out var taiwanYearWithSlash)
            && int.TryParse(rawDate.Substring(4, 2), out var monthWithSlash)
            && int.TryParse(rawDate.Substring(7, 2), out var dayWithSlash))
        {
            var gregorianYear = taiwanYearWithSlash + 1911;
            return new DateTime(gregorianYear, monthWithSlash, dayWithSlash).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (rawDate.Length == 7
            && int.TryParse(rawDate.Substring(0, 3), out var taiwanYear)
            && int.TryParse(rawDate.Substring(3, 2), out var month)
            && int.TryParse(rawDate.Substring(5, 2), out var day))
        {
            var gregorianYear = taiwanYear + 1911;
            return new DateTime(gregorianYear, month, day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return rawDate;
    }

    /// <summary>
    /// 將設定檔輸入的日期字串正規化為 <c>yyyy-MM-dd</c> 格式。
    /// </summary>
    private static string? NormalizeTradeDate(string tradeDate)
    {
        var supportedFormats = new[] { "yyyy-MM-dd", "yyyyMMdd" };
        return DateTime.TryParseExact(
            tradeDate.Trim(),
            supportedFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>
    /// 將資料庫使用的交易日期轉為上櫃法人 API 查詢參數格式 <c>yyy/MM/dd</c>。
    /// </summary>
    private static string? ConvertTradeDateToOtcApiDate(string tradeDate)
    {
        if (!DateTime.TryParseExact(tradeDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return null;
        }

        var taiwanYear = parsed.Year - 1911;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{taiwanYear:000}/{parsed.Month:00}/{parsed.Day:00}");
    }

    /// <summary>
    /// 將交易日期代入 API URL 範本，組出實際請求位址。
    /// </summary>
    private static string BuildApiUrl(string apiUrlTemplate, string apiDate, string settingName)
    {
        if (!apiUrlTemplate.Contains("{date}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{settingName} must contain the {{date}} placeholder.");
        }

        return apiUrlTemplate.Replace("{date}", apiDate, StringComparison.Ordinal);
    }

    /// <summary>
    /// 將原始 API 回應壓縮成單行片段，方便寫入警告日誌排查問題。
    /// </summary>
    private static string CreateResponsePreview(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return "(empty)";
        }

        var preview = response
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal)
            .Trim();

        return preview.Length <= 240
            ? preview
            : $"{preview[..240]}...";
    }

    /// <summary>
    /// 解析欄位中的整數資料，並處理字串與數值混合格式。
    /// </summary>
    private static long ParseLong(JsonElement element)
    {
        var cleaned = GetElementText(element).Trim().Replace(",", string.Empty);
        return long.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0L;
    }

    /// <summary>
    /// 將 JSON 欄位統一轉成字串，方便後續解析。
    /// </summary>
    private static string GetElementText(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.Null => string.Empty,
            _ => element.GetRawText()
        };
    }
}
