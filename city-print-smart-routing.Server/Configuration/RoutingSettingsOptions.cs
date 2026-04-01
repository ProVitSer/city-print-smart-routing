namespace CityPrintSmartRouting.Configuration;

public class RoutingSettingsOptions
{
    public const string SectionName = "RoutingSettings";

    /// <summary>
    /// Внутренние номера IVR, входящие на которые перехватываются для умной маршрутизации.
    /// </summary>
    public string[] IvrExtensions { get; set; } = [];
}
