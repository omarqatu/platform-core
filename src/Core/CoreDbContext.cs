using Microsoft.EntityFrameworkCore;

namespace Core;

// The core schema's entities arrive in T2.
public class CoreDbContext : DbContext
{
    public CoreDbContext(DbContextOptions<CoreDbContext> options) : this((DbContextOptions)options)
    {
    }

    protected CoreDbContext(DbContextOptions options) : base(options)
    {
        // SaveChanges must not open a transaction of its own: outside the unit of
        // work its commands run with none, and the interceptor throws (3.5, T1.1).
        Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
    }
}
