namespace CityPrintSmartRouting.Configuration;

public class OneCSettingsOptions
{
    public const string SectionName = "OneCSettings";

    /// <summary>URL эндпоинта 1С, возвращает JSON-массив контактов</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>none | token | basic</summary>
    public string AuthType { get; set; } = "none";

    public string? Token { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}
