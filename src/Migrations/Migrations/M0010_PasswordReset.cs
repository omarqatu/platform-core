using Core;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Migrations.Migrations;

[DbContext(typeof(CoreDbContext))]
[Migration("20261017000010_PasswordReset")]
public sealed class M0010_PasswordReset() : SqlFileMigration("0010_password_reset.sql");
