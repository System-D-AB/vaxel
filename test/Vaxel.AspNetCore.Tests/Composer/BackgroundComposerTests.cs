using Vaxel;

namespace Vaxel.AspNetCore.Tests.Composer;

public sealed class BackgroundComposerTests : IClassFixture<ComposerApiFactory>
{
    private readonly ComposerApiFactory _factory;

    public BackgroundComposerTests(ComposerApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Background_PlainPartial_Succeeds()
    {
        var composer = _factory.Background();
        var html = HtmlAssert.ToHtml(await composer.PartialAsync("_Plain"));
        Assert.Contains("ok", html);
    }

    [Fact]
    public async Task Background_UrlPage_ThrowsNamed()
    {
        var composer = _factory.Background();
        var ex = await Assert.ThrowsAsync<VaxelFragmentContextException>(
            () => composer.PartialAsync("_NeedsUrl"));
        Assert.Equal("Url.Page", ex.MissingCapability);
    }

    [Fact]
    public async Task Background_User_ThrowsNamed()
    {
        var composer = _factory.Background();
        var ex = await Assert.ThrowsAsync<VaxelFragmentContextException>(
            () => composer.PartialAsync("_NeedsUser"));
        Assert.Equal("User", ex.MissingCapability);
    }
}
