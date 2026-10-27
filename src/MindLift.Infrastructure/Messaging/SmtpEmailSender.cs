using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MindLift.Application.Abstractions;

namespace MindLift.Infrastructure.Messaging;

/// <summary>Sends plain-text email through SMTP (Mailpit in development). Localized HTML templates come later (plan 20.8).</summary>
public sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public Task SendAsync(string to, string subject, string textBody, CancellationToken cancellationToken = default)
        => SendAsync(to, subject, textBody, [], cancellationToken);

    public Task SendHtmlAsync(string to, string subject, string textBody, string htmlBody, CancellationToken cancellationToken = default)
        => DeliverAsync(to, subject, textBody, [], htmlBody, cancellationToken);

    public Task SendAsync(string to, string subject, string textBody, IReadOnlyList<EmailAttachment> attachments, CancellationToken cancellationToken = default)
        => DeliverAsync(to, subject, textBody, attachments, null, cancellationToken);

    private async Task DeliverAsync(string to, string subject, string textBody, IReadOnlyList<EmailAttachment> attachments, string? htmlBody, CancellationToken cancellationToken)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        var body = new BodyBuilder { TextBody = textBody, HtmlBody = htmlBody };
        foreach (var attachment in attachments)
        {
            body.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        }

        message.Body = body.ToMessageBody();

        using var client = new SmtpClient();
        var security = _options.Port == 465
            ? SecureSocketOptions.SslOnConnect
            : (_options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto);
        await client.ConnectAsync(_options.Host, _options.Port, security, cancellationToken);

        if (!string.IsNullOrEmpty(_options.Username))
        {
            await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
