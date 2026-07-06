using LinqToDB;
using LinqToDB.Async;
using ConsoleStockDown.DataAccess;
using ConsoleStockDown.Models;

namespace ConsoleStockDown.Repository;

/// <summary>
/// 封裝 <see cref="StockDaily"/> 的資料表建立、查詢與寫入邏輯。
/// </summary>
public sealed class StockRepository : IStockRepository
{
    private readonly string _connectionString;

    /// <summary>
    /// 建立股票資料存取物件。
    /// </summary>
    public StockRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// 若資料表不存在則建立股票日資料表。
    /// </summary>
    public Task InitializeDatabaseAsync()
    {
        using var db = new AppDataConnection(_connectionString);
        db.CreateTable<StockDaily>(tableOptions: TableOptions.CreateIfNotExists);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 查詢指定日期之前最近的一個交易日期。
    /// </summary>
    public async Task<string?> GetLatestTradeDateBeforeDateAsync(string tradeDate)
    {
        using var db = new AppDataConnection(_connectionString);
        return await db.GetTable<StockDaily>()
            .Where(x => x.TradeDate.CompareTo(tradeDate) < 0)
            .OrderByDescending(x => x.TradeDate)
            .Select(x => x.TradeDate)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// 取得指定交易日期的全部股票資料，並以股票代號建立索引。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, StockDaily>> GetStocksByTradeDateAsync(string tradeDate)
    {
        using var db = new AppDataConnection(_connectionString);
        var items = await db.GetTable<StockDaily>()
            .Where(x => x.TradeDate == tradeDate)
            .ToListAsync();

        return items
            .GroupBy(x => x.StockCode)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    }

    /// <summary>
    /// 查詢目前資料庫中最新的交易日期。
    /// </summary>
    public async Task<string?> GetLatestTradeDateAsync()
    {
        using var db = new AppDataConnection(_connectionString);
        return await db.GetTable<StockDaily>()
            .OrderByDescending(x => x.TradeDate)
            .Select(x => x.TradeDate)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// 依股票代號與交易日期查詢單筆股票資料。
    /// </summary>
    public async Task<StockDaily?> GetStockByCodeAndTradeDateAsync(string stockCode, string tradeDate)
    {
        using var db = new AppDataConnection(_connectionString);
        return await db.GetTable<StockDaily>()
            .Where(x => x.StockCode == stockCode && x.TradeDate == tradeDate)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// 以單一交易刪除並重建指定交易日期的股票資料，避免中途中斷留下部分資料。
    /// </summary>
    public async Task ReplaceByTradeDateAsync(string tradeDate, IEnumerable<StockDaily> items)
    {
        using var db = new AppDataConnection(_connectionString);
        using var transaction = db.BeginTransaction();

        await db.GetTable<StockDaily>()
            .Where(x => x.TradeDate == tradeDate)
            .DeleteAsync();

        foreach (var item in items)
        {
            await db.InsertAsync(item);
        }

        transaction.Commit();
    }

}
