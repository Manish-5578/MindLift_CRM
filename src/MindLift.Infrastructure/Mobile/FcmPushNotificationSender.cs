using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;
using MindLift.Infrastructure.Persistence;

namespace MindLift.Infrastructure.Mobile;

/// <summary>
/// Sends push notifications to a user's registered Android devices through Firebase Cloud Messaging (HTTP v1).
/// Configure with <c>Fcm:ServiceAccountJson</c> (the service-account key JSON as a string, e.g. a secret env var
/// <c>Fcm__ServiceAccountJson</c>) or <c>Fcm:ServiceAccountPath</c>. When neither is set it does nothing.
/// </summary>
public sealed class FcmPushNotificationSender(
    AppDbContext db,
    IDataProtectionProvider dataProtectionProvider,
    IMobileDeviceService devices,
    IConfiguration config,
    IHttpClientFactory httpFactory,
    ILogger<FcmPushNotificationSender> logger) : IPushNotificationSender
{
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static string? _accessToken;
    private static DateTime _accessTokenExpiresUtc;

    private sealed record ServiceAccount(string ProjectId, string ClientEmail, string PrivateKey);

    private ServiceAccount? LoadAccount()
    {
        try
        {
            var json = config["Fcm:ServiceAccountJson"];
            var path = config["Fcm:ServiceAccountPath"];
            if (string.IsNullOrWhiteSpace(json) && !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                json = File.ReadAllText(path);
            }
            if (string.IsNullOrWhiteSpace(json)) return null;
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            return new ServiceAccount(
                r.GetProperty("project_id").GetString()!,
                r.GetProperty("client_email").GetString()!,
                r.GetProperty("private_key").GetString()!);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FCM service account is configured but could not be read.");
            return null;
        }
    }

    public async Task SendPushAsync(Guid tenantId, Guid userId, string title, string? body,
        IReadOnlyDictionary<string, string> payload, CancellationToken cancellationToken = default)
    {
        var account = LoadAccount();
        if (account is null) return;

        var rows = await db.MobileDevices.IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId && d.UserId == userId && d.RevokedAtUtc == null
                        && d.NotificationsAllowed && d.Provider == "fcm")
            .ToListAsync(cancellationToken);
        if (rows.Count == 0) return;

        string accessToken;
        try
        {
            accessToken = await GetAccessTokenAsync(account, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not obtain an FCM access token.");
            return;
        }

        var protector = dataProtectionProvider.CreateProtector("MindLift.Mobile.DeviceTokens");
        var http = httpFactory.CreateClient();
        var url = $"https://fcm.googleapis.com/v1/projects/{account.ProjectId}/messages:send";

        foreach (var device in rows)
        {
            try
            {
                var token = protector.Unprotect(device.TokenCiphertext);
                var message = new Dictionary<string, object>
                {
                    ["message"] = new Dictionary<string, object>
                    {
                        ["token"] = token,
                        ["notification"] = new Dictionary<string, string> { ["title"] = title, ["body"] = body ?? string.Empty },
                        ["data"] = payload.ToDictionary(k => k.Key, v => v.Value),
                        ["android"] = new Dictionary<string, object>
                        {
                            ["priority"] = "HIGH",
                            ["notification"] = new Dictionary<string, string> { ["channel_id"] = "mindlift_push" }
                        }
                    }
                };
                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(JsonSerializer.Serialize(message), Encoding.UTF8, "application/json")
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using var res = await http.SendAsync(req, cancellationToken);
                if (res.IsSuccessStatusCode)
                {
                    await devices.RecordPushSuccessAsync(device.Id, cancellationToken);
                }
                else
                {
                    var text = await res.Content.ReadAsStringAsync(cancellationToken);
                    var code = text.Contains("UNREGISTERED") ? "UNREGISTERED"
                        : text.Contains("INVALID_ARGUMENT") ? "INVALID_ARGUMENT"
                        : ((int)res.StatusCode).ToString();
                    logger.LogWarning("FCM rejected push for device {DeviceId}: {Status} {Body}", device.Id, (int)res.StatusCode, text);
                    await devices.RecordPushFailureAsync(device.Id, code, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "FCM push failed for device {DeviceId}", device.Id);
            }
        }
    }

    private async Task<string> GetAccessTokenAsync(ServiceAccount account, CancellationToken ct)
    {
        if (_accessToken is not null && DateTime.UtcNow < _accessTokenExpiresUtc.AddMinutes(-2)) return _accessToken;
        await TokenLock.WaitAsync(ct);
        try
        {
            if (_accessToken is not null && DateTime.UtcNow < _accessTokenExpiresUtc.AddMinutes(-2)) return _accessToken;

            static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var header = B64(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"));
            var claims = B64(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
            {
                ["iss"] = account.ClientEmail,
                ["scope"] = "https://www.googleapis.com/auth/firebase.messaging",
                ["aud"] = "https://oauth2.googleapis.com/token",
                ["iat"] = now,
                ["exp"] = now + 3600
            }));
            using var rsa = RSA.Create();
            rsa.ImportFromPem(account.PrivateKey);
            var sig = B64(rsa.SignData(Encoding.UTF8.GetBytes($"{header}.{claims}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

            var http = httpFactory.CreateClient();
            using var res = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = $"{header}.{claims}.{sig}"
            }), ct);
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            _accessToken = doc.RootElement.GetProperty("access_token").GetString()!;
            _accessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(doc.RootElement.GetProperty("expires_in").GetInt32());
            return _accessToken;
        }
        finally
        {
            TokenLock.Release();
        }
    }
}
