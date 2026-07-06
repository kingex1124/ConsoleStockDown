using System.Globalization;
using System.Text.Json;
using ConsoleStockDown.Models;
using ConsoleStockDown.Repository;
using Microsoft.Extensions.Logging;

namespace ConsoleStockDown.Services;

/// <summary>
/// 負責抓取上櫃股票每日資料、轉換欄位並寫入資料庫。
/// </summary>
public sealed class OtcStockService : IOtcStockService
{
    private readonly IStockRepository _repository;
    private readonly ILogger<OtcStockService> _logger;
    private readonly string _apiUrl;

    /// <summary>
    /// 建立上櫃股票日資料服務。
    /// </summary>
    public OtcStockService(IStockRepository repository, ILogger<OtcStockService> logger, string apiUrl)
    {
        _repository = repository;
        _logger = logger;
        _apiUrl = apiUrl;
    }

    /// <summary>
    /// 抓取最新上櫃股票日資料，並依前一交易日收盤價補上漲跌幅後寫入資料庫。
    /// </summary>
    public async Task FetchAndStoreLatestAsync()
    {
        _logger.LogInformation("Initializing OTC stock database.");
        await _repository.InitializeDatabaseAsync();

        using var httpClient = new HttpClient();
        _logger.LogInformation("Calling OTC API: {ApiUrl}", _apiUrl);

        var response = await httpClient.GetStringAsync(_apiUrl);
        var records = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(response);
        if (records is null || records.Count == 0)
        {
            _logger.LogWarning("No data returned from OTC API.");
            return;
        }

        var stockItems = new List<StockDaily>(records.Count);

        if (!TryGetString(records[0], "Date", out var rawApiTradeDate))
        {
            _logger.LogWarning("Unable to determine OTC trade date from API response.");
            return;
        }

        var apiTradeDate = ConvertDate(rawApiTradeDate);
        var priorTradeDate = await _repository.GetLatestTradeDateBeforeDateAsync(apiTradeDate);
        IReadOnlyDictionary<string, StockDaily> priorStocksByCode = new Dictionary<string, StockDaily>(StringComparer.Ordinal);
        if (priorTradeDate is not null)
        {
            _logger.LogInformation("Using prior trade date {PriorTradeDate} for OTC API trade date {TradeDate}.", priorTradeDate, apiTradeDate);
            priorStocksByCode = await _repository.GetStocksByTradeDateAsync(priorTradeDate);
        }
        else
        {
            _logger.LogWarning("No prior trade date found before OTC API trade date {TradeDate}.", apiTradeDate);
        }

        var missingPriorStockCodes = new List<string>();
        var missingPriorStockCount = 0;

        foreach (var record in records)
        {
            if (!TryParseRecord(record, out var stockDaily))
            {
                _logger.LogWarning("Failed to parse a record from OTC API response.");
                continue;
            }

            if (priorTradeDate is not null)
            {
                if (priorStocksByCode.TryGetValue(stockDaily.StockCode, out var priorRecord))
                {
                    stockDaily.ChangeRate = CalculateChangeRate(priorRecord.ClosingPrice, stockDaily.ClosingPrice);
                }
                else
                {
                    missingPriorStockCount++;
                    if (missingPriorStockCodes.Count < 10)
                    {
                        missingPriorStockCodes.Add(stockDaily.StockCode);
                    }
                }
            }
            else
            {
                _logger.LogDebug("Skipping OTC change rate calculation for stock {StockCode} because prior trade date is unavailable.", stockDaily.StockCode);
            }

            stockItems.Add(stockDaily);
        }

        if (priorTradeDate is not null && missingPriorStockCount > 0)
        {
            _logger.LogInformation(
                "Skipped change rate calculation for {Count} OTC stocks because no prior-day record existed on {PriorTradeDate}. Sample codes: {StockCodes}",
                missingPriorStockCount,
                priorTradeDate,
                string.Join(", ", missingPriorStockCodes));
        }

        if (stockItems.Count == 0)
        {
            _logger.LogWarning("No OTC stock records were parsed successfully.");
            return;
        }

        var latestTradeDate = stockItems.Select(x => x.TradeDate).Distinct().OrderByDescending(x => x).First();
        _logger.LogInformation("Persisting {Count} OTC records for trade date {TradeDate}.", stockItems.Count, latestTradeDate);

        await _repository.InsertAsync(stockItems);

        _logger.LogInformation("Inserted {Count} OTC records for trade date {TradeDate}.", stockItems.Count, latestTradeDate);
    }

    /// <summary>
    /// 將單筆 API 回傳資料轉成 <see cref="StockDaily"/> 模型。
    /// </summary>
    private static bool TryParseRecord(Dictionary<string, JsonElement> record, out StockDaily stockDaily)
    {
        stockDaily = new StockDaily();

        if (!TryGetString(record, "Date", out var rawDate))
        {
            return false;
        }

        stockDaily.RawDate = rawDate;
        stockDaily.TradeDate = ConvertDate(rawDate);

        if (!TryGetString(record, "SecuritiesCompanyCode", out var code)
            || !TryGetString(record, "CompanyName", out var name))
        {
            return false;
        }

        stockDaily.StockCode = code;
        stockDaily.StockName = name;
        stockDaily.TradeVolume = ParseLong(record, "TradingShares");
        stockDaily.TradeValue = ParseLong(record, "TransactionAmount");
        stockDaily.OpeningPrice = ParseDecimal(record, "Open");
        stockDaily.HighestPrice = ParseDecimal(record, "High");
        stockDaily.LowestPrice = ParseDecimal(record, "Low");
        stockDaily.ClosingPrice = ParseDecimal(record, "Close");
        stockDaily.PriceChange = ParseDecimal(record, "Change");
        stockDaily.TransactionCount = (int)ParseLong(record, "TransactionNumber");

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
    /// 從指定欄位讀取整數資料，並處理逗號格式。
    /// </summary>
    private static long ParseLong(Dictionary<string, JsonElement> record, string key)
    {
        if (record.TryGetValue(key, out var element))
        {
            var cleaned = GetElementText(element).Trim().Replace(",", string.Empty);
            return long.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0L;
        }

        return 0L;
    }

    /// <summary>
    /// 從指定欄位讀取小數資料，並處理符號與缺值文字。
    /// </summary>
    private static decimal ParseDecimal(Dictionary<string, JsonElement> record, string key)
    {
        if (record.TryGetValue(key, out var element))
        {
            var cleaned = GetElementText(element)
                .Trim()
                .Replace(",", string.Empty)
                .Replace("+", string.Empty, StringComparison.Ordinal);

            if (cleaned is "" or "--" or "---" or "----" or "除息" or "除權" or "除權息")
            {
                return 0m;
            }

            return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0m;
        }

        return 0m;
    }

    /// <summary>
    /// 嘗試從指定欄位讀取非空字串內容。
    /// </summary>
    private static bool TryGetString(Dictionary<string, JsonElement> record, string key, out string value)
    {
        value = string.Empty;
        if (!record.TryGetValue(key, out var element))
        {
            return false;
        }

        value = GetElementText(element).Trim();
        return value.Length > 0;
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
    /// 以前一交易日收盤價計算當日漲跌幅百分比。
    /// </summary>
    private static decimal? CalculateChangeRate(decimal priorClose, decimal currentClose)
    {
        if (priorClose == 0m)
        {
            return null;
        }

        return Math.Round((currentClose - priorClose) / priorClose * 100m, 4);
    }
}
