using ConsoleStockDown.Models;

namespace ConsoleStockDown.Repository;

/// <summary>
/// 提供每月營收彙總資料存取行為的資料庫介面。
/// </summary>
public interface IMonthlyRevenueSummaryRepository
{
    /// <summary>
    /// 初始化每月營收彙總資料表。
    /// </summary>
    Task InitializeDatabaseAsync();

    /// <summary>
    /// 判斷指定營收月份是否已存在資料。
    /// </summary>
    Task<bool> ExistsByRevenueMonthAsync(string revenueMonth);

    /// <summary>
    /// 以單一交易寫入同一批每月營收彙總資料。
    /// </summary>
    Task InsertAsync(IEnumerable<MonthlyRevenueSummary> items);
}
