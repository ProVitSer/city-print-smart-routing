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

    /// <summary>
    /// Рабочие дни недели. Если не задано или пустой список — маршрутизация работает каждый день.
    /// Допустимые значения: "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday".
    /// По умолчанию: с понедельника по пятницу.
    /// </summary>
    public DayOfWeek[] WorkingDays { get; set; } =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday
    ];
}
