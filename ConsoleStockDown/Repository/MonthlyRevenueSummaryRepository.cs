using ConsoleStockDown.DataAccess;
using ConsoleStockDown.Models;
using LinqToDB;
using LinqToDB.Async;

namespace ConsoleStockDown.Repository;

/// <summary>
/// 封裝 <see cref="MonthlyRevenueSummary"/> 的資料表建立、查詢與寫入邏輯。
/// </summary>
public sealed class MonthlyRevenueSummaryRepository : IMonthlyRevenueSummaryRepository
{
    private readonly string _connectionString;

    /// <summary>
    /// 建立每月營收彙總資料存取物件。
    /// </summary>
    public MonthlyRevenueSummaryRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// 若資料表不存在則建立每月營收彙總資料表。
    /// </summary>
    public Task InitializeDatabaseAsync()
    {
        using var db = new AppDataConnection(_connectionString);
        db.CreateTable<MonthlyRevenueSummary>(tableOptions: TableOptions.CreateIfNotExists);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 判斷指定營收月份是否已存在資料。
    /// </summary>
    public async Task<bool> ExistsByRevenueMonthAsync(string revenueMonth)
    {
        using var db = new AppDataConnection(_connectionString);
        return await db.GetTable<MonthlyRevenueSummary>()
            .Where(x => x.RevenueMonth == revenueMonth)
            .AnyAsync();
    }

    /// <summary>
    /// 以單一交易整批寫入每月營收彙總資料，避免中途中斷留下部分資料。
    /// </summary>
    public async Task InsertAsync(IEnumerable<MonthlyRevenueSummary> items)
    {
        using var db = new AppDataConnection(_connectionString);
        using var transaction = db.BeginTransaction();

        foreach (var item in items)
        {
            await db.InsertAsync(item);
        }

        transaction.Commit();
    }
}
