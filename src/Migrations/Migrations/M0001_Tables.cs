using Core;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Migrations.Migrations;

[DbContext(typeof(CoreDbContext))]
[Migration("20260924000001_Tables")]
public sealed class M0001_Tables() : SqlFileMigration("0001_tables.sql");
