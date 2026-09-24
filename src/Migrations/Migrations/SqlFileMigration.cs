using Microsoft.EntityFrameworkCore.Migrations;

namespace Migrations.Migrations;

/// <summary>
/// A migration whose whole content is one SQL file embedded in this assembly
/// (src/Migrations/Sql). The schema is written as SQL, not derived from an EF model:
/// its policies, grants and constraints are the document's text (PLATFORM_CORE v1.14).
/// </summary>
public abstract class SqlFileMigration(string fileName) : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(ReadSql(fileName));

    // Forward-only: a data migration is never undone by dropping what it built.
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException($"{GetType().Name} is forward-only.");

    private static string ReadSql(string fileName)
    {
        var assembly = typeof(SqlFileMigration).Assembly;
        var resource = assembly.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith("." + fileName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded migration SQL '{fileName}' not found.");
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
