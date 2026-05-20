using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ApiGateway.Api.Swagger;

public class BookingPublicSwaggerDocumentFilter : IDocumentFilter
{
    private static readonly OpenApiSchema AnyObjectSchema = new()
    {
        Type = "object",
        AdditionalPropertiesAllowed = true
    };

    private static readonly OpenApiResponse JsonOk = new()
    {
        Description = "Respuesta JSON del microservicio.",
        Content =
        {
            ["application/json"] = new OpenApiMediaType
            {
                Schema = AnyObjectSchema
            }
        }
    };

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        swaggerDoc.Paths.Clear();

        AddGet(swaggerDoc, "/health", "ApiGateway.Api", "Estado del API Gateway.");

        AddPost(swaggerDoc, "/api/v1/auth/login", "Autenticacion", "Autentica un usuario y devuelve JWT.",
            RequestBody(("userName", "admin"), ("password", "123456")));
        AddPost(swaggerDoc, "/api/v1/auth/register", "Autenticacion", "Registra un usuario.",
            RequestBody(("userName", "cliente.demo@correo.com"), ("password", "123456"), ("roles", new[] { "CLIENTE" })));

        AddGet(swaggerDoc, "/api/v2/atracciones", "Booking - Atracciones", "Lista atracciones disponibles segun contrato Booking v2.",
            Query("ciudad", "string", "Filtra por ciudad."),
            Query("tipo", "string", "Filtro de tipo/categoria."),
            Query("subtipo", "string", "Filtro de subtipo/categoria hija."),
            Query("etiqueta", "string", "free_cancellation, skip_the_line."),
            Query("idioma", "string", "en, es, fr, it, de, ru, pt, ja, ar, pl."),
            Query("calificacion_min", "number", "Calificacion minima."),
            Query("horario", "string", "05:00-12:00, 12:00-18:00, 18:00-05:00."),
            Query("disponible", "boolean", "Filtrar solo atracciones con disponibilidad."),
            Query("ordenar_por", "string", "trending, lowest_price, highest_weighted_rating."),
            Query("page", "integer", "Numero de pagina. Default: 1."),
            Query("limit", "integer", "Resultados por pagina. Default: 10, maximo: 50."));
        AddGet(swaggerDoc, "/api/v2/atracciones/filtros", "Booking - Atracciones", "Devuelve filtros disponibles.",
            Query("ciudad", "string", "Filtra contadores por ciudad."));
        AddGet(swaggerDoc, "/api/v2/atracciones/{guid}", "Booking - Atracciones", "Detalle de una atraccion.", PathGuid("guid"));
        AddGet(swaggerDoc, "/api/v2/atracciones/{guid}/tickets", "Booking - Atracciones", "Tickets de una atraccion.", PathGuid("guid"));
        AddGet(swaggerDoc, "/api/v2/atracciones/{guid}/horarios", "Booking - Atracciones", "Horarios disponibles de una atraccion.", PathGuid("guid"));
        AddGet(swaggerDoc, "/api/v2/atracciones/{guid}/horarios/{horarioGuid}/tickets", "Booking - Atracciones",
            "Tickets disponibles para un horario concreto.",
            PathGuid("guid"),
            PathGuid("horarioGuid"));

