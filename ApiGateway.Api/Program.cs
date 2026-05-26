using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ApiGateway.Api.Swagger;
using Atracciones.Grpc;
using Atracciones.Shared.Extensions;
using Grpc.Net.Client;
using System.IdentityModel.Tokens.Jwt;
using Swashbuckle.AspNetCore.SwaggerGen;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAtraccionesApiDefaults(builder.Configuration, "api-gateway");
builder.Services.Configure<SwaggerGenOptions>(options =>
{
    options.DocumentFilter<BookingPublicSwaggerDocumentFilter>();
});

builder.Services.AddHttpClient("proxy", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendCors", policy =>
    {
        policy
            .WithOrigins(
                "https://atracciones-front.onrender.com",
                "http://localhost:5173"
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseAtraccionesApiDefaults();
app.UseCors("FrontendCors");

app.MapGet("/health", (IConfiguration configuration) => Results.Ok(new
{
    service = "api-gateway",
    status = "running",
    auditLogging = configuration.GetValue("AuditLogging", true),
    downstream = new
    {
        identidad = GetConfiguredServiceUrl(configuration, "Identidad"),
        atracciones = GetConfiguredServiceUrl(configuration, "Atracciones"),
        reservas = GetConfiguredServiceUrl(configuration, "Reservas"),
        facturacion = GetConfiguredServiceUrl(configuration, "Facturacion"),
        auditoria = GetConfiguredServiceUrl(configuration, "Auditoria")
    }
}));

if (app.Configuration.GetValue("Gateway:UseGrpc", true))
{
    app.MapPost("/api/v1/auth/login", async (HttpContext context, IConfiguration configuration) =>
    {
        using var channel = CreateGrpcChannel(configuration, "Identidad");
        var client = new IdentidadGrpc.IdentidadGrpcClient(channel);
        var reply = await client.LoginAsync(new JsonRequest { Json = await ReadBodyAsync(context) }, cancellationToken: context.RequestAborted);
        return FromGrpc(reply);
    });

    app.MapPost("/api/v1/auth/register", async (HttpContext context, IConfiguration configuration) =>
    {
        using var channel = CreateGrpcChannel(configuration, "Identidad");
        var client = new IdentidadGrpc.IdentidadGrpcClient(channel);
        var reply = await client.RegisterAsync(new JsonRequest { Json = await ReadBodyAsync(context) }, cancellationToken: context.RequestAborted);
        return FromGrpc(reply);
    });

    app.MapGet("/api/v1/clientes/usuario/{usuarioGuid:guid}", async (Guid usuarioGuid, HttpContext context, IConfiguration configuration) =>
    {
        using var channel = CreateGrpcChannel(configuration, "Clientes");
        var client = new ClientesGrpc.ClientesGrpcClient(channel);
        var reply = await client.ObtenerPorUsuarioGuidAsync(new GuidRequest { Guid = usuarioGuid.ToString() }, cancellationToken: context.RequestAborted);
        return FromGrpc(reply);
    });

    app.MapGet("/api/v1/atracciones", ListarAtraccionesGrpcAsync);
    app.MapGet("/api/v1/booking/atracciones", ListarAtraccionesGrpcAsync);

    app.MapGet("/api/v1/atracciones/filtros", async (HttpContext context, IConfiguration configuration) =>
    {
        using var channel = CreateGrpcChannel(configuration, "Atracciones");
        var client = new AtraccionesGrpc.AtraccionesGrpcClient(channel);
        var reply = await client.ObtenerFiltrosAsync(new EmptyRequest(), cancellationToken: context.RequestAborted);
        return FromGrpc(reply);
    });

    app.MapGet("/api/v1/atracciones/{guid:guid}", ObtenerAtraccionGrpcAsync);
    app.MapGet("/api/v1/booking/atracciones/{guid:guid}", ObtenerAtraccionGrpcAsync);

    app.MapGet("/api/v1/atracciones/{guid:guid}/tickets", async (Guid guid, HttpContext context, IConfiguration configuration) =>
    {
        using var channel = CreateGrpcChannel(configuration, "Atracciones");
        var client = new AtraccionesGrpc.AtraccionesGrpcClient(channel);
        var reply = await client.ObtenerTicketsAsync(new GuidRequest { Guid = guid.ToString() }, cancellationToken: context.RequestAborted);
        return FromGrpc(reply);
    });

    app.MapGet("/api/v1/atracciones/{guid:guid}/horarios", async (Guid guid, HttpContext context, IConfiguration configuration) =>
    {
        using var channel = CreateGrpcChannel(configuration, "Atracciones");
        var client = new AtraccionesGrpc.AtraccionesGrpcClient(channel);
        var disponibles = bool.TryParse(context.Request.Query["disponibles"].ToString(), out var value) && value;
        var reply = await client.ObtenerHorariosAtraccionAsync(new HorariosAtraccionRequest
        {
            AtraccionGuid = guid.ToString(),
            Disponibles = disponibles
        }, cancellationToken: context.RequestAborted);
        return FromGrpc(reply);
    });

    app.MapGet("/api/v1/atracciones/{guid:guid}/horarios/{horarioId:guid}/tickets", async (
        Guid guid,
        Guid horarioId,
        HttpContext context,
        IConfiguration configuration) =>
    {
        using var channel = CreateGrpcChannel(configuration, "Atracciones");
        var client = new AtraccionesGrpc.AtraccionesGrpcClient(channel);
        var reply = await client.ObtenerTicketsPorHorarioAsync(new HorarioTicketsRequest
        {
            AtraccionGuid = guid.ToString(),
            HorarioGuid = horarioId.ToString()
        }, cancellationToken: context.RequestAborted);
        return FromGrpc(reply);
    });

    app.MapGet("/api/v1/tickets/{guid:guid}/horarios", async (Guid guid, HttpContext context, IConfiguration configuration) =>
    {
        using var channel = CreateGrpcChannel(configuration, "Atracciones");
        var client = new AtraccionesGrpc.AtraccionesGrpcClient(channel);
        var reply = await client.ObtenerHorariosPorTicketAsync(new GuidRequest { Guid = guid.ToString() }, cancellationToken: context.RequestAborted);
        return FromGrpc(reply);
    });

    app.MapPost("/api/v1/reservas", CrearReservaGrpcAsync);
    app.MapPost("/api/v1/booking/reservas", CrearReservaGrpcAsync);

    app.MapGet("/api/v1/reservas", async (HttpContext context, IConfiguration configuration) =>
    {
        using var channel = CreateGrpcChannel(configuration, "Reservas");
        var client = new ReservasGrpc.ReservasGrpcClient(channel);
        var clienteGuid = GetClienteGuidFromToken(context);
        var reply = await client.ListarAsync(new ListarReservasRequest
        {
            ClienteGuid = clienteGuid is not null && !IsStaff(context)
                ? clienteGuid.Value.ToString()
                : string.Empty
        }, cancellationToken: context.RequestAborted);
        return FromGrpc(reply);
    });

    app.MapGet("/api/v1/reservas/{guid:guid}", async (Guid guid, HttpContext context, IConfiguration configuration) =>
    {
        using var channel = CreateGrpcChannel(configuration, "Reservas");
        var client = new ReservasGrpc.ReservasGrpcClient(channel);
        var reply = await client.ObtenerAsync(new GuidRequest { Guid = guid.ToString() }, cancellationToken: context.RequestAborted);
        return FromGrpc(reply);
    });

    app.MapPost("/api/v1/reservas/{guid:guid}/pagos/confirmacion", async (Guid guid, HttpContext context, IConfiguration configuration) =>
    {
        using var channel = CreateGrpcChannel(configuration, "Reservas");
        var client = new ReservasGrpc.ReservasGrpcClient(channel);
        var reply = await client.ConfirmarPagoAsync(new GuidJsonRequest
        {
            Guid = guid.ToString(),
            Json = await ReadBodyAsync(context)
        }, cancellationToken: context.RequestAborted);
        return FromGrpc(reply);
    });

    app.MapGet("/api/v1/atracciones/{guid:guid}/resenias", async (Guid guid, HttpContext context, IConfiguration configuration) =>
    {
        using var channel = CreateGrpcChannel(configuration, "Atracciones");
        var client = new AtraccionesGrpc.AtraccionesGrpcClient(channel);
        var reply = await client.ListarReseniasAsync(new GuidRequest { Guid = guid.ToString() }, cancellationToken: context.RequestAborted);
        return FromGrpc(reply);
    });

    app.MapPost("/api/v1/atracciones/{guid:guid}/resenias", async (Guid guid, HttpContext context, IConfiguration configuration) =>
    {
        using var channel = CreateGrpcChannel(configuration, "Atracciones");
        var client = new AtraccionesGrpc.AtraccionesGrpcClient(channel);
        var body = await ReadBodyAsync(context);
        var reply = await client.CrearReseniaAsync(new JsonRequest
        {
            Json = WithAtraccionGuidAndUsuario(body, guid, GetUsuarioActual(context))
        }, cancellationToken: context.RequestAborted);
        return FromGrpc(reply);
    });

    app.MapPost("/api/v1/facturas", async (HttpContext context, IConfiguration configuration) =>
    {
        using var channel = CreateGrpcChannel(configuration, "Facturacion");
        var client = new FacturacionGrpc.FacturacionGrpcClient(channel);
        var reply = await client.CrearFacturaAsync(new JsonRequest { Json = await ReadBodyAsync(context) }, cancellationToken: context.RequestAborted);
        return FromGrpc(reply);
    });

    app.MapGet("/api/v1/facturas", async (HttpContext context, IConfiguration configuration) =>
    {
        using var channel = CreateGrpcChannel(configuration, "Facturacion");
        var client = new FacturacionGrpc.FacturacionGrpcClient(channel);
        var reply = await client.ListarFacturasAsync(new EmptyRequest(), cancellationToken: context.RequestAborted);
        return FromGrpc(reply);
    });

    app.MapGet("/api/v1/datos-facturacion", async (HttpContext context, IConfiguration configuration) =>
    {
        using var channel = CreateGrpcChannel(configuration, "Facturacion");
        var client = new FacturacionGrpc.FacturacionGrpcClient(channel);
        var reply = await client.ListarDatosFacturacionAsync(new EmptyRequest(), cancellationToken: context.RequestAborted);
        return FromGrpc(reply);
    });
}

app.MapBookingV2Endpoints();

app.MapPost("/api/v1/reservas/{guid:guid}/cancelar", CancelarReservaConCuposAsync);
app.MapPatch("/api/v1/reservas/{guid:guid}/cancelar", CancelarReservaConCuposAsync);

app.MapMethods(
    "/api/v1/{**path}",
    new[] { "GET", "POST", "PUT", "PATCH", "DELETE" },
    ProxyAsync);

app.MapMethods(
    "/api/v2/{**path}",
    new[] { "GET", "POST", "PUT", "PATCH", "DELETE" },
    ProxyAsync);

app.Run();

static async Task<IResult> ListarAtraccionesGrpcAsync(HttpContext context, IConfiguration configuration)
{
    using var channel = CreateGrpcChannel(configuration, "Atracciones");
    var client = new AtraccionesGrpc.AtraccionesGrpcClient(channel);
    var reply = await client.ListarAtraccionesAsync(new AtraccionesListRequest
    {
        Nombre = context.Request.Query["nombre"].ToString(),
        DestinoGuid = context.Request.Query["destinoGuid"].ToString(),
        CategoriaGuid = context.Request.Query["categoriaGuid"].ToString(),
        Page = int.TryParse(context.Request.Query["page"].ToString(), out var page) ? page : 0,
        PageSize = TryGetIntQuery(context, "limit", out var limit)
            ? limit
            : TryGetIntQuery(context, "pageSize", out var pageSize) ? pageSize : 0
    }, cancellationToken: context.RequestAborted);

    return FromGrpc(reply);
}

static async Task<IResult> ProxyAsync(HttpContext context, IHttpClientFactory httpClientFactory, IConfiguration configuration, string path)
{
    var target = ResolveTarget(configuration, path);
    if (target is null)
        return Results.NotFound(new { success = false, message = "Ruta no registrada en el API Gateway." });

    var client = httpClientFactory.CreateClient("proxy");
    var targetUri = BuildTargetUri(context, target.Value.BaseUrl, target.Value.Path);
    using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);

    CopyRequestHeaders(context, request);

    if (context.Request.ContentLength is > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
    {
        request.Content = new StreamContent(context.Request.Body);
        if (!string.IsNullOrWhiteSpace(context.Request.ContentType))
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType);
    }

    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
    context.Response.StatusCode = (int)response.StatusCode;
    CopyResponseHeaders(context, response);
    await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    return Results.Empty;
}

static async Task<IResult> CancelarReservaConCuposAsync(
    Guid guid,
    HttpContext context,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration)
{
    var client = httpClientFactory.CreateClient("proxy");
    var body = await ReadJsonObjectAsync(context);
    if (string.IsNullOrWhiteSpace(GetString(body, "motivo")))
        body["motivo"] = "Cancelacion solicitada desde API Gateway.";

    var reservaResponse = await GetServiceJsonAsync(
        client,
        configuration,
        "Reservas",
        $"/api/v1/reservas/{guid}",
        context.RequestAborted);

    if (!reservaResponse.IsSuccess)
        return ToJsonResult(reservaResponse);

    var reserva = reservaResponse.Body?["data"] as JsonObject ?? new JsonObject();

    var cancelResponse = await SendServiceJsonAsync(
        client,
        configuration,
        "Reservas",
        HttpMethod.Patch,
        $"/api/v1/reservas/{guid}/cancelar",
        body,
        context);

    if (!cancelResponse.IsSuccess)
        return ToJsonResult(cancelResponse);

    if (ShouldReturnCupos(reserva))
        await TryLiberarCuposReservaAsync(client, configuration, reserva, context);

    return ToJsonResult(cancelResponse);
}

static async Task<IResult> ObtenerAtraccionGrpcAsync(Guid guid, HttpContext context, IConfiguration configuration)
{
    using var channel = CreateGrpcChannel(configuration, "Atracciones");
    var client = new AtraccionesGrpc.AtraccionesGrpcClient(channel);
    var reply = await client.ObtenerDetalleAsync(new GuidRequest { Guid = guid.ToString() }, cancellationToken: context.RequestAborted);
    return FromGrpc(reply);
}

static async Task<IResult> CrearReservaGrpcAsync(HttpContext context, IConfiguration configuration)
{
    using var channel = CreateGrpcChannel(configuration, "Reservas");
    var client = new ReservasGrpc.ReservasGrpcClient(channel);
    var reply = await client.CrearAsync(new JsonRequest { Json = await ReadBodyAsync(context) }, cancellationToken: context.RequestAborted);
    return FromGrpc(reply);
}

static bool TryGetIntQuery(HttpContext context, string key, out int value)
{
    return int.TryParse(context.Request.Query[key].ToString(), out value);
}

static GrpcChannel CreateGrpcChannel(IConfiguration configuration, string serviceKey)
{
    var baseUrl = configuration[$"GrpcServiceUrls:{serviceKey}"]
        ?? configuration[$"ServiceUrls:{serviceKey}"]
        ?? throw new InvalidOperationException($"No se configuro URL gRPC para {serviceKey}.");

    return GrpcChannel.ForAddress(baseUrl);
}

static IResult FromGrpc(JsonReply reply)
{
    return Results.Content(reply.Json, "application/json", Encoding.UTF8, statusCode: reply.StatusCode);
}

static async Task<string> ReadBodyAsync(HttpContext context)
{
    using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
    var body = await reader.ReadToEndAsync(context.RequestAborted);
    return string.IsNullOrWhiteSpace(body) ? "{}" : body;
}

static async Task<JsonObject> ReadJsonObjectAsync(HttpContext context)
{
    var raw = await ReadBodyAsync(context);
    return JsonNode.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw)?.AsObject() ?? new JsonObject();
}

static string WithAtraccionGuidAndUsuario(string body, Guid atraccionGuid, string usuarioCreacion)
{
    var node = JsonNode.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body)?.AsObject() ?? new JsonObject();
    node["atraccionGuid"] = atraccionGuid.ToString();
    node["usuarioCreacion"] = usuarioCreacion;
    return node.ToJsonString();
}

