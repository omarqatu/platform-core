using Core;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Migrations.Migrations;

[DbContext(typeof(CoreDbContext))]
[Migration("20260924000006_DropAttributionColumns")]
public sealed class M0006_DropAttributionColumns() : SqlFileMigration("0006_drop_attribution_columns.sql");
