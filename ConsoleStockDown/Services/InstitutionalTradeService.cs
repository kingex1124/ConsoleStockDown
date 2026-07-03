using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ConsoleStockDown.Models;
using ConsoleStockDown.Repository;
using Microsoft.Extensions.Logging;

namespace ConsoleStockDown.Services;

/// <summary>
/// 負責抓取 TWSE 三大法人資料、轉換欄位並寫入資料庫。
/// </summary>
public sealed class InstitutionalTradeService : IInstitutionalTradeService
{
    private readonly IInstitutionalTradeRepository _repository;
    private readonly IStockRepository _stockRepository;
    private readonly ILogger<InstitutionalTradeService> _logger;
    private readonly string _apiUrlTemplate;
    private readonly string? _configuredTradeDate;

    /// <summary>
    /// 建立三大法人資料服務。
    /// </summary>
    public InstitutionalTradeService(
        IInstitutionalTradeRepository repository,
        IStockRepository stockRepository,
        ILogger<InstitutionalTradeService> logger,
        string apiUrlTemplate,
        string? configuredTradeDate)
    {
        _repository = repository;
        _stockRepository = stockRepository;
        _logger = logger;
        _apiUrlTemplate = apiUrlTemplate;
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

        var apiDate = ConvertTradeDateToApiDate(targetTradeDate);
        if (apiDate is null)
        {
            _logger.LogWarning("Unable to convert trade date {TradeDate} to institutional trade API date.", targetTradeDate);
            return;
        }

        var apiUrl = BuildApiUrl(apiDate);
        using var httpClient = new HttpClient();
        _logger.LogInformation("Calling institutional trade API: {ApiUrl}", apiUrl);

        var response = await httpClient.GetStringAsync(apiUrl);
        var apiResponse = JsonSerializer.Deserialize<InstitutionalTradeApiResponse>(response);
        if (apiResponse is null || !string.Equals(apiResponse.Stat, "OK", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Institutional trade API returned an invalid response.");
            return;
        }

        if (apiResponse.Data.Count == 0)
        {
            _logger.LogWarning("No institutional trade data returned from API for trade date {TradeDate}.", targetTradeDate);
            return;
        }

        var rawTradeDate = apiResponse.Date.Trim();
        var tradeDate = ConvertDate(rawTradeDate);
        if (!string.Equals(tradeDate, targetTradeDate, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Institutional trade API returned trade date {ApiTradeDate}, which differs from requested trade date {RequestedTradeDate}.",
                tradeDate,
                targetTradeDate);
        }

        var items = new List<InstitutionalTradeDaily>(apiResponse.Data.Count);
        foreach (var record in apiResponse.Data)
        {
            if (!TryParseRecord(record, tradeDate, rawTradeDate, out var institutionalTrade))
            {
                _logger.LogWarning("Failed to parse an institutional trade record from API response.");
                continue;
            }

            items.Add(institutionalTrade);
        }

        if (items.Count == 0)
        {
            _logger.LogWarning("No institutional trade records were parsed successfully.");
            return;
        }

        _logger.LogInformation("Persisting {Count} institutional trade records for trade date {TradeDate}.", items.Count, tradeDate);

        await _repository.DeleteByTradeDateAsync(tradeDate);
        await _repository.InsertInstitutionalTradesAsync(items);

        _logger.LogInformation("Inserted {Count} institutional trade records for trade date {TradeDate}.", items.Count, tradeDate);
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

        var latestTradeDate = await _stockRepository.GetLatestTradeDateAsync();
        if (latestTradeDate is null)
        {
            _logger.LogWarning("No stock trade date found. Skipping institutional trade sync.");
            return null;
        }

        _logger.LogInformation(
            "No InstitutionalTradeFetchDate configured. Using latest stock trade date {TradeDate}.",
            latestTradeDate);

        return latestTradeDate;
    }

    /// <summary>
    /// 將交易日期代入 API URL 範本，組出實際請求位址。
    /// </summary>
    private string BuildApiUrl(string apiDate)
    {
        if (!_apiUrlTemplate.Contains("{date}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("InstitutionalTradeApiUrlTemplate must contain the {date} placeholder.");
        }

        return _apiUrlTemplate.Replace("{date}", apiDate, StringComparison.Ordinal);
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
    /// 將單筆法人資料列轉成 <see cref="InstitutionalTradeDaily"/> 模型。
    /// </summary>
    private static bool TryParseRecord(
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
    /// 將資料庫使用的交易日期轉為 API 查詢參數格式 <c>yyyyMMdd</c>。
    /// </summary>
    private static string? ConvertTradeDateToApiDate(string tradeDate)
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

    /// <summary>
    /// 對應 T86 API 最外層回傳結構的內部模型。
    /// </summary>
    private sealed record InstitutionalTradeApiResponse
    {
        /// <summary>
        /// API 回傳狀態。
        /// </summary>
        [JsonPropertyName("stat")]
        public string Stat { get; init; } = string.Empty;

        /// <summary>
        /// API 回傳的交易日期。
        /// </summary>
        [JsonPropertyName("date")]
        public string Date { get; init; } = string.Empty;

        /// <summary>
        /// API 回傳的明細資料列。
        /// </summary>
        [JsonPropertyName("data")]
        public List<List<JsonElement>> Data { get; init; } = [];
    }
}
