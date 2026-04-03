namespace CityPrintSmartRouting.Configuration;

public class SyncSettingsOptions
{
    public const string SectionName = "SyncSettings";

    /// <summary>В какие часы выполнять выгрузку из 1С (0–23)</summary>
    public int[] OneCFetchHours { get; set; } = [9, 21];

    /// <summary>В какие часы выполнять синхронизацию телефонной книги 3CX (0–23)</summary>
    public int[] PhoneBookSyncHours { get; set; } = [22];

    /// <summary>Выполнить полную синхронизацию сразу при запуске</summary>
    public bool RunOnStartup { get; set; } = true;
}
