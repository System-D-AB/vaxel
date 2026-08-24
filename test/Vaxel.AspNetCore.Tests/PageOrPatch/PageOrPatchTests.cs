using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Vaxel;
using Vaxel.AspNetCore.Tests.Composer;
using Xunit;

namespace Vaxel.AspNetCore.Tests.PageOrPatchTests;

public sealed class PageOrPatchTests : IClassFixture<ComposerApiFactory>
{
    private readonly ComposerApiFactory _factory;
    private readonly HttpClient _client;

    public PageOrPatchTests(ComposerApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task NoHeader_RunsPage()
    {
        var response = await _client.GetAsync("/tabs");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<html><body><section id=\"pane\">page body</section></body></html>", content);
    }

    [Fact]
    public async Task VxRequest1_RunsPatch()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/tabs");
        request.Headers.Add("VX-Request", "1");
        request.Headers.Add("VX-Protocol", "1");

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/vnd.vaxel-patch+html", response.Content.Headers.ContentType?.ToString());
        Assert.Equal("1", response.Headers.GetValues("VX-Protocol").FirstOrDefault());
        Assert.Contains("<vx-patch target=\"#pane\" mode=\"morph\">", content);
    }

    [Fact]
    public void UnusedDelegate_NotInvoked()
    {
        var pageCount = 0;
        var patchCount = 0;

        var contextNoHeader = new DefaultHttpContext();
        var resultPage = Vaxel.PageOrPatch(
            contextNoHeader,
            page: () => { pageCount++; return Results.Ok(); },
            patch: () => { patchCount++; return Results.Ok(); });

        Assert.Equal(1, pageCount);
        Assert.Equal(0, patchCount);

        var contextWithHeader = new DefaultHttpContext();
        contextWithHeader.Request.Headers["VX-Request"] = "1";
        var resultPatch = Vaxel.PageOrPatch(
            contextWithHeader,
            page: () => { pageCount++; return Results.Ok(); },
            patch: () => { patchCount++; return Results.Ok(); });

        Assert.Equal(1, pageCount);
        Assert.Equal(1, patchCount);
    }

    [Fact]
    public async Task Protocol2_RunsPage()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/tabs");
        request.Headers.Add("VX-Request", "1");
        request.Headers.Add("VX-Protocol", "2");

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<html><body><section id=\"pane\">page body</section></body></html>", content);
    }

    [Fact]
    public async Task Request1_WithoutProtocol_RunsPatch()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/tabs");
        request.Headers.Add("VX-Request", "1");

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/vnd.vaxel-patch+html", response.Content.Headers.ContentType?.ToString());
        Assert.Contains("<vx-patch target=\"#pane\" mode=\"morph\">", content);
    }

    [Fact]
    public async Task PageBranch_Redirects()
    {
        var nonRedirectingClient = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/cookbook/contact");
        var response = await nonRedirectingClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/thank-you", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task PatchBranch_PushUrl()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/cookbook/contact");
        request.Headers.Add("VX-Request", "1");

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/vnd.vaxel-patch+html", response.Content.Headers.ContentType?.ToString());
        Assert.Contains("<vx-patch target=\"#contact\" mode=\"morph\">", content);
        Assert.Contains("<vx-directive push-url=\"/thank-you\" />", content);
    }

    [Fact]
    public async Task HistoryRestore_IsPatch()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/tabs");
        request.Headers.Add("VX-Request", "1");
        request.Headers.Add("VX-History", "restore");

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/vnd.vaxel-patch+html", response.Content.Headers.ContentType?.ToString());
        Assert.Contains("<vx-patch target=\"#pane\" mode=\"morph\">", content);
    }

    [Fact]
    public async Task PageOrPatch_PageModel()
    {
        // 1. Without header -> full page
        var pageResponse = await _client.GetAsync("/page-or-patch-page");
        var pageContent = await pageResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        Assert.Contains("<div id=\"full-page\">Full Page Body</div>", pageContent);

        // 2. With VX-Request: 1 -> patch
        var patchRequest = new HttpRequestMessage(HttpMethod.Get, "/page-or-patch-page");
        patchRequest.Headers.Add("VX-Request", "1");
        var patchResponse = await _client.SendAsync(patchRequest);
        var patchContent = await patchResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        Assert.StartsWith("text/vnd.vaxel-patch+html", patchResponse.Content.Headers.ContentType?.ToString());
        Assert.Contains("<div id=\"pane\">page patch</div>", patchContent);
    }

    [Fact]
    public async Task PageOrPatch_Controller()
    {
        // 1. Without header -> full controller page
        var pageResponse = await _client.GetAsync("/page-or-patch/controller");
        var pageContent = await pageResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        Assert.Equal("<html>controller page</html>", pageContent);

        // 2. With VX-Request: 1 -> patch
        var patchRequest = new HttpRequestMessage(HttpMethod.Get, "/page-or-patch/controller");
        patchRequest.Headers.Add("VX-Request", "1");
        var patchResponse = await _client.SendAsync(patchRequest);
        var patchContent = await patchResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        Assert.StartsWith("text/vnd.vaxel-patch+html", patchResponse.Content.Headers.ContentType?.ToString());
        Assert.Contains("<div>controller patch</div>", patchContent);
    }

    [Fact]
    public async Task PageOrPatch_MinimalApi()
    {
        // 1. Without header -> page
        var pageResponse = await _client.GetAsync("/tabs");
        var pageContent = await pageResponse.Content.ReadAsStringAsync();
        Assert.Contains("<html><body><section id=\"pane\">page body</section></body></html>", pageContent);

        // 2. With header -> patch
        var patchRequest = new HttpRequestMessage(HttpMethod.Get, "/tabs");
        patchRequest.Headers.Add("VX-Request", "1");
        var patchResponse = await _client.SendAsync(patchRequest);
        var patchContent = await patchResponse.Content.ReadAsStringAsync();
        Assert.StartsWith("text/vnd.vaxel-patch+html", patchResponse.Content.Headers.ContentType?.ToString());
        Assert.Contains("<vx-patch target=\"#pane\" mode=\"morph\">", patchContent);
    }
}
