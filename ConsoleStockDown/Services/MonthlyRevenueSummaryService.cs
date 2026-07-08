using System.Globalization;
using System.Text.Json;
using ConsoleStockDown.Models;
using ConsoleStockDown.Repository;
using Microsoft.Extensions.Logging;

namespace ConsoleStockDown.Services;

/// <summary>
/// 負責抓取 TWSE 上市公司每月營業收入彙總資料並寫入資料庫。
/// </summary>
public sealed class MonthlyRevenueSummaryService : IMonthlyRevenueSummaryService
{
    private const string ReportDateKey = "出表日期";
    private const string RevenueMonthKey = "資料年月";
    private const string StockCodeKey = "公司代號";
    private const string StockNameKey = "公司名稱";
    private const string IndustryCategoryKey = "產業別";
    private const string CurrentMonthRevenueKey = "營業收入-當月營收";
    private const string PreviousMonthRevenueKey = "營業收入-上月營收";
    private const string LastYearSameMonthRevenueKey = "營業收入-去年當月營收";
    private const string MonthOverMonthChangeRateKey = "營業收入-上月比較增減(%)";
    private const string YearOverYearChangeRateKey = "營業收入-去年同月增減(%)";
    private const string CurrentCumulativeRevenueKey = "累計營業收入-當月累計營收";
    private const string LastYearCumulativeRevenueKey = "累計營業收入-去年累計營收";
    private const string CumulativeYearOverYearChangeRateKey = "累計營業收入-前期比較增減(%)";
    private const string NoteKey = "備註";

    private readonly IMonthlyRevenueSummaryRepository _repository;
    private readonly ILogger<MonthlyRevenueSummaryService> _logger;
    private readonly string _apiUrl;

    /// <summary>
    /// 建立每月營收彙總資料服務。
    /// </summary>
    public MonthlyRevenueSummaryService(
        IMonthlyRevenueSummaryRepository repository,
        ILogger<MonthlyRevenueSummaryService> logger,
        string apiUrl)
    {
        _repository = repository;
        _logger = logger;
        _apiUrl = apiUrl;
    }

    /// <summary>
    /// 抓取最新每月營收彙總資料，若資料庫尚未存在該月份資料則寫入。
    /// </summary>
    public async Task FetchAndStoreLatestAsync()
    {
        _logger.LogInformation("Initializing monthly revenue summary database.");
        await _repository.InitializeDatabaseAsync();

        using var httpClient = new HttpClient();
        _logger.LogInformation("Calling monthly revenue summary API: {ApiUrl}", _apiUrl);

        var response = await httpClient.GetStringAsync(_apiUrl);
        var records = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(response);
        if (records is null || records.Count == 0)
        {
            _logger.LogWarning("No data returned from monthly revenue summary API.");
            return;
        }

        if (!TryGetString(records[0], RevenueMonthKey, out var firstRawRevenueMonth))
        {
            _logger.LogWarning("Unable to determine revenue month from monthly revenue summary API response.");
            return;
        }

        var revenueMonth = NormalizeRevenueMonth(firstRawRevenueMonth);
        if (revenueMonth is null)
        {
            _logger.LogWarning(
                "Unable to normalize revenue month {RawRevenueMonth} from monthly revenue summary API response.",
                firstRawRevenueMonth);
            return;
        }

        if (!TryGetString(records[0], ReportDateKey, out var firstRawReportDate))
        {
            _logger.LogWarning("Unable to determine report date from monthly revenue summary API response.");
            return;
        }

        var reportDate = NormalizeDate(firstRawReportDate);
        if (reportDate is null)
        {
            _logger.LogWarning(
                "Unable to normalize report date {RawReportDate} from monthly revenue summary API response.",
                firstRawReportDate);
            return;
        }

        _logger.LogInformation(
            "Resolved monthly revenue summary revenue month {RevenueMonth} and report date {ReportDate}.",
            revenueMonth,
            reportDate);

        if (await _repository.ExistsByRevenueMonthAsync(revenueMonth))
        {
            _logger.LogInformation(
                "Monthly revenue summary data for revenue month {RevenueMonth} already exists. Skipping insert.",
                revenueMonth);
            return;
        }

        var items = new List<MonthlyRevenueSummary>(records.Count);
        var failedParseCount = 0;

        foreach (var record in records)
        {
            if (!TryParseRecord(record, revenueMonth, out var item))
            {
                failedParseCount++;
                continue;
            }

            items.Add(item);
        }

        if (failedParseCount > 0)
        {
            _logger.LogWarning(
                "Failed to parse {Count} monthly revenue summary records for revenue month {RevenueMonth}.",
                failedParseCount,
                revenueMonth);
        }

        if (items.Count == 0)
        {
            _logger.LogWarning(
                "No valid monthly revenue summary records remained for revenue month {RevenueMonth}.",
                revenueMonth);
            return;
        }

        _logger.LogInformation(
            "Persisting {Count} monthly revenue summary records for revenue month {RevenueMonth}.",
            items.Count,
            revenueMonth);

        await _repository.InsertAsync(items);

        _logger.LogInformation(
            "Inserted {Count} monthly revenue summary records for revenue month {RevenueMonth}.",
            items.Count,
            revenueMonth);
    }

