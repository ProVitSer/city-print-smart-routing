namespace CityPrintSmartRouting.Configuration;

public class RoutingSettingsOptions
{
    public const string SectionName = "RoutingSettings";

    /// <summary>
    /// Внутренние номера IVR, входящие на которые перехватываются для умной маршрутизации.
    /// </summary>
    public string[] IvrExtensions { get; set; } = [];

    /// <summary>
    /// Начало рабочего времени умной маршрутизации (например "10:00").
    /// Если не задано — маршрутизация работает круглосуточно.
    /// </summary>
    public string? WorkingHoursStart { get; set; }

    /// <summary>
    /// Конец рабочего времени умной маршрутизации (например "21:00").
    /// Если не задано — маршрутизация работает круглосуточно.
    /// </summary>
    public string? WorkingHoursEnd { get; set; }
}
