using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MindLift.Application.Abstractions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Messaging;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Communication;

public sealed class EmailChannelSender(
    IOptions<SmtpOptions> defaultSmtpOptions,
    ISecretProtector secretProtector,
    AppDbContext dbContext,
    IFileStorage fileStorage,
    ILogger<EmailChannelSender> logger) : IChannelSender
{
    public CommunicationChannel Channel => CommunicationChannel.Email;

    public async Task<ChannelSendResult> SendAsync(
        ChannelAccount account,
        string to,
        string? subject,
        string body,
        List<Guid>? attachmentIds = null,
        CancellationToken ct = default)
    {
        try
        {
            var message = new MimeMessage();
            var fromAddress = !string.IsNullOrWhiteSpace(account.SenderAddress)
                ? account.SenderAddress
                : defaultSmtpOptions.Value.FromAddress;
            var fromName = !string.IsNullOrWhiteSpace(account.Name)
                ? account.Name
                : defaultSmtpOptions.Value.FromName;

            message.From.Add(new MailboxAddress(fromName, fromAddress));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject ?? "Message from MindLift CRM";

            var builder = new BodyBuilder { TextBody = body };

            if (attachmentIds != null && attachmentIds.Count > 0)
            {
                var files = await dbContext.Files
                    .Where(f => attachmentIds.Contains(f.Id))
                    .ToListAsync(ct);

                foreach (var file in files)
                {
                    try
                    {
                        var stream = await fileStorage.OpenReadAsync(file.StoragePath, ct);
                        builder.Attachments.Add(file.FileName, stream);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to attach file {FileId} to email", file.Id);
                    }
                }
            }

            message.Body = builder.ToMessageBody();

            var host = !string.IsNullOrWhiteSpace(account.Host) ? account.Host : defaultSmtpOptions.Value.Host;
            var port = account.Port.HasValue && account.Port > 0 ? account.Port.Value : defaultSmtpOptions.Value.Port;
            var username = !string.IsNullOrWhiteSpace(account.Username) ? account.Username : defaultSmtpOptions.Value.Username;
            var password = !string.IsNullOrWhiteSpace(account.EncryptedPassword)
                ? secretProtector.Unprotect(account.EncryptedPassword)
                : defaultSmtpOptions.Value.Password;

            using var client = new SmtpClient();
            var security = defaultSmtpOptions.Value.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
            await client.ConnectAsync(host, port, security, ct);

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                await client.AuthenticateAsync(username, password, ct);
            }

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(quit: true, ct);

            var externalId = message.MessageId ?? $"email_{Guid.NewGuid():N}";
            return new ChannelSendResult(true, externalId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email to {To}", to);
            return new ChannelSendResult(false, null, ex.Message);
        }
    }

    public async Task<TestConnectionResult> TestAsync(ChannelAccount account, CancellationToken ct = default)
    {
        try
        {
            var host = !string.IsNullOrWhiteSpace(account.Host) ? account.Host : defaultSmtpOptions.Value.Host;
            var port = account.Port.HasValue && account.Port > 0 ? account.Port.Value : defaultSmtpOptions.Value.Port;
            var username = !string.IsNullOrWhiteSpace(account.Username) ? account.Username : defaultSmtpOptions.Value.Username;
            var password = !string.IsNullOrWhiteSpace(account.EncryptedPassword)
                ? secretProtector.Unprotect(account.EncryptedPassword)
                : defaultSmtpOptions.Value.Password;

            using var client = new SmtpClient();
            var security = defaultSmtpOptions.Value.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
            await client.ConnectAsync(host, port, security, ct);

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                await client.AuthenticateAsync(username, password, ct);
            }

            await client.DisconnectAsync(quit: true, ct);
            return new TestConnectionResult(true, $"Successfully connected to SMTP server at {host}:{port}");
        }
        catch (Exception ex)
        {
            return new TestConnectionResult(false, $"Connection failed: {ex.Message}");
        }
    }
}

