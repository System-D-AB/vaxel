using System.Net;
using System.Net.Http;
using Xunit;

namespace Vaxel.AspNetCore.Tests.PatchTests;

/// <summary>
/// US-2: a composed patch document must be written with asynchronous writes so that it
/// completes on a default Kestrel host (<c>AllowSynchronousIO = false</c>), rather than
/// failing with "Synchronous operations are disallowed" and forcing the agent to fall
/// back to a full navigation.
/// </summary>
public sealed class PatchResultKestrelTests : IClassFixture<ComposerHostKestrelFactory>
{
    private readonly ComposerHostKestrelFactory _factory;

    public PatchResultKestrelTests(ComposerHostKestrelFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ComposedPatch_OnDefaultKestrel_Answers200WithDocument()
    {
        using var client = new HttpClient { BaseAddress = _factory.ServerAddress };

        // /tabs composes the _Hello partial into a ViewBuffer and writes it as a patch
        // when VX-Request is present. This is the composer's real output, not an HtmlString.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/tabs");
        request.Headers.Add("VX-Request", "1");

        using var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/vnd.vaxel-patch+html", response.Content.Headers.ContentType?.ToString());
        Assert.Contains("<vx-patch target=\"#pane\" mode=\"morph\">", content);
        Assert.Contains("<p id=\"hello-text\">world</p>", content);
    }

    [Fact]
    public async Task LargePatch_OnDefaultKestrel_DoesNotFailOnFlush()
    {
        // AC-2: a fragment larger than the response writer's buffer forces a flush to the
        // body mid-write. A synchronous write throws "Synchronous operations are disallowed"
        // on default Kestrel; the async writer must complete with 200.
        using var client = new HttpClient { BaseAddress = _factory.ServerAddress };

        using var response = await client.GetAsync("/patch/large");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/vnd.vaxel-patch+html", response.Content.Headers.ContentType?.ToString());
        Assert.Contains("<vx-patch target=\"#pane\" mode=\"morph\">", content);
        Assert.Contains("<p>row</p>", content);
        Assert.True(content.Length > 16 * 1024, "fragment must exceed the writer buffer to exercise a flush.");
    }
}