static string GetUsuarioActual(HttpContext context)
{
    if (context.User.Identity?.IsAuthenticated != true)
        return "booking-public";

    return context.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? context.User.FindFirstValue(JwtRegisteredClaimNames.UniqueName)
        ?? context.User.Identity.Name
        ?? "booking-authenticated";
}

static Guid? GetClienteGuidFromToken(HttpContext context)
{
    if (context.User.Identity?.IsAuthenticated != true)
        return null;

    var sub = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    return Guid.TryParse(sub, out var guid) ? guid : null;
}

static bool IsStaff(HttpContext context)
{
    return context.User.IsInRole("ADMIN") || context.User.IsInRole("OPERADOR");
}

static bool ShouldReturnCupos(JsonObject reserva)
{
    var origen = GetString(reserva, "origenCanal") ?? GetString(reserva, "origen_canal");
    return string.Equals(origen, "BOOKING", StringComparison.OrdinalIgnoreCase);
}

static async Task TryLiberarCuposReservaAsync(
    HttpClient client,
    IConfiguration configuration,
    JsonObject reserva,
    HttpContext context)
{
    try
    {
        var horarioGuid = GetString(reserva, "horarioGuid") ?? GetString(reserva, "hor_guid");
        var detalles = BuildMovimientoCuposDetalles(reserva["detalles"] as JsonArray);
        if (string.IsNullOrWhiteSpace(horarioGuid) || detalles.Count == 0)
            return;

        var atraccionGuid = await ResolveAtraccionGuidForReservaAsync(client, configuration, reserva, context.RequestAborted);
        if (string.IsNullOrWhiteSpace(atraccionGuid))
            return;

        var body = new JsonObject { ["detalles"] = Clone(detalles) };
        await SendServiceJsonAsync(
            client,
            configuration,
            "Atracciones",
            HttpMethod.Post,
            $"/api/v1/atracciones/{atraccionGuid}/horarios/{horarioGuid}/cupos/liberar",
            body,
            context);
    }
    catch
    {
    }
}

