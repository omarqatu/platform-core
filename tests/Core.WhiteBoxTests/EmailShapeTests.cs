using System.Text.RegularExpressions;
using Core.Identity;
using Npgsql;

namespace Core.WhiteBoxTests;

/// <summary>
/// [W] One shape for a deliverable address, written once (the project owner's item 35, group ج). The database
/// already refuses a malformed client address with <c>clients_email_check</c>; the invitation path must refuse by the
/// same rule, and the two must not drift. So the application's pattern is the constraint's own text, read here from
/// the live catalog and compared character for character.
/// </summary>
[Collection(WhiteBoxCollection.Name)]
public class EmailShapeTests(WhiteBoxFixture fixture)
{
    [Fact]
    public async Task TheApplicationsPattern_IsClientsEmailChecksOwnText()
    {
        await using var source = fixture.CreateMigratorDataSource();
        await using var command = source.CreateCommand(
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'clients_email_check'");
        var definition = (string)(await command.ExecuteScalarAsync())!;

        // CHECK (((email ~ '<pattern>'::text) AND (length(email) <= <max>)))
        var match = Regex.Match(definition, @"email ~ '(?<pattern>[^']+)'::text\) AND \(length\(email\) <= (?<max>\d+)\)");
        Assert.True(match.Success, definition);
        Assert.Equal(match.Groups["pattern"].Value, EmailAddress.ShapePattern);
        Assert.Equal(int.Parse(match.Groups["max"].Value, System.Globalization.CultureInfo.InvariantCulture), EmailAddress.MaxLength);
    }

    // The parse must give back the input itself — no display name, no list, nothing dropped before a space.
    [Theory]
    [InlineData("someone@b35.test", true)]
    [InlineData("  someone@b35.test ", true)]          // surrounding spaces: EmailAddress's comparison rule trims them
    [InlineData("عدنان@مثال.فلسطين", true)]
    [InlineData("adnan", false)]
    [InlineData("adnan@", false)]
    [InlineData("@b35.test", false)]
    [InlineData("adnan@localhost", false)]
    [InlineData("a b@b35.test", false)]
    [InlineData("\"Adnan\" <adnan@b35.test>", false)]
    [InlineData("a@b35.test, b@b35.test", false)]
    [InlineData("a@b35.test,b@b35.test", false)]
    [InlineData("a@b35.test;b@b35.test", false)]
    public void IsSendable_AcceptsOneAddressAndNothingElse(string input, bool sendable) =>
        Assert.Equal(sendable, EmailAddress.IsSendable(input));
}
