using ConsoleStockDown.Configuration;
using ConsoleStockDown.Logging;
using ConsoleStockDown.Repository;
using ConsoleStockDown.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        config.SetBasePath(AppContext.BaseDirectory);
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
    })
    .ConfigureServices((context, services) =>
    {
        services.Configure<AppSettings>(context.Configuration.GetSection("AppSettings"));
        var appSettings = context.Configuration.GetSection("AppSettings").Get<AppSettings>()!;
        var basePath = AppContext.BaseDirectory;
        var projectRoot = Path.GetFullPath(Path.Combine(basePath, "..", "..", ".."));
        var dbPath = Path.Combine(projectRoot, appSettings.DatabaseFileName);
        var connectionString = $"Data Source={dbPath};Pooling=true;";
        var logFilePath = Path.Combine(basePath, appSettings.LogFilePath);

        services.AddSingleton<LatestTradeDateContext>();
        services.AddSingleton<IStockRepository>(_ => new StockRepository(connectionString));
        services.AddSingleton<IInstitutionalTradeRepository>(_ => new InstitutionalTradeRepository(connectionString));
        services.AddSingleton<IStockService>(_ => new StockService(
            _.GetRequiredService<IStockRepository>(),
            _.GetRequiredService<LatestTradeDateContext>(),
            _.GetRequiredService<ILogger<StockService>>(),
            appSettings.ApiUrl));
        services.AddSingleton<IOtcStockService>(_ => new OtcStockService(
            _.GetRequiredService<IStockRepository>(),
            _.GetRequiredService<ILogger<OtcStockService>>(),
            appSettings.OtcApiUrl));
        services.AddSingleton<IOtcInstitutionalTradeService>(_ => new OtcInstitutionalTradeService(
            _.GetRequiredService<IInstitutionalTradeRepository>(),
            _.GetRequiredService<IStockRepository>(),
            _.GetRequiredService<LatestTradeDateContext>(),
            _.GetRequiredService<ILogger<OtcInstitutionalTradeService>>(),
            appSettings.OtcInstitutionalTradeApiUrlTemplate,
            appSettings.InstitutionalTradeFetchDate));
        services.AddSingleton<IInstitutionalTradeService>(_ => new InstitutionalTradeService(
            _.GetRequiredService<IInstitutionalTradeRepository>(),
            _.GetRequiredService<IStockRepository>(),
            _.GetRequiredService<LatestTradeDateContext>(),
            _.GetRequiredService<IOtcInstitutionalTradeService>(),
            _.GetRequiredService<ILogger<InstitutionalTradeService>>(),
            appSettings.InstitutionalTradeApiUrlTemplate,
            appSettings.InstitutionalTradeFetchDate));
        services.AddSingleton(appSettings);

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddConfiguration(context.Configuration.GetSection("Logging"));
            builder.AddProvider(new FileLoggerProvider(logFilePath));
        });
    })
    .Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
var stockService = host.Services.GetRequiredService<IStockService>();
var otcStockService = host.Services.GetRequiredService<IOtcStockService>();
var institutionalTradeService = host.Services.GetRequiredService<IInstitutionalTradeService>();

logger.LogInformation("Application started.");

try
{
    await stockService.FetchAndStoreLatestAsync();
    await otcStockService.FetchAndStoreLatestAsync();
    await institutionalTradeService.FetchAndStoreLatestAsync();
    logger.LogInformation("Service execution completed successfully.");
}
catch (Exception ex)
{
    logger.LogError(ex, "Unhandled exception while executing services.");
}
finally
{
    logger.LogInformation("Application finished.");
}
