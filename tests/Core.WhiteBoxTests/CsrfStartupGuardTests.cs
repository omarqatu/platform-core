namespace Core.WhiteBoxTests;

// CSRF (OPEN_ITEMS 28): Security:AllowedOrigins decides which unsafe requests Api accepts. With none, or with an
// entry no browser ever sends as Origin (a path, a trailing slash, upper case, a default port), it would refuse
// every write — so Api refuses to start instead, the same pattern as its other configuration guards. Api hosted
// in-process; the harness's own origin is replaced by the value under test.
[Collection(WhiteBoxCollection.Name)]
public class CsrfStartupGuardTests
{
    [Theory]
    [InlineData("", "is not configured")]
    [InlineData("https://localhost/", "is not an origin")]
    [InlineData("https://localhost/app", "is not an origin")]
    [InlineData("https://LOCALHOST", "is not an origin")]
    [InlineData("https://localhost:443", "is not an origin")]
    [InlineData("localhost:5173", "is not an origin")]
    [InlineData("null", "is not an origin")]
    public Task AllowedOrigins_NoneOrMalformed_ApiRefusesToStart(string origin, string reason) =>
        InProcessApi.WithoutMigratorVariableAsync(async () =>
        {
            await using var api = InProcessApi.Create(null, ("Security:AllowedOrigins:0", origin));

            var error = Assert.ThrowsAny<Exception>(() => api.Services);

            Assert.Contains(Chain(error), e => e is InvalidOperationException && e.Message.StartsWith("Security:AllowedOrigins") &&
                e.Message.Contains(reason));
        });

    [Theory]
    [InlineData("https://localhost")]
    [InlineData("http://localhost:5173")]
    [InlineData("https://app.example.com:8443")]
    public Task AllowedOrigins_SerializedOrigin_ApiStarts(string origin) =>
        InProcessApi.WithoutMigratorVariableAsync(async () =>
        {
            await using var api = InProcessApi.Create(null, ("Security:AllowedOrigins:0", origin));

            Assert.NotNull(api.Services);
            await Task.CompletedTask;
        });

    private static IEnumerable<Exception> Chain(Exception error)
    {
        for (var e = error; e is not null; e = e.InnerException)
            yield return e;
    }
}
