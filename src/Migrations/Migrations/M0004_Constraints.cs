using Core;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Migrations.Migrations;

[DbContext(typeof(CoreDbContext))]
[Migration("20260924000004_Constraints")]
public sealed class M0004_Constraints() : SqlFileMigration("0004_constraints.sql");
