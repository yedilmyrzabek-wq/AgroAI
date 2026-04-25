using AgroShield.Application.Auth;
using AgroShield.Application.DTOs.Chat;
using AgroShield.Domain.Entities;
using AgroShield.Domain.Exceptions;
using AgroShield.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/assistant")]
[Authorize]
public class AssistantController(
    IHttpClientFactory factory,
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    ILogger<AssistantController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions SnakeOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    [HttpPost("chat")]
    public async Task Chat([FromBody] ChatRequestDto dto, CancellationToken ct)
    {
        var userId = currentUser.UserId;

        // 1. Get or create session
        ChatSession session;
        if (!string.IsNullOrEmpty(dto.SessionId) && Guid.TryParse(dto.SessionId, out var sid))
        {
            session = await db.ChatSessions.FindAsync([sid], ct)
                ?? throw new NotFoundException($"Session {dto.SessionId} not found");
        }
        else
        {
            session = new ChatSession
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = dto.Message.Length > 60 ? dto.Message[..60] + "…" : dto.Message,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.ChatSessions.Add(session);
        }

        db.ChatMessages.Add(new ChatMessage
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            Role = "user",
            Content = dto.Message,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Chat session {SessionId} started for {UserId}", session.Id, userId);

        // 2. SSE headers — must be set before first write
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        var feature = HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
        feature?.DisableBuffering();

        var finalText = "";

        try
        {
            var client = factory.CreateClient("AiAssistant");
            var payload = new
            {
                message = dto.Message,
                session_id = session.Id.ToString(),
                user_context = new
                {
                    user_id = userId.ToString(),
                    role = currentUser.Role.ToString().ToLower(),
                    username = currentUser.Email,
                    language = "ru",
                },
                history = dto.History,
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "/chat")
            {
                Content = JsonContent.Create(payload, options: SnakeOpts),
            };
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            string? currentEvent = null;
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                await Response.WriteAsync(line + "\n", ct);
                await Response.Body.FlushAsync(ct);

                if (line.StartsWith("event:"))
                    currentEvent = line["event:".Length..].Trim();
                else if (line.StartsWith("data:") && currentEvent == "done")
                {
                    try
                    {
                        var doc = JsonSerializer.Deserialize<JsonElement>(line["data:".Length..].Trim());
                        if (doc.TryGetProperty("final_text", out var ft))
                            finalText = ft.GetString() ?? "";
                    }
                    catch { }
                }
                else if (string.IsNullOrEmpty(line))
                    currentEvent = null;
            }
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ExternalServiceException)
        {
            logger.LogWarning(ex, "Chat SSE error for session {SessionId}", session.Id);
            var errJson = JsonSerializer.Serialize(new { error = "service_unavailable", message = ex.Message }, SnakeOpts);
            await Response.WriteAsync($"event: error\ndata: {errJson}\n\n");
            await Response.Body.FlushAsync();
        }

        // 3. Save assistant reply (ignore cancellation — data is more important)
        if (!string.IsNullOrEmpty(finalText))
        {
            db.ChatMessages.Add(new ChatMessage
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                Role = "assistant",
                Content = finalText,
                CreatedAt = DateTime.UtcNow,
            });
            session.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions(CancellationToken ct)
    {
        var sessions = await db.ChatSessions
            .Where(s => s.UserId == currentUser.UserId)
            .OrderByDescending(s => s.UpdatedAt)
            .Select(s => new { s.Id, s.Title, s.CreatedAt, s.UpdatedAt })
            .ToListAsync(ct);

        return Ok(sessions);
    }

    [HttpGet("sessions/{id:guid}/messages")]
    public async Task<IActionResult> GetMessages(Guid id, CancellationToken ct)
    {
        var session = await db.ChatSessions.FindAsync([id], ct)
            ?? throw new NotFoundException($"Session {id} not found");

        if (session.UserId != currentUser.UserId)
            throw new ForbiddenException("Access denied");

        var messages = await db.ChatMessages
            .Where(m => m.SessionId == id)
            .OrderByDescending(m => m.CreatedAt)
            .Take(50)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { m.Id, m.Role, m.Content, m.CreatedAt })
            .ToListAsync(ct);

        return Ok(messages);
    }

    [HttpDelete("sessions/{id:guid}")]
    public async Task<IActionResult> DeleteSession(Guid id, CancellationToken ct)
    {
        var session = await db.ChatSessions.FindAsync([id], ct)
            ?? throw new NotFoundException($"Session {id} not found");

        if (session.UserId != currentUser.UserId)
            throw new ForbiddenException("Access denied");

        db.ChatSessions.Remove(session);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
