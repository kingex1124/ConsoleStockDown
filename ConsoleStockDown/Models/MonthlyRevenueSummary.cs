using LinqToDB.Mapping;

namespace ConsoleStockDown.Models;

/// <summary>
/// 上市公司每月營業收入彙總資料的資料表模型。
/// </summary>
[Table(Name = "MonthlyRevenueSummary")]
public sealed class MonthlyRevenueSummary
{
    /// <summary>
    /// 資料表主鍵，使用資料庫自動遞增。
    /// </summary>
    [PrimaryKey, Identity]
    public int Id { get; set; }

    /// <summary>
    /// 營收所屬月份，統一使用 <c>yyyy-MM</c> 格式。
    /// </summary>
    [Column, NotNull]
    public string RevenueMonth { get; set; } = string.Empty;

    /// <summary>
    /// API 原始回傳的資料年月，保留原始格式供除錯與追蹤使用。
    /// </summary>
    [Column, NotNull]
    public string RawRevenueMonth { get; set; } = string.Empty;

    /// <summary>
    /// 出表日期，統一使用 <c>yyyy-MM-dd</c> 格式。
    /// </summary>
    [Column, NotNull]
    public string ReportDate { get; set; } = string.Empty;

    /// <summary>
    /// API 原始回傳的出表日期，保留原始格式供除錯與追蹤使用。
    /// </summary>
    [Column, NotNull]
    public string RawReportDate { get; set; } = string.Empty;

    /// <summary>
    /// 公司代號。
    /// </summary>
    [Column, NotNull]
    public string StockCode { get; set; } = string.Empty;

    /// <summary>
    /// 公司名稱。
    /// </summary>
    [Column, NotNull]
    public string StockName { get; set; } = string.Empty;

    /// <summary>
    /// 產業別。
    /// </summary>
    [Column, NotNull]
    public string IndustryCategory { get; set; } = string.Empty;

    /// <summary>
    /// 當月營收。
    /// </summary>
    [Column, NotNull]
    public long CurrentMonthRevenue { get; set; }

    /// <summary>
    /// 上月營收。
    /// </summary>
    [Column, NotNull]
    public long PreviousMonthRevenue { get; set; }

    /// <summary>
    /// 去年同月營收。
    /// </summary>
    [Column, NotNull]
    public long LastYearSameMonthRevenue { get; set; }

    /// <summary>
    /// 當月與上月比較增減百分比。
    /// </summary>
    [Column]
    public decimal? MonthOverMonthChangeRate { get; set; }

    /// <summary>
    /// 當月與去年同月比較增減百分比。
    /// </summary>
    [Column]
    public decimal? YearOverYearChangeRate { get; set; }

    /// <summary>
    /// 當月累計營收。
    /// </summary>
    [Column, NotNull]
    public long CurrentCumulativeRevenue { get; set; }

    /// <summary>
    /// 去年累計營收。
    /// </summary>
    [Column, NotNull]
    public long LastYearCumulativeRevenue { get; set; }

    /// <summary>
    /// 累計營收與去年同期比較增減百分比。
    /// </summary>
    [Column]
    public decimal? CumulativeYearOverYearChangeRate { get; set; }

    /// <summary>
    /// API 回傳的備註內容。
    /// </summary>
    [Column, NotNull]
    public string Note { get; set; } = string.Empty;
}