    /// <summary>
    /// 將單筆 API 回傳資料轉成 <see cref="MonthlyRevenueSummary"/> 模型。
    /// </summary>
    private static bool TryParseRecord(
        Dictionary<string, JsonElement> record,
        string expectedRevenueMonth,
        out MonthlyRevenueSummary monthlyRevenueSummary)
    {
        monthlyRevenueSummary = new MonthlyRevenueSummary();

        if (!TryGetString(record, RevenueMonthKey, out var rawRevenueMonth)
            || !TryGetString(record, ReportDateKey, out var rawReportDate)
            || !TryGetString(record, StockCodeKey, out var stockCode)
            || !TryGetString(record, StockNameKey, out var stockName)
            || !TryGetString(record, IndustryCategoryKey, out var industryCategory))
        {
            return false;
        }

        var revenueMonth = NormalizeRevenueMonth(rawRevenueMonth);
        var reportDate = NormalizeDate(rawReportDate);
        if (revenueMonth is null
            || reportDate is null
            || !string.Equals(revenueMonth, expectedRevenueMonth, StringComparison.Ordinal))
        {
            return false;
        }

        monthlyRevenueSummary.RawRevenueMonth = rawRevenueMonth;
        monthlyRevenueSummary.RevenueMonth = revenueMonth;
        monthlyRevenueSummary.RawReportDate = rawReportDate;
        monthlyRevenueSummary.ReportDate = reportDate;
        monthlyRevenueSummary.StockCode = stockCode;
        monthlyRevenueSummary.StockName = stockName;
        monthlyRevenueSummary.IndustryCategory = industryCategory;
        monthlyRevenueSummary.CurrentMonthRevenue = ParseLong(record, CurrentMonthRevenueKey);
        monthlyRevenueSummary.PreviousMonthRevenue = ParseLong(record, PreviousMonthRevenueKey);
        monthlyRevenueSummary.LastYearSameMonthRevenue = ParseLong(record, LastYearSameMonthRevenueKey);
        monthlyRevenueSummary.MonthOverMonthChangeRate = ParseNullableDecimal(record, MonthOverMonthChangeRateKey);
        monthlyRevenueSummary.YearOverYearChangeRate = ParseNullableDecimal(record, YearOverYearChangeRateKey);
        monthlyRevenueSummary.CurrentCumulativeRevenue = ParseLong(record, CurrentCumulativeRevenueKey);
        monthlyRevenueSummary.LastYearCumulativeRevenue = ParseLong(record, LastYearCumulativeRevenueKey);
        monthlyRevenueSummary.CumulativeYearOverYearChangeRate = ParseNullableDecimal(record, CumulativeYearOverYearChangeRateKey);
        monthlyRevenueSummary.Note = TryGetString(record, NoteKey, out var note) ? note : string.Empty;

        return true;
    }

