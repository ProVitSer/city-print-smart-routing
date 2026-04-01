namespace CityPrintSmartRouting.Services;

public interface IOneCService
{
    Task<List<OneCContactDto>> FetchContactsAsync(CancellationToken ct = default);
}

public class OneCContactDto
{
    public string ContactID { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string Deleted { get; set; } = "False";
    public string ManagerLocPhone { get; set; } = string.Empty;
    public string ManagerName { get; set; } = string.Empty;
    public string ClientPhone { get; set; } = string.Empty;
    public string ManagerID { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string ClientID { get; set; } = string.Empty;

    public bool IsDeleted => string.Equals(Deleted, "True", StringComparison.OrdinalIgnoreCase);

    public bool IsValid =>
        !IsDeleted &&
        !string.IsNullOrWhiteSpace(ClientPhone);
}
