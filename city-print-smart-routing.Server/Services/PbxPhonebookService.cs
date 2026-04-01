using CityPrintSmartRouting.Configuration;
using CityPrintSmartRouting.Models;
using Microsoft.Extensions.Options;
using TCX.Configuration;

namespace CityPrintSmartRouting.Services;

/// <summary>
/// Управляет контактами телефонной книги 3CX через 3cxpscomcpp2.
///
/// Все контакты, добавленные этим сервисом, помечаются тегом "CPSR"
/// (City Print Smart Routing) — это позволяет безопасно управлять только
/// своими записями, не затрагивая остальные контакты в телефонной книге.
///
/// Дедубликация происходит по номеру телефона (PhoneBookEntry.PhoneNumber).
/// </summary>
public class PbxPhonebookService(
    IOptions<PbxSettingsOptions> options,
    ILogger<PbxPhonebookService> logger) : IPbxPhonebookService, IDisposable
{
    private const string ManagedTag = "CPSR";

    private readonly PbxSettingsOptions _settings = options.Value;
    private PhoneSystem? _phoneSystem;
    private bool _connected;

    // ─── Подключение ─────────────────────────────────────────────────────────

    private void EnsureConnected()
    {
        if (_connected && _phoneSystem != null)
            return;

        logger.LogInformation("Загрузка tcxpscom_native.dll из AppPath={AppPath}", _settings.AppPath);
        LoadNativeDll(_settings.AppPath);

        logger.LogInformation(
            "Подключение к 3CX: {Host}:{Port} как {User}",
            _settings.CfgServerHost, _settings.CfgServerPort, _settings.CfgServerUser);

        _phoneSystem = PhoneSystem.Reset(
            ApplicationName: "CityPrintSmartRouting",
            CfgServerHost: _settings.CfgServerHost,
            CfgServerPort: _settings.CfgServerPort,
            CfgServerUser: _settings.CfgServerUser,
            CfgServerPassword: _settings.CfgServerPassword,
            inserted: null,
            updated: null,
            deleted: null);

        _connected = _phoneSystem.WaitForConnect(TimeSpan.FromSeconds(30));

        if (_connected)
            logger.LogInformation("Подключение к 3CX установлено");
        else
            throw new InvalidOperationException(
                "3CX не ответила в течение 30 секунд. Проверьте настройки PbxSettings в appsettings.json.");
    }

    private void LoadNativeDll(string? appPath)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(appPath))
            candidates.Add(Path.Combine(appPath.TrimEnd('\\', '/'), "tcxpscom_native.dll"));

        candidates.Add(@"C:\Program Files\3CX Phone System\Bin\tcxpscom_native.dll");
        candidates.Add(@"C:\Program Files\3CX Phone System\tcxpscom_native.dll");
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "tcxpscom_native.dll"));

        var errors = new List<string>();

        foreach (var path in candidates.Where(File.Exists))
        {
            try
            {
                System.Runtime.InteropServices.NativeLibrary.Load(path);
                logger.LogInformation("tcxpscom_native.dll загружена из {Path}", path);
                return;
            }
            catch (Exception ex)
            {
                errors.Add($"{path}: {ex.Message}");
            }
        }

        var notFound = candidates.Where(c => !File.Exists(c)).ToList();
        logger.LogError(
            "tcxpscom_native.dll не удалось загрузить. Не найдены: [{NotFound}]. Ошибки: [{Errors}]",
            string.Join(", ", notFound),
            string.Join(", ", errors));
    }

    // ─── Получить наши контакты ───────────────────────────────────────────────

    public Task<HashSet<string>> GetManagedPhonesAsync(CancellationToken ct = default)
    {
        EnsureConnected();

        var tenant = _phoneSystem!.GetTenant();
        var entries = tenant.PhoneBookEntries;

        var phones = entries
            .Where(e => e.Tag == ManagedTag && !string.IsNullOrWhiteSpace(e.PhoneNumber))
            .Select(e => e.PhoneNumber.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        logger.LogDebug("В телефонной книге 3CX найдено {Count} контактов с тегом {Tag}",
            phones.Count, ManagedTag);

        return Task.FromResult(phones);
    }

    // ─── Добавить контакт ─────────────────────────────────────────────────────

    public Task AddContactAsync(Contact contact, CancellationToken ct = default)
    {
        EnsureConnected();

        var tenant = _phoneSystem!.GetTenant();

        // Русский формат ФИО: Фамилия Имя Отчество
        var nameParts = contact.ContactName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var lastName  = nameParts.Length > 0 ? nameParts[0] : contact.ContactName;
        var firstName = nameParts.Length > 1 ? nameParts[1] : string.Empty;

        var entry = tenant.CreatePhoneBookEntry();
        entry.LastName    = lastName;
        entry.FirstName   = firstName;
        entry.PhoneNumber = contact.ClientPhone;
        entry.CompanyName = contact.ClientName;
        entry.Tag         = ManagedTag;
        entry.Save();

        logger.LogInformation(
            "Добавлен контакт в телефонную книгу 3CX: {LastName} {FirstName} ({Phone}), компания: {Company}",
            lastName, firstName, contact.ClientPhone, contact.ClientName);

        return Task.CompletedTask;
    }

    // ─── Удалить контакт ──────────────────────────────────────────────────────

    public Task DeleteContactByPhoneAsync(string clientPhone, CancellationToken ct = default)
    {
        EnsureConnected();

        var tenant = _phoneSystem!.GetTenant();
        var entries = tenant.PhoneBookEntries;

        var toDelete = entries
            .Where(e => e.Tag == ManagedTag &&
                        string.Equals(e.PhoneNumber?.Trim(), clientPhone.Trim(),
                            StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (toDelete.Count == 0)
        {
            logger.LogWarning("Контакт с телефоном {Phone} и тегом {Tag} не найден в 3CX",
                clientPhone, ManagedTag);
            return Task.CompletedTask;
        }

        foreach (var entry in toDelete)
        {
            entry.Delete();
            logger.LogInformation(
                "Удалён контакт из телефонной книги 3CX: {LastName} {FirstName} ({Phone})",
                entry.LastName, entry.FirstName, entry.PhoneNumber);
        }

        return Task.CompletedTask;
    }

    // ─── Сброс подключения при ошибке ────────────────────────────────────────

    public void ResetConnection()
    {
        _connected = false;
        try { PhoneSystem.Shutdown(); } catch { /* ignore */ }
        _phoneSystem = null;
        logger.LogInformation("Подключение к 3CX сброшено");
    }

    public void Dispose()
    {
        try { PhoneSystem.Shutdown(); } catch { /* ignore */ }
    }
}
