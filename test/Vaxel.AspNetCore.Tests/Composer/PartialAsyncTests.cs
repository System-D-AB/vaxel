using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Html;
using Microsoft.Extensions.DependencyInjection;
using Vaxel;
using Vaxel.AspNetCore.Tests.Fixtures.ComposerHost;

namespace Vaxel.AspNetCore.Tests.Composer;

public sealed class PartialAsyncTests : IClassFixture<ComposerApiFactory>
{
    private readonly ComposerApiFactory _factory;

    public PartialAsyncTests(ComposerApiFactory factory) => _factory = factory;

    [Fact]
    public async Task PartialAsync_MatchesInPageRender()
    {
        var client = _factory.CreateClient();
        var page = await client.GetStringAsync("/UsesPartial");
        var fromPage = HtmlAssert.Normalize(HtmlAssert.InnerById(page, "hello"));

        var composed = await _factory.ComposeAsync(c => c.PartialAsync("_Hello", "world"));
        Assert.Equal(fromPage, HtmlAssert.Normalize(composed));
    }

    [Fact]
    public async Task PartialAsync_UnknownName_ThrowsNamed()
    {
        var ex = await Assert.ThrowsAsync<VaxelFragmentNotFoundException>(
            () => _factory.ComposeAsync(c => c.PartialAsync("_DoesNotExist")));
        Assert.Contains("_DoesNotExist", ex.FragmentName);
        Assert.Contains("_DoesNotExist", ex.Message);
    }

    [Fact]
    public async Task PartialAsync_EncodesAtValues()
    {
        var html = await _factory.ComposeAsync(c => c.PartialAsync("_Encoded", new EncodedModel { Name = "<b>x</b>" }));
        Assert.Contains("&lt;b&gt;x&lt;/b&gt;", html);
        Assert.DoesNotContain("<b>x</b>", html);
    }

    [Fact]
    public async Task PartialAsync_NullModel_Renders()
    {
        var html = await _factory.ComposeAsync(c => c.PartialAsync("_Hello", null));
        Assert.Contains("hello-text", html);
    }
}

internal static class ComposerFactoryExtensions
{
    public static async Task<string> ComposeAsync(
        this ComposerApiFactory factory,
        Func<IFragmentComposer, Task<IHtmlContent>> action)
    {
        using var scope = factory.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Method = "GET";
        http.Request.Path = "/";
        http.Request.Host = new HostString("localhost");
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = http;
        var composer = scope.ServiceProvider.GetRequiredService<IFragmentComposer>();
        return HtmlAssert.ToHtml(await action(composer));
    }

    public static IFragmentComposer Background(this ComposerApiFactory factory)
        => factory.Services.GetRequiredService<IFragmentComposerFactory>().CreateBackground();
}
