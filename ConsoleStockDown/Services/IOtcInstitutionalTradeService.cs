namespace ConsoleStockDown.Services;

/// <summary>
/// 提供上櫃三大法人資料抓取與寫入流程的服務介面。
/// </summary>
public interface IOtcInstitutionalTradeService
{
    /// <summary>
    /// 依設定規則抓取最新或指定日期的上櫃三大法人資料並寫入資料庫。
    /// </summary>
    Task FetchAndStoreLatestAsync();

    /// <summary>
    /// 抓取指定交易日期的上櫃三大法人資料並寫入資料庫。
    /// </summary>
    Task FetchAndStoreAsync(string tradeDate);
}
