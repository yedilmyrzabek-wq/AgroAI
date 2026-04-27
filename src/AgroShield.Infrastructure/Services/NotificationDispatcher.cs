using AgroShield.Application.Services;
using AgroShield.Domain.Entities;
using AgroShield.Domain.Enums;
using AgroShield.Infrastructure.ExternalServices;
using AgroShield.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace AgroShield.Infrastructure.Services;

public class NotificationDispatcher(
    AppDbContext db,
    IHttpClientFactory factory,
    IEmailSender email,
    IRealtimePublisher realtime,
    ILogger<NotificationDispatcher> logger) : INotificationDispatcher
{
    private static readonly JsonSerializerOptions SnakeOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task SendBatchFrozenAsync(Guid batchId)
    {
        var batch = await db.SupplyChainBatches
            .Include(b => b.Farm)
            .ThenInclude(f => f.Owner)
            .FirstOrDefaultAsync(b => b.Id == batchId);

        if (batch is null)
        {
            logger.LogWarning("NotificationDispatcher: batch {Id} not found", batchId);
            return;
        }

        var owner = batch.Farm.Owner;
        var message =
            $"🧊 *Партия {batch.BatchCode} заморожена*\n" +
            $"Ферма: {batch.Farm.Name}\n" +
            $"Причина: {batch.FreezeReason ?? "не указана"}\n" +
            $"Время: {batch.FrozenAt:yyyy-MM-dd HH:mm} UTC";

        if (owner.TelegramChatId is long chatId)
        {
            try
            {
                var bot = factory.CreateClient("TelegramBot");
                await bot.PostAsJsonAsync("/send", new
                {
                    telegram_chat_id = chatId,
                    message,
                    parse_mode = "Markdown",
                }, SnakeOpts);
            }
            catch (Exception ex) { logger.LogWarning(ex, "TelegramBot freeze notify failed"); }
        }

        if (!string.IsNullOrEmpty(owner.Email))
        {
            try
            {
                var html = $"<p>{System.Net.WebUtility.HtmlEncode(message).Replace("\n", "<br/>")}</p>";
                await email.SendAsync(owner.Email, $"AgroShield: партия {batch.BatchCode} заморожена", html);
            }
            catch (Exception ex) { logger.LogWarning(ex, "Email freeze notify failed"); }
        }

        try
        {
            await realtime.PushBatchFrozenAsync(batch.FarmId, new
            {
                batchId = batch.Id,
                batchCode = batch.BatchCode,
                reason = batch.FreezeReason,
                frozenAt = batch.FrozenAt,
            });
        }
        catch (Exception ex) { logger.LogWarning(ex, "SignalR freeze notify failed"); }

        db.Alerts.Add(new Alert
        {
            Id = Guid.NewGuid(),
            UserId = owner.Id,
            FarmId = batch.FarmId,
            Type = AlertType.Anomaly,
            Title = $"Партия {batch.BatchCode} заморожена",
            Message = batch.FreezeReason ?? "Партия заморожена инспектором",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
