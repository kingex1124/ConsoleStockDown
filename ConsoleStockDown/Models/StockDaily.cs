using LinqToDB.Mapping;

namespace ConsoleStockDown.Models;

/// <summary>
/// 上市股票每日交易資料的資料表模型。
/// </summary>
[Table(Name = "StockDaily")]
public sealed class StockDaily
{
    /// <summary>
    /// 資料表主鍵，使用資料庫自動遞增。
    /// </summary>
    [PrimaryKey, Identity]
    public int Id { get; set; }

    /// <summary>
    /// 交易日期，統一使用 <c>yyyy-MM-dd</c> 格式。
    /// </summary>
    [Column, NotNull]
    public string TradeDate { get; set; } = string.Empty;

    /// <summary>
    /// API 原始回傳日期，保留原始格式供除錯與追蹤使用。
    /// </summary>
    [Column, NotNull]
    public string RawDate { get; set; } = string.Empty;

    /// <summary>
    /// 股票代號。
    /// </summary>
    [Column, NotNull]
    public string StockCode { get; set; } = string.Empty;

    /// <summary>
    /// 股票名稱。
    /// </summary>
    [Column, NotNull]
    public string StockName { get; set; } = string.Empty;

    /// <summary>
    /// 成交股數。
    /// </summary>
    [Column, NotNull]
    public long TradeVolume { get; set; }

    /// <summary>
    /// 成交金額。
    /// </summary>
    [Column, NotNull]
    public long TradeValue { get; set; }

    /// <summary>
    /// 開盤價。
    /// </summary>
    [Column, NotNull]
    public decimal OpeningPrice { get; set; }

    /// <summary>
    /// 最高價。
    /// </summary>
    [Column, NotNull]
    public decimal HighestPrice { get; set; }

    /// <summary>
    /// 最低價。
    /// </summary>
    [Column, NotNull]
    public decimal LowestPrice { get; set; }

    /// <summary>
    /// 收盤價。
    /// </summary>
    [Column, NotNull]
    public decimal ClosingPrice { get; set; }

    /// <summary>
    /// 漲跌價差。
    /// </summary>
    [Column, NotNull]
    public decimal PriceChange { get; set; }

    /// <summary>
    /// 成交筆數。
    /// </summary>
    [Column, NotNull]
    public int TransactionCount { get; set; }

    /// <summary>
    /// 相對前一交易日收盤價計算出的漲跌幅百分比。
    /// </summary>
    [Column]
    public decimal? ChangeRate { get; set; }
}
