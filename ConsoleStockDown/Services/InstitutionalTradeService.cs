using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using ConsoleStockDown.Configuration;
using ConsoleStockDown.Models;
using ConsoleStockDown.Repository;
using Microsoft.Extensions.Logging;

namespace ConsoleStockDown.Services;

/// <summary>
/// 負責抓取 TWSE 三大法人資料，並在完成後串接上櫃三大法人同步流程。
/// </summary>
public sealed class InstitutionalTradeService : IInstitutionalTradeService
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
    private readonly IOtcInstitutionalTradeService _otcInstitutionalTradeService;
    private readonly ILogger<InstitutionalTradeService> _logger;
    private readonly string _twseApiUrlTemplate;
    private readonly string? _configuredTradeDate;

    /// <summary>
    /// 建立三大法人資料服務。
    /// </summary>
    public InstitutionalTradeService(
        IInstitutionalTradeRepository repository,
        IStockRepository stockRepository,
        LatestTradeDateContext latestTradeDateContext,
        IOtcInstitutionalTradeService otcInstitutionalTradeService,
        ILogger<InstitutionalTradeService> logger,
        string twseApiUrlTemplate,
        string? configuredTradeDate)
    {
        _repository = repository;
        _stockRepository = stockRepository;
        _latestTradeDateContext = latestTradeDateContext;
        _otcInstitutionalTradeService = otcInstitutionalTradeService;
        _logger = logger;
        _twseApiUrlTemplate = twseApiUrlTemplate;
        _configuredTradeDate = configuredTradeDate;
    }

    /// <summary>
    /// 抓取最新或指定日期的三大法人資料，並寫入資料庫。
    /// </summary>
    public async Task FetchAndStoreLatestAsync()
    {
        _logger.LogInformation("Initializing institutional trade database.");
        await _repository.InitializeDatabaseAsync();

        var targetTradeDate = await ResolveTargetTradeDateAsync();
        if (targetTradeDate is null)
        {
            return;
        }

        using var httpClient = new HttpClient();
        var twsePayload = await FetchTwseInstitutionalTradePayloadAsync(httpClient, targetTradeDate);
        if (twsePayload is null)
        {
            return;
        }

        var tradeDate = twsePayload.TradeDate;
        var stocksByCode = await _stockRepository.GetStocksByTradeDateAsync(tradeDate);
        if (stocksByCode.Count == 0)
        {
            _logger.LogWarning(
                "No StockDaily records found for trade date {TradeDate}. Skipping institutional trade sync.",
                tradeDate);
            return;
        }

        var availableStockCodes = new HashSet<string>(stocksByCode.Keys, StringComparer.Ordinal);
        _logger.LogInformation(
            "Filtering institutional trade records by {Count} stock codes from StockDaily trade date {TradeDate}.",
            availableStockCodes.Count,
            tradeDate);

        var twseItems = FilterAndParseInstitutionalTrades(
            twsePayload.Records,
            twsePayload.TradeDate,
            twsePayload.RawTradeDate,
            availableStockCodes,
            "TWSE",
            TryParseTwseRecord);

        if (twseItems.Count == 0)
        {
            _logger.LogWarning(
                "No TWSE institutional trade records remained after StockDaily filtering for trade date {TradeDate}.",
                tradeDate);
            return;
        }

        var mergedItems = await MergeWithExistingOtcInstitutionalTradesAsync(
            tradeDate,
            twsePayload.RawTradeDate,
            twseItems);

        _logger.LogInformation(
            "Persisting {Count} institutional trade records after TWSE sync for trade date {TradeDate}.",
            mergedItems.Count,
            tradeDate);

        await _repository.ReplaceByTradeDateAsync(tradeDate, mergedItems);

        _logger.LogInformation(
            "Inserted {Count} institutional trade records after TWSE sync for trade date {TradeDate}.",
            mergedItems.Count,
            tradeDate);

        _logger.LogInformation("Starting TPEX institutional trade sync after TWSE sync for trade date {TradeDate}.", tradeDate);
        await _otcInstitutionalTradeService.FetchAndStoreAsync(tradeDate);
    }

    /// <summary>
    /// 抓取並整理上市三大法人 API 回應，保留後續過濾所需的原始資料列。
    /// </summary>
    private async Task<InstitutionalTradePayload?> FetchTwseInstitutionalTradePayloadAsync(
        HttpClient httpClient,
        string requestedTradeDate)
    {
        var apiDate = ConvertTradeDateToTwseApiDate(requestedTradeDate);
        if (apiDate is null)
        {
            _logger.LogWarning("Unable to convert trade date {TradeDate} to TWSE institutional trade API date.", requestedTradeDate);
            return null;
        }

        var apiUrl = BuildApiUrl(_twseApiUrlTemplate, apiDate, nameof(AppSettings.InstitutionalTradeApiUrlTemplate));
        _logger.LogInformation("Calling TWSE institutional trade API: {ApiUrl}", apiUrl);

        var apiResponse = await GetApiResponseWithRetryAsync(
            httpClient,
            apiUrl,
            requestedTradeDate,
            "TWSE institutional trade",
            response => JsonSerializer.Deserialize<TwseInstitutionalTradeApiResponse>(response),
            response => string.Equals(response.Stat, "OK", StringComparison.OrdinalIgnoreCase),
            response => response.Stat);

        if (apiResponse is null)
        {
            return null;
        }

        var rawTradeDate = apiResponse.Date.Trim();
        var tradeDate = ConvertDate(rawTradeDate);
        if (!string.Equals(tradeDate, requestedTradeDate, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "TWSE institutional trade API returned trade date {ApiTradeDate}, which differs from requested trade date {RequestedTradeDate}.",
                tradeDate,
                requestedTradeDate);
        }

        if (apiResponse.Data.Count == 0)
        {
            _logger.LogWarning("No data returned from TWSE institutional trade API for trade date {TradeDate}.", tradeDate);
        }

        return new InstitutionalTradePayload(tradeDate, rawTradeDate, apiResponse.Data);
    }

    /// <summary>
    /// 以重試機制呼叫上市法人 API，並在回應異常時記錄狀態與回應片段。
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
                var response = await SendApiRequestAsync(httpClient, apiUrl);
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

                    var stat = apiResponse is null ? "(null)" : getStatus(apiResponse);
                    var responsePreview = CreateResponsePreview(response);
                    if (attempt < MaxApiRetryCount)
                    {
                        _logger.LogInformation(
                            "{SourceName} API returned a temporary abnormal response on attempt {Attempt}/{MaxAttempts} for trade date {TradeDate}. Stat: {Stat}. ApiUrl: {ApiUrl}. ResponsePreview: {ResponsePreview}",
                            sourceName,
                            attempt,
                            MaxApiRetryCount,
                            requestedTradeDate,
                            stat,
                            apiUrl,
                            responsePreview);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "{SourceName} API returned an abnormal response on final attempt {Attempt}/{MaxAttempts} for trade date {TradeDate}. Stat: {Stat}. ApiUrl: {ApiUrl}. ResponsePreview: {ResponsePreview}",
                            sourceName,
                            attempt,
                            MaxApiRetryCount,
                            requestedTradeDate,
                            stat,
                            apiUrl,
                            responsePreview);
                    }
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
    /// 送出上市三大法人 API 請求，附加禁止快取設定與時間戳記，降低官方端偶發回傳舊快取結果的機率。
    /// </summary>
    private static async Task<string> SendApiRequestAsync(HttpClient httpClient, string apiUrl)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AppendCacheBustQueryParameter(apiUrl));
        request.Headers.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true
        };
        request.Headers.Pragma.Add(new NameValueHeaderValue("no-cache"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// 解析並過濾單一來源的法人資料列，只保留 StockDaily 已存在的股票代碼。
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
    /// 寫入上市法人資料前，保留同交易日已存在的上櫃法人資料。
    /// </summary>
    private async Task<List<InstitutionalTradeDaily>> MergeWithExistingOtcInstitutionalTradesAsync(
        string tradeDate,
        string twseRawTradeDate,
        List<InstitutionalTradeDaily> twseItems)
    {
        var existingItems = await _repository.GetByTradeDateAsync(tradeDate);
        if (existingItems.Count == 0)
        {
            return twseItems;
        }

        var preservedOtcItems = existingItems
            .Where(item => !string.Equals(item.RawDate, twseRawTradeDate, StringComparison.Ordinal))
            .ToList();

        if (preservedOtcItems.Count == 0)
        {
            return twseItems;
        }

        _logger.LogInformation(
            "Preserving {Count} institutional trade records already stored for trade date {TradeDate} while refreshing TWSE data.",
            preservedOtcItems.Count,
            tradeDate);

        return twseItems
            .Concat(preservedOtcItems)
            .ToList();
    }

    /// <summary>
    /// 決定本次抓取三大法人資料所使用的交易日期。
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
                "Using configured institutional trade date {TradeDate} from AppSettings.",
                configuredTradeDate);

            return configuredTradeDate;
        }

        if (!string.IsNullOrWhiteSpace(_latestTradeDateContext.LatestTwseTradeDate))
        {
            _logger.LogInformation(
                "No InstitutionalTradeFetchDate configured. Using TWSE stock API trade date {TradeDate} resolved from AppSettings.ApiUrl.",
                _latestTradeDateContext.LatestTwseTradeDate);

            return _latestTradeDateContext.LatestTwseTradeDate;
        }

        var latestTradeDate = await _stockRepository.GetLatestTradeDateAsync();
        if (latestTradeDate is null)
        {
            _logger.LogWarning("No stock trade date found. Skipping institutional trade sync.");
            return null;
        }

        _logger.LogWarning(
            "TWSE stock API trade date was unavailable in the current execution. Falling back to latest stock trade date {TradeDate} from database.",
            latestTradeDate);

        return latestTradeDate;
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
    /// 為請求網址補上時間戳記參數，避免同一查詢條件被中間快取回傳舊結果。
    /// </summary>
    private static string AppendCacheBustQueryParameter(string apiUrl)
    {
        if (apiUrl.Contains("_=", StringComparison.Ordinal))
        {
            return apiUrl;
        }

        var separator = apiUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{apiUrl}{separator}_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
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
    /// 將上市單筆法人資料列轉成 <see cref="InstitutionalTradeDaily"/> 模型。
    /// </summary>
    private static bool TryParseTwseRecord(
        IReadOnlyList<JsonElement> record,
        string tradeDate,
        string rawTradeDate,
        out InstitutionalTradeDaily institutionalTrade)
    {
        institutionalTrade = new InstitutionalTradeDaily();

        if (record.Count is not (19 or 16))
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

        // 標準格式包含外資自營商的三組欄位，共 19 欄。
        if (record.Count == 19)
        {
            institutionalTrade.ForeignInvestorBuy = ParseLong(record[2]);
            institutionalTrade.ForeignInvestorSell = ParseLong(record[3]);
            institutionalTrade.ForeignInvestorNet = ParseLong(record[4]);
            institutionalTrade.ForeignDealerBuy = ParseLong(record[5]);
            institutionalTrade.ForeignDealerSell = ParseLong(record[6]);
            institutionalTrade.ForeignDealerNet = ParseLong(record[7]);
            institutionalTrade.InvestmentTrustBuy = ParseLong(record[8]);
            institutionalTrade.InvestmentTrustSell = ParseLong(record[9]);
            institutionalTrade.InvestmentTrustNet = ParseLong(record[10]);
            institutionalTrade.DealerNet = ParseLong(record[11]);
            institutionalTrade.DealerSelfBuy = ParseLong(record[12]);
            institutionalTrade.DealerSelfSell = ParseLong(record[13]);
            institutionalTrade.DealerSelfNet = ParseLong(record[14]);
            institutionalTrade.DealerHedgeBuy = ParseLong(record[15]);
            institutionalTrade.DealerHedgeSell = ParseLong(record[16]);
            institutionalTrade.DealerHedgeNet = ParseLong(record[17]);
            institutionalTrade.InstitutionalInvestorsNet = ParseLong(record[18]);
            return true;
        }

        // 特殊格式省略外資自營商欄位，因此以 0 補齊對應欄位。
        institutionalTrade.ForeignInvestorBuy = ParseLong(record[2]);
        institutionalTrade.ForeignInvestorSell = ParseLong(record[3]);
        institutionalTrade.ForeignInvestorNet = ParseLong(record[4]);
        institutionalTrade.ForeignDealerBuy = 0;
        institutionalTrade.ForeignDealerSell = 0;
        institutionalTrade.ForeignDealerNet = 0;
        institutionalTrade.InvestmentTrustBuy = ParseLong(record[5]);
        institutionalTrade.InvestmentTrustSell = ParseLong(record[6]);
        institutionalTrade.InvestmentTrustNet = ParseLong(record[7]);
        institutionalTrade.DealerNet = ParseLong(record[8]);
        institutionalTrade.DealerSelfBuy = ParseLong(record[9]);
        institutionalTrade.DealerSelfSell = ParseLong(record[10]);
        institutionalTrade.DealerSelfNet = ParseLong(record[11]);
        institutionalTrade.DealerHedgeBuy = ParseLong(record[12]);
        institutionalTrade.DealerHedgeSell = ParseLong(record[13]);
        institutionalTrade.DealerHedgeNet = ParseLong(record[14]);
        institutionalTrade.InstitutionalInvestorsNet = ParseLong(record[15]);

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
    /// 將資料庫使用的交易日期轉為上市法人 API 查詢參數格式 <c>yyyyMMdd</c>。
    /// </summary>
    private static string? ConvertTradeDateToTwseApiDate(string tradeDate)
    {
        return DateTime.TryParseExact(tradeDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
            : null;
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
