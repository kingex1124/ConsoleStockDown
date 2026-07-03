namespace ConsoleStockDown.Configuration;

public sealed record AppSettings
{
    public required string DatabaseFileName { get; init; }
    public required string ApiUrl { get; init; }
    public required string InstitutionalTradeApiUrlTemplate { get; init; }
    public string? InstitutionalTradeFetchDate { get; init; }
    public required string LogFilePath { get; init; }
}
