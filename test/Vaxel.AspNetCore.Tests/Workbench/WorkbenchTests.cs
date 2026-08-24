using System.Net;
using System.Text.RegularExpressions;
using Vaxel.Testing;
using Xunit;

namespace Vaxel.AspNetCore.Tests.Workbench;

public sealed class WorkbenchTests : IClassFixture<WorkbenchApiFactory>
{
    private readonly WorkbenchApiFactory _factory;
    private readonly HttpClient _client;

    public WorkbenchTests(WorkbenchApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    private async Task<string> GetAntiforgeryTokenAsync(string url)
    {
        var response = await _client.GetAsync(url);
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html, @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""");
        if (!match.Success)
        {
            match = Regex.Match(html, @"value=""([^""]+)""[^>]*name=""__RequestVerificationToken""");
        }
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    [Fact]
    public async Task Workbench_VaxelJs_Returns_200()
    {
        var response = await _client.GetAsync("/_vaxel/vaxel.js");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/javascript; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        var js = await response.Content.ReadAsStringAsync();
        Assert.Contains("vx:after-apply", js);
        Assert.Contains("morphIntoTarget", js);
        Assert.Contains("parseXmlAttrs", js);
    }

    [Fact]
    public async Task Workbench_NoJs_FullPage_RendersSinglePane()
    {
        var response = await _client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        var paneMatches = Regex.Matches(html, @"\bid=""pane""", RegexOptions.IgnoreCase);
        Assert.Single(paneMatches);
        Assert.Contains("<section id=\"pane\" class=\"pane\" vx-region>", html);
        Assert.Contains("Welcome to vaxel Workbench", html);
    }

    [Theory]
    [InlineData("submissions", "Submissions")]
    [InlineData("proposals", "Project Proposals")]
    [InlineData("settings", "Settings")]
    public async Task Workbench_TabNavigation_ReturnsPatchDocument(string tab, string expectedHeading)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/?tab={tab}");
        request.Headers.Add("VX-Request", "1");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/vnd.vaxel-patch+html; charset=utf-8", response.Content.Headers.ContentType?.ToString());

        var patch = await response.Content.ReadAsStringAsync();
        Assert.Contains("<vx-patch target=\"#pane\" mode=\"morph\">", patch);
        Assert.Contains(expectedHeading, patch);
        Assert.Contains($"push-url=\"/?tab={tab}\"", patch);
    }

    [Theory]
    [InlineData("/", "/", "pane")]
    [InlineData("/?tab=submissions", "/?tab=submissions", "pane")]
    [InlineData("/?tab=proposals", "/?tab=proposals", "pane")]
    [InlineData("/?tab=settings", "/?tab=settings", "pane")]
    [InlineData("/contact", "/contact", "pane")]
    [InlineData("/contact?status=success", "/contact?status=success", "pane")]
    [InlineData("/proposals/201/edit", "/proposals/201/edit", "prop-201")]
    public async Task Workbench_Parity_Assert_Regions(string pageUrl, string patchUrl, string regionId)
    {
        // Prove Rule R3 across all tabs, contact form, thank-you state, and proposal edit form
        await VaxelParity.AssertAsync(_client, pageUrl, patchUrl, regionId);
    }

    [Fact]
    public async Task Workbench_ContactGet_PatchPutsContactRegionInsidePane()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/contact");
        request.Headers.Add("VX-Request", "1");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var patch = await response.Content.ReadAsStringAsync();
        Assert.Contains("<vx-patch target=\"#pane\" mode=\"morph\">", patch);
        Assert.Contains("id=\"contact\"", patch);
        Assert.Contains("Contact Engineering Support", patch);
        Assert.DoesNotContain("<vx-patch target=\"#contact\"", patch);
    }

    [Fact]
    public async Task Workbench_ContactForm_Validation422_ReRendersFormWithErrorMessage()
    {
        var token = await GetAntiforgeryTokenAsync("/contact");

        var request = new HttpRequestMessage(HttpMethod.Post, "/contact");
        request.Headers.Add("VX-Request", "1");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Name"] = "",
            ["Message"] = ""
        });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var patch = await response.Content.ReadAsStringAsync();
        Assert.Contains("<vx-patch target=\"#contact\" mode=\"morph\">", patch);
        Assert.Contains("Name and message are required", patch);
        Assert.Contains("<form method=\"post\" action=\"/contact\"", patch);
    }

    [Fact]
    public async Task Workbench_ContactForm_Success_PatchesContactAndNotices()
    {
        var token = await GetAntiforgeryTokenAsync("/contact");

        var request = new HttpRequestMessage(HttpMethod.Post, "/contact");
        request.Headers.Add("VX-Request", "1");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Name"] = "Alice",
            ["Message"] = "Excited for vaxel v1.0 release!"
        });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var patch = await response.Content.ReadAsStringAsync();
        Assert.Contains("<vx-patch target=\"#contact\" mode=\"morph\">", patch);
        Assert.Contains("Thank you, Alice!", patch);
        Assert.Contains("<vx-patch target=\"#notices\" mode=\"append\">", patch);
        Assert.Contains("Contact message submitted successfully.", patch);
    }

    [Fact]
    public async Task Workbench_Anonymous_Approval_RefusedIntoNotices()
    {
        var token = await GetAntiforgeryTokenAsync("/?tab=submissions");

        var request = new HttpRequestMessage(HttpMethod.Post, "/submissions/101/approve");
        request.Headers.Add("VX-Request", "1");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var patch = await response.Content.ReadAsStringAsync();
        Assert.Contains("<vx-patch target=\"#notices\" mode=\"append\">", patch);
        Assert.Contains("Action Refused", patch);
    }

    [Fact]
    public async Task Workbench_Approver_SignInAndApprove_Succeeds()
    {
        // 1. Sign in as Alice (Approver)
        var loginResponse = await _client.PostAsync("/login?handler=SignInAlice", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);

        var token = await GetAntiforgeryTokenAsync("/?tab=submissions");

        var request = new HttpRequestMessage(HttpMethod.Post, "/submissions/101/approve");
        request.Headers.Add("VX-Request", "1");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var patch = await response.Content.ReadAsStringAsync();
        Assert.Contains("<vx-patch target=\"#sub-101\" mode=\"morph\">", patch);
        Assert.Contains("Approved by Alice", patch);
        Assert.Contains("<vx-patch target=\"#notices\" mode=\"append\">", patch);
        Assert.Contains("Submission #101 approved.", patch);
    }

    [Fact]
    public async Task Workbench_ProposalEdit_ReturnsEditFormPatch()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/proposals/201/edit");
        request.Headers.Add("VX-Request", "1");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var patch = await response.Content.ReadAsStringAsync();
        Assert.Contains("<vx-patch target=\"#prop-201\" mode=\"morph\">", patch);
        Assert.Contains("Edit Proposal #201 Title:", patch);
    }
}
