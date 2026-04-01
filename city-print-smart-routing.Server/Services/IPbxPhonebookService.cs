using CityPrintSmartRouting.Models;

namespace CityPrintSmartRouting.Services;

public interface IPbxPhonebookService
{
    /// <summary>
    /// Получить все контакты в телефонной книге 3CX, добавленные этим сервисом (Tag = "CPSR").
    /// Возвращает Set телефонных номеров (ClientPhone).
    /// </summary>
    Task<HashSet<string>> GetManagedPhonesAsync(CancellationToken ct = default);

    /// <summary>Добавить контакт в телефонную книгу 3CX.</summary>
    Task AddContactAsync(Contact contact, CancellationToken ct = default);

    /// <summary>Удалить контакт из телефонной книги 3CX по номеру телефона.</summary>
    Task DeleteContactByPhoneAsync(string clientPhone, CancellationToken ct = default);
}
