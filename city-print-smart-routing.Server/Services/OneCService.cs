using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CityPrintSmartRouting.Configuration;
using Microsoft.Extensions.Options;

namespace CityPrintSmartRouting.Services;

public class OneCService(
    IHttpClientFactory httpClientFactory,
    IOptions<OneCSettingsOptions> options,
    ILogger<OneCService> logger) : IOneCService
{
    private readonly OneCSettingsOptions _settings = options.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<List<OneCContactDto>> FetchContactsAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.Url))
            throw new InvalidOperationException("OneCSettings:Url не задан в appsettings.json");

        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        ApplyAuth(client);

        logger.LogInformation("Запрос контактов из 1С: {Url}", _settings.Url);

        var response = await client.GetAsync(_settings.Url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var contacts = JsonSerializer.Deserialize<List<OneCContactDto>>(json, JsonOptions) ?? [];

        logger.LogInformation("1С вернула {Total} записей", contacts.Count);
        return contacts;
    }

    private void ApplyAuth(HttpClient client)
    {
        switch (_settings.AuthType?.ToLower())
        {
            case "token" when !string.IsNullOrEmpty(_settings.Token):
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _settings.Token);
                break;
            case "basic" when !string.IsNullOrEmpty(_settings.Username):
                var credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{_settings.Username}:{_settings.Password}"));
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", credentials);
                break;
        }
    }
}
