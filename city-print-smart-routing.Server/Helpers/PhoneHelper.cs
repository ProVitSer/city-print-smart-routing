namespace CityPrintSmartRouting.Helpers;

public static class PhoneHelper
{
    /// <summary>
    /// Приводит номер телефона к формату 7XXXXXXXXXX (11 цифр, начиная с 7).
    ///   +79161234567  → 79161234567
    ///    89161234567  → 79161234567
    ///    79161234567  → 79161234567
    ///     9161234567  → 79161234567
    /// Если нормализовать не удалось — возвращает исходную строку без изменений.
    /// </summary>
    public static string Normalize(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return phone ?? string.Empty;

        // Оставить только цифры
        var digits = new string(phone.Where(char.IsDigit).ToArray());

        return digits.Length switch
        {
            11 when digits.StartsWith('8') => "7" + digits[1..],
            11 when digits.StartsWith('7') => digits,
            10 when digits.StartsWith('9') => "7" + digits,
            _ => phone.Trim() // не удалось нормализовать — вернуть как есть
        };
    }
}
