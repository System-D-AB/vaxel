namespace Vaxel.AspNetCore.Tests.Composer;

public sealed class ViewAndPageAsyncTests : IClassFixture<ComposerApiFactory>
{
    private readonly ComposerApiFactory _factory;

    public ViewAndPageAsyncTests(ComposerApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ViewAsync_RendersWithoutLayout()
    {
        var html = await _factory.ComposeAsync(c => c.ViewAsync("Bare"));
        Assert.Contains("bare-view", html);
        Assert.DoesNotContain("layout-chrome", html);
        Assert.DoesNotContain("<html", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ViewAsync_Unknown_ThrowsNamed()
    {
        var ex = await Assert.ThrowsAsync<VaxelFragmentNotFoundException>(
            () => _factory.ComposeAsync(c => c.ViewAsync("NoSuchView")));
        Assert.Contains("NoSuchView", ex.Message);
    }

    [Fact]
    public async Task PageAsync_RendersPageBody()
    {
        var client = _factory.CreateClient();
        var page = await client.GetStringAsync("/FragmentPage");
        Assert.Contains("layout-chrome", page);

        var composed = await _factory.ComposeAsync(c => c.PageAsync("/FragmentPage"));
        Assert.Contains("fragment-page-body", composed);
        Assert.DoesNotContain("layout-chrome", composed);
    }

    [Fact]
    public async Task PageAsync_Unknown_ThrowsNamed()
    {
        var ex = await Assert.ThrowsAsync<VaxelFragmentNotFoundException>(
            () => _factory.ComposeAsync(c => c.PageAsync("/NoSuchPage")));
        Assert.Contains("NoSuchPage", ex.Message);
    }
}
