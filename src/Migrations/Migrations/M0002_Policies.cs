using Core;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Migrations.Migrations;

[DbContext(typeof(CoreDbContext))]
[Migration("20260924000002_Policies")]
public sealed class M0002_Policies() : SqlFileMigration("0002_policies.sql");
