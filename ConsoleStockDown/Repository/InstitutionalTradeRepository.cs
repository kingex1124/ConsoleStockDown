using ConsoleStockDown.DataAccess;
using ConsoleStockDown.Models;
using LinqToDB;
using LinqToDB.Async;

namespace ConsoleStockDown.Repository;

/// <summary>
/// 封裝 <see cref="InstitutionalTradeDaily"/> 的資料表建立、刪除與寫入邏輯。
/// </summary>
public sealed class InstitutionalTradeRepository : IInstitutionalTradeRepository
{
    private readonly string _connectionString;

    /// <summary>
    /// 建立三大法人資料存取物件。
    /// </summary>
    public InstitutionalTradeRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// 若資料表不存在則建立三大法人資料表。
    /// </summary>
    public Task InitializeDatabaseAsync()
    {
        using var db = new AppDataConnection(_connectionString);
        db.CreateTable<InstitutionalTradeDaily>(tableOptions: TableOptions.CreateIfNotExists);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 取得指定交易日期的全部三大法人資料。
    /// </summary>
    public async Task<IReadOnlyList<InstitutionalTradeDaily>> GetByTradeDateAsync(string tradeDate)
    {
        using var db = new AppDataConnection(_connectionString);
        return await db.GetTable<InstitutionalTradeDaily>()
            .Where(x => x.TradeDate == tradeDate)
            .ToListAsync();
    }

    /// <summary>
    /// 以單一交易刪除並重建指定交易日期的三大法人資料，避免中途中斷留下部分資料。
    /// </summary>
    public async Task ReplaceByTradeDateAsync(string tradeDate, IEnumerable<InstitutionalTradeDaily> items)
    {
        using var db = new AppDataConnection(_connectionString);
        using var transaction = db.BeginTransaction();

        await db.GetTable<InstitutionalTradeDaily>()
            .Where(x => x.TradeDate == tradeDate)
            .DeleteAsync();

        foreach (var item in items)
        {
            await db.InsertAsync(item);
        }

        transaction.Commit();
    }
}
