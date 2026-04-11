using System.Runtime.InteropServices;
using CityPrintSmartRouting.Configuration;
using CityPrintSmartRouting.Data;
using CityPrintSmartRouting.Helpers;
using CityPrintSmartRouting.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TCX.Configuration;
using TCX.PBXAPI;

namespace CityPrintSmartRouting.Services;

/// <summary>
/// Фоновая служба умной маршрутизации.
/// Подключается к 3CX, слушает входящие вызовы на IVR-номерах,
/// ищет клиента в локальной БД по последним 10 цифрам номера
/// и переводит вызов на добавочный менеджера (ManagerLocPhone).
///
/// Также реализует IPbxConnectionProvider — предоставляет единственный
/// экземпляр PhoneSystem для PbxPhonebookService.
/// </summary>
public class CallRoutingService : BackgroundService, IPbxConnectionProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PbxSettingsOptions _pbxSettings;
    private readonly RoutingSettingsOptions _routingSettings;
    private readonly ILogger<CallRoutingService> _logger;

    // IPbxConnectionProvider
    public bool IsConnected { get; private set; }
    public PhoneSystem? PhoneSystem { get; private set; }

    // CallID-ы, для которых маршрутизация уже запущена (защита от двойного срабатывания)
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> _routingInProgress = new();

    private CancellationToken _stoppingToken;

    public CallRoutingService(
        IServiceScopeFactory scopeFactory,
        IOptions<PbxSettingsOptions> pbxOptions,
        IOptions<RoutingSettingsOptions> routingOptions,
        ILogger<CallRoutingService> logger)
    {
        _scopeFactory    = scopeFactory;
        _pbxSettings     = pbxOptions.Value;
        _routingSettings = routingOptions.Value;
        _logger          = logger;
    }

    // ─── Жизненный цикл ──────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;

        _logger.LogInformation(
            "CallRoutingService запущен. IVR-номера для перехвата: [{IvrExtensions}]",
            string.Join(", ", _routingSettings.IvrExtensions));

        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        ConnectToPbx();

        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { /* нормальное завершение */ }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("CallRoutingService останавливается");
        if (IsConnected)
        {
            try { TCX.Configuration.PhoneSystem.Shutdown(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Ошибка при отключении от 3CX"); }
        }
        await base.StopAsync(cancellationToken);
    }

    // ─── Подключение к 3CX ───────────────────────────────────────────────────

    private void ConnectToPbx()
    {
        _logger.LogInformation("Загрузка tcxpscom_native.dll из AppPath={AppPath}", _pbxSettings.AppPath);
        LoadNativeDll(_pbxSettings.AppPath);

        _logger.LogInformation(
            "Подключение к 3CX: {Host}:{Port} как {User}",
            _pbxSettings.CfgServerHost, _pbxSettings.CfgServerPort, _pbxSettings.CfgServerUser);

        try
        {
            PhoneSystem = TCX.Configuration.PhoneSystem.Reset(
                ApplicationName: "CityPrintSmartRouting",
                CfgServerHost: _pbxSettings.CfgServerHost,
                CfgServerPort: _pbxSettings.CfgServerPort,
                CfgServerUser: _pbxSettings.CfgServerUser,
                CfgServerPassword: _pbxSettings.CfgServerPassword,
                inserted: OnPbxInserted,
                updated: OnPbxUpdated,
                deleted: OnPbxDeleted);

            IsConnected = PhoneSystem.WaitForConnect(TimeSpan.FromSeconds(30));

            if (IsConnected)
                _logger.LogInformation("Подключение к 3CX установлено");
            else
                _logger.LogWarning("3CX не ответила в течение 30 сек — маршрутизация неактивна");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при подключении к 3CX");
        }
    }

    private void LoadNativeDll(string? appPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            _logger.LogInformation("Linux: нативная библиотека 3CX не требуется, подключение через TCP");
            return;
        }

        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(appPath))
            candidates.Add(Path.Combine(appPath.TrimEnd('\\', '/'), "tcxpscom_native.dll"));

        candidates.Add(@"C:\Program Files\3CX Phone System\Bin\tcxpscom_native.dll");
        candidates.Add(@"C:\Program Files\3CX Phone System\tcxpscom_native.dll");
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "tcxpscom_native.dll"));

        foreach (var path in candidates.Where(File.Exists))
        {
            try
            {
                NativeLibrary.Load(path);
                _logger.LogInformation("tcxpscom_native.dll загружена из {Path}", path);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Не удалось загрузить {Path}: {Error}", path, ex.Message);
            }
        }

        _logger.LogError("tcxpscom_native.dll не найдена ни по одному из путей: [{Paths}]",
            string.Join(", ", candidates));
    }

    // ─── Обработчики событий 3CX ─────────────────────────────────────────────

    private void OnPbxInserted(object sender, NotificationEventArgs e) { }

    private void OnPbxUpdated(object sender, NotificationEventArgs e)
    {
        if (e.ConfObject is not ActiveConnection ac) return;

        // Интересуют только входящие вызовы на наших IVR-номерах
        if (!ac.IsInbound || string.IsNullOrEmpty(ac.ExternalParty))
            return;

        var dnNumber = ac.DN?.Number;
        if (!_routingSettings.IvrExtensions.Any(ext =>
                string.Equals(dnNumber, ext, StringComparison.OrdinalIgnoreCase)))
            return;

        // Ждём статуса Connected + ivr_ready=1
        if (ac.Status.ToString() != "Connected")
            return;

        string? ivrReady = null;
        ac.AttachedData?.TryGetValue("public_ivr_ready", out ivrReady);
        if (ivrReady != "1")
            return;

        // Запускаем маршрутизацию только один раз на CallID
        if (!_routingInProgress.TryAdd(ac.CallID, true))
            return;

        _logger.LogInformation(
            "Входящий вызов от {Caller} на IVR {Dn} — запуск маршрутизации",
            ac.ExternalParty, dnNumber);

        _ = RouteCallAsync(ac, ac.ExternalParty);
    }

    private void OnPbxDeleted(object sender, NotificationEventArgs e) { }

    // ─── Маршрутизация ────────────────────────────────────────────────────────

    /// <summary>
    /// Проверяет, попадает ли текущее местное время и день недели в рабочий диапазон маршрутизации.
    /// Если WorkingHoursStart/End не заданы — время не проверяется.
    /// Если WorkingDays пустой — дни не проверяются.
    /// </summary>
    private bool IsWithinWorkingHours()
    {
        var now = DateTime.Now;

        // Проверка рабочего дня
        if (_routingSettings.WorkingDays.Length > 0 &&
            !_routingSettings.WorkingDays.Contains(now.DayOfWeek))
            return false;

        if (string.IsNullOrWhiteSpace(_routingSettings.WorkingHoursStart) ||
            string.IsNullOrWhiteSpace(_routingSettings.WorkingHoursEnd))
            return true;

        if (!TimeOnly.TryParse(_routingSettings.WorkingHoursStart, out var start) ||
            !TimeOnly.TryParse(_routingSettings.WorkingHoursEnd, out var end))
        {
            _logger.LogWarning(
                "Некорректный формат рабочего времени: Start={Start}, End={End}. Маршрутизация работает круглосуточно.",
                _routingSettings.WorkingHoursStart, _routingSettings.WorkingHoursEnd);
            return true;
        }

        var time = TimeOnly.FromDateTime(now);

        // Поддержка диапазонов через полночь (например 22:00–06:00)
        return start <= end
            ? time >= start && time < end
            : time >= start || time < end;
    }

    private async Task RouteCallAsync(ActiveConnection ac, string callerRaw)
    {
        var callerNormalized = PhoneHelper.Normalize(callerRaw);
        var last10 = callerNormalized.Length >= 10
            ? callerNormalized[^10..]
            : callerNormalized;

        // Проверка рабочего времени и дней
        if (!IsWithinWorkingHours())
        {
            _logger.LogInformation(
                "Вызов от {Caller} вне рабочего расписания (дни: {Days}, время: {Start}–{End}) — стандартная маршрутизация",
                callerRaw,
                string.Join(", ", _routingSettings.WorkingDays),
                _routingSettings.WorkingHoursStart,
                _routingSettings.WorkingHoursEnd);
            await SaveRoutingLogAsync(callerNormalized, null, "OutOfHours", null);
            return;
        }

        _logger.LogInformation(
            "Поиск контакта по последним 10 цифрам: {Last10} (исходный: {Raw})",
            last10, callerRaw);

        Contact? contact = null;
        string result = "NotFound";
        string? errorMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            contact = await db.Contacts
                .FirstOrDefaultAsync(
                    c => EF.Functions.Like(c.ClientPhone, "%" + last10),
                    _stoppingToken);

            if (contact == null)
            {
                _logger.LogInformation(
                    "Контакт не найден для {Last10} — стандартная маршрутизация",
                    last10);
            }
            else
            {
                _logger.LogInformation(
                    "Найден контакт: {ContactName} ({ClientName}) → {ManagerLocPhone} ({ManagerName})",
                    contact.ContactName, contact.ClientName,
                    contact.ManagerLocPhone, contact.ManagerName);

                var routeResult = await ac.ReplaceWithAsync(
                    number: contact.ManagerLocPhone,
                    divertreason: CallControlAPI.DivertReason.BasedOnCallerID);

                _logger.LogInformation(
                    "Вызов переведён на {Ext}: Status={Status}",
                    contact.ManagerLocPhone, routeResult.FinalStatus);

                result = "Routed";
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            result = "Error";
            errorMessage = ex.Message;
            _logger.LogError(ex, "Ошибка при маршрутизации вызова от {Caller}", callerRaw);
        }

        await SaveRoutingLogAsync(callerNormalized, contact, result, errorMessage);
    }

    private async Task SaveRoutingLogAsync(
        string callerPhone, Contact? contact, string result, string? errorMessage)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            db.RoutingLogs.Add(new RoutingLog
            {
                CallerPhone     = callerPhone,
                ManagerLocPhone = contact?.ManagerLocPhone,
                ManagerName     = contact?.ManagerName,
                ClientName      = contact?.ClientName,
                Result          = result,
                ErrorMessage    = errorMessage,
                CallTime        = DateTime.UtcNow
            });

            await db.SaveChangesAsync(_stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при сохранении лога маршрутизации");
        }
    }
}
