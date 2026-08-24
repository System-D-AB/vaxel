using System.Net;
using Vaxel.AspNetCore.Tests.Composer;
using Xunit;

namespace Vaxel.AspNetCore.Tests.PatchTests;

public sealed class PatchResultHttpTests : IClassFixture<ComposerApiFactory>
{
    private readonly HttpClient _client;

    public PatchResultHttpTests(ComposerApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PatchResult_FromPage()
    {
        var response = await _client.GetAsync("/patch-page?handler=Patch");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1", response.Headers.GetValues("VX-Protocol").FirstOrDefault());
        Assert.StartsWith("text/vnd.vaxel-patch+html", response.Content.Headers.ContentType?.ToString());
        Assert.Contains("<vx-patch target=\"#page-pane\" mode=\"morph\">", content);
        Assert.Contains("<div id=\"page-pane\">page patch</div>", content);
        Assert.Contains("<vx-directive announce=\"Page patch loaded\" />", content);
    }

    [Fact]
    public async Task PatchResult_FromController()
    {
        var response = await _client.GetAsync("/patch/controller");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1", response.Headers.GetValues("VX-Protocol").FirstOrDefault());
        Assert.StartsWith("text/vnd.vaxel-patch+html", response.Content.Headers.ContentType?.ToString());
        Assert.Contains("<vx-patch target=\"#controller-pane\" mode=\"morph\">", content);
        Assert.Contains("<div>controller content</div>", content);
        Assert.Contains("<vx-directive push-url=\"/apps/test\" />", content);
    }

    [Fact]
    public async Task PatchResult_FromMinimalApi()
    {
        var response = await _client.GetAsync("/patch/minimal");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1", response.Headers.GetValues("VX-Protocol").FirstOrDefault());
        Assert.StartsWith("text/vnd.vaxel-patch+html", response.Content.Headers.ContentType?.ToString());
        Assert.Contains("<vx-patch target=\"#pane\" mode=\"morph\">", content);
        Assert.Contains("<section id=\"pane\">minimal</section>", content);
        Assert.Contains("<vx-signals>{\"draftSeq\":149}</vx-signals>", content);
        Assert.Contains("<vx-directive focus=\"#filter\" />", content);
    }

    [Fact]
    public async Task Status_422_StillAPatch()
    {
        var response = await _client.PostAsync("/patch/status-422", null);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("1", response.Headers.GetValues("VX-Protocol").FirstOrDefault());
        Assert.StartsWith("text/vnd.vaxel-patch+html", response.Content.Headers.ContentType?.ToString());
        Assert.Contains("<vx-patch target=\"#errors\" mode=\"morph\">", content);
        Assert.Contains("<div id=\"errors\">Invalid input</div>", content);
    }

    [Fact]
    public async Task UseVaxel_SetsVaryOnPatch()
    {
        var response = await _client.GetAsync("/patch/minimal");

        Assert.True(response.Headers.Vary.Contains("VX-Request") || response.Headers.TryGetValues("Vary", out var values) && values.Any(v => v.Contains("VX-Request")));
    }

    [Fact]
    public async Task UseVaxel_DoesNotShortCircuitOtherRoutes()
    {
        var response = await _client.GetAsync("/plain-route");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("plain text", content);
    }
}
