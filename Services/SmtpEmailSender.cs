using FinzatiDailyReport.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace FinzatiDailyReport.Services;

public sealed class SmtpEmailSender
{
    private readonly EmailSettings _settings;
    public SmtpEmailSender(EmailSettings settings) => _settings = settings;

    public async Task SendHtmlAsync(string subject, string htmlBody, CancellationToken cancellationToken)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromAddress));
        foreach (var address in _settings.To.Where(x => !string.IsNullOrWhiteSpace(x))) message.To.Add(MailboxAddress.Parse(address));
        foreach (var address in _settings.Cc.Where(x => !string.IsNullOrWhiteSpace(x))) message.Cc.Add(MailboxAddress.Parse(address));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody, TextBody = "Finzati daily report. Please open this message in an HTML-capable email client." }.ToMessageBody();

        using var client = new SmtpClient();
        client.Timeout = 120_000;
        await client.ConnectAsync(_settings.Host, _settings.Port, ParseSecurity(_settings.Security), cancellationToken);
        if (!string.IsNullOrWhiteSpace(_settings.UserName)) await client.AuthenticateAsync(_settings.UserName, _settings.Password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    private static SecureSocketOptions ParseSecurity(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "ssl" or "sslonconnect" => SecureSocketOptions.SslOnConnect,
        "starttls" => SecureSocketOptions.StartTls,
        "starttlswhenavailable" => SecureSocketOptions.StartTlsWhenAvailable,
        "none" => SecureSocketOptions.None,
        _ => SecureSocketOptions.Auto
    };
}
