namespace AgroShield.Domain.Exceptions;

public class NotFoundException : AppException
{
    public NotFoundException(string message) : base(message, 404) { }
}
