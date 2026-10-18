using Core;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Migrations.Migrations;

[DbContext(typeof(CoreDbContext))]
[Migration("20261017000012_Classification")]
public sealed class M0012_Classification() : SqlFileMigration("0012_classification.sql");