static JsonArray BuildMovimientoCuposDetalles(JsonArray? detalles)
{
    var result = new JsonArray();
    foreach (var detalle in (detalles ?? new JsonArray()).OfType<JsonObject>())
    {
        var ticketGuid = GetString(detalle, "ticketGuid") ?? GetString(detalle, "tck_guid");
        var cantidad = GetInt(detalle, "cantidad", 0);
        if (string.IsNullOrWhiteSpace(ticketGuid) || cantidad <= 0)
            continue;

        result.Add(new JsonObject
        {
            ["ticketGuid"] = ticketGuid,
            ["cantidad"] = cantidad
        });
    }

    return result;
}

static async Task<string?> ResolveAtraccionGuidForReservaAsync(
    HttpClient client,
    IConfiguration configuration,
    JsonObject reserva,
    CancellationToken cancellationToken)
{
    var firstTicketGuid = (reserva["detalles"] as JsonArray ?? new JsonArray())
        .OfType<JsonObject>()
        .Select(x => GetString(x, "ticketGuid") ?? GetString(x, "tck_guid"))
        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

    if (string.IsNullOrWhiteSpace(firstTicketGuid))
        return null;

    var attractionsResponse = await GetServiceJsonAsync(client, configuration, "Atracciones", "/api/v1/atracciones", cancellationToken);
    if (!attractionsResponse.IsSuccess)
        return null;

    foreach (var attraction in DataArray(attractionsResponse.Body).OfType<JsonObject>())
    {
        var atraccionGuid = GetString(attraction, "guid");
        if (string.IsNullOrWhiteSpace(atraccionGuid))
            continue;

        var ticketsResponse = await GetServiceJsonAsync(client, configuration, "Atracciones", $"/api/v1/atracciones/{atraccionGuid}/tickets", cancellationToken);
        if (!ticketsResponse.IsSuccess)
            continue;

        if (DataArray(ticketsResponse.Body).OfType<JsonObject>()
            .Any(x => string.Equals(GetString(x, "guid"), firstTicketGuid, StringComparison.OrdinalIgnoreCase)))
            return atraccionGuid;
    }

    return null;
}

