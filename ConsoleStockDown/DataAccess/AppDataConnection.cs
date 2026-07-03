using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider.SQLite;
using ConsoleStockDown.Models;

namespace ConsoleStockDown.DataAccess;

public sealed class AppDataConnection : DataConnection
{
    public AppDataConnection(string connectionString)
        : base(new DataOptions().UseConnectionString(SQLiteTools.GetDataProvider(), connectionString))
    {
    }
}
