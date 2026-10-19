using Core;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Migrations.Migrations;

[DbContext(typeof(CoreDbContext))]
[Migration("20261019000014_BootstrapInvitation")]
public sealed class M0014_BootstrapInvitation() : SqlFileMigration("0014_bootstrap_invitation.sql");
