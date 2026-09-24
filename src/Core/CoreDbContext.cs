using Microsoft.EntityFrameworkCore;

namespace Core;

// The core schema's entities arrive in T2. Until then this context only carries
// the migration pipeline (T0.4).
public sealed class CoreDbContext(DbContextOptions<CoreDbContext> options) : DbContext(options);
