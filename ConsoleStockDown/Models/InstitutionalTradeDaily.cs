using LinqToDB.Mapping;

namespace ConsoleStockDown.Models;

/// <summary>
/// 三大法人每日買賣超資料的資料表模型。
/// </summary>
[Table(Name = "InstitutionalTradeDaily")]
public sealed class InstitutionalTradeDaily
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
    /// 股票或商品代號。
    /// </summary>
    [Column, NotNull]
    public string StockCode { get; set; } = string.Empty;

    /// <summary>
    /// 股票或商品名稱。
    /// </summary>
    [Column, NotNull]
    public string StockName { get; set; } = string.Empty;

    /// <summary>
    /// 外資及陸資買進股數，不含外資自營商。
    /// </summary>
    [Column, NotNull]
    public long ForeignInvestorBuy { get; set; }

    /// <summary>
    /// 外資及陸資賣出股數，不含外資自營商。
    /// </summary>
    [Column, NotNull]
    public long ForeignInvestorSell { get; set; }

    /// <summary>
    /// 外資及陸資買賣超股數，不含外資自營商。
    /// </summary>
    [Column, NotNull]
    public long ForeignInvestorNet { get; set; }

    /// <summary>
    /// 外資自營商買進股數。
    /// </summary>
    [Column, NotNull]
    public long ForeignDealerBuy { get; set; }

    /// <summary>
    /// 外資自營商賣出股數。
    /// </summary>
    [Column, NotNull]
    public long ForeignDealerSell { get; set; }

    /// <summary>
    /// 外資自營商買賣超股數。
    /// </summary>
    [Column, NotNull]
    public long ForeignDealerNet { get; set; }

    /// <summary>
    /// 投信買進股數。
    /// </summary>
    [Column, NotNull]
    public long InvestmentTrustBuy { get; set; }

    /// <summary>
    /// 投信賣出股數。
    /// </summary>
    [Column, NotNull]
    public long InvestmentTrustSell { get; set; }

    /// <summary>
    /// 投信買賣超股數。
    /// </summary>
    [Column, NotNull]
    public long InvestmentTrustNet { get; set; }

    /// <summary>
    /// 自營商整體買賣超股數。
    /// </summary>
    [Column, NotNull]
    public long DealerNet { get; set; }

    /// <summary>
    /// 自營商自行買賣買進股數。
    /// </summary>
    [Column, NotNull]
    public long DealerSelfBuy { get; set; }

    /// <summary>
    /// 自營商自行買賣賣出股數。
    /// </summary>
    [Column, NotNull]
    public long DealerSelfSell { get; set; }

    /// <summary>
    /// 自營商自行買賣買賣超股數。
    /// </summary>
    [Column, NotNull]
    public long DealerSelfNet { get; set; }

    /// <summary>
    /// 自營商避險買進股數。
    /// </summary>
    [Column, NotNull]
    public long DealerHedgeBuy { get; set; }

    /// <summary>
    /// 自營商避險賣出股數。
    /// </summary>
    [Column, NotNull]
    public long DealerHedgeSell { get; set; }

    /// <summary>
    /// 自營商避險買賣超股數。
    /// </summary>
    [Column, NotNull]
    public long DealerHedgeNet { get; set; }

    /// <summary>
    /// 三大法人合計買賣超股數。
    /// </summary>
    [Column, NotNull]
    public long InstitutionalInvestorsNet { get; set; }
}
