using CityPrintSmartRouting.Configuration;
using CityPrintSmartRouting.Data;
using CityPrintSmartRouting.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Запуск city-print-smart-routing...");

    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSystemd();

    builder.Services.AddSerilog((ctx, config) =>
    {
        config
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                path: Path.Combine(AppContext.BaseDirectory, "logs", "log-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
    });

    // Конфигурация
    builder.Services.Configure<OneCSettingsOptions>(
        builder.Configuration.GetSection(OneCSettingsOptions.SectionName));
    builder.Services.Configure<PbxSettingsOptions>(
        builder.Configuration.GetSection(PbxSettingsOptions.SectionName));
    builder.Services.Configure<SyncSettingsOptions>(
        builder.Configuration.GetSection(SyncSettingsOptions.SectionName));
    builder.Services.Configure<RoutingSettingsOptions>(
        builder.Configuration.GetSection(RoutingSettingsOptions.SectionName));

    // База данных
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

    // HTTP клиент (для 1С)
    builder.Services.AddHttpClient();

    // Сервисы синхронизации
    builder.Services.AddScoped<IOneCService, OneCService>();
    builder.Services.AddScoped<IContactSyncService, ContactSyncService>();

    // CallRoutingService — singleton, управляет подключением к 3CX и маршрутизацией вызовов.
    // Регистрируем как singleton, чтобы PbxPhonebookService мог получить то же соединение.
    builder.Services.AddSingleton<CallRoutingService>();
    builder.Services.AddSingleton<IPbxConnectionProvider>(sp =>
        sp.GetRequiredService<CallRoutingService>());
    builder.Services.AddHostedService(sp =>
        sp.GetRequiredService<CallRoutingService>());

    // PbxPhonebookService использует соединение из CallRoutingService
    builder.Services.AddSingleton<IPbxPhonebookService, PbxPhonebookService>();

    // Фоновая служба синхронизации контактов (1С → БД → телефонная книга 3CX)
    builder.Services.AddHostedService<SyncBackgroundService>();

    var host = builder.Build();

    using (var scope = host.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.EnsureCreated();
        Log.Information("База данных инициализирована");
    }

    await host.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Приложение завершилось с ошибкой");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

return 0;