        AddPost(swaggerDoc, "/api/v2/reservas", "Booking - Reservas", "Crea una reserva pendiente.",
            RequestBody(
                ("at_guid", "35000000-0000-0000-0000-000000000001"),
                ("hor_guid", "37000000-0000-0000-0000-000000000001"),
                ("lineas", new[] { new Dictionary<string, object?>
                {
                    ["tck_guid"] = "36000000-0000-0000-0000-000000000001",
                    ["cantidad"] = 2
                } }),
                ("origen_canal", "BOOKING"),
                ("cliente_invitado", new Dictionary<string, object?>
                {
                    ["tipo_identificacion"] = "CEDULA",
                    ["numero_identificacion"] = "1712345678",
                    ["nombres"] = "Juan Carlos",
                    ["apellidos"] = "Perez Gomez",
                    ["correo"] = "juan.perez@email.com",
                    ["telefono"] = "0991234567",
                    ["direccion"] = "Av. Principal 123"
                })));
        AddGet(swaggerDoc, "/api/v2/reservas", "Booking - Reservas", "Lista reservas creadas por el canal Booking. No requiere token.",
            Query("page", "integer", "Numero de pagina. Default: 1."),
            Query("limit", "integer", "Resultados por pagina. Default: 10."));
        AddGet(swaggerDoc, "/api/v2/reservas/{guid}", "Booking - Reservas", "Detalle de una reserva.", PathGuid("guid"));
        AddPost(swaggerDoc, "/api/v2/reservas/{guid}/pagos/confirmacion", "Booking - Reservas",
            "Confirma el pago de una reserva.",
            RequestBody(
                ("nombre_receptor", "Juan Carlos"),
                ("apellido_receptor", "Perez Gomez"),
                ("correo_receptor", "juan@email.com"),
                ("telefono_receptor", "0991234567"),
                ("observacion", "Pago confirmado desde Booking")),
            PathGuid("guid"));
    }

    private static void AddGet(OpenApiDocument doc, string path, string tag, string summary, params OpenApiParameter[] parameters)
    {
        AddOperation(doc, path, OperationType.Get, tag, summary, parameters: parameters);
    }

    private static void AddPost(OpenApiDocument doc, string path, string tag, string summary, OpenApiRequestBody? body = null, params OpenApiParameter[] parameters)
    {
        AddOperation(doc, path, OperationType.Post, tag, summary, body, parameters);
    }

    private static void AddOperation(
        OpenApiDocument doc,
        string path,
        OperationType type,
        string tag,
        string summary,
        OpenApiRequestBody? requestBody = null,
        params OpenApiParameter[] parameters)
    {
        if (!doc.Paths.TryGetValue(path, out var pathItem))
        {
            pathItem = new OpenApiPathItem();
            doc.Paths[path] = pathItem;
        }

        var successCode = type == OperationType.Post &&
            (path.Equals("/api/v2/reservas", StringComparison.OrdinalIgnoreCase) ||
             path.EndsWith("/pagos/confirmacion", StringComparison.OrdinalIgnoreCase))
            ? "201"
            : "200";

        pathItem.Operations[type] = new OpenApiOperation
        {
            Tags = new List<OpenApiTag> { new() { Name = tag } },
            Summary = summary,
            Parameters = parameters.ToList(),
            RequestBody = requestBody,
            Responses =
            {
                [successCode] = JsonOk,
                ["400"] = new OpenApiResponse { Description = "Solicitud invalida." },
                ["401"] = new OpenApiResponse { Description = "No autorizado." },
                ["403"] = new OpenApiResponse { Description = "Prohibido." },
                ["404"] = new OpenApiResponse { Description = "Recurso no encontrado." },
                ["409"] = new OpenApiResponse { Description = "Conflicto de negocio." },
                ["500"] = new OpenApiResponse { Description = "Error interno." }
            },
            Security = new List<OpenApiSecurityRequirement>()
        };
    }

    private static OpenApiParameter PathGuid(string name)
    {
        return new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Path,
            Required = true,
            Schema = new OpenApiSchema { Type = "string", Format = "uuid" }
        };
    }

    private static OpenApiParameter Query(string name, string type, string description, string? format = null)
    {
        return new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Query,
            Required = false,
            Description = description,
            Schema = new OpenApiSchema { Type = type, Format = format }
        };
    }

    private static OpenApiRequestBody RequestBody(params (string Name, object? Value)[] exampleValues)
    {
        var example = new OpenApiObject();
        var schema = new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>(),
            Required = new HashSet<string>()
        };

        foreach (var (name, value) in exampleValues)
        {
            example[name] = ToOpenApiAny(value);
            schema.Properties[name] = ToOpenApiSchema(value);

            if (value is not null)
                schema.Required.Add(name);
        }

        return new OpenApiRequestBody
        {
            Required = exampleValues.Length > 0,
            Content =
            {
                ["application/json"] = new OpenApiMediaType
                {
                    Schema = schema,
                    Example = example
                }
            }
        };
    }

    private static OpenApiSchema ToOpenApiSchema(object? value)
    {
        return value switch
        {
            null => new OpenApiSchema { Nullable = true },
            string text when Guid.TryParse(text, out _) => new OpenApiSchema { Type = "string", Format = "uuid" },
            string => new OpenApiSchema { Type = "string" },
            int => new OpenApiSchema { Type = "integer", Format = "int32" },
            decimal => new OpenApiSchema { Type = "number", Format = "decimal" },
            bool => new OpenApiSchema { Type = "boolean" },
            Dictionary<string, object?> dictionary => ToObjectSchema(dictionary),
            IEnumerable<string> => new OpenApiSchema
            {
                Type = "array",
                Items = new OpenApiSchema { Type = "string" }
            },
            IEnumerable<Dictionary<string, object?>> dictionaries => new OpenApiSchema
            {
                Type = "array",
                Items = ToObjectSchema(dictionaries.FirstOrDefault() ?? new Dictionary<string, object?>())
            },
            _ => new OpenApiSchema { Type = "string" }
        };
    }

    private static OpenApiSchema ToObjectSchema(Dictionary<string, object?> values)
    {
        var schema = new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>(),
            Required = new HashSet<string>()
        };

        foreach (var (name, value) in values)
        {
            schema.Properties[name] = ToOpenApiSchema(value);
            if (value is not null)
                schema.Required.Add(name);
        }

        return schema;
    }

    private static IOpenApiAny ToOpenApiAny(object? value)
    {
        return value switch
        {
            null => new OpenApiNull(),
            string text => new OpenApiString(text),
            int number => new OpenApiInteger(number),
            decimal number => new OpenApiDouble((double)number),
            bool flag => new OpenApiBoolean(flag),
            Dictionary<string, object?> dictionary => ToObject(dictionary),
            IEnumerable<string> strings => ToArray(strings.Select(x => new OpenApiString(x)).Cast<IOpenApiAny>()),
            IEnumerable<Dictionary<string, object?>> dictionaries => ToArray(dictionaries.Select(dictionary => (IOpenApiAny)ToObject(dictionary))),
            _ => new OpenApiString(value.ToString())
        };
    }

    private static OpenApiObject ToObject(Dictionary<string, object?> dictionary)
    {
        var obj = new OpenApiObject();
        foreach (var (key, item) in dictionary)
            obj[key] = ToOpenApiAny(item);
        return obj;
    }

    private static OpenApiArray ToArray(IEnumerable<IOpenApiAny> values)
    {
        var array = new OpenApiArray();
        foreach (var value in values)
            array.Add(value);
        return array;
    }
}
