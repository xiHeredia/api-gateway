using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.HttpResults;

public static class BookingV2Endpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapBookingV2Endpoints(this WebApplication app)
    {
        app.MapGet("/api/v2/atracciones", ListarAtraccionesAsync);
        app.MapGet("/api/v2/atracciones/filtros", ObtenerFiltrosAsync);
        app.MapGet("/api/v2/atracciones/{guid:guid}", ObtenerAtraccionAsync);
        app.MapGet("/api/v2/atracciones/{guid:guid}/tickets", ObtenerTicketsAsync);
        app.MapGet("/api/v2/atracciones/{guid:guid}/horarios", ObtenerHorariosAsync);
        app.MapGet("/api/v2/atracciones/{guid:guid}/horarios/{horarioGuid:guid}/tickets", ObtenerTicketsPorHorarioAsync);
        app.MapPost("/api/v2/reservas", CrearReservaAsync);
        app.MapGet("/api/v2/reservas", ListarReservasAsync);
        app.MapGet("/api/v2/reservas/{guid:guid}", ObtenerReservaAsync);
        app.MapPost("/api/v2/reservas/{guid:guid}/pagos/confirmacion", ConfirmarPagoAsync);
    }

    private static async Task<IResult> ListarAtraccionesAsync(HttpContext context, IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        if (!TryReadPagination(context, out var page, out var limit, out var paginationError))
            return paginationError;

        var client = httpClientFactory.CreateClient("proxy");
        var response = await GetServiceJsonAsync(client, configuration, "Atracciones", "/api/v1/atracciones", context.RequestAborted);
        if (!response.IsSuccess)
            return response.ToResult();

        var allItems = DataArray(response.Body);
        var filtered = new List<JsonObject>();

        foreach (var item in allItems.OfType<JsonObject>())
        {
            var contractItem = await ToAttractionListItemAsync(item, client, configuration, context.RequestAborted);
            await EnrichAttractionListItemAsync(contractItem, client, configuration, context.RequestAborted);
            if (!MatchesAttractionFilters(contractItem, context))
                continue;

            filtered.Add(contractItem);
        }

        SortAttractions(filtered, context.Request.Query["ordenar_por"].ToString());

        var total = filtered.Count;
        var pageItems = filtered.Skip((page - 1) * limit).Take(limit).ToList();

        var data = new JsonArray();
        foreach (var item in pageItems)
            data.Add(Clone(item));

        var payload = Envelope(200, "Consulta exitosa", data);
        payload["pagination"] = Pagination(page, limit, total);
        payload["filterStats"] = new JsonObject
        {
            ["filteredProductCount"] = total,
            ["unfilteredProductCount"] = allItems.Count
        };
        payload["sorters"] = new JsonArray
        {
            new JsonObject { ["name"] = "Mas populares", ["value"] = "trending" },
            new JsonObject { ["name"] = "Menor precio", ["value"] = "lowest_price" },
            new JsonObject { ["name"] = "Mejor calificacion", ["value"] = "highest_weighted_rating" }
        };
        payload["defaultSorter"] = new JsonObject { ["name"] = "Mas populares", ["value"] = "trending" };
        payload["_links"] = new JsonObject { ["self"] = $"/api/v2/atracciones?page={page}&limit={limit}" };

        return Results.Json(payload, statusCode: 200);
    }

    private static async Task<IResult> ObtenerFiltrosAsync(HttpContext context, IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        var client = httpClientFactory.CreateClient("proxy");
        var filtersResponse = await GetServiceJsonAsync(client, configuration, "Atracciones", "/api/v1/atracciones/filtros", context.RequestAborted);
        if (!filtersResponse.IsSuccess)
            return filtersResponse.ToResult();

        var attractionsResponse = await GetServiceJsonAsync(client, configuration, "Atracciones", "/api/v1/atracciones", context.RequestAborted);
        var attractions = attractionsResponse.IsSuccess ? DataArray(attractionsResponse.Body) : new JsonArray();
        var data = filtersResponse.Body?["data"] as JsonObject ?? new JsonObject();

        var payloadData = new JsonObject
        {
            ["destinationFilters"] = BuildFilterArray(data["destinos"] as JsonArray, attractions, "destinoGuid", "destinoNombre", includeImage: true),
            ["typeFilters"] = BuildFilterArray(data["categorias"] as JsonArray, attractions, null, null),
            ["labelFilters"] = new JsonArray
            {
                new JsonObject { ["name"] = "Cancelacion gratuita", ["tagname"] = "free_cancellation", ["productCount"] = attractions.Count },
                new JsonObject { ["name"] = "Sin fila", ["tagname"] = "skip_the_line", ["productCount"] = attractions.Count }
            },
            ["minRatingFilter"] = new JsonArray
            {
                new JsonObject { ["name"] = "3.0+", ["tagname"] = "3.0", ["productCount"] = attractions.Count },
                new JsonObject { ["name"] = "3.5+", ["tagname"] = "3.5", ["productCount"] = attractions.Count },
                new JsonObject { ["name"] = "4.0+", ["tagname"] = "4.0", ["productCount"] = attractions.Count },
                new JsonObject { ["name"] = "4.5+", ["tagname"] = "4.5", ["productCount"] = attractions.Count }
            },
            ["timeOfDayFilters"] = new JsonArray
            {
                new JsonObject { ["name"] = "Manana (05:00-12:00)", ["tagname"] = "05:00-12:00", ["productCount"] = attractions.Count },
                new JsonObject { ["name"] = "Tarde (12:00-18:00)", ["tagname"] = "12:00-18:00", ["productCount"] = attractions.Count },
                new JsonObject { ["name"] = "Noche (18:00-05:00)", ["tagname"] = "18:00-05:00", ["productCount"] = attractions.Count }
            },
            ["supportedLanguageFilters"] = BuildLanguageFilters(data["idiomas"] as JsonArray, attractions.Count)
        };

        return Results.Json(Envelope(200, "Operacion exitosa", payloadData), statusCode: 200);
    }

    private static async Task<IResult> ObtenerAtraccionAsync(Guid guid, HttpContext context, IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        var client = httpClientFactory.CreateClient("proxy");
        var detailResponse = await GetServiceJsonAsync(client, configuration, "Atracciones", $"/api/v1/atracciones/{guid}", context.RequestAborted);
        if (!detailResponse.IsSuccess)
            return detailResponse.ToResult();

        var detail = detailResponse.Body?["data"] as JsonObject ?? new JsonObject();
        var horarios = await GetHorariosRawAsync(client, configuration, guid.ToString(), context.RequestAborted);
        var data = await ToAttractionDetailAsync(detail, horarios, client, configuration, context.RequestAborted);
        return Results.Json(Envelope(200, "Operacion exitosa", data), statusCode: 200);
    }

    private static async Task<IResult> ObtenerTicketsAsync(Guid guid, HttpContext context, IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        var client = httpClientFactory.CreateClient("proxy");
        var response = await GetServiceJsonAsync(client, configuration, "Atracciones", $"/api/v1/atracciones/{guid}/tickets", context.RequestAborted);
        if (!response.IsSuccess)
            return response.ToResult();

        return Results.Json(Envelope(200, "Operacion exitosa", TransformTickets(DataArray(response.Body))), statusCode: 200);
    }

    private static async Task<IResult> ObtenerHorariosAsync(Guid guid, HttpContext context, IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        var client = httpClientFactory.CreateClient("proxy");
        var response = await GetServiceJsonAsync(client, configuration, "Atracciones", $"/api/v1/atracciones/{guid}/horarios?disponibles=true", context.RequestAborted);
        if (!response.IsSuccess)
            return response.ToResult();

        return Results.Json(Envelope(200, "Operacion exitosa", TransformHorarios(DataArray(response.Body))), statusCode: 200);
    }

    private static async Task<IResult> ObtenerTicketsPorHorarioAsync(Guid guid, Guid horarioGuid, HttpContext context, IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        var client = httpClientFactory.CreateClient("proxy");
        var response = await GetServiceJsonAsync(client, configuration, "Atracciones", $"/api/v1/atracciones/{guid}/horarios/{horarioGuid}/tickets", context.RequestAborted);
        if (!response.IsSuccess)
            return response.ToResult();

        var data = new JsonObject { ["items"] = TransformTickets(DataArray(response.Body)) };
        return Results.Json(Envelope(200, "Operacion exitosa", data), statusCode: 200);
    }

    private static async Task<IResult> CrearReservaAsync(HttpContext context, IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        var client = httpClientFactory.CreateClient("proxy");
        var body = await ReadJsonObjectAsync(context);
        var atraccionGuid = GetString(body, "at_guid") ?? GetString(body, "atraccionGuid");
        var horarioGuid = GetString(body, "hor_guid") ?? GetString(body, "horarioGuid") ?? GetString(body, "horarioId");
        var guest = body["cliente_invitado"] as JsonObject;

        if (string.IsNullOrWhiteSpace(atraccionGuid) || string.IsNullOrWhiteSpace(horarioGuid))
            return Results.Json(Error(400, "Body invalido", "at_guid y hor_guid son obligatorios."), statusCode: 400);

        if (!IsValidGuest(guest, out var guestError))
            return Results.Json(Error(400, "Body invalido", guestError), statusCode: 400);

        var ticketsResponse = await GetServiceJsonAsync(client, configuration, "Atracciones", $"/api/v1/atracciones/{atraccionGuid}/horarios/{horarioGuid}/tickets", context.RequestAborted);
        if (!ticketsResponse.IsSuccess)
            return ticketsResponse.ToResult();

        var requestedLines = body["lineas"] as JsonArray ?? body["tickets"] as JsonArray ?? new JsonArray();
        if (requestedLines.Count == 0)
            return Results.Json(Error(400, "Body invalido", "Debe enviar al menos una linea."), statusCode: 400);

        var detalles = new JsonArray();
        foreach (var line in requestedLines.OfType<JsonObject>())
        {
            var ticketGuid = GetString(line, "tck_guid") ?? GetString(line, "ticketGuid");
            var cantidad = GetInt(line, "cantidad", 0);
            var ticket = FindByGuid(DataArray(ticketsResponse.Body), ticketGuid);
            if (ticket is null || cantidad <= 0)
                return Results.Json(Error(400, "Body invalido", $"Ticket o cantidad invalida: {ticketGuid}."), statusCode: 400);

            if (cantidad > GetInt(ticket, "cuposDisponibles", cantidad))
                return Results.Json(Error(409, "Cupos insuficientes", $"No hay cupos suficientes para el ticket {ticketGuid}."), statusCode: 409);

            var precio = GetDecimal(ticket, "precio", 0);
            detalles.Add(new JsonObject
            {
                ["ticketGuid"] = ticketGuid,
                ["cantidad"] = cantidad,
                ["precioUnitario"] = precio
            });
        }

        var internalBody = new JsonObject
        {
            ["atraccionGuid"] = atraccionGuid,
            ["horarioGuid"] = horarioGuid,
            ["origenCanal"] = "BOOKING",
            ["detalles"] = detalles
        };

        internalBody["cliente_invitado"] = Clone(guest);

        if (body["clienteGuid"] is not null || body["cliente_guid"] is not null)
            internalBody["clienteGuid"] = Clone(body["clienteGuid"] ?? body["cliente_guid"]);

        var response = await SendServiceJsonAsync(client, configuration, "Reservas", HttpMethod.Post, "/api/v1/reservas", internalBody, context);
        if (!response.IsSuccess)
            return response.ToResult();

        var reserva = response.Body?["data"] as JsonObject ?? new JsonObject();
        var data = await ToReservaContractAsync(reserva, client, configuration, context.RequestAborted, atraccionGuid);
        return Results.Json(Envelope(201, "Operacion exitosa", data), statusCode: 201);
    }

    private static async Task<IResult> ListarReservasAsync(HttpContext context, IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        if (!TryReadPagination(context, out var page, out var limit, out var paginationError))
            return paginationError;

        var client = httpClientFactory.CreateClient("proxy");
        var response = await GetServiceJsonAsync(client, configuration, "Reservas", "/api/v1/reservas", context.RequestAborted);
        if (!response.IsSuccess)
            return response.ToResult();

        var bookingReservas = DataArray(response.Body)
            .OfType<JsonObject>()
            .Where(x => string.Equals(GetString(x, "origenCanal"), "BOOKING", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var total = bookingReservas.Count;

        var data = new JsonArray();
        foreach (var reserva in bookingReservas.Skip((page - 1) * limit).Take(limit))
            data.Add(await ToReservaListItemAsync(reserva, client, configuration, context.RequestAborted));

        var payload = Envelope(200, "Operacion exitosa", data);
        payload["pagination"] = Pagination(page, limit, total);
        return Results.Json(payload, statusCode: 200);
    }

    private static async Task<IResult> ObtenerReservaAsync(Guid guid, HttpContext context, IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        var client = httpClientFactory.CreateClient("proxy");
        var response = await GetServiceJsonAsync(client, configuration, "Reservas", $"/api/v1/reservas/{guid}", context.RequestAborted);
        if (!response.IsSuccess)
            return response.ToResult();

        var reserva = response.Body?["data"] as JsonObject ?? new JsonObject();
        if (!string.Equals(GetString(reserva, "origenCanal"), "BOOKING", StringComparison.OrdinalIgnoreCase))
            return Results.Json(Error(404, "Reserva no encontrada", "La reserva no pertenece al canal BOOKING."), statusCode: 404);

        var data = await ToReservaContractAsync(reserva, client, configuration, context.RequestAborted);
        return Results.Json(Envelope(200, "Operacion exitosa", data), statusCode: 200);
    }

    private static async Task<IResult> ConfirmarPagoAsync(Guid guid, HttpContext context, IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        var client = httpClientFactory.CreateClient("proxy");
        var body = await ReadJsonObjectAsync(context);
        var response = await SendServiceJsonAsync(client, configuration, "Reservas", HttpMethod.Post, $"/api/v1/reservas/{guid}/pagos/confirmacion", body, context);
        if (!response.IsSuccess)
            return response.ToResult();

        var data = response.Body?["data"] as JsonObject ?? new JsonObject();
        var contractData = new JsonObject
        {
            ["fac_guid"] = GetString(data, "fac_guid") ?? GetString(data, "facGuid"),
            ["fac_numero"] = GetString(data, "fac_numero") ?? GetString(data, "facNumero"),
            ["rev_codigo"] = GetString(data, "rev_codigo") ?? GetString(data, "revCodigo"),
            ["total"] = GetDecimal(data, "total", 0),
            ["moneda"] = GetString(data, "moneda") ?? "USD",
            ["fecha_emision"] = GetString(data, "fecha_emision") ?? GetString(data, "fechaEmision"),
            ["estado"] = GetString(data, "estado") ?? "E",
            ["nombre_receptor"] = GetString(data, "nombre_receptor") ?? GetString(data, "nombreReceptor"),
            ["correo_receptor"] = GetString(data, "correo_receptor") ?? GetString(data, "correoReceptor")
        };

        return Results.Json(Envelope(201, "Operacion exitosa", contractData), statusCode: 201);
    }

    private static async Task<JsonObject> ToAttractionListItemAsync(JsonObject item, HttpClient client, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var guid = GetString(item, "guid") ?? string.Empty;
        var horarios = string.IsNullOrWhiteSpace(guid)
            ? new JsonArray()
            : await GetHorariosRawAsync(client, configuration, guid, cancellationToken);
        var availability = BuildAvailability(horarios, GetBool(item, "disponible"));

        return new JsonObject
        {
            ["id"] = guid,
            ["nombre"] = GetString(item, "nombre"),
            ["ciudad"] = GetString(item, "destinoNombre"),
            ["pais"] = GetString(item, "destinoPais"),
            ["tipo_tagname"] = "atraccion",
            ["tipo_nombre"] = "Atraccion",
            ["subtipo_tagname"] = null,
            ["subtipo_nombre"] = null,
            ["etiquetas"] = new JsonArray("free_cancellation", "skip_the_line"),
            ["descripcion_corta"] = Truncate(GetString(item, "descripcion"), 150),
            ["imagen_principal"] = GetString(item, "imagenUrl"),
            ["duracion_minutos"] = GetNullableInt(item, "duracionMinutos"),
            ["precio_desde"] = GetDecimal(item, "precioReferencia", 0),
            ["moneda"] = "USD",
            ["calificacion"] = 4.5,
            ["total_resenas"] = GetInt(item, "totalResenias", 0),
            ["idiomas_disponibles"] = new JsonArray("es"),
            ["disponibilidad"] = availability,
            ["horarios_proximos"] = TransformHorarios(horarios),
            ["_links"] = new JsonObject { ["self"] = $"/api/v2/atracciones/{guid}" }
        };
    }

    private static async Task EnrichAttractionListItemAsync(JsonObject item, HttpClient client, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var guid = GetString(item, "id");
        if (string.IsNullOrWhiteSpace(guid))
            return;

        var detailResponse = await GetServiceJsonAsync(client, configuration, "Atracciones", $"/api/v1/atracciones/{guid}", cancellationToken);
        if (!detailResponse.IsSuccess || detailResponse.Body?["data"] is not JsonObject detail)
            return;

        var categorias = detail["categorias"] as JsonArray ?? new JsonArray();
        var categoriaItems = categorias.OfType<JsonObject>().ToList();
        var firstCategoria = categoriaItems.FirstOrDefault();
        var secondCategoria = categoriaItems.Skip(1).FirstOrDefault();

        if (firstCategoria is not null)
        {
            item["tipo_tagname"] = Slug(GetString(firstCategoria, "nombre"));
            item["tipo_nombre"] = GetString(firstCategoria, "nombre");
        }

        if (secondCategoria is not null)
        {
            item["subtipo_tagname"] = Slug(GetString(secondCategoria, "nombre"));
            item["subtipo_nombre"] = GetString(secondCategoria, "nombre");
        }

        item["idiomas_disponibles"] = LanguageCodes(detail["idiomas"] as JsonArray);
    }

    private static async Task<JsonObject> ToAttractionDetailAsync(JsonObject detail, JsonArray horarios, HttpClient client, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var baseItem = await ToAttractionListItemAsync(detail, client, configuration, cancellationToken);
        baseItem["descripcion"] = GetString(detail, "descripcion");
        baseItem["imagenes"] = Clone(detail["imagenes"] as JsonArray ?? new JsonArray());
        baseItem["incluye"] = NamesArray(detail["incluye"] as JsonArray);
        baseItem["no_incluye"] = new JsonArray();
        baseItem["punto_encuentro"] = GetString(detail, "puntoEncuentro");
        baseItem["incluye_transporte"] = GetBool(detail, "incluyeTransporte");
        baseItem["incluye_acompaniante"] = GetBool(detail, "incluyeAcompaniante");
        baseItem["tickets"] = TransformTickets(detail["tickets"] as JsonArray ?? new JsonArray());
        baseItem["horarios_proximos"] = TransformHorarios(horarios);

        var categorias = detail["categorias"] as JsonArray ?? new JsonArray();
        var firstCategoria = categorias.OfType<JsonObject>().FirstOrDefault();
        if (firstCategoria is not null)
        {
            baseItem["tipo_tagname"] = Slug(GetString(firstCategoria, "nombre"));
            baseItem["tipo_nombre"] = GetString(firstCategoria, "nombre");
        }

        baseItem["idiomas_disponibles"] = LanguageCodes(detail["idiomas"] as JsonArray);
        return baseItem;
    }

    private static JsonArray TransformTickets(JsonArray tickets)
    {
        var result = new JsonArray();
        foreach (var ticket in tickets.OfType<JsonObject>())
        {
            result.Add(new JsonObject
            {
                ["tck_guid"] = GetString(ticket, "guid"),
                ["tipo"] = GetString(ticket, "tipoParticipante") ?? GetString(ticket, "titulo"),
                ["precio"] = GetDecimal(ticket, "precio", 0),
                ["moneda"] = "USD"
            });
        }

        return result;
    }

    private static JsonArray TransformHorarios(JsonArray horarios)
    {
        var groups = horarios
            .OfType<JsonObject>()
            .GroupBy(x => $"{GetString(x, "fecha")}|{GetString(x, "horaInicio")}|{GetString(x, "horaFin")}");

        var result = new JsonArray();
        foreach (var group in groups)
        {
            var first = group.First();
            result.Add(new JsonObject
            {
                ["hor_guid"] = GetString(first, "guid"),
                ["fecha"] = GetString(first, "fecha"),
                ["hora_inicio"] = GetString(first, "horaInicio"),
                ["hora_fin"] = GetString(first, "horaFin"),
                ["cupos"] = group.Sum(x => GetInt(x, "cuposDisponibles", 0))
            });
        }

        return result;
    }

    private static async Task<JsonObject> ToReservaContractAsync(JsonObject reserva, HttpClient client, IConfiguration configuration, CancellationToken cancellationToken, string? atraccionGuid = null)
    {
        var context = await ResolveReservaContextAsync(reserva, client, configuration, cancellationToken, atraccionGuid);
        var detalles = TransformReservaDetalles(reserva["detalles"] as JsonArray ?? new JsonArray(), context.Tickets);

        return new JsonObject
        {
            ["rev_guid"] = GetString(reserva, "guid"),
            ["rev_codigo"] = GetString(reserva, "codigo"),
            ["hor_fecha"] = context.Fecha,
            ["hor_hora_inicio"] = context.HoraInicio,
            ["hor_hora_fin"] = context.HoraFin,
            ["atraccion_nombre"] = context.AtraccionNombre,
            ["rev_subtotal"] = GetDecimal(reserva, "subtotal", 0),
            ["rev_valor_iva"] = GetDecimal(reserva, "valorIva", 0),
            ["rev_total"] = GetDecimal(reserva, "total", 0),
            ["moneda"] = "USD",
            ["rev_estado"] = MapReservaEstado(GetString(reserva, "estado")),
            ["rev_fecha_reserva_utc"] = GetString(reserva, "fechaReservaUtc"),
            ["detalle"] = detalles,
            ["_links"] = new JsonObject
            {
                ["self"] = $"/api/v2/reservas/{GetString(reserva, "guid")}",
                ["confirmar_pago"] = $"/api/v2/reservas/{GetString(reserva, "guid")}/pagos/confirmacion"
            }
        };
    }

    private static async Task<JsonObject> ToReservaListItemAsync(JsonObject reserva, HttpClient client, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var full = await ToReservaContractAsync(reserva, client, configuration, cancellationToken);
        return new JsonObject
        {
            ["rev_guid"] = Clone(full["rev_guid"]),
            ["rev_codigo"] = Clone(full["rev_codigo"]),
            ["hor_fecha"] = Clone(full["hor_fecha"]),
            ["hor_hora_inicio"] = Clone(full["hor_hora_inicio"]),
            ["atraccion_nombre"] = Clone(full["atraccion_nombre"]),
            ["rev_total"] = Clone(full["rev_total"]),
            ["moneda"] = "USD",
            ["rev_estado"] = Clone(full["rev_estado"]),
            ["_links"] = Clone(full["_links"])
        };
    }

    private static JsonArray TransformReservaDetalles(JsonArray detalles, Dictionary<string, JsonObject> tickets)
    {
        var result = new JsonArray();
        foreach (var detail in detalles.OfType<JsonObject>())
        {
            var ticketGuid = GetString(detail, "ticketGuid");
            tickets.TryGetValue(ticketGuid ?? string.Empty, out var ticket);
            var cantidad = GetInt(detail, "cantidad", 0);
            var precio = GetDecimal(detail, "precioUnitario", GetDecimal(ticket, "precio", 0));
            result.Add(new JsonObject
            {
                ["tck_tipo_participante"] = GetString(ticket, "tipoParticipante") ?? ticketGuid,
                ["cantidad"] = cantidad,
                ["precio_unit"] = precio,
                ["subtotal"] = GetDecimal(detail, "subtotal", cantidad * precio)
            });
        }

        return result;
    }

    private static async Task<ReservaContext> ResolveReservaContextAsync(JsonObject reserva, HttpClient client, IConfiguration configuration, CancellationToken cancellationToken, string? atraccionGuid = null)
    {
        var context = new ReservaContext();
        var detalles = reserva["detalles"] as JsonArray ?? new JsonArray();
        var firstTicketGuid = detalles.OfType<JsonObject>().Select(x => GetString(x, "ticketGuid")).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

        if (!string.IsNullOrWhiteSpace(atraccionGuid))
        {
            await FillContextFromAttractionAsync(context, atraccionGuid, reserva, client, configuration, cancellationToken);
            return context;
        }

        var attractionsResponse = await GetServiceJsonAsync(client, configuration, "Atracciones", "/api/v1/atracciones", cancellationToken);
        if (!attractionsResponse.IsSuccess)
            return context;

        foreach (var attraction in DataArray(attractionsResponse.Body).OfType<JsonObject>())
        {
            var guid = GetString(attraction, "guid");
            if (string.IsNullOrWhiteSpace(guid))
                continue;

            var ticketsResponse = await GetServiceJsonAsync(client, configuration, "Atracciones", $"/api/v1/atracciones/{guid}/tickets", cancellationToken);
            if (!ticketsResponse.IsSuccess)
                continue;

            var tickets = DataArray(ticketsResponse.Body).OfType<JsonObject>().ToList();
            if (tickets.Any(x => string.Equals(GetString(x, "guid"), firstTicketGuid, StringComparison.OrdinalIgnoreCase)))
            {
                context.AtraccionNombre = GetString(attraction, "nombre");
                foreach (var ticket in tickets)
                    context.Tickets[GetString(ticket, "guid") ?? string.Empty] = ticket;

                await FillScheduleAsync(context, GetString(reserva, "horarioGuid"), guid, client, configuration, cancellationToken);
                return context;
            }
        }

        return context;
    }

    private static async Task FillContextFromAttractionAsync(ReservaContext context, string atraccionGuid, JsonObject reserva, HttpClient client, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var detailResponse = await GetServiceJsonAsync(client, configuration, "Atracciones", $"/api/v1/atracciones/{atraccionGuid}", cancellationToken);
        if (detailResponse.IsSuccess && detailResponse.Body?["data"] is JsonObject detail)
            context.AtraccionNombre = GetString(detail, "nombre");

        var ticketsResponse = await GetServiceJsonAsync(client, configuration, "Atracciones", $"/api/v1/atracciones/{atraccionGuid}/tickets", cancellationToken);
        if (ticketsResponse.IsSuccess)
        {
            foreach (var ticket in DataArray(ticketsResponse.Body).OfType<JsonObject>())
                context.Tickets[GetString(ticket, "guid") ?? string.Empty] = ticket;
        }

        await FillScheduleAsync(context, GetString(reserva, "horarioGuid"), atraccionGuid, client, configuration, cancellationToken);
    }

    private static async Task FillScheduleAsync(ReservaContext context, string? horarioGuid, string? atraccionGuid, HttpClient client, IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(horarioGuid) || string.IsNullOrWhiteSpace(atraccionGuid))
            return;

        var horarios = await GetHorariosRawAsync(client, configuration, atraccionGuid, cancellationToken);
        var horario = horarios.OfType<JsonObject>().FirstOrDefault(x => string.Equals(GetString(x, "guid"), horarioGuid, StringComparison.OrdinalIgnoreCase));
        if (horario is null)
            return;

        context.Fecha = GetString(horario, "fecha");
        context.HoraInicio = GetString(horario, "horaInicio");
        context.HoraFin = GetString(horario, "horaFin");
    }

    private static async Task<JsonArray> GetHorariosRawAsync(HttpClient client, IConfiguration configuration, string atraccionGuid, CancellationToken cancellationToken)
    {
        var response = await GetServiceJsonAsync(client, configuration, "Atracciones", $"/api/v1/atracciones/{atraccionGuid}/horarios?disponibles=true", cancellationToken);
        return response.IsSuccess ? DataArray(response.Body) : new JsonArray();
    }

    private static JsonObject BuildAvailability(JsonArray horarios, bool disponible)
    {
        var now = DateOnly.FromDateTime(DateTime.UtcNow);
        var rows = horarios.OfType<JsonObject>().ToList();
        var next = rows
            .Select(x => GetString(x, "fecha"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .OrderBy(x => x)
            .FirstOrDefault();

        return new JsonObject
        {
            ["disponible"] = disponible && rows.Sum(x => GetInt(x, "cuposDisponibles", 0)) > 0,
            ["disponible_hoy"] = rows.Any(x => GetString(x, "fecha") == now.ToString("yyyy-MM-dd") && GetInt(x, "cuposDisponibles", 0) > 0),
            ["proxima_fecha_disponible"] = next,
            ["cupos_disponibles"] = rows.Sum(x => GetInt(x, "cuposDisponibles", 0))
        };
    }

    private static JsonArray BuildFilterArray(JsonArray? source, JsonArray attractions, string? guidField, string? nameField, bool includeImage = false)
    {
        var result = new JsonArray();
        foreach (var item in (source ?? new JsonArray()).OfType<JsonObject>())
        {
            var name = GetString(item, "nombre") ?? string.Empty;
            var guid = GetString(item, "guid");
            var count = guidField is null || nameField is null
                ? attractions.Count
                : attractions.OfType<JsonObject>().Count(x => string.Equals(GetString(x, guidField), guid, StringComparison.OrdinalIgnoreCase) ||
                                                              string.Equals(GetString(x, nameField), name, StringComparison.OrdinalIgnoreCase));

            var filter = new JsonObject
            {
                ["name"] = name,
                ["tagname"] = Slug(name),
                ["productCount"] = count,
                ["childFilterOptions"] = null
            };

            if (includeImage)
                filter["image"] = new JsonObject { ["url"] = GetString(item, "imagenUrl") };

            result.Add(filter);
        }

        return result;
    }

    private static JsonArray BuildLanguageFilters(JsonArray? source, int productCount)
    {
        var result = new JsonArray();
        foreach (var item in (source ?? new JsonArray()).OfType<JsonObject>())
        {
            var name = GetString(item, "nombre") ?? string.Empty;
            result.Add(new JsonObject
            {
                ["name"] = name,
                ["tagname"] = LanguageCode(name),
                ["productCount"] = productCount
            });
        }

        if (result.Count == 0)
            result.Add(new JsonObject { ["name"] = "Espanol", ["tagname"] = "es", ["productCount"] = productCount });

        return result;
    }

    private static JsonArray NamesArray(JsonArray? source)
    {
        var result = new JsonArray();
        foreach (var item in (source ?? new JsonArray()).OfType<JsonObject>())
            result.Add(GetString(item, "nombre"));
        return result;
    }

    private static JsonArray LanguageCodes(JsonArray? source)
    {
        var result = new JsonArray();
        foreach (var item in (source ?? new JsonArray()).OfType<JsonObject>())
            result.Add(LanguageCode(GetString(item, "nombre")));
        return result.Count == 0 ? new JsonArray("es") : result;
    }

    private static bool MatchesTextFilter(JsonObject item, string field, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
            return true;

        return (GetString(item, field) ?? string.Empty).Contains(expected.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesGuidOrText(JsonObject item, string guidField, string textField, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
            return true;

        var value = expected.Trim();
        return string.Equals(GetString(item, guidField), value, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(GetString(item, textField), value, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Slug(GetString(item, textField)), Slug(value), StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesAttractionFilters(JsonObject item, HttpContext context)
    {
        if (!MatchesTextOrSlug(GetString(item, "ciudad"), context.Request.Query["ciudad"].ToString()))
            return false;

        if (!MatchesTextOrSlug(GetString(item, "tipo_tagname"), context.Request.Query["tipo"].ToString()) &&
            !MatchesTextOrSlug(GetString(item, "tipo_nombre"), context.Request.Query["tipo"].ToString()))
            return false;

        if (!MatchesTextOrSlug(GetString(item, "subtipo_tagname"), context.Request.Query["subtipo"].ToString()) &&
            !MatchesTextOrSlug(GetString(item, "subtipo_nombre"), context.Request.Query["subtipo"].ToString()))
            return false;

        if (!MatchesArrayValue(item["etiquetas"] as JsonArray, context.Request.Query["etiqueta"].ToString()))
            return false;

        if (!MatchesArrayValue(item["idiomas_disponibles"] as JsonArray, NormalizeLanguageQuery(context.Request.Query["idioma"].ToString())))
            return false;

        if (decimal.TryParse(context.Request.Query["calificacion_min"].ToString(), out var minRating) &&
            GetDecimal(item, "calificacion", 0) < minRating)
            return false;

        if (bool.TryParse(context.Request.Query["disponible"].ToString(), out var available) &&
            GetBoolValue(item["disponibilidad"] as JsonObject, "disponible") != available)
            return false;

        if (!MatchesScheduleRange(item["horarios_proximos"] as JsonArray, context.Request.Query["horario"].ToString()))
            return false;

        return true;
    }

    private static bool MatchesTextOrSlug(string? actual, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
            return true;

        if (string.IsNullOrWhiteSpace(actual))
            return false;

        var value = expected.Trim();
        return actual.Contains(value, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Slug(actual), Slug(value), StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesArrayValue(JsonArray? values, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
            return true;

        return (values ?? new JsonArray())
            .Any(x => MatchesTextOrSlug(GetNodeString(x), expected));
    }

    private static bool MatchesScheduleRange(JsonArray? horarios, string? range)
    {
        if (string.IsNullOrWhiteSpace(range))
            return true;

        var parts = range.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            !TimeOnly.TryParse(parts[0], out var start) ||
            !TimeOnly.TryParse(parts[1], out var end))
            return false;

        return (horarios ?? new JsonArray())
            .OfType<JsonObject>()
            .Any(x =>
            {
                if (!TimeOnly.TryParse(GetString(x, "hora_inicio"), out var time))
                    return false;

                return start <= end
                    ? time >= start && time < end
                    : time >= start || time < end;
            });
    }

    private static void SortAttractions(List<JsonObject> items, string? sorter)
    {
        var sorted = (sorter ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "lowest_price" => items.OrderBy(x => GetDecimal(x, "precio_desde", decimal.MaxValue)).ThenBy(x => GetString(x, "nombre")),
            "highest_price" => items.OrderByDescending(x => GetDecimal(x, "precio_desde", 0)).ThenBy(x => GetString(x, "nombre")),
            "highest_weighted_rating" => items.OrderByDescending(x => GetDecimal(x, "calificacion", 0)).ThenByDescending(x => GetInt(x, "total_resenas", 0)),
            _ => items.OrderByDescending(x => GetInt(x, "total_resenas", 0)).ThenByDescending(x => GetDecimal(x, "calificacion", 0))
        };

        var ordered = sorted.ToList();
        items.Clear();
        items.AddRange(ordered);
    }

    private static string? NormalizeLanguageQuery(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return language;

        return language.Length <= 3 ? language.Trim() : LanguageCode(language);
    }

    private static bool TryReadPagination(HttpContext context, out int page, out int limit, out IResult error)
    {
        page = 1;
        limit = 10;
        error = Results.Empty;

        var rawPage = context.Request.Query["page"].ToString();
        var rawLimit = context.Request.Query["limit"].ToString();

        if (!string.IsNullOrWhiteSpace(rawPage) && (!int.TryParse(rawPage, out page) || page < 1))
        {
            error = Results.Json(Error(400, "Parametro invalido", "El campo page debe ser un entero mayor o igual a 1."), statusCode: 400);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rawLimit) && (!int.TryParse(rawLimit, out limit) || limit < 1 || limit > 50))
        {
            error = Results.Json(Error(400, "Parametro invalido", "El campo limit debe ser un entero entre 1 y 50."), statusCode: 400);
            return false;
        }

        return true;
    }

    private static bool IsValidGuest(JsonObject? guest, out string error)
    {
        error = string.Empty;
        if (guest is null)
        {
            error = "cliente_invitado es obligatorio para crear reservas desde Booking V2.";
            return false;
        }

        var required = new[]
        {
            "tipo_identificacion",
            "numero_identificacion",
            "nombres",
            "apellidos",
            "correo",
            "telefono",
            "direccion"
        };

        foreach (var field in required)
        {
            if (string.IsNullOrWhiteSpace(GetString(guest, field)))
            {
                error = $"cliente_invitado.{field} es obligatorio.";
                return false;
            }
        }

        return true;
    }

    private static JsonObject? FindByGuid(JsonArray source, string? guid)
    {
        return source.OfType<JsonObject>().FirstOrDefault(x => string.Equals(GetString(x, "guid"), guid, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<ServiceJsonResponse> GetServiceJsonAsync(HttpClient client, IConfiguration configuration, string serviceKey, string path, CancellationToken cancellationToken)
    {
        var uri = new Uri($"{GetServiceBaseUrl(configuration, serviceKey)}{path}");
        using var response = await client.GetAsync(uri, cancellationToken);
        return await ServiceJsonResponse.FromAsync(response, cancellationToken);
    }

    private static async Task<ServiceJsonResponse> SendServiceJsonAsync(
        HttpClient client,
        IConfiguration configuration,
        string serviceKey,
        HttpMethod method,
        string path,
        JsonObject body,
        HttpContext context)
    {
        var uri = new Uri($"{GetServiceBaseUrl(configuration, serviceKey)}{path}");
        using var request = new HttpRequestMessage(method, uri)
        {
            Content = new StringContent(body.ToJsonString(JsonOptions), Encoding.UTF8, "application/json")
        };

        if (context.Request.Headers.TryGetValue("Authorization", out var authorization))
            request.Headers.TryAddWithoutValidation("Authorization", authorization.ToArray());

        using var response = await client.SendAsync(request, context.RequestAborted);
        return await ServiceJsonResponse.FromAsync(response, context.RequestAborted);
    }

    private static string GetServiceBaseUrl(IConfiguration configuration, string serviceKey)
    {
        var raw = configuration[$"ServiceUrls:{serviceKey}"]
            ?? configuration[$"GrpcServiceUrls:{serviceKey}"]
            ?? throw new InvalidOperationException($"No se configuro URL para {serviceKey}.");

        return NormalizeBaseUrl(raw);
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        var normalized = baseUrl.Trim().TrimEnd('/');
        foreach (var suffix in new[] { "/swagger/index.html", "/swagger", "/api/v1", "/api/v2" })
        {
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                normalized = normalized[..^suffix.Length].TrimEnd('/');
        }

        return normalized;
    }

    private static JsonObject Envelope(int status, string message, JsonNode? data)
    {
        return new JsonObject
        {
            ["status"] = status,
            ["message"] = message,
            ["data"] = data is null ? null : Clone(data)
        };
    }

    private static JsonObject Error(int status, string error, string detail)
    {
        return new JsonObject
        {
            ["status"] = status,
            ["error"] = error,
            ["details"] = new JsonArray(detail),
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
        };
    }

    private static JsonObject Pagination(int page, int limit, int total)
    {
        return new JsonObject
        {
            ["page"] = page,
            ["limit"] = limit,
            ["total"] = total,
            ["total_pages"] = (int)Math.Ceiling(total / (double)limit)
        };
    }

    private static JsonArray DataArray(JsonObject? root)
    {
        return root?["data"] as JsonArray ?? new JsonArray();
    }

    private static async Task<JsonObject> ReadJsonObjectAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
        var raw = await reader.ReadToEndAsync(context.RequestAborted);
        return JsonNode.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw)?.AsObject() ?? new JsonObject();
    }

    private static JsonNode? Clone(JsonNode? node)
    {
        return node is null ? null : JsonNode.Parse(node.ToJsonString());
    }

    private static string? GetString(JsonObject? obj, string name)
    {
        if (obj is null || obj[name] is null)
            return null;

        return obj[name]!.GetValueKind() == JsonValueKind.String
            ? obj[name]!.GetValue<string>()
            : obj[name]!.ToJsonString().Trim('"');
    }

    private static int GetInt(JsonObject? obj, string name, int fallback)
    {
        if (obj is null || obj[name] is null)
            return fallback;

        return int.TryParse(GetString(obj, name), out var value) ? value : fallback;
    }

    private static int? GetNullableInt(JsonObject obj, string name)
    {
        return int.TryParse(GetString(obj, name), out var value) ? value : null;
    }

    private static decimal GetDecimal(JsonObject? obj, string name, decimal fallback)
    {
        if (obj is null || obj[name] is null)
            return fallback;

        return decimal.TryParse(GetString(obj, name), out var value) ? value : fallback;
    }

    private static bool GetBool(JsonObject obj, string name)
    {
        return bool.TryParse(GetString(obj, name), out var value) && value;
    }

    private static bool GetBoolValue(JsonObject? obj, string name)
    {
        return bool.TryParse(GetString(obj, name), out var value) && value;
    }

    private static string? GetNodeString(JsonNode? node)
    {
        if (node is null)
            return null;

        return node.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : node.ToJsonString().Trim('"');
    }

    private static int GetIntQuery(HttpContext context, string key, int fallback)
    {
        return int.TryParse(context.Request.Query[key].ToString(), out var value) ? value : fallback;
    }

    private static string Slug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().ToLowerInvariant()
            .Replace("á", "a")
            .Replace("é", "e")
            .Replace("í", "i")
            .Replace("ó", "o")
            .Replace("ú", "u")
            .Replace("ñ", "n");

        var chars = normalized.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string LanguageCode(string? name)
    {
        var slug = Slug(name);
        return slug switch
        {
            "ingles" or "english" => "en",
            "frances" => "fr",
            "italiano" => "it",
            "aleman" => "de",
            "ruso" => "ru",
            "portugues" => "pt",
            "japones" => "ja",
            "arabe" => "ar",
            "polaco" => "pl",
            _ => "es"
        };
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }

    private static string MapReservaEstado(string? estado)
    {
        return estado?.ToUpperInvariant() switch
        {
            "C" => "CANCELADA",
            "P" => "PAGADA",
            "A" => "PENDIENTE",
            _ => estado ?? "PENDIENTE"
        };
    }

    private sealed class ReservaContext
    {
        public string? AtraccionNombre { get; set; }
        public string? Fecha { get; set; }
        public string? HoraInicio { get; set; }
        public string? HoraFin { get; set; }
        public Dictionary<string, JsonObject> Tickets { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ServiceJsonResponse
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
                Raw = raw
            };
        }

        public IResult ToResult()
        {
            return Results.Content(Raw, "application/json", Encoding.UTF8, StatusCode);
        }
    }
}
