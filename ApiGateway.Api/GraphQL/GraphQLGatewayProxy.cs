using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ApiGateway.Api.GraphQL;

public class GraphQLGatewayProxy
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GraphQLGatewayProxy(IHttpClientFactory httpClientFactory, IHttpContextAccessor httpContextAccessor)
    {
        _httpClientFactory = httpClientFactory;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<JsonElement> GetAsync(string path, IReadOnlyDictionary<string, object?>? query, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(path, query));
        CopyAuthorization(request);

        using var response = await _httpClientFactory.CreateClient("proxy")
            .SendAsync(request, cancellationToken);

        return await ReadJsonAsync(response, cancellationToken);
    }

    public async Task<JsonElement> PostAsync(string path, JsonElement input, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(path, null));
        CopyAuthorization(request);
        request.Content = new StringContent(input.GetRawText(), Encoding.UTF8, "application/json");

        using var response = await _httpClientFactory.CreateClient("proxy")
            .SendAsync(request, cancellationToken);

        return await ReadJsonAsync(response, cancellationToken);
    }

    public async Task<JsonElement> PatchAsync(string path, JsonElement input, CancellationToken cancellationToken)
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

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
            raw = "{}";

        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement.Clone();
        if (response.IsSuccessStatusCode)
            return root;

        var error = new JsonObject
        {
            ["status"] = (int)response.StatusCode,
            ["message"] = "Error devuelto por el API Gateway REST.",
            ["data"] = JsonNode.Parse(raw)
        };

        using var errorDocument = JsonDocument.Parse(error.ToJsonString(JsonOptions));
        return errorDocument.RootElement.Clone();
    }
}
