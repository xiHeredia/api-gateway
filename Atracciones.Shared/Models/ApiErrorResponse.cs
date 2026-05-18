namespace Atracciones.Shared.Models;

public class ApiErrorResponse
{
    public bool Success { get; set; } = false;
    public string Message { get; set; } = null!;
    public IReadOnlyCollection<string> Errors { get; set; } = Array.Empty<string>();
}
