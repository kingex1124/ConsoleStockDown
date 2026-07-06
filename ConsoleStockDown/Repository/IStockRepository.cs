using ConsoleStockDown.Models;

namespace ConsoleStockDown.Repository;

/// <summary>
/// 提供股票日資料存取行為的資料庫介面。
/// </summary>
public interface IStockRepository
{
    /// <summary>
    /// 初始化股票日資料表。
    /// </summary>
    Task InitializeDatabaseAsync();

    /// <summary>
    /// 依股票代號與交易日期取得單筆股票資料。
    /// </summary>
    Task<StockDaily?> GetStockByCodeAndTradeDateAsync(string stockCode, string tradeDate);

    /// <summary>
    /// 取得目前資料庫中最新的股票交易日期。
    /// </summary>
    Task<string?> GetLatestTradeDateAsync();

    /// <summary>
    /// 取得指定日期之前最近的一個股票交易日期。
    /// </summary>
    Task<string?> GetLatestTradeDateBeforeDateAsync(string tradeDate);

    /// <summary>
    /// 以交易方式覆寫指定交易日期的全部股票資料。
    /// </summary>
    Task ReplaceByTradeDateAsync(string tradeDate, IEnumerable<StockDaily> items);
}
