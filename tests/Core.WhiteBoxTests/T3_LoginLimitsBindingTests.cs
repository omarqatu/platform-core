using Core.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Core.WhiteBoxTests;

// OPEN_ITEMS 91: the limiter's escalation is exactly what its configuration says. It was read wrong twice over while
// it was a list: the binder appended it to the property's default — the deployed sequence was
// [1, 2, 4, 8, 15, 1, 2, 4, 8, 15], falling back to 1 s after the fifth delay, not the escalating delay of Amendment
// 01 §5.2 — and configuration layers merge a list index by index, so a shorter sequence set over a longer one kept
// the longer one's tail. It is one comma-separated value now; these hold it to that, through the real host.
[Collection(WhiteBoxCollection.Name)]
public class T3_LoginLimitsBindingTests
{
    // As deployed: appsettings.json's sequence, nothing appended, the last value the cap.
    [Fact]
    public Task TheDeployedHost_EscalatesExactlyAsItsConfigurationSays() =>
        InProcessApi.WithoutMigratorVariableAsync(async () =>
        {
            await using var api = InProcessApi.Create("Production");

            var limits = Limits(api);

            Assert.Equal([1, 2, 4, 8, 15], limits.EffectiveDelaySeconds());
            Assert.Equal(TimeSpan.FromSeconds(15), limits.DelayFor(limits.FreeFailures + 4));
            Assert.Equal(TimeSpan.FromSeconds(15), limits.DelayFor(limits.FreeFailures + 5));
        });

    // A shorter sequence set over appsettings.json's, in a later layer — as launch numbers would be, by environment
    // variable — is that sequence alone: no default in front of it, and no tail inherited from the layer beneath.
    [Fact]
    public Task AnOverride_IsExactlyTheOverride_WithNoInheritedTail() =>
        InProcessApi.WithoutMigratorVariableAsync(async () =>
        {
            await using var api = InProcessApi.Create("Production",
                ("Identity:LoginLimits:FreeFailures", "0"), ("Identity:LoginLimits:DelaySecondsByFailure", "5, 10"));

            var limits = Limits(api);

            Assert.Equal([5, 10], limits.EffectiveDelaySeconds());
            Assert.Equal(
                [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)],
                Enumerable.Range(0, 4).Select(limits.DelayFor));
        });

    // Nothing configured, an empty value, and a JSON [] are the default — never "no delay": the limiter does not
    // fail open on a configuration that says nothing.
    [Fact]
    public void NoSequence_OrAnEmptyOne_IsTheDefault()
    {
        Assert.Equal([1, 2, 4, 8, 15], Bind(new Dictionary<string, string?>()).EffectiveDelaySeconds());
        Assert.Equal([1, 2, 4, 8, 15], Bind(new() { ["Identity:LoginLimits:DelaySecondsByFailure"] = "" }).EffectiveDelaySeconds());
        Assert.Equal([1, 2, 4, 8, 15], Bind(new() { ["Identity:LoginLimits:DelaySecondsByFailure"] = "  " }).EffectiveDelaySeconds());

        var emptyList = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream("""{"Identity":{"LoginLimits":{"DelaySecondsByFailure":[]}}}"""u8.ToArray()))
            .Build();
        Assert.Equal([1, 2, 4, 8, 15], Resolve(emptyList).EffectiveDelaySeconds());
    }

    // Fail-closed: a malformed sequence stops Api at startup. Never skipped, never replaced by the default.
    [Theory]
    [InlineData("5,,10")]
    [InlineData("5,-1")]
    [InlineData("5,x")]
    [InlineData("5;10")]
    [InlineData("NaN")]
    [InlineData("1,Infinity")]
    [InlineData(" , ")]
    // Not escalating (Amendment 01 §5.2, a project-owner decision): a falling step, or a zero delay.
    [InlineData("5,3,1")]
    [InlineData("0,1,2")]
    public Task AMalformedSequence_ApiRefusesToStart(string sequence) =>
        InProcessApi.WithoutMigratorVariableAsync(async () =>
        {
            await using var api = InProcessApi.Create("Production", ("Identity:LoginLimits:DelaySecondsByFailure", sequence));

            var error = Assert.ThrowsAny<Exception>(() => api.Services);

            Assert.Contains(Chain(error), e => e is FormatException && e.Message.Contains("DelaySecondsByFailure"));
        });

    // The old shape — a list, as a JSON array or __0-style variables — would bind to nothing and so silently become
    // the default. Api refuses it instead.
    [Fact]
    public Task TheOldListShape_ApiRefusesToStart() =>
        InProcessApi.WithoutMigratorVariableAsync(async () =>
        {
            await using var api = InProcessApi.Create("Production",
                ("Identity:LoginLimits:DelaySecondsByFailure:0", "5"), ("Identity:LoginLimits:DelaySecondsByFailure:1", "10"));

            var error = Assert.ThrowsAny<Exception>(() => api.Services);

            Assert.Contains(Chain(error), e => e is InvalidOperationException && e.Message.Contains("is a list"));
        });

    private static LoginLimits Limits(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> api) =>
        api.Services.GetRequiredService<IOptions<LoginLimits>>().Value;

    private static LoginLimits Bind(Dictionary<string, string?> settings) =>
        Resolve(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

    // Bound the way Api binds it (Program.cs: Configure<LoginLimits> on the section).
    private static LoginLimits Resolve(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.Configure<LoginLimits>(configuration.GetSection("Identity:LoginLimits"));
        return services.BuildServiceProvider().GetRequiredService<IOptions<LoginLimits>>().Value;
    }

    private static IEnumerable<Exception> Chain(Exception error)
    {
        for (Exception? e = error; e is not null; e = e.InnerException)
            yield return e;
    }
}
