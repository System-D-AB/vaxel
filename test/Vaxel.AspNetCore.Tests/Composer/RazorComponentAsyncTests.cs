using Vaxel.AspNetCore.Tests.Fixtures.ComposerHost.Components;

namespace Vaxel.AspNetCore.Tests.Composer;

public sealed class RazorComponentAsyncTests : IClassFixture<ComposerApiFactory>
{
    private readonly ComposerApiFactory _factory;

    public RazorComponentAsyncTests(ComposerApiFactory factory) => _factory = factory;

    [Fact]
    public async Task RazorComponentAsync_MatchesStaticPageRender()
    {
        var client = _factory.CreateClient();
        var page = await client.GetStringAsync("/UsesRazorComponent");
        var fromPage = HtmlAssert.Normalize(HtmlAssert.InnerById(page, "rc"));

        var composed = await _factory.ComposeAsync(c =>
            c.RazorComponentAsync<HelloComponent>(new { Message = "world" }));
        Assert.Equal(fromPage, HtmlAssert.Normalize(composed));
    }
}
