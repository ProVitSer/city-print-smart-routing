using CityPrintSmartRouting.Configuration;
using CityPrintSmartRouting.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CityPrintSmartRouting.Services;

/// <summary>
/// Фоновая служба, которая по расписанию выполняет две задачи:
///   1. Каждые N минут (OneCFetchIntervalMinutes) — выгрузка из 1С в локальную БД.
///   2. В указанные часы (PhoneBookSyncHours) — синхронизация локальной БД с телефонной книгой 3CX.
/// </summary>
public class SyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<SyncSettingsOptions> options,
    ILogger<SyncBackgroundService> logger) : BackgroundService
{
    private readonly SyncSettingsOptions _settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var fetchHours = string.Join(", ", _settings.OneCFetchHours.Select(h => $"{h:D2}:00"));
        var syncHours  = string.Join(", ", _settings.PhoneBookSyncHours.Select(h => $"{h:D2}:00"));
        logger.LogInformation(
            "SyncBackgroundService запущен. Выгрузка из 1С в {FetchHours}. Синхронизация телефонной книги в {SyncHours}.",
            fetchHours, syncHours);

        if (_settings.RunOnStartup)
        {
            logger.LogInformation("RunOnStartup=true — начальная синхронизация через 5 сек...");
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            await RunOneCFetchAsync(stoppingToken);
            await RunPhoneBookSyncAsync(stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            if (stoppingToken.IsCancellationRequested) break;

            var now = DateTime.Now;

            if (await IsOneCFetchDueAsync(now, stoppingToken))
                await RunOneCFetchAsync(stoppingToken);

            if (await IsPhoneBookSyncDueAsync(now, stoppingToken))
                await RunPhoneBookSyncAsync(stoppingToken);
        }

        logger.LogInformation("SyncBackgroundService остановлен");
    }

    // ─── Проверки расписания ──────────────────────────────────────────────────

    private async Task<bool> IsOneCFetchDueAsync(DateTime now, CancellationToken ct)
    {
        if (!_settings.OneCFetchHours.Contains(now.Hour))
            return false;

        // Уже запускалась в этом часу?
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var windowStart = now.Date.AddHours(now.Hour).ToUniversalTime();
        var windowEnd   = windowStart.AddHours(1);

        return !await db.SyncLogs.AnyAsync(l =>
            l.SyncType == "OneCFetch" &&
            l.StartTime >= windowStart &&
            l.StartTime < windowEnd, ct);
    }

    private async Task<bool> IsPhoneBookSyncDueAsync(DateTime now, CancellationToken ct)
    {
        if (!_settings.PhoneBookSyncHours.Contains(now.Hour))
            return false;

        // Уже запускался в этом часу?
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var windowStart = now.Date.AddHours(now.Hour).ToUniversalTime();
        var windowEnd   = windowStart.AddHours(1);

        return !await db.SyncLogs.AnyAsync(l =>
            l.SyncType == "PhoneBook" &&
            l.StartTime >= windowStart &&
            l.StartTime < windowEnd, ct);
    }

    // ─── Выполнение задач ─────────────────────────────────────────────────────

    private async Task RunOneCFetchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sync = scope.ServiceProvider.GetRequiredService<IContactSyncService>();
        var result = await sync.FetchFromOneCAsync(ct);
        if (!result.Success)
            logger.LogWarning("Выгрузка из 1С завершилась с ошибкой: {Message}", result.Message);
    }

    private async Task RunPhoneBookSyncAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sync = scope.ServiceProvider.GetRequiredService<IContactSyncService>();
        var result = await sync.SyncToPhoneBookAsync(ct);
        if (!result.Success)
            logger.LogWarning("Синхронизация телефонной книги завершилась с ошибкой: {Message}", result.Message);
    }
}
