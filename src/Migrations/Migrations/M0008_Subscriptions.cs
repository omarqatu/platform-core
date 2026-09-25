using Core;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Migrations.Migrations;

[DbContext(typeof(CoreDbContext))]
[Migration("20260925000008_Subscriptions")]
public sealed class M0008_Subscriptions() : SqlFileMigration("0008_subscriptions.sql");