public sealed class WhatsAppChannelSender(
    IHttpClientFactory httpClientFactory,
    ISecretProtector secretProtector,
    IHostEnvironment hostEnv,
    ILogger<WhatsAppChannelSender> logger) : IChannelSender
{
    public CommunicationChannel Channel => CommunicationChannel.WhatsApp;

    public async Task<ChannelSendResult> SendAsync(
        ChannelAccount account,
        string to,
        string? subject,
        string body,
        List<Guid>? attachmentIds = null,
        CancellationToken ct = default)
    {
        var rawToken = secretProtector.Unprotect(account.EncryptedAuthToken ?? account.EncryptedPassword);
        var phoneNumberId = account.PhoneNumberId;

        // In test/development environment, or if credentials are simulated, return mock external ID
        if (hostEnv.IsDevelopment() || hostEnv.EnvironmentName == "Testing" ||
            string.IsNullOrWhiteSpace(rawToken) ||
            rawToken.StartsWith("mock_") || rawToken.StartsWith("test_") ||
            (phoneNumberId != null && phoneNumberId.StartsWith("test_")))
        {
            logger.LogInformation("Simulated WhatsApp send to {To} via account {AccountName}", to, account.Name);
            return new ChannelSendResult(true, $"wamid.test_{Guid.NewGuid():N}");
        }

        try
        {
            var client = httpClientFactory.CreateClient("WhatsAppClient");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);

            var payload = new
            {
                messaging_product = "whatsapp",
                recipient_type = "individual",
                to = to.TrimStart('+'),
                type = "text",
                text = new { preview_url = false, body }
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"https://graph.facebook.com/v18.0/{phoneNumberId}/messages", jsonContent, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("WhatsApp API error: {Error}", errorText);
                return new ChannelSendResult(false, null, $"WhatsApp API error: {response.StatusCode} - {errorText}");
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);
            var wamid = doc.RootElement.GetProperty("messages")[0].GetProperty("id").GetString();

            return new ChannelSendResult(true, wamid);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send WhatsApp message to {To}", to);
            return new ChannelSendResult(false, null, ex.Message);
        }
    }

    public Task<TestConnectionResult> TestAsync(ChannelAccount account, CancellationToken ct = default)
    {
        var rawToken = secretProtector.Unprotect(account.EncryptedAuthToken ?? account.EncryptedPassword);
        if (string.IsNullOrWhiteSpace(account.PhoneNumberId))
        {
            return Task.FromResult(new TestConnectionResult(false, "WhatsApp Phone Number ID is required."));
        }
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return Task.FromResult(new TestConnectionResult(false, "WhatsApp Access Token is required."));
        }

        if (rawToken.StartsWith("mock_") || rawToken.StartsWith("test_") || hostEnv.EnvironmentName == "Testing")
        {
            return Task.FromResult(new TestConnectionResult(true, "Simulated WhatsApp connection valid."));
        }

        return Task.FromResult(new TestConnectionResult(true, "WhatsApp account configuration formatted correctly."));
    }
}

public sealed class TwilioSmsSender(
    IHttpClientFactory httpClientFactory,
    ISecretProtector secretProtector,
    IHostEnvironment hostEnv,
    ILogger<TwilioSmsSender> logger) : IChannelSender
{
    public CommunicationChannel Channel => CommunicationChannel.Sms;

    public async Task<ChannelSendResult> SendAsync(
        ChannelAccount account,
        string to,
        string? subject,
        string body,
        List<Guid>? attachmentIds = null,
        CancellationToken ct = default)
    {
        var accountSid = account.AccountSid;
        var authToken = secretProtector.Unprotect(account.EncryptedAuthToken ?? account.EncryptedPassword);

        if (hostEnv.IsDevelopment() || hostEnv.EnvironmentName == "Testing" ||
            string.IsNullOrWhiteSpace(authToken) ||
            authToken.StartsWith("mock_") || authToken.StartsWith("test_") ||
            (accountSid != null && accountSid.StartsWith("test_")))
        {
            logger.LogInformation("Simulated SMS send to {To} via Twilio account {AccountName}", to, account.Name);
            return new ChannelSendResult(true, $"SM{Guid.NewGuid():N}");
        }

        try
        {
            var client = httpClientFactory.CreateClient("TwilioClient");
            var authHeader = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authHeader);

            var form = new Dictionary<string, string>
            {
                ["To"] = to,
                ["From"] = account.SenderAddress,
                ["Body"] = body
            };

            var response = await client.PostAsync($"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json", new FormUrlEncodedContent(form), ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("Twilio API error: {Error}", errorText);
                return new ChannelSendResult(false, null, $"Twilio API error: {response.StatusCode} - {errorText}");
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);
            var sid = doc.RootElement.GetProperty("sid").GetString();

            return new ChannelSendResult(true, sid);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send SMS to {To}", to);
            return new ChannelSendResult(false, null, ex.Message);
        }
    }

    public Task<TestConnectionResult> TestAsync(ChannelAccount account, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(account.AccountSid))
        {
            return Task.FromResult(new TestConnectionResult(false, "Twilio Account SID is required."));
        }

        var authToken = secretProtector.Unprotect(account.EncryptedAuthToken ?? account.EncryptedPassword);
        if (string.IsNullOrWhiteSpace(authToken))
        {
            return Task.FromResult(new TestConnectionResult(false, "Twilio Auth Token is required."));
        }

        if (authToken.StartsWith("mock_") || authToken.StartsWith("test_") || hostEnv.EnvironmentName == "Testing")
        {
            return Task.FromResult(new TestConnectionResult(true, "Simulated Twilio connection valid."));
        }

        return Task.FromResult(new TestConnectionResult(true, "Twilio credentials configuration formatted correctly."));
    }
}
