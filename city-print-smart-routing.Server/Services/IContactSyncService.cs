namespace CityPrintSmartRouting.Services;

public interface IContactSyncService
{
    /// <summary>
    /// Шаг 1: Получить контакты из 1С и синхронизировать с локальной БД.
    /// Добавляет новые, удаляет устаревшие.
    /// </summary>
    Task<SyncResult> FetchFromOneCAsync(CancellationToken ct = default);

    /// <summary>
    /// Шаг 2: Синхронизировать локальную БД с телефонной книгой 3CX.
    /// Добавляет новые контакты, удаляет удалённые.
    /// </summary>
    Task<SyncResult> SyncToPhoneBookAsync(CancellationToken ct = default);
}

public record SyncResult(bool Success, int Added, int Deleted, int Unchanged, string Message);
