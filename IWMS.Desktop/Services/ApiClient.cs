using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace IWMS.Desktop.Services;

public sealed class ApiClient
{
    private readonly HttpClient _http = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string BaseUrl { get; private set; } = "https://localhost:7001/";
    public bool IsSignedIn => _http.DefaultRequestHeaders.Authorization is not null;
    public string? CurrentUserName { get; private set; }

    public void SetBaseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new ArgumentException("Enter a valid API URL, including https:// or http://.");
        BaseUrl = uri.ToString().TrimEnd('/') + "/";
        _http.BaseAddress = new Uri(BaseUrl);
        _http.DefaultRequestHeaders.Authorization = null;
    }

    public async Task SignInAsync(string email, string password, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("api/auth/login",
            new { email, password }, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                ? "Email or password was not accepted."
                : $"Sign-in failed ({(int)response.StatusCode}). Check the API URL and server logs.");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = document.RootElement;
        var token = root.GetProperty("token").GetString();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("The API response did not contain an access token.");

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        CurrentUserName = root.TryGetProperty("user", out var user) &&
                          user.TryGetProperty("fullName", out var name)
            ? name.GetString()
            : email;
    }

    public async Task<T> GetAsync<T>(string path, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(path, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Request failed ({(int)response.StatusCode}) for {path}.");
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
            ?? throw new InvalidOperationException("The API returned an empty response.");
    }

    public async Task<byte[]> GetBytesAsync(string path, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(path, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Request failed ({(int)response.StatusCode}) for {path}.");
        return await response.Content.ReadAsByteArrayAsync(ct);
    }
}

public sealed record DashboardDto(
    decimal StockValue,
    int LowStockProducts,
    int OpenPurchaseOrders,
    int PendingApprovals,
    int OpenSalesOrders,
    decimal UnitsOnHand,
    decimal UnitsReserved);