static string GetConfiguredServiceUrl(IConfiguration configuration, string serviceKey)
{
    return configuration[$"ServiceUrls:{serviceKey}"]
        ?? configuration[$"GrpcServiceUrls:{serviceKey}"]
        ?? string.Empty;
}

static (string BaseUrl, string Path)? ResolveTarget(IConfiguration configuration, string path)
{
    var normalized = path.Trim('/');
    var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
    if (segments.Length == 0)
        return null;

    var booking = segments[0].Equals("booking", StringComparison.OrdinalIgnoreCase);
    var resource = booking && segments.Length > 1 ? segments[1] : segments[0];
    var forwardedPath = booking
        ? string.Join('/', segments.Skip(1))
        : normalized;

    var serviceKey = resource.ToLowerInvariant() switch
    {
        "auth" or "usuarios" or "roles" or "usuarios-roles" => "Identidad",
        "clientes" => "Clientes",
        "atracciones" or "destinos" or "categorias" or "idiomas" or "incluye" or "incluyes" or "imagenes" or "tickets" or "horarios" or "resenias" => "Atracciones",
        "reservas" => "Reservas",
        "facturas" or "datos-facturacion" => "Facturacion",
        _ => string.Empty
    };

    if (string.IsNullOrWhiteSpace(serviceKey))
        return null;

    var baseUrl = configuration[$"ServiceUrls:{serviceKey}"]
        ?? configuration[$"GrpcServiceUrls:{serviceKey}"];
    if (string.IsNullOrWhiteSpace(baseUrl))
        return null;

    return (baseUrl.TrimEnd('/'), forwardedPath);
}

