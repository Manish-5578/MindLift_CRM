namespace MindLift.Application.Abstractions;

/// <summary>
/// Stores file bytes. Keys are server-generated and tenant-prefixed, e.g. "{tenantId}/{guid}.pdf".
/// Implementations must never let a key escape the storage root.
/// </summary>
public interface IFileStorage
{
    Task<string> SaveAsync(string key, Stream content, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
}
