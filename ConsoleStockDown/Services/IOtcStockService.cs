namespace ConsoleStockDown.Services;

/// <summary>
/// 提供上櫃股票日資料抓取與寫入流程的服務介面。
/// </summary>
public interface IOtcStockService
{
    /// <summary>
    /// 抓取最新上櫃股票日資料並寫入資料庫。
    /// </summary>
    Task FetchAndStoreLatestAsync();
}
