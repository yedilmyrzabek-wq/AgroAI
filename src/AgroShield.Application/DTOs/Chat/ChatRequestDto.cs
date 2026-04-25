namespace AgroShield.Application.DTOs.Chat;

public class ChatRequestDto
{
    public string Message { get; set; } = null!;
    public string? SessionId { get; set; }
    public List<ChatHistoryItemDto> History { get; set; } = [];
}

public class ChatHistoryItemDto
{
    public string Role { get; set; } = null!;
    public string Content { get; set; } = null!;
}
