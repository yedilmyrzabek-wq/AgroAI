using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace AgroShield.Infrastructure.ExternalServices;

public class SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        var smtp = config.GetSection("Smtp");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(smtp["FromName"] ?? "AgroShield", smtp["FromAddress"]));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        using var client = new SmtpClient();
        try
        {
            var enableSsl = bool.Parse(smtp["EnableSsl"] ?? "true");
            var port = int.Parse(smtp["Port"] ?? "587");

            await client.ConnectAsync(
                smtp["Host"],
                port,
                enableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
                ct);

            await client.AuthenticateAsync(smtp["Username"], smtp["Password"], ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            logger.LogInformation("Email sent to {Email} [{Subject}]", to, subject);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email to {Email}", to);
        }
    }
}
