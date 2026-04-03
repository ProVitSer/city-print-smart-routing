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

            // Сначала получаем данные из 1С — если не удалось, БД не трогаем
            List<OneCContactDto> oneCContacts;
            try
            {
                oneCContacts = await oneCService.FetchContactsAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Не удалось получить данные из 1С — локальная БД не изменена");
                log.EndTime = DateTime.UtcNow;
                log.Status  = "Error";
                log.Message = $"Ошибка получения данных из 1С: {ex.Message}";
                await db.SaveChangesAsync(CancellationToken.None);
                return new SyncResult(false, 0, 0, 0, log.Message);
            }

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

            // Удаляем все существующие контакты из БД
            var existing = await db.Contacts.ToListAsync(ct);
            db.Contacts.RemoveRange(existing);

            // Добавляем все актуальные контакты из 1С заново
            foreach (var (phone, dto) in validByPhone)
            {
                db.Contacts.Add(new Contact
                {
                    ContactID       = dto.ContactID,
                    ContactName     = dto.ContactName,
                    ManagerLocPhone = dto.ManagerLocPhone,
                    ManagerName     = dto.ManagerName,
                    ClientPhone     = phone,
                    ClientName      = dto.ClientName,
                    CreatedAt       = DateTime.UtcNow,
                    UpdatedAt       = DateTime.UtcNow
                });
            }

            await db.SaveChangesAsync(ct);

            int added = validByPhone.Count;
            int deleted = existing.Count;
            var msg = $"Очищено: {deleted}, добавлено: {added}";
            logger.LogInformation("=== Выгрузка из 1С завершена. {Message} ===", msg);

            log.EndTime   = DateTime.UtcNow;
            log.Status    = "Success";
            log.Added     = added;
            log.Deleted   = deleted;
            log.Unchanged = 0;
            log.Message   = msg;
            await db.SaveChangesAsync(ct);

            return new SyncResult(true, added, deleted, 0, msg);
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

            // Удаляем все наши контакты из 3CX
            int deleted = await pbxService.DeleteAllManagedAsync(ct);

            // Добавляем все актуальные контакты из локальной БД заново
            var localContacts = await db.Contacts.ToListAsync(ct);
            foreach (var contact in localContacts)
                await pbxService.AddContactAsync(contact, ct);

            int added = localContacts.Count;
            var msg = $"Очищено: {deleted}, добавлено: {added}";
            logger.LogInformation("=== Синхронизация телефонной книги завершена. {Message} ===", msg);

            log.EndTime   = DateTime.UtcNow;
            log.Status    = "Success";
            log.Added     = added;
            log.Deleted   = deleted;
            log.Unchanged = 0;
            log.Message   = msg;
            await db.SaveChangesAsync(ct);

            return new SyncResult(true, added, deleted, 0, msg);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при синхронизации телефонной книги 3CX");


            log.EndTime = DateTime.UtcNow;
            log.Status  = "Error";
            log.Message = ex.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            return new SyncResult(false, 0, 0, 0, ex.Message);
        }
    }
}
