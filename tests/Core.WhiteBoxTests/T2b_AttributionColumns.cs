using Microsoft.EntityFrameworkCore;

namespace Core.WhiteBoxTests;

// PROOF_SPEC v1.2 T2b.2 [W] — PLATFORM_CORE v1.15 §4.1: the four attribution columns are gone from the
// scope surface, in the catalog and in the EF model. The audit log (§7) is the sole source of attribution.
[Collection(WhiteBoxCollection.Name)]
public class T2b_AttributionColumns(WhiteBoxFixture fixture)
{
    private static readonly (string Table, string Column)[] Dropped =
    [
        ("membership_scope", "updated_by"),
        ("membership_scope", "updated_at"),
        ("scope_assignments", "granted_by"),
        ("scope_assignments", "granted_at"),
    ];

    [Fact]
    public async Task T2b_2_TheFourColumns_AreNotInTheCatalog()
    {
        foreach (var (table, column) in Dropped)
            Assert.Equal(0, await fixture.CountAsMigratorAsync(
                "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = @t AND column_name = @c",
                ("t", table), ("c", column)));
    }

    [Fact]
    public async Task T2b_2_TheFourColumns_AreNotInTheEfModel()
    {
        await using var dataSource = fixture.CreateAppUserDataSource();
        await using var db = WhiteBoxFixture.Context(dataSource);

        var mapped = db.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties().Select(p => (Table: e.GetTableName(), Column: p.GetColumnName())))
            .ToHashSet();

        foreach (var dropped in Dropped)
            Assert.DoesNotContain(dropped, mapped);
        // The tables themselves are still mapped — the columns are gone, not the entities.
        Assert.Contains(mapped, m => m.Table == "membership_scope");
        Assert.Contains(mapped, m => m.Table == "scope_assignments");
    }
}
