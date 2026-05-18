namespace Atracciones.Shared.Exceptions;

public class UnauthorizedBusinessException : BusinessException
{
    public UnauthorizedBusinessException(string message)
        : base(message)
    {
    }
}
