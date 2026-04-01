namespace CityPrintSmartRouting.Configuration;

public class SyncSettingsOptions
{
    public const string SectionName = "SyncSettings";

    /// <summary>Как часто (в минутах) получать данные из 1С</summary>
    public int OneCFetchIntervalMinutes { get; set; } = 60;

    /// <summary>В какие часы выполнять синхронизацию телефонной книги 3CX (0–23)</summary>
    public int[] PhoneBookSyncHours { get; set; } = [10];

    /// <summary>Выполнить полную синхронизацию сразу при запуске</summary>
    public bool RunOnStartup { get; set; } = true;
}
