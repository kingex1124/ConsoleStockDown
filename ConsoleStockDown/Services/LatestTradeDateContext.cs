namespace ConsoleStockDown.Services;

/// <summary>
/// 保存本次程式執行期間各服務解析出的交易日期，供後續流程共用。
/// </summary>
public sealed class LatestTradeDateContext
{
    /// <summary>
    /// 保存本次上市日線 API 實際回傳的交易日期。
    /// </summary>
    public string? LatestTwseTradeDate { get; set; }
}
