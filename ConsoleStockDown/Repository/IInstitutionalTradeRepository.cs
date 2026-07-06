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
    /// 以交易方式覆寫指定交易日期的全部三大法人資料。
    /// </summary>
    Task ReplaceByTradeDateAsync(string tradeDate, IEnumerable<InstitutionalTradeDaily> items);
}
