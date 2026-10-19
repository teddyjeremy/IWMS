using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

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
            throw new InvalidOperationException(await ReadFailureAsync(response, path, ct));
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
            ?? throw new InvalidOperationException("The API returned an empty response.");
    }

    public async Task PostJsonAsync(string path, object payload, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync(path, payload, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadFailureAsync(response, path, ct));
    }

    public async Task PutJsonAsync(string path, object payload, CancellationToken ct = default)
    {
        using var response = await _http.PutAsJsonAsync(path, payload, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadFailureAsync(response, path, ct));
    }
    public async Task<JsonElement> GetJsonAsync(string path, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(path, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadFailureAsync(response, path, ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
    }

    public async Task<JsonElement> GetListJsonAsync(string path, CancellationToken ct = default)
    {
        var first = await GetJsonAsync(WithPage(path, 1, 100), ct);
        if (first.ValueKind != JsonValueKind.Object ||
            !first.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array)
            return first;

        var total = first.TryGetProperty("totalCount", out var totalElement) && totalElement.TryGetInt32(out var parsedTotal)
            ? parsedTotal
            : items.GetArrayLength();
        var all = new JsonArray();
        foreach (var item in items.EnumerateArray())
            all.Add(JsonNode.Parse(item.GetRawText()));

        var page = 2;
        while (all.Count < total && all.Count < 400 && page <= 8)
        {
            var next = await GetJsonAsync(WithPage(path, page, 100), ct);
            if (!next.TryGetProperty("items", out var nextItems) || nextItems.ValueKind != JsonValueKind.Array || nextItems.GetArrayLength() == 0)
                break;
            foreach (var item in nextItems.EnumerateArray())
                all.Add(JsonNode.Parse(item.GetRawText()));
            page++;
        }

        return JsonSerializer.SerializeToElement(new { items = all, totalCount = total, pageSize = all.Count }, JsonOptions);
    }

    private static string WithPage(string path, int page, int pageSize)
    {
        var split = path.Split('?', 2);
        var kept = split.Length == 2
            ? split[1].Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(part => !part.StartsWith("page=", StringComparison.OrdinalIgnoreCase) &&
                               !part.StartsWith("pageSize=", StringComparison.OrdinalIgnoreCase))
            : Array.Empty<string>();
        var query = string.Join("&", kept.Append($"page={page}").Append($"pageSize={pageSize}"));
        return split[0] + "?" + query;
    }

    public async Task<byte[]> GetBytesAsync(string path, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(path, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadFailureAsync(response, path, ct));
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task<string> PostForTextAsync(string path, object payload, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync(path, payload, JsonOptions, ct);
        var detail = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ExplainFailure((int)response.StatusCode, path, detail));
        return detail;
    }

    private static async Task<string> ReadFailureAsync(HttpResponseMessage response, string path, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        return ExplainFailure((int)response.StatusCode, path, body);
    }

    private static string ExplainFailure(int status, string path, string body)
    {
        var detail = string.Empty;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("detail", out var problem) && problem.ValueKind == JsonValueKind.String)
                        detail = problem.GetString() ?? string.Empty;
                    else if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                        detail = title.GetString() ?? string.Empty;
                    else if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                        detail = message.GetString() ?? string.Empty;
                }
                else if (root.ValueKind == JsonValueKind.String)
                    detail = root.GetString() ?? string.Empty;
            }
            catch (JsonException)
            {
                detail = body.Trim();
            }
        }

        if (status == 404)
            return string.IsNullOrWhiteSpace(detail)
                ? "That record or action was not found. Refresh the list and try the row again."
                : detail;
        if (status == 401)
            return "Your session expired. Sign in again.";
        if (status == 403)
            return "Your role is not allowed to do that.";
        if (string.IsNullOrWhiteSpace(detail))
            return $"The server rejected the request ({status}) for {path}.";
        return detail;
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
