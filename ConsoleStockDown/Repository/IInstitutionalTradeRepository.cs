using ConsoleStockDown.Models;

namespace ConsoleStockDown.Repository;

/// <summary>
/// 提供三大法人資料存取行為的資料庫介面。
/// </summary>
public interface IInstitutionalTradeRepository
{
    /// <summary>
    /// 初始化三大法人資料表。
    /// </summary>
    Task InitializeDatabaseAsync();

    /// <summary>
    /// 刪除指定交易日期的三大法人資料。
    /// </summary>
    Task DeleteByTradeDateAsync(string tradeDate);

    /// <summary>
    /// 新增一批三大法人資料。
    /// </summary>
    Task InsertInstitutionalTradesAsync(IEnumerable<InstitutionalTradeDaily> items);
}
