using Microsoft.EntityFrameworkCore;

namespace MindLift.Application.Abstractions;

/// <summary>
/// The only database access surface that application services may use.
/// Implemented by MindLift.Infrastructure's AppDbContext.
/// </summary>
public interface IAppDbContext
{
    DbSet<TEntity> Set<TEntity>() where TEntity : class;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
