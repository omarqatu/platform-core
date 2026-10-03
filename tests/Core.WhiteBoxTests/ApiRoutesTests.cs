using Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Core.WhiteBoxTests;

// The path space (OPEN_ITEMS 37, decided by the project owner): every route Api registers is under /api, except the
// documented screens rendered on the server (WebInterface.ServerRenderedScreens — the original T8 screen). Read from
// Api's own endpoint data source, hosted in-process in CI (bootstrap registered too). Seen failing on a planted bare
// route, with the same check.
[Collection(WhiteBoxCollection.Name)]
public class ApiRoutesTests
{
    [Fact]
    public Task EveryRoute_IsUnderApi_ButTheDocumentedScreens() => InProcessApi.WithoutMigratorVariableAsync(async () =>
    {
        await using var api = InProcessApi.Create("CI");
        var endpoints = api.Services.GetRequiredService<EndpointDataSource>();

        Assert.Empty(OutsideApi(endpoints));
        Assert.Equal(WebInterface.ServerRenderedScreens, Patterns(endpoints).Where(p => !p.StartsWith(WebInterface.ApiPrefix + "/")));
        Assert.Contains($"{WebInterface.ApiPrefix}/provision/tenants", Patterns(endpoints));
    });

    [Fact]
    public Task TheCheck_FindsAPlantedBareRoute() => InProcessApi.WithoutMigratorVariableAsync(async () =>
    {
        await using var api = InProcessApi.Create("CI");
        var planted = new DefaultEndpointDataSource(
            new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/tenants"), 0).Build());

        var outside = OutsideApi(new CompositeEndpointDataSource([api.Services.GetRequiredService<EndpointDataSource>(), planted]));

        Assert.Equal(["/tenants"], outside);
    });

    private static IEnumerable<string> Patterns(EndpointDataSource endpoints) =>
        endpoints.Endpoints.OfType<RouteEndpoint>().Select(e => e.RoutePattern.RawText ?? "");

    private static List<string> OutsideApi(EndpointDataSource endpoints) =>
        Patterns(endpoints).Where(p => !p.StartsWith(WebInterface.ApiPrefix + "/") && !WebInterface.ServerRenderedScreens.Contains(p)).ToList();
}
