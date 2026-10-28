using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

public interface ILinkedInLookupService
{
    Task<LinkedInPersonProfileDto?> LookupPersonAsync(LookupLinkedInPersonRequest request, CancellationToken ct = default);
    Task<LinkedInCompanyProfileDto?> LookupCompanyAsync(LookupLinkedInCompanyRequest request, CancellationToken ct = default);
    Task<bool> ApplyToContactAsync(ApplyLinkedInToContactRequest request, CancellationToken ct = default);
    Task<bool> ApplyToLeadAsync(ApplyLinkedInToLeadRequest request, CancellationToken ct = default);
    Task<bool> ApplyToAccountAsync(ApplyLinkedInToAccountRequest request, CancellationToken ct = default);
}
