using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Communication;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.CalendarSync;

/// <summary>
/// Keeps a person's MindLift meetings and their Google or Microsoft calendar in step: meetings are copied out (created, changed, removed),
/// and the times that are already taken over there are read back, so booking pages and the calendar know about them.
/// A person connects their own calendar; nobody else's calendar is read.
/// </summary>
public sealed class CalendarSyncService(
    AppDbContext db,
    ITenantContext tenant,
    IClock clock,
    IEnumerable<ICalendarProvider> providers,
    ISecretProtector protector,
    IConfiguration configuration,
    ILogger<CalendarSyncService> logger)
{
    private static readonly TimeSpan StateLife = TimeSpan.FromMinutes(15);
    private const int PushBackDays = 1;
    private const int PushAheadDays = 90;
    private const int PullAheadDays = 60;

    private string BaseUrl => (configuration["App:BaseUrl"] ?? string.Empty).TrimEnd('/');
    private Guid Me => tenant.UserId ?? throw new UnauthorizedException("not_signed_in", "Please sign in.");
    private ICalendarProvider Provider(string key) => providers.FirstOrDefault(p => p.Key == key) ?? throw new NotFoundException("Calendar service", key);

    private string RedirectUri(string provider) => $"{BaseUrl}/calendar/oauth/{provider}/callback";

    // ------------------------------------------------------------------ the person's settings

    public async Task<CalendarSyncDto> StatusAsync(CancellationToken cancellationToken = default)
    {
        var me = Me;
        var connections = await db.CalendarConnections.AsNoTracking().Where(c => c.UserId == me).OrderBy(c => c.Provider)
            .Select(c => new CalendarConnectionDto(c.Id, c.Provider, c.AccountEmail, c.LastSyncAt, c.LastError)).ToListAsync(cancellationToken);
        return new CalendarSyncDto(providers.Select(p => new CalendarProviderDto(p.Key, p.DisplayName, p.Configured)).ToList(), connections,
            await db.CalendarFeeds.AnyAsync(f => f.UserId == me, cancellationToken));
    }

    /// <summary>The address to send the person to, to give MindLift access to their calendar.</summary>
    public string ConnectUrl(string provider)
    {
        var p = Provider(provider);
        if (!p.Configured)
        {
            throw new BusinessRuleException("calendar_not_set_up", $"{p.DisplayName} has not been set up for this installation. Ask the administrator to add its client id and secret.");
        }

        var state = protector.Protect($"{tenant.TenantId:N}|{Me:N}|{provider}|{(clock.UtcNow + StateLife).Ticks}")!;
        return p.AuthorizeUrl(state, RedirectUri(provider));
    }

    /// <summary>Called when the calendar service sends the person back. Returns true when the connection was made.</summary>
    public async Task<bool> CompleteAsync(string provider, string? code, string? state, CancellationToken cancellationToken = default)
    {
        var parts = protector.Unprotect(state ?? string.Empty)?.Split('|');
        if (string.IsNullOrEmpty(code) || parts is not { Length: 4 } || parts[2] != provider || !Guid.TryParseExact(parts[0], "N", out var tenantId)
            || !Guid.TryParseExact(parts[1], "N", out var userId) || !long.TryParse(parts[3], out var ticks) || new DateTime(ticks, DateTimeKind.Utc) < clock.UtcNow)
        {
            return false;
        }

        using var scope = tenant.Use(tenantId, userId);
        var p = Provider(provider);
        try
        {
            var tokens = await p.ExchangeCodeAsync(code, RedirectUri(provider), cancellationToken);
            if (string.IsNullOrEmpty(tokens.RefreshToken))
            {
                logger.LogWarning("{Provider} gave no refresh token; the connection would stop working within the hour", provider);
                return false;
            }

            var email = await p.AccountEmailAsync(tokens.AccessToken, cancellationToken);
            var existing = await db.CalendarConnections.Where(c => c.UserId == userId && c.Provider == provider).ToListAsync(cancellationToken);
            foreach (var old in existing) await RemoveAsync(old, cancellationToken);

            db.CalendarConnections.Add(new CalendarConnection
            {
                UserId = userId, Provider = provider, AccountEmail = email,
                AccessToken = protector.Protect(tokens.AccessToken)!, RefreshToken = protector.Protect(tokens.RefreshToken)!, AccessExpiresAt = tokens.ExpiresAtUtc,
            });
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (CalendarProviderException ex)
        {
            logger.LogWarning(ex, "Connecting {Provider} failed", provider);
            return false;
        }
    }

    public async Task DisconnectAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var connection = await db.CalendarConnections.FirstOrDefaultAsync(c => c.Id == id && c.UserId == Me, cancellationToken) ?? throw new NotFoundException("Calendar connection", id);
        await RemoveAsync(connection, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Removing a connection takes the copies it made out of the other calendar as well.</summary>
    private async Task RemoveAsync(CalendarConnection connection, CancellationToken cancellationToken)
    {
        var links = await db.CalendarEventLinks.Where(l => l.ConnectionId == connection.Id).ToListAsync(cancellationToken);
        if (links.Count > 0)
        {
            try
            {
                var token = await AccessTokenAsync(connection, cancellationToken);
                foreach (var link in links) await Provider(connection.Provider).DeleteEventAsync(token, link.ExternalId, cancellationToken);
            }
            catch (Exception ex) when (ex is CalendarProviderException or HttpRequestException)
            {
                logger.LogInformation(ex, "Could not remove copies from {Provider} while disconnecting", connection.Provider);
            }
        }

        db.CalendarEventLinks.RemoveRange(links);
        db.ExternalBusyBlocks.RemoveRange(await db.ExternalBusyBlocks.Where(b => b.ConnectionId == connection.Id).ToListAsync(cancellationToken));
        db.CalendarConnections.Remove(connection);
    }

    // ------------------------------------------------------------------ the feed

    public async Task<string> NewFeedAsync(CancellationToken cancellationToken = default)
    {
        var me = Me;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var row = await db.CalendarFeeds.FirstOrDefaultAsync(f => f.UserId == me, cancellationToken);
        if (row is null) db.CalendarFeeds.Add(row = new CalendarFeed { UserId = me });
        row.TokenHash = Hash(token);
        await db.SaveChangesAsync(cancellationToken);
        return $"{BaseUrl}/calendar/feed/{token}.ics";
    }

    public async Task RemoveFeedAsync(CancellationToken cancellationToken = default)
    {
        var me = Me;
        db.CalendarFeeds.RemoveRange(await db.CalendarFeeds.Where(f => f.UserId == me).ToListAsync(cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The calendar a feed address names, as an .ics file, or null when the address is wrong. Works without signing in.</summary>
    public async Task<string?> FeedAsync(string token, CancellationToken cancellationToken = default)
    {
        var hash = Hash(token);
        var feed = await db.CalendarFeeds.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(f => f.TokenHash == hash && !f.IsDeleted, cancellationToken);
        if (feed is null)
        {
            return null;
        }

        using var scope = tenant.Use(feed.TenantId, feed.UserId);
        var now = clock.UtcNow;
        var meetings = await MeetingsAsync(feed.UserId, now.AddDays(-30), now.AddDays(180), cancellationToken);
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//MindLift//Calendar//EN\r\nCALSCALE:GREGORIAN\r\nMETHOD:PUBLISH\r\nX-WR-CALNAME:MindLift\r\n");
        foreach (var m in meetings)
        {
            sb.Append("BEGIN:VEVENT\r\n");
            Line(sb, "UID", $"{m.Id:N}@mindlift");
            Line(sb, "DTSTAMP", Stamp(now));
            Line(sb, "DTSTART", Stamp(m.Start));
            Line(sb, "DTEND", Stamp(m.End));
            Line(sb, "SUMMARY", Escape(m.Subject));
            if (!string.IsNullOrWhiteSpace(m.Location)) Line(sb, "LOCATION", Escape(m.Location));
            if (!string.IsNullOrWhiteSpace(m.Description)) Line(sb, "DESCRIPTION", Escape(m.Description));
            sb.Append("END:VEVENT\r\n");
        }

        return sb.Append("END:VCALENDAR\r\n").ToString();
    }

    private static string Stamp(DateTime utc) => utc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", string.Empty);

    /// <summary>One content line, folded at 75 bytes as the format requires.</summary>
    private static void Line(StringBuilder sb, string name, string value)
    {
        var line = $"{name}:{value}";
        var bytes = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (bytes + size > 74) { sb.Append("\r\n "); bytes = 1; }
            sb.Append(rune.ToString());
            bytes += size;
        }

        sb.Append("\r\n");
    }

    // ------------------------------------------------------------------ syncing

    private sealed record Meeting(Guid Id, string Subject, string? Description, string? Location, DateTime Start, DateTime End);

    private async Task<List<Meeting>> MeetingsAsync(Guid userId, DateTime from, DateTime to, CancellationToken cancellationToken)
        => (await db.Activities.AsNoTracking()
                .Where(a => a.OwnerId == userId && a.Type == ActivityTypes.Meeting && a.Status == "Open" && a.DueAt != null && a.DueAt >= from && a.DueAt < to)
                .OrderBy(a => a.DueAt).Take(1000)
                .Select(a => new { a.Id, a.Subject, a.Description, a.Location, Start = a.DueAt!.Value, a.EndAt }).ToListAsync(cancellationToken))
            .Select(a => new Meeting(a.Id, a.Subject, a.Description, a.Location, a.Start, a.EndAt ?? a.Start.AddHours(1))).ToList();

    /// <summary>Syncs the signed-in person's connections now.</summary>
    public async Task SyncMineAsync(CancellationToken cancellationToken = default)
    {
        var me = Me;
        foreach (var id in await db.CalendarConnections.Where(c => c.UserId == me).Select(c => c.Id).ToListAsync(cancellationToken))
        {
            await SyncAsync(id, cancellationToken);
        }
    }

    public async Task SyncAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        var connection = await db.CalendarConnections.FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken);
        if (connection is null) return;
        var p = Provider(connection.Provider);
        var now = clock.UtcNow;
        try
        {
            var token = await AccessTokenAsync(connection, cancellationToken);
            await PushAsync(connection, p, token, now, cancellationToken);
            await PullAsync(connection, p, token, now, cancellationToken);
            connection.LastSyncAt = now;
            connection.LastError = null;
        }
        catch (CalendarProviderException ex)
        {
            logger.LogWarning(ex, "Calendar sync with {Provider} failed", connection.Provider);
            connection.LastError = ex.NeedsReconnect ? "The connection no longer works. Disconnect and connect again." : ex.Message;
        }
        catch (HttpRequestException ex)
        {
            connection.LastError = "The calendar service could not be reached. It will be tried again.";
            logger.LogInformation(ex, "Calendar service unreachable");
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Meetings out: new ones are created over there, changed ones updated, and ones that were done, cancelled or removed are deleted.</summary>
    private async Task PushAsync(CalendarConnection connection, ICalendarProvider p, string token, DateTime now, CancellationToken cancellationToken)
    {
        var meetings = await MeetingsAsync(connection.UserId, now.AddDays(-PushBackDays), now.AddDays(PushAheadDays), cancellationToken);
        var links = await db.CalendarEventLinks.Where(l => l.ConnectionId == connection.Id).ToListAsync(cancellationToken);

        foreach (var m in meetings)
        {
            var external = new ExternalEvent(m.Subject, m.Description, m.Location, m.Start, m.End);
            var hash = Hash($"{m.Subject}|{m.Description}|{m.Location}|{m.Start:o}|{m.End:o}");
            var link = links.FirstOrDefault(l => l.ActivityId == m.Id);
            if (link is null)
            {
                var id = await p.CreateEventAsync(token, external, cancellationToken);
                db.CalendarEventLinks.Add(new CalendarEventLink { ConnectionId = connection.Id, ActivityId = m.Id, ExternalId = id, ContentHash = hash });
            }
            else if (link.ContentHash != hash)
            {
                await p.UpdateEventAsync(token, link.ExternalId, external, cancellationToken);
                link.ContentHash = hash;
            }
        }

        var current = meetings.Select(m => m.Id).ToHashSet();
        foreach (var gone in links.Where(l => !current.Contains(l.ActivityId)))
        {
            await p.DeleteEventAsync(token, gone.ExternalId, cancellationToken);
            db.CalendarEventLinks.Remove(gone);
        }
    }

    /// <summary>Times taken over there come in, replacing the last set. The copies MindLift itself made are left out, so a meeting is not counted twice.</summary>
    private async Task PullAsync(CalendarConnection connection, ICalendarProvider p, string token, DateTime now, CancellationToken cancellationToken)
    {
        var busy = await p.BusyAsync(token, now.AddDays(-1), now.AddDays(PullAheadDays), cancellationToken);
        var ours = (await db.CalendarEventLinks.AsNoTracking().Where(l => l.ConnectionId == connection.Id).Select(l => l.ExternalId).ToListAsync(cancellationToken)).ToHashSet();
        var old = await db.ExternalBusyBlocks.Where(b => b.ConnectionId == connection.Id).ToListAsync(cancellationToken);
        db.ExternalBusyBlocks.RemoveRange(old);
        foreach (var b in busy.Where(b => !ours.Contains(b.Id)))
        {
            db.ExternalBusyBlocks.Add(new ExternalBusyBlock { ConnectionId = connection.Id, UserId = connection.UserId, ExternalId = b.Id, StartAt = b.StartUtc, EndAt = b.EndUtc });
        }
    }

    private async Task<string> AccessTokenAsync(CalendarConnection connection, CancellationToken cancellationToken)
    {
        if (connection.AccessExpiresAt > clock.UtcNow.AddMinutes(2) && protector.Unprotect(connection.AccessToken) is { Length: > 0 } valid)
        {
            return valid;
        }

        var refresh = protector.Unprotect(connection.RefreshToken) ?? throw new CalendarProviderException(401, "The saved sign-in cannot be read.");
        var tokens = await Provider(connection.Provider).RefreshAsync(refresh, cancellationToken);
        connection.AccessToken = protector.Protect(tokens.AccessToken)!;
        connection.AccessExpiresAt = tokens.ExpiresAtUtc;
        if (!string.IsNullOrEmpty(tokens.RefreshToken)) connection.RefreshToken = protector.Protect(tokens.RefreshToken)!;
        await db.SaveChangesAsync(cancellationToken);
        return tokens.AccessToken;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

/// <summary>Every ten minutes: syncs every connected calendar, each inside its own company.</summary>
public sealed class CalendarSyncJob(AppDbContext db, ITenantContext tenant, CalendarSyncService sync, ILogger<CalendarSyncJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var all = await db.CalendarConnections.IgnoreQueryFilters().AsNoTracking().Where(c => !c.IsDeleted).Select(c => new { c.TenantId, c.UserId, c.Id }).ToListAsync(cancellationToken);
        foreach (var c in all)
        {
            try
            {
                using var scope = tenant.Use(c.TenantId, c.UserId);
                await sync.SyncAsync(c.Id, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Calendar sync for connection {Id} failed", c.Id);
            }
        }
    }
}
