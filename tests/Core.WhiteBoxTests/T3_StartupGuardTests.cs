namespace Core.WhiteBoxTests;

// PROOF_SPEC v1.2 T3, decision 33 as amended by the project owner: Session:RequireHttps=false (a cookie without
// the Secure flag) is allowed only in an environment named Development or CI — anywhere else Api refuses to
// start, the same pattern as its migrator connection-string guard. Api hosted in-process, as deployed.
[Collection(WhiteBoxCollection.Name)]
public class T3_StartupGuardTests
{
    private const string Refusal = "Session:RequireHttps=false is allowed only in Development or CI";

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("ci")]
    public Task RequireHttpsFalse_OutsideDevelopmentOrCI_ApiRefusesToStart(string environment) =>
        InProcessApi.WithoutMigratorVariableAsync(async () =>
        {
            await using var api = InProcessApi.Create(environment, ("Session:RequireHttps", "false"));

            var error = Assert.ThrowsAny<Exception>(() => api.Services);

            Assert.Contains(Chain(error), e => e is InvalidOperationException && e.Message.StartsWith(Refusal));
        });

    // The other direction: the same setting in Development or CI, and the default anywhere → Api starts.
    [Theory]
    [InlineData("Development", "false")]
    [InlineData("CI", "false")]
    [InlineData("Production", null)]
    [InlineData("Production", "true")]
    public Task AllowedCombinations_ApiStarts(string environment, string? requireHttps) =>
        InProcessApi.WithoutMigratorVariableAsync(async () =>
        {
            await using var api = requireHttps is null
                ? InProcessApi.Create(environment)
                : InProcessApi.Create(environment, ("Session:RequireHttps", requireHttps));

            Assert.NotNull(api.Services);
            await Task.CompletedTask;
        });

    private static IEnumerable<Exception> Chain(Exception error)
    {
        for (var e = error; e is not null; e = e.InnerException)
            yield return e;
    }
}
