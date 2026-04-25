namespace AgroShield.Domain.Exceptions;

public class UnauthorizedException(string message = "Unauthorized") : AppException(message, 401);
