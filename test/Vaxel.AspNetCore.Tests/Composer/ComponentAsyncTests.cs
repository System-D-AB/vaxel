using Vaxel;
using Vaxel.AspNetCore.Tests.Fixtures.ComposerHost.ViewComponents;

namespace Vaxel.AspNetCore.Tests.Composer;

public sealed class ComponentAsyncTests : IClassFixture<ComposerApiFactory>
{
    private readonly ComposerApiFactory _factory;

    public ComponentAsyncTests(ComposerApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ComponentAsync_MatchesInPageRender()
    {
        var client = _factory.CreateClient();
        var page = await client.GetStringAsync("/UsesComponent");
        var fromPage = HtmlAssert.Normalize(HtmlAssert.InnerById(page, "badge-wrap"));

        var generic = await _factory.ComposeAsync(c => c.ComponentAsync<BadgeViewComponent>(new { label = "ok" }));
        var named = await _factory.ComposeAsync(c => c.ComponentAsync("Badge", new { label = "ok" }));

        Assert.Equal(fromPage, HtmlAssert.Normalize(generic));
        Assert.Equal(fromPage, HtmlAssert.Normalize(named));
    }

    [Fact]
    public async Task ComponentAsync_Unknown_ThrowsNamed()
    {
        var ex = await Assert.ThrowsAsync<VaxelFragmentNotFoundException>(
            () => _factory.ComposeAsync(c => c.ComponentAsync("NoSuchComponent")));
        Assert.Contains("NoSuchComponent", ex.Message);
    }
}
