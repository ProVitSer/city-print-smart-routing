namespace CityPrintSmartRouting.Configuration;

public class PbxSettingsOptions
{
    public const string SectionName = "PbxSettings";

    public string CfgServerHost { get; set; } = "localhost";
    public int CfgServerPort { get; set; } = 5481;
    public string CfgServerUser { get; set; } = "admin";
    public string CfgServerPassword { get; set; } = string.Empty;

    /// <summary>
    /// Путь к директории установки 3CX (где лежат tcxpscom_native.dll и зависимости).
    /// Пример: C:\Program Files\3CX Phone System\Bin\
    /// </summary>
    public string AppPath { get; set; } = string.Empty;

}
