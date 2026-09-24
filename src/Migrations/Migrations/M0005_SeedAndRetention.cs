using Core;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Migrations.Migrations;

[DbContext(typeof(CoreDbContext))]
[Migration("20260924000005_SeedAndRetention")]
public sealed class M0005_SeedAndRetention() : SqlFileMigration("0005_seed_and_retention.sql");
