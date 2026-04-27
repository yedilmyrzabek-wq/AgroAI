using System.Text.Json.Serialization;

namespace AgroShield.Application.DTOs.Auth;

public record StartRegistrationRequest(string Email);
public record VerifyRegistrationRequest(string Email, string Code, string Password, string? FullName, string? PhoneNumber);
public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record StartPasswordResetRequest(string Email);
public record ConfirmPasswordResetRequest(string Email, string Code, string NewPassword);

public record TokenResponse(string AccessToken, string RefreshToken, int ExpiresIn, UserDto User);
public record UserDto(Guid Id, string Email, string? FullName, string Role, bool TelegramLinked, Guid? FarmId, string? AssignedRegion);

public record BotRegisterStartRequest(
    [property: JsonPropertyName("email")] string Email
);

public record BotRegisterVerifyRequest(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("telegram_chat_id")] long TelegramChatId,
    [property: JsonPropertyName("telegram_username")] string? TelegramUsername
);

public record BotLinkExistingRequest(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("telegram_chat_id")] long TelegramChatId,
    [property: JsonPropertyName("telegram_username")] string? TelegramUsername
);

public record BotUserContext(Guid UserId, string Email, string? FullName, string Role, Guid? FarmId);
