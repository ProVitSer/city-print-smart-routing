using CityPrintSmartRouting.Models;
using TCX.Configuration;

namespace CityPrintSmartRouting.Services;

/// <summary>
/// Управляет контактами телефонной книги 3CX через 3cxpscomcpp2.
/// Использует уже установленное подключение из IPbxConnectionProvider (CallRoutingService),
/// чтобы не вызывать PhoneSystem.Reset() повторно.
///
/// Контакты помечаются тегом "CPSR" — сервис управляет только своими записями.
/// </summary>
public class PbxPhonebookService(
    IPbxConnectionProvider connection,
    ILogger<PbxPhonebookService> logger) : IPbxPhonebookService
{
    private const string ManagedTag = "CPSR";

    private PhoneSystem PhoneSystem => connection.PhoneSystem
        ?? throw new InvalidOperationException(
            "Подключение к 3CX не установлено. Дождитесь запуска CallRoutingService.");

    // ─── Получить наши контакты ───────────────────────────────────────────────

    public Task<HashSet<string>> GetManagedPhonesAsync(CancellationToken ct = default)
    {
        if (!connection.IsConnected)
            throw new InvalidOperationException("3CX не подключена");

        var entries = PhoneSystem.GetTenant().PhoneBookEntries;

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
        if (!connection.IsConnected)
            throw new InvalidOperationException("3CX не подключена");

        var parts     = contact.ContactName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var lastName  = $"{contact.ClientName} {(parts.Length > 0 ? parts[0] : contact.ContactName)}".Trim();
        var firstName = parts.Length > 1 ? parts[1] : string.Empty;

        var entry = PhoneSystem.GetTenant().CreatePhoneBookEntry();
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
        if (!connection.IsConnected)
            throw new InvalidOperationException("3CX не подключена");

        var toDelete = PhoneSystem.GetTenant().PhoneBookEntries
            .Where(e => e.Tag == ManagedTag &&
                        string.Equals(e.PhoneNumber?.Trim(), clientPhone.Trim(),
                            StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (toDelete.Count == 0)
        {
            logger.LogWarning("Контакт с телефоном {Phone} (тег {Tag}) не найден в 3CX",
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

    // ─── Удалить все контакты из телефонной книги ────────────────────────────

    public Task<int> DeleteAllManagedAsync(CancellationToken ct = default)
    {
        if (!connection.IsConnected)
            throw new InvalidOperationException("3CX не подключена");

        var toDelete = PhoneSystem.GetTenant().PhoneBookEntries.ToList();

        foreach (var entry in toDelete)
            entry.Delete();

        logger.LogInformation(
            "Удалено {Count} контактов из телефонной книги 3CX",
            toDelete.Count);

        return Task.FromResult(toDelete.Count);
    }
}
