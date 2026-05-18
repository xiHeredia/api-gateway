using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Atracciones.Grpc;
using Atracciones.Shared.Extensions;
using Grpc.Net.Client;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAtraccionesApiDefaults(builder.Configuration, "api-gateway");

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

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "api-gateway" }));

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
        var reply = await client.ListarAsync(new EmptyRequest(), cancellationToken: context.RequestAborted);
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
        var reply = await client.ConfirmarPagoAsync(new GuidRequest { Guid = guid.ToString() }, cancellationToken: context.RequestAborted);
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
            Json = WithAtraccionGuid(body, guid)
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

app.MapMethods(
    "/api/v1/{**path}",
    new[] { "GET", "POST", "PUT", "PATCH", "DELETE" },
    async (HttpContext context, IHttpClientFactory httpClientFactory, IConfiguration configuration, string path) =>
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
    });

app.Run();

static async Task<IResult> ListarAtraccionesGrpcAsync(HttpContext context, IConfiguration configuration)
{
    using var channel = CreateGrpcChannel(configuration, "Atracciones");
    var client = new AtraccionesGrpc.AtraccionesGrpcClient(channel);
    var reply = await client.ListarAtraccionesAsync(new AtraccionesListRequest
    {
        Nombre = context.Request.Query["nombre"].ToString(),
        DestinoGuid = context.Request.Query["destinoGuid"].ToString(),
        CategoriaGuid = context.Request.Query["categoriaGuid"].ToString()
    }, cancellationToken: context.RequestAborted);

    return FromGrpc(reply);
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

static string WithAtraccionGuid(string body, Guid atraccionGuid)
{
    var node = JsonNode.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body)?.AsObject() ?? new JsonObject();
    node["atraccionGuid"] = atraccionGuid.ToString();
    return node.ToJsonString();
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
