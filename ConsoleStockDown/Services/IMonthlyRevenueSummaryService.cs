namespace ConsoleStockDown.Services;

/// <summary>
/// 提供上市公司每月營收彙總資料同步流程。
/// </summary>
public interface IMonthlyRevenueSummaryService
{
    /// <summary>
    /// 抓取最新每月營收彙總資料，若資料庫尚未存在該月份資料則寫入。
    /// </summary>
    Task FetchAndStoreLatestAsync();
}
