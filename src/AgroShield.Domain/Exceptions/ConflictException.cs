namespace AgroShield.Domain.Exceptions;

public class ConflictException : AppException
{
    public ConflictException(string message) : base(message, 409) { }
    public ConflictException(string message, string code) : base(message, 409, code) { }
}
