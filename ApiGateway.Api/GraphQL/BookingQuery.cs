using System.Text.Json;
using HotChocolate.Types;

namespace ApiGateway.Api.GraphQL;

public class BookingQuery
{
    [GraphQLDescription("Lista atracciones segun el contrato Booking v2.")]
    public Task<JsonElement> Atracciones(
        int? page,
        int? limit,
        string? destino,
        string? tipo,
        string? etiqueta,
        string? ordenarPor,
        [Service] GraphQLGatewayProxy proxy,
        CancellationToken cancellationToken)
    {
        return proxy.GetAsync("/api/v2/atracciones", new Dictionary<string, object?>
        {
            ["page"] = page,
            ["limit"] = limit,
            ["destino"] = destino,
            ["tipo"] = tipo,
            ["etiqueta"] = etiqueta,
            ["ordenar_por"] = ordenarPor
        }, cancellationToken);
    }

    [GraphQLDescription("Devuelve filtros disponibles para Booking.")]
    public Task<JsonElement> FiltrosAtracciones([Service] GraphQLGatewayProxy proxy, CancellationToken cancellationToken)
    {
        return proxy.GetAsync("/api/v2/atracciones/filtros", null, cancellationToken);
    }

    [GraphQLDescription("Detalle de una atraccion.")]
    public Task<JsonElement> Atraccion(Guid guid, [Service] GraphQLGatewayProxy proxy, CancellationToken cancellationToken)
    {
        return proxy.GetAsync($"/api/v2/atracciones/{guid}", null, cancellationToken);
    }

    [GraphQLDescription("Tickets de una atraccion.")]
    public Task<JsonElement> TicketsAtraccion(Guid guid, [Service] GraphQLGatewayProxy proxy, CancellationToken cancellationToken)
    {
        return proxy.GetAsync($"/api/v2/atracciones/{guid}/tickets", null, cancellationToken);
    }

    [GraphQLDescription("Horarios de una atraccion.")]
    public Task<JsonElement> HorariosAtraccion(Guid guid, [Service] GraphQLGatewayProxy proxy, CancellationToken cancellationToken)
    {
        return proxy.GetAsync($"/api/v2/atracciones/{guid}/horarios", null, cancellationToken);
    }

    [GraphQLDescription("Reservas del canal Booking.")]
    public Task<JsonElement> Reservas(
        int? page,
        int? limit,
        [Service] GraphQLGatewayProxy proxy,
        CancellationToken cancellationToken)
    {
        return proxy.GetAsync("/api/v2/reservas", new Dictionary<string, object?>
        {
            ["page"] = page,
            ["limit"] = limit
        }, cancellationToken);
    }

    [GraphQLDescription("Detalle de reserva.")]
    public Task<JsonElement> Reserva(Guid guid, [Service] GraphQLGatewayProxy proxy, CancellationToken cancellationToken)
    {
        return proxy.GetAsync($"/api/v2/reservas/{guid}", null, cancellationToken);
    }
}