    /// <summary>
    /// 將原始資料年月統一轉成 <c>yyyy-MM</c> 格式。
    /// </summary>
    private static string? NormalizeRevenueMonth(string rawRevenueMonth)
    {
        var trimmed = rawRevenueMonth.Trim();
        if (DateTime.TryParseExact(
            trimmed,
            new[] { "yyyyMM", "yyyy-MM", "yyyy/MM" },
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed))
        {
            return parsed.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        }

        if (trimmed.Length == 5
            && int.TryParse(trimmed[..3], NumberStyles.None, CultureInfo.InvariantCulture, out var taiwanYear)
            && int.TryParse(trimmed[3..5], NumberStyles.None, CultureInfo.InvariantCulture, out var month))
        {
            return new DateTime(taiwanYear + 1911, month, 1).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        }

        if (trimmed.Length == 6
            && trimmed[3] == '/'
            && int.TryParse(trimmed[..3], NumberStyles.None, CultureInfo.InvariantCulture, out var taiwanYearWithSlash)
            && int.TryParse(trimmed[4..6], NumberStyles.None, CultureInfo.InvariantCulture, out var monthWithSlash))
        {
            return new DateTime(taiwanYearWithSlash + 1911, monthWithSlash, 1).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        }

        return null;
    }

    /// <summary>
    /// 將原始 API 日期統一轉成 <c>yyyy-MM-dd</c> 格式。
    /// </summary>
    private static string? NormalizeDate(string rawDate)
    {
        var trimmed = rawDate.Trim();
        if (DateTime.TryParseExact(
            trimmed,
            new[] { "yyyyMMdd", "yyyy-MM-dd", "yyyy/MM/dd" },
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed))
        {
            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (trimmed.Length == 7
            && int.TryParse(trimmed[..3], NumberStyles.None, CultureInfo.InvariantCulture, out var taiwanYear)
            && int.TryParse(trimmed[3..5], NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            && int.TryParse(trimmed[5..7], NumberStyles.None, CultureInfo.InvariantCulture, out var day))
        {
            return new DateTime(taiwanYear + 1911, month, day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (trimmed.Length == 9
            && trimmed[3] == '/'
            && trimmed[6] == '/'
            && int.TryParse(trimmed[..3], NumberStyles.None, CultureInfo.InvariantCulture, out var taiwanYearWithSlash)
            && int.TryParse(trimmed[4..6], NumberStyles.None, CultureInfo.InvariantCulture, out var monthWithSlash)
            && int.TryParse(trimmed[7..9], NumberStyles.None, CultureInfo.InvariantCulture, out var dayWithSlash))
        {
            return new DateTime(taiwanYearWithSlash + 1911, monthWithSlash, dayWithSlash).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return null;
    }

    /// <summary>
    /// 從指定欄位讀取整數資料，並處理逗號格式。
    /// </summary>
    private static long ParseLong(Dictionary<string, JsonElement> record, string key)
    {
        if (!record.TryGetValue(key, out var element))
        {
            return 0L;
        }

        var cleaned = GetElementText(element)
            .Trim()
            .Replace(",", string.Empty, StringComparison.Ordinal);

        return long.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0L;
    }

    /// <summary>
    /// 從指定欄位讀取可為空的小數資料，並處理逗號與缺值符號。
    /// </summary>
    private static decimal? ParseNullableDecimal(Dictionary<string, JsonElement> record, string key)
    {
        if (!record.TryGetValue(key, out var element))
        {
            return null;
        }

        var cleaned = GetElementText(element)
            .Trim()
            .Replace(",", string.Empty, StringComparison.Ordinal);

        if (cleaned.Length == 0 || cleaned == "-")
        {
            return null;
        }

        return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
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
}
