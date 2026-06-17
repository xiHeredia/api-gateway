using HotChocolate.Types;

namespace ApiGateway.Api.GraphQL;

public class BookingQuery
{
    [GraphQLDescription("Lista atracciones segun el contrato Booking v2.")]
    [GraphQLType(typeof(AnyType))]
    public Task<object?> Atracciones(
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
    [GraphQLType(typeof(AnyType))]
    public Task<object?> FiltrosAtracciones([Service] GraphQLGatewayProxy proxy, CancellationToken cancellationToken)
    {
        return proxy.GetAsync("/api/v2/atracciones/filtros", null, cancellationToken);
    }

    [GraphQLDescription("Detalle de una atraccion.")]
    [GraphQLType(typeof(AnyType))]
    public Task<object?> Atraccion(Guid guid, [Service] GraphQLGatewayProxy proxy, CancellationToken cancellationToken)
    {
        return proxy.GetAsync($"/api/v2/atracciones/{guid}", null, cancellationToken);
    }

    [GraphQLDescription("Tickets de una atraccion.")]
    [GraphQLType(typeof(AnyType))]
    public Task<object?> TicketsAtraccion(Guid guid, [Service] GraphQLGatewayProxy proxy, CancellationToken cancellationToken)
    {
        return proxy.GetAsync($"/api/v2/atracciones/{guid}/tickets", null, cancellationToken);
    }

    [GraphQLDescription("Horarios de una atraccion.")]
    [GraphQLType(typeof(AnyType))]
    public Task<object?> HorariosAtraccion(Guid guid, [Service] GraphQLGatewayProxy proxy, CancellationToken cancellationToken)
    {
        return proxy.GetAsync($"/api/v2/atracciones/{guid}/horarios", null, cancellationToken);
    }

    [GraphQLDescription("Reservas del canal Booking.")]
    [GraphQLType(typeof(AnyType))]
    public Task<object?> Reservas(
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
    [GraphQLType(typeof(AnyType))]
    public Task<object?> Reserva(Guid guid, [Service] GraphQLGatewayProxy proxy, CancellationToken cancellationToken)
    {
        return proxy.GetAsync($"/api/v2/reservas/{guid}", null, cancellationToken);
    }
}
