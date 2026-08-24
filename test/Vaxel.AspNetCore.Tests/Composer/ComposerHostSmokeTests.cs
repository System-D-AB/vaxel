using Microsoft.AspNetCore.Mvc.Testing;

namespace Vaxel.AspNetCore.Tests.Composer;

public sealed class ComposerHostSmokeTests : IClassFixture<ComposerApiFactory>
{
    private readonly HttpClient _client;

    public ComposerHostSmokeTests(ComposerApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task UsesPartial_Returns_200()
    {
        var response = await _client.GetAsync("/UsesPartial");
        var html = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Status {(int)response.StatusCode}: {html}");
        Assert.Contains("hello-text", html);
        Assert.Contains("world", html);
    }

    [Fact]
    public async Task VaxelJs_Returns_200_WithJavascriptContentType()
    {
        var response = await _client.GetAsync("/_vaxel/vaxel.js");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/javascript; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        var js = await response.Content.ReadAsStringAsync();
        Assert.Contains("vx:after-apply", js);
    }
}