static Uri BuildTargetUri(HttpContext context, string baseUrl, string forwardedPath)
{
    var normalizedBaseUrl = NormalizeServiceBaseUrl(baseUrl);
    var normalizedPath = forwardedPath.Trim('/');
    var query = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : string.Empty;
    return new Uri($"{normalizedBaseUrl}/api/v1/{normalizedPath}{query}");
}

static async Task<ServiceJsonResponse> GetServiceJsonAsync(
    HttpClient client,
    IConfiguration configuration,
    string serviceKey,
    string path,
    CancellationToken cancellationToken)
{
    var uri = new Uri($"{NormalizeServiceBaseUrl(GetConfiguredServiceUrl(configuration, serviceKey))}{path}");
    using var response = await client.GetAsync(uri, cancellationToken);
    return await ServiceJsonResponse.FromAsync(response, cancellationToken);
}

static async Task<ServiceJsonResponse> SendServiceJsonAsync(
    HttpClient client,
    IConfiguration configuration,
    string serviceKey,
    HttpMethod method,
    string path,
    JsonObject body,
    HttpContext context)
{
    var uri = new Uri($"{NormalizeServiceBaseUrl(GetConfiguredServiceUrl(configuration, serviceKey))}{path}");
    using var request = new HttpRequestMessage(method, uri)
    {
        Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
    };

    if (context.Request.Headers.TryGetValue("Authorization", out var authorization))
        request.Headers.TryAddWithoutValidation("Authorization", authorization.ToArray());

    using var response = await client.SendAsync(request, context.RequestAborted);
    return await ServiceJsonResponse.FromAsync(response, context.RequestAborted);
}

