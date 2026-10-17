using Core;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Migrations.Migrations;

[DbContext(typeof(CoreDbContext))]
[Migration("20261017000011_NotificationSenderAndForgot")]
public sealed class M0011_NotificationSenderAndForgot() : SqlFileMigration("0011_notification_sender_and_forgot.sql");
