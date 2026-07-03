namespace ConsoleStockDown.Services;

/// <summary>
/// 提供三大法人資料抓取與寫入流程的服務介面。
/// </summary>
public interface IInstitutionalTradeService
{
    /// <summary>
    /// 抓取最新或指定日期的三大法人資料並寫入資料庫。
    /// </summary>
    Task FetchAndStoreLatestAsync();
}
