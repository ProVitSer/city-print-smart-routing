using CityPrintSmartRouting.Data;
using CityPrintSmartRouting.Helpers;
using CityPrintSmartRouting.Models;
using Microsoft.EntityFrameworkCore;

namespace CityPrintSmartRouting.Services;

public class ContactSyncService(
    ApplicationDbContext db,
    IOneCService oneCService,
    IPbxPhonebookService pbxService,
    ILogger<ContactSyncService> logger) : IContactSyncService
{
    // ─── Шаг 1: 1С → локальная БД ────────────────────────────────────────────

    public async Task<SyncResult> FetchFromOneCAsync(CancellationToken ct = default)
    {
        var log = new SyncLog { SyncType = "OneCFetch", StartTime = DateTime.UtcNow };
        db.SyncLogs.Add(log);
        await db.SaveChangesAsync(ct);

        try
        {
            logger.LogInformation("=== Начало выгрузки контактов из 1С ===");

            var oneCContacts = await oneCService.FetchContactsAsync(ct);

            // Нормализовать телефоны, оставить только валидные,
            // дедублицировать по нормализованному ClientPhone
            var validByPhone = oneCContacts
                .Where(c => c.IsValid)
                .Select(c => (dto: c, phone: PhoneHelper.Normalize(c.ClientPhone)))
                .GroupBy(x => x.phone, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().dto, StringComparer.OrdinalIgnoreCase);

            logger.LogInformation(
                "Из 1С получено {Total} записей, валидных (уникальных по телефону): {Valid}",
                oneCContacts.Count, validByPhone.Count);

            var existing = await db.Contacts.ToListAsync(ct);
            var existingByPhone = existing.ToDictionary(
                c => c.ClientPhone, StringComparer.OrdinalIgnoreCase);

            int added = 0, deleted = 0, unchanged = 0;

            // Добавить новые (есть в 1С, нет в БД)
            foreach (var (phone, dto) in validByPhone)
            {
                if (existingByPhone.ContainsKey(phone))
                {
                    unchanged++;
                    continue;
                }

                db.Contacts.Add(new Contact
                {
                    ContactID       = dto.ContactID,
                    ContactName     = dto.ContactName,
                    ManagerLocPhone = dto.ManagerLocPhone,
                    ManagerName     = dto.ManagerName,
                    ClientPhone     = phone,           // сохраняем нормализованный номер
                    ClientName      = dto.ClientName,
                    CreatedAt       = DateTime.UtcNow,
                    UpdatedAt       = DateTime.UtcNow
                });
                added++;
                logger.LogDebug("+ Добавлен: {Name} ({Phone})", dto.ContactName, phone);
            }

            // Удалить устаревшие (есть в БД, нет в ответе 1С)
            foreach (var contact in existing)
            {
                if (!validByPhone.ContainsKey(contact.ClientPhone))
                {
                    db.Contacts.Remove(contact);
                    deleted++;
                    logger.LogDebug("- Удалён: {Name} ({Phone})", contact.ContactName, contact.ClientPhone);
                }
            }

            await db.SaveChangesAsync(ct);

            var msg = $"Добавлено: {added}, удалено: {deleted}, без изменений: {unchanged}";
            logger.LogInformation("=== Выгрузка из 1С завершена. {Message} ===", msg);

            log.EndTime  = DateTime.UtcNow;
            log.Status   = "Success";
            log.Added    = added;
            log.Deleted  = deleted;
            log.Unchanged = unchanged;
            log.Message  = msg;
            await db.SaveChangesAsync(ct);

            return new SyncResult(true, added, deleted, unchanged, msg);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при выгрузке контактов из 1С");
            log.EndTime = DateTime.UtcNow;
            log.Status  = "Error";
            log.Message = ex.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            return new SyncResult(false, 0, 0, 0, ex.Message);
        }
    }

    // ─── Шаг 2: Локальная БД → телефонная книга 3CX ──────────────────────────

    public async Task<SyncResult> SyncToPhoneBookAsync(CancellationToken ct = default)
    {
        var log = new SyncLog { SyncType = "PhoneBook", StartTime = DateTime.UtcNow };
        db.SyncLogs.Add(log);
        await db.SaveChangesAsync(ct);

        try
        {
            logger.LogInformation("=== Начало синхронизации телефонной книги 3CX ===");

            var localContacts = await db.Contacts.ToListAsync(ct);
            var localPhones   = localContacts
                .Select(c => c.ClientPhone)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Контакты с тегом CPSR в телефонной книге 3CX
            var phonebookPhones = await pbxService.GetManagedPhonesAsync(ct);

            int added = 0, deleted = 0, unchanged = 0;

            // Добавить в 3CX то, чего ещё нет
            foreach (var contact in localContacts)
            {
                if (phonebookPhones.Contains(contact.ClientPhone))
                {
                    unchanged++;
                    continue;
                }

                await pbxService.AddContactAsync(contact, ct);
                added++;
            }

            // Удалить из 3CX то, чего нет в локальной БД
            foreach (var phone in phonebookPhones)
            {
                if (!localPhones.Contains(phone))
                {
                    await pbxService.DeleteContactByPhoneAsync(phone, ct);
                    deleted++;
                }
            }

            var msg = $"Добавлено: {added}, удалено: {deleted}, без изменений: {unchanged}";
            logger.LogInformation("=== Синхронизация телефонной книги завершена. {Message} ===", msg);

            log.EndTime   = DateTime.UtcNow;
            log.Status    = "Success";
            log.Added     = added;
            log.Deleted   = deleted;
            log.Unchanged = unchanged;
            log.Message   = msg;
            await db.SaveChangesAsync(ct);

            return new SyncResult(true, added, deleted, unchanged, msg);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при синхронизации телефонной книги 3CX");

            if (pbxService is PbxPhonebookService svc)
                svc.ResetConnection();

            log.EndTime = DateTime.UtcNow;
            log.Status  = "Error";
            log.Message = ex.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            return new SyncResult(false, 0, 0, 0, ex.Message);
        }
    }
}
