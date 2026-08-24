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
}