static JsonArray DataArray(JsonObject? root)
{
    return root?["data"] as JsonArray ?? new JsonArray();
}

static JsonNode? Clone(JsonNode? node)
{
    return node is null ? null : JsonNode.Parse(node.ToJsonString());
}

static string? GetString(JsonObject? obj, string name)
{
    if (obj is null || obj[name] is null)
        return null;

    return obj[name]!.GetValueKind() == JsonValueKind.String
        ? obj[name]!.GetValue<string>()
        : obj[name]!.ToJsonString().Trim('"');
}

static int GetInt(JsonObject? obj, string name, int fallback)
{
    if (obj is null || obj[name] is null)
        return fallback;

    return int.TryParse(GetString(obj, name), out var value) ? value : fallback;
}

static IResult ToJsonResult(ServiceJsonResponse response)
{
    return Results.Content(response.Raw, "application/json", Encoding.UTF8, response.StatusCode);
}

static string NormalizeServiceBaseUrl(string baseUrl)
{
    var normalized = baseUrl.Trim().TrimEnd('/');
    var suffixes = new[] { "/swagger/index.html", "/swagger", "/api/v1" };

    foreach (var suffix in suffixes)
    {
        if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^suffix.Length].TrimEnd('/');
            break;
        }
    }

    return normalized;
}

static void CopyRequestHeaders(HttpContext context, HttpRequestMessage request)
{
    foreach (var header in context.Request.Headers)
    {
        if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
            header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
            header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            continue;

        request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
    }
}

static void CopyResponseHeaders(HttpContext context, HttpResponseMessage response)
{
    foreach (var header in response.Headers)
        context.Response.Headers[header.Key] = header.Value.ToArray();

    foreach (var header in response.Content.Headers)
        context.Response.Headers[header.Key] = header.Value.ToArray();

    context.Response.Headers.Remove("transfer-encoding");
}

sealed class ServiceJsonResponse
{
    public int StatusCode { get; init; }
    public JsonObject? Body { get; init; }
    public string Raw { get; init; } = "{}";
    public bool IsSuccess => StatusCode is >= 200 and <= 299;

    public static async Task<ServiceJsonResponse> FromAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonObject? body = null;
        if (!string.IsNullOrWhiteSpace(raw))
            body = JsonNode.Parse(raw) as JsonObject;

        return new ServiceJsonResponse
        {
            StatusCode = (int)response.StatusCode,
            Body = body,
            Raw = string.IsNullOrWhiteSpace(raw) ? "{}" : raw
        };
    }
}
