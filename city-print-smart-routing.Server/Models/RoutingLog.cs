namespace CityPrintSmartRouting.Models;

public class RoutingLog
{
    public int Id { get; set; }

    /// <summary>Нормализованный номер входящего звонка (7XXXXXXXXXX)</summary>
    public string CallerPhone { get; set; } = string.Empty;

    /// <summary>Внутренний номер менеджера, на который перевели вызов. Null = маршрут не найден.</summary>
    public string? ManagerLocPhone { get; set; }

    public string? ManagerName { get; set; }
    public string? ClientName { get; set; }

    /// <summary>Routed | NotFound | Error</summary>
    public string Result { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public DateTime CallTime { get; set; } = DateTime.UtcNow;
}
