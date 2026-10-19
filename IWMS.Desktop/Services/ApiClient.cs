using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace IWMS.Desktop.Services;

public sealed class ApiClient
{
    private HttpClient _http = new() { BaseAddress = new Uri("https://iwms-api-si63.onrender.com/") };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string BaseUrl { get; private set; } = "https://iwms-api-si63.onrender.com/";
    public bool IsSignedIn => _http.DefaultRequestHeaders.Authorization is not null;
    public string? CurrentUserName { get; private set; }
    public IReadOnlyList<string> CurrentUserRoles { get; private set; } = Array.Empty<string>();
    public bool IsInRole(string role) => CurrentUserRoles.Contains(role, StringComparer.OrdinalIgnoreCase);

    public void SetBaseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new ArgumentException("Enter a valid API URL, including https:// or http://.");

        var isLocalDevelopment = uri.IsLoopback ||
            string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
        if (uri.Scheme != Uri.UriSchemeHttps && !isLocalDevelopment)
            throw new ArgumentException("For security, remote API servers must use HTTPS. HTTP is allowed only for localhost development.");

        var normalizedUrl = uri.ToString().TrimEnd('/') + "/";
        if (_http.BaseAddress is null ||
            !string.Equals(_http.BaseAddress.AbsoluteUri, normalizedUrl, StringComparison.OrdinalIgnoreCase))
        {
            _http.Dispose();
            _http = new HttpClient { BaseAddress = new Uri(normalizedUrl) };
        }

        BaseUrl = normalizedUrl;
        SignOut();
    }

    public void SignOut()
    {
        _http.DefaultRequestHeaders.Authorization = null;
        CurrentUserName = null;
        CurrentUserRoles = Array.Empty<string>();
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
        CurrentUserRoles = user.ValueKind == JsonValueKind.Object &&
                           user.TryGetProperty("roles", out var roles) &&
                           roles.ValueKind == JsonValueKind.Array
            ? roles.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.String)
                .Select(r => r.GetString()!).Where(r => !string.IsNullOrWhiteSpace(r)).ToArray()
            : Array.Empty<string>();
    }

    public async Task<T> GetAsync<T>(string path, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(path, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Request failed ({(int)response.StatusCode}) for {path}.");
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
            ?? throw new InvalidOperationException("The API returned an empty response.");
    }

    public async Task PostJsonAsync(string path, object payload, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync(path, payload, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Request failed ({(int)response.StatusCode}): {detail}");
        }
    }

    public async Task PutJsonAsync(string path, object payload, CancellationToken ct = default)
    {
        using var response = await _http.PutAsJsonAsync(path, payload, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Request failed ({(int)response.StatusCode}): {detail}");
        }
    }
    public async Task<JsonElement> GetJsonAsync(string path, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(path, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Request failed ({(int)response.StatusCode}) for {path}.");
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
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
