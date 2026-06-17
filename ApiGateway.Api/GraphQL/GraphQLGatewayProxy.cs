using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ApiGateway.Api.GraphQL;

public class GraphQLGatewayProxy
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GraphQLGatewayProxy(IHttpClientFactory httpClientFactory, IHttpContextAccessor httpContextAccessor)
    {
        _httpClientFactory = httpClientFactory;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<object?> GetAsync(string path, IReadOnlyDictionary<string, object?>? query, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(path, query));
        CopyAuthorization(request);

        using var response = await _httpClientFactory.CreateClient("proxy")
            .SendAsync(request, cancellationToken);

        return await ReadJsonAsync(response, cancellationToken);
    }

    public async Task<object?> PostAsync(string path, JsonElement input, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(path, null));
        CopyAuthorization(request);
        request.Content = new StringContent(input.GetRawText(), Encoding.UTF8, "application/json");

        using var response = await _httpClientFactory.CreateClient("proxy")
            .SendAsync(request, cancellationToken);

        return await ReadJsonAsync(response, cancellationToken);
    }

    public async Task<object?> PatchAsync(string path, JsonElement input, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, BuildUri(path, null));
        CopyAuthorization(request);
        request.Content = new StringContent(input.GetRawText(), Encoding.UTF8, "application/json");

        using var response = await _httpClientFactory.CreateClient("proxy")
            .SendAsync(request, cancellationToken);

        return await ReadJsonAsync(response, cancellationToken);
    }

    private Uri BuildUri(string path, IReadOnlyDictionary<string, object?>? query)
    {
        var context = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No hay HttpContext disponible para GraphQL.");

        var scheme = context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault()
            ?? context.Request.Scheme;
        var baseUri = $"{scheme}://{context.Request.Host}";
        var uri = $"{baseUri}{path}";

        if (query is null || query.Count == 0)
            return new Uri(uri);

        var queryParts = query
            .Where(x => x.Value is not null)
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(Convert.ToString(x.Value) ?? string.Empty)}");

        return new Uri($"{uri}?{string.Join('&', queryParts)}");
    }

    private void CopyAuthorization(HttpRequestMessage request)
    {
        var authorization = _httpContextAccessor.HttpContext?.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(authorization))
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
    }

    private static async Task<object?> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
            raw = "{}";

        using var document = JsonDocument.Parse(raw);
        if (response.IsSuccessStatusCode)
            return ToGraphQlValue(document.RootElement);

        return new Dictionary<string, object?>
        {
            ["status"] = (int)response.StatusCode,
            ["message"] = "Error devuelto por el API Gateway REST.",
            ["data"] = ToGraphQlValue(document.RootElement)
        };
    }

    private static object? ToGraphQlValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(property => property.Name, property => ToGraphQlValue(property.Value)),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(ToGraphQlValue)
                .ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => element.ToString()
        };
    }
}
