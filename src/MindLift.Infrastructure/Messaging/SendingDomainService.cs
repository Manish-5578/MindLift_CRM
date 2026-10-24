using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DnsClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Messaging;

/// <summary>Looks up a domain's TXT records. A seam so tests do not depend on the internet.</summary>
public interface IDnsTxtLookup
{
    Task<IReadOnlyList<string>> TxtAsync(string name, CancellationToken cancellationToken);
}

public sealed class DnsTxtLookup : IDnsTxtLookup
{
    private static readonly LookupClient Client = new(new LookupClientOptions { UseCache = false, Timeout = TimeSpan.FromSeconds(5), Retries = 1 });

    public async Task<IReadOnlyList<string>> TxtAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var result = await Client.QueryAsync(name, QueryType.TXT, cancellationToken: cancellationToken);
            return result.Answers.TxtRecords().Select(r => string.Concat(r.Text)).ToList();
        }
        catch (DnsResponseException)
        {
            return [];
        }
    }
}

/// <summary>
/// The domains a company sends mail from, and the DNS records that prove them: a TXT record for ownership, SPF that allows the platform's
/// mail service (Email:SpfInclude), and a DMARC policy. The records to add are shown on the settings page; "Check" looks them up.
/// </summary>
public sealed partial class SendingDomainService(AppDbContext db, ITenantContext tenant, IDnsTxtLookup dns, IConfiguration configuration, IClock clock, EmailGuard guard)
{
    private const string TokenPrefix = "mindlift-verification=";

    private string? SpfInclude => configuration["Email:SpfInclude"] is { Length: > 0 } include ? include : null;

    public async Task<EmailSendingDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var domains = await db.SendingDomains.AsNoTracking().OrderBy(d => d.Domain).ToListAsync(cancellationToken);
        var suppressed = await db.EmailSuppressions.AsNoTracking().OrderByDescending(s => s.CreatedAt).Take(200).ToListAsync(cancellationToken);
        return new EmailSendingDto(
            await guard.SentTodayAsync(cancellationToken),
            guard.DailyLimit,
            domains.Select(Map).ToList(),
            suppressed.Select(s => new EmailSuppressionDto(s.Id, s.Email, s.Reason, s.CreatedAt)).ToList());
    }

    public async Task<SendingDomainDto> AddAsync(AddSendingDomainRequest request, CancellationToken cancellationToken = default)
    {
        var domain = (request.Domain ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();
        if (domain.StartsWith("http", StringComparison.Ordinal) || !DomainShape().IsMatch(domain))
        {
            throw new ValidationException("domain", "Enter a domain such as example.com, without http:// or an email address.");
        }

        if (await db.SendingDomains.AnyAsync(d => d.Domain == domain, cancellationToken))
        {
            throw new ConflictException("domain_exists", "That domain is already added.");
        }

        var row = new SendingDomain { Domain = domain, VerificationToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant() };
        db.SendingDomains.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return Map(row);
    }

    public async Task<SendingDomainDto> CheckAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await db.SendingDomains.FirstOrDefaultAsync(d => d.Id == id, cancellationToken) ?? throw new NotFoundException("Domain", id);
        var root = await dns.TxtAsync(row.Domain, cancellationToken);
        var dmarc = await dns.TxtAsync($"_dmarc.{row.Domain}", cancellationToken);

        row.OwnershipVerified = root.Any(t => t.Trim() == TokenPrefix + row.VerificationToken);
        var spf = root.FirstOrDefault(t => t.StartsWith("v=spf1", StringComparison.OrdinalIgnoreCase));
        row.SpfOk = spf is not null && (SpfInclude is null || spf.Contains($"include:{SpfInclude}", StringComparison.OrdinalIgnoreCase));
        row.DmarcOk = dmarc.Any(t => t.StartsWith("v=DMARC1", StringComparison.OrdinalIgnoreCase));
        row.LastCheckedAt = clock.UtcNow;

        var missing = new List<string>();
        if (!row.OwnershipVerified) missing.Add("the ownership TXT record");
        if (!row.SpfOk) missing.Add(spf is null ? "an SPF record" : $"\"include:{SpfInclude}\" in the SPF record");
        if (!row.DmarcOk) missing.Add("a DMARC record");
        row.LastCheckMessage = missing.Count == 0 ? "Everything is in place." : "Not found yet: " + string.Join(", ", missing) + ". DNS changes can take up to an hour to appear.";

        await db.SaveChangesAsync(cancellationToken);
        return Map(row);
    }

    public async Task RemoveDomainAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await db.SendingDomains.FirstOrDefaultAsync(d => d.Id == id, cancellationToken) ?? throw new NotFoundException("Domain", id);
        row.IsDeleted = true;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Lets mail go to an address again (for example after the person asks to be added back).</summary>
    public async Task RemoveSuppressionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await db.EmailSuppressions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken) ?? throw new NotFoundException("Address", id);
        db.EmailSuppressions.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task AddSuppressionAsync(string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            throw new ValidationException("email", "Enter an email address.");
        }

        return guard.SuppressAsync(email, "manual", cancellationToken);
    }

    private SendingDomainDto Map(SendingDomain d)
    {
        var spfValue = SpfInclude is null ? "v=spf1 ~all (your mail service's include goes before ~all)" : $"v=spf1 include:{SpfInclude} ~all";
        return new SendingDomainDto(d.Id, d.Domain, d.OwnershipVerified && d.SpfOk && d.DmarcOk, d.OwnershipVerified, d.SpfOk, d.DmarcOk, d.LastCheckedAt, d.LastCheckMessage,
        [
            new DnsRecordDto("TXT", d.Domain, TokenPrefix + d.VerificationToken, "Proves the domain is yours.", d.OwnershipVerified),
            new DnsRecordDto("TXT", d.Domain, spfValue, "SPF: says which servers may send mail for the domain. If you already have an SPF record, add the include to it rather than creating a second one.", d.SpfOk),
            new DnsRecordDto("TXT", $"_dmarc.{d.Domain}", $"v=DMARC1; p=none; rua=mailto:dmarc@{d.Domain}", "DMARC: tells receiving mail servers what to do with mail that fails the checks. Start with p=none.", d.DmarcOk),
        ]);
    }

    [GeneratedRegex(@"^(?=.{4,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$")]
    private static partial Regex DomainShape();
}
