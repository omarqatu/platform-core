using Core;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Migrations.Migrations;

[DbContext(typeof(CoreDbContext))]
[Migration("20261018000013_ClientsAndLifecycle")]
public sealed class M0013_ClientsAndLifecycle() : SqlFileMigration("0013_clients_and_lifecycle.sql");
