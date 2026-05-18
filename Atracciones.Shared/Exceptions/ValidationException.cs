namespace Atracciones.Shared.Exceptions;

public class ValidationException : BusinessException
{
    public ValidationException(string message, IReadOnlyCollection<string>? errors = null)
        : base(message)
    {
        Errors = errors ?? Array.Empty<string>();
    }

    public IReadOnlyCollection<string> Errors { get; }
}
