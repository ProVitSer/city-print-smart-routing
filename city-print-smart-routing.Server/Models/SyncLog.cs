namespace CityPrintSmartRouting.Models;

public class SyncLog
{
    public int Id { get; set; }

    /// <summary>OneCFetch или PhoneBook</summary>
    public string SyncType { get; set; } = string.Empty;

    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime? EndTime { get; set; }

    /// <summary>Success, Error, Running</summary>
    public string Status { get; set; } = "Running";

    public int Added { get; set; }
    public int Deleted { get; set; }
    public int Unchanged { get; set; }

    public string? Message { get; set; }
}
