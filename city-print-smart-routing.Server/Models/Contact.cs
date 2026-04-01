using System.ComponentModel.DataAnnotations;

namespace CityPrintSmartRouting.Models;

public class Contact
{
    public int Id { get; set; }

    /// <summary>ID контакта в 1С (справочная информация)</summary>
    [MaxLength(100)]
    public string ContactID { get; set; } = string.Empty;

    [MaxLength(300)]
    public string ContactName { get; set; } = string.Empty;

    /// <summary>Внутренний номер (добавочный) менеджера в 3CX</summary>
    [MaxLength(50)]
    public string ManagerLocPhone { get; set; } = string.Empty;

    [MaxLength(200)]
    public string ManagerName { get; set; } = string.Empty;

    /// <summary>Телефон клиента — основной ключ дедубликации</summary>
    [MaxLength(50)]
    public string ClientPhone { get; set; } = string.Empty;

    [MaxLength(300)]
    public string ClientName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
