using System.Text.Json;
using HotChocolate.Types;

namespace ApiGateway.Api.GraphQL;

public class BookingMutation
{
    [GraphQLDescription("Crea una reserva pendiente usando el contrato Booking v2.")]
    [GraphQLType(typeof(AnyType))]
    public Task<object?> CrearReserva(
        [GraphQLType(typeof(AnyType))] JsonElement input,
        [Service] GraphQLGatewayProxy proxy,
        CancellationToken cancellationToken)
    {
        return proxy.PostAsync("/api/v2/reservas", input, cancellationToken);
    }

    [GraphQLDescription("Confirma pago y emite factura para una reserva.")]
    [GraphQLType(typeof(AnyType))]
    public Task<object?> ConfirmarPagoReserva(
        Guid guid,
        [GraphQLType(typeof(AnyType))] JsonElement input,
        [Service] GraphQLGatewayProxy proxy,
        CancellationToken cancellationToken)
    {
        return proxy.PostAsync($"/api/v2/reservas/{guid}/pagos/confirmacion", input, cancellationToken);
    }

    [GraphQLDescription("Cancela una reserva.")]
    [GraphQLType(typeof(AnyType))]
    public Task<object?> CancelarReserva(
        Guid guid,
        [GraphQLType(typeof(AnyType))] JsonElement input,
        [Service] GraphQLGatewayProxy proxy,
        CancellationToken cancellationToken)
    {
        return proxy.PatchAsync($"/api/v2/reservas/{guid}/cancelar", input, cancellationToken);
    }
}
