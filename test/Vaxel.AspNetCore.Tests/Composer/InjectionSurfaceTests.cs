namespace Vaxel.AspNetCore.Tests.Composer;

public sealed class InjectionSurfaceTests : IClassFixture<ComposerApiFactory>
{
    private readonly HttpClient _client;

    public InjectionSurfaceTests(ComposerApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Composer_FromMinimalApi()
    {
        var html = await _client.GetStringAsync("/compose-partial");
        Assert.Contains("hello-text", html);
        Assert.Contains("world", html);
    }

    [Fact]
    public async Task Composer_FromController()
    {
        var html = await _client.GetStringAsync("/compose/partial");
        Assert.Contains("hello-text", html);
        Assert.Contains("world", html);
    }

    [Fact]
    public async Task Composer_FromPageModel()
    {
        var html = await _client.GetStringAsync("/ComposeFromPage");
        Assert.Contains("hello-text", html);
        Assert.Contains("world", html);
    }
}
