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

        var host = smtp["Host"]?.Trim();
        var portRaw = smtp["Port"]?.Trim();
        var username = smtp["Username"]?.Trim();
        var password = smtp["Password"];               // do NOT trim — App Password может включать пробелы как разделители, но Gmail трактует и со/без пробелов одинаково
        var fromAddress = smtp["FromAddress"]?.Trim();
        var fromName = smtp["FromName"]?.Trim() ?? "AgroShield";
        var enableSslRaw = smtp["EnableSsl"]?.Trim();

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(fromAddress))
        {
            logger.LogError(
                "SMTP misconfigured: Host={HostSet} Username={UserSet} Password={PassSet} FromAddress={FromSet}. " +
                "Make sure Smtp__Host / Smtp__Username / Smtp__Password / Smtp__FromAddress env vars are set on Railway.",
                !string.IsNullOrEmpty(host), !string.IsNullOrEmpty(username),
                !string.IsNullOrEmpty(password), !string.IsNullOrEmpty(fromAddress));
            throw new InvalidOperationException("SMTP configuration incomplete");
        }

        var port = int.TryParse(portRaw, out var p) ? p : 587;
        var enableSsl = !bool.TryParse(enableSslRaw, out var s) || s;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        using var client = new SmtpClient();
        try
        {
            // Для Gmail: 587+StartTls или 465+SslOnConnect
            var socketOpts = enableSsl
                ? (port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls)
                : SecureSocketOptions.None;

            logger.LogInformation("SMTP connecting to {Host}:{Port} ({Opts}) as {User}",
                host, port, socketOpts, username);

            await client.ConnectAsync(host, port, socketOpts, ct);
            await client.AuthenticateAsync(username, password, ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            logger.LogInformation("Email sent to {Email} [{Subject}]", to, subject);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to send email to {Email} via {Host}:{Port} (ssl={Ssl}). " +
                "Error: {Type} - {Message}",
                to, host, port, enableSsl, ex.GetType().Name, ex.Message);
            // Не пробрасываем — регистрация не должна 500'ить из-за SMTP. Ошибка уже в логах.
        }
    }
}
