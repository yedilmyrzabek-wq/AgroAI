namespace AgroShield.Infrastructure.ExternalServices;

public static class EmailTemplates
{
    public static (string subject, string html) VerificationCodeEmail(string code, string purpose)
    {
        var subject = purpose == "Registration"
            ? "Регистрация в AgroShield"
            : "Сброс пароля AgroShield";

        var action = purpose == "Registration" ? "регистрации" : "сброса пароля";

        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:24px">
              <h2 style="color:#2d7a4f;margin-bottom:8px">AgroShield</h2>
              <p>Ваш код {action}:</p>
              <h1 style="letter-spacing:10px;font-size:52px;color:#2d7a4f;text-align:center;
                         background:#f0f7f2;padding:16px;border-radius:8px">{code}</h1>
              <p style="color:#666;font-size:14px">Действует 10 минут. Не передавайте код никому.</p>
            </div>
            """;

        return (subject, html);
    }
}
