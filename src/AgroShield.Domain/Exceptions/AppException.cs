namespace AgroShield.Domain.Exceptions;

public class AppException : Exception
{
    public int StatusCode { get; }
    public string? Code { get; }

    public AppException(string message, int statusCode = 500, string? code = null) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }
}
