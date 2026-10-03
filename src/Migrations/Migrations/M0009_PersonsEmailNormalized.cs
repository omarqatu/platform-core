using Core;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Migrations.Migrations;

[DbContext(typeof(CoreDbContext))]
[Migration("20261003000009_PersonsEmailNormalized")]
public sealed class M0009_PersonsEmailNormalized() : SqlFileMigration("0009_persons_email_normalized.sql");
