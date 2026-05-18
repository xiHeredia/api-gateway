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
        swaggerDoc.Paths.Remove("/api/v1/{path}");
        swaggerDoc.Paths.Remove("/api/v1/{**path}");

        AddPost(swaggerDoc, "/api/v1/auth/login", "Autenticacion", "Autentica un usuario y devuelve JWT.",
            RequestBody(("userName", "admin"), ("password", "123456")));
        AddPost(swaggerDoc, "/api/v1/auth/register", "Autenticacion", "Registra un usuario.",
            RequestBody(("userName", "cliente.demo@correo.com"), ("password", "123456"), ("roles", new[] { "CLIENTE" })));

        AddGet(swaggerDoc, "/api/v1/atracciones", "Booking - Atracciones", "Lista atracciones disponibles.",
            Query("nombre", "string", "Filtro opcional por nombre."),
            Query("destinoGuid", "string", "Filtro opcional por destino GUID.", "uuid"),
            Query("categoriaGuid", "string", "Filtro opcional por categoria GUID.", "uuid"),
            Query("page", "integer", "Pagina opcional para paginacion."),
            Query("pageSize", "integer", "Tamano de pagina opcional."));
        AddGet(swaggerDoc, "/api/v1/atracciones/filtros", "Booking - Atracciones", "Devuelve filtros disponibles.");
        AddGet(swaggerDoc, "/api/v1/atracciones/{guid}", "Booking - Atracciones", "Detalle de una atraccion.", PathGuid("guid"));
        AddGet(swaggerDoc, "/api/v1/atracciones/{guid}/tickets", "Booking - Atracciones", "Tickets de una atraccion.", PathGuid("guid"));
        AddGet(swaggerDoc, "/api/v1/atracciones/{guid}/horarios", "Booking - Atracciones", "Horarios de una atraccion.",
            PathGuid("guid"),
            Query("disponibles", "boolean", "Si true, devuelve solo horarios con cupo."));
        AddGet(swaggerDoc, "/api/v1/atracciones/{guid}/horarios/{horarioId}/tickets", "Booking - Atracciones",
            "Tickets disponibles para un horario concreto.",
            PathGuid("guid"),
            PathGuid("horarioId"));

        AddPost(swaggerDoc, "/api/v1/reservas", "Booking - Reservas", "Crea una reserva pendiente.",
            RequestBody(
                ("clienteGuid", "00000000-0000-0000-0000-000000000000"),
                ("horarioGuid", "00000000-0000-0000-0000-000000000000"),
                ("origenCanal", "BOOKING"),
                ("detalles", new[] { new Dictionary<string, object?>
                {
                    ["ticketGuid"] = "00000000-0000-0000-0000-000000000000",
                    ["cantidad"] = 1,
                    ["precioUnitario"] = 25.00m
                } })));
        AddGet(swaggerDoc, "/api/v1/reservas", "Booking - Reservas", "Lista reservas. Si hay token de cliente, filtra sus reservas.");
        AddGet(swaggerDoc, "/api/v1/reservas/{guid}", "Booking - Reservas", "Detalle de una reserva.", PathGuid("guid"));
        AddPost(swaggerDoc, "/api/v1/reservas/{guid}/pagos/confirmacion", "Booking - Reservas",
            "Confirma el pago de una reserva.", RequestBody(), PathGuid("guid"));

        AddGet(swaggerDoc, "/api/v1/atracciones/{guid}/resenias", "Booking - Resenias", "Lista resenias de una atraccion.", PathGuid("guid"));
        AddPost(swaggerDoc, "/api/v1/atracciones/{guid}/resenias", "Booking - Resenias", "Crea una resenia para una atraccion.",
            RequestBody(
                ("reservaGuid", "00000000-0000-0000-0000-000000000000"),
                ("rating", 5),
                ("comentario", "Excelente experiencia.")),
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

        pathItem.Operations[type] = new OpenApiOperation
        {
            Tags = new List<OpenApiTag> { new() { Name = tag } },
            Summary = summary,
            Parameters = parameters.ToList(),
            RequestBody = requestBody,
            Responses =
            {
                ["200"] = JsonOk,
                ["400"] = new OpenApiResponse { Description = "Solicitud invalida." },
                ["404"] = new OpenApiResponse { Description = "Recurso no encontrado." },
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
            IEnumerable<string> strings => ToArray(strings.Select(x => new OpenApiString(x)).Cast<IOpenApiAny>()),
            IEnumerable<Dictionary<string, object?>> dictionaries => ToArray(dictionaries.Select(dictionary =>
            {
                var obj = new OpenApiObject();
                foreach (var (key, item) in dictionary)
                    obj[key] = ToOpenApiAny(item);
                return (IOpenApiAny)obj;
            })),
            _ => new OpenApiString(value.ToString())
        };
    }

    private static OpenApiArray ToArray(IEnumerable<IOpenApiAny> values)
    {
        var array = new OpenApiArray();
        foreach (var value in values)
            array.Add(value);
        return array;
    }
}
