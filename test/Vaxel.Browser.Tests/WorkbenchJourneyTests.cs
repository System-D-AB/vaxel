using Microsoft.Playwright;
using Xunit;

namespace Vaxel.Browser.Tests;

[Collection(WorkbenchBrowserCollection.Name)]
public sealed class WorkbenchJourneyTests : IAsyncLifetime
{
    private readonly WorkbenchBrowserFixture _fx;
    private IBrowserContext _context = null!;
    private IPage _page = null!;

    public WorkbenchJourneyTests(WorkbenchBrowserFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _context = await _fx.Browser.NewContextAsync();
        _page = await _context.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    [Fact]
    public async Task TabClick_MorphsPane_WithoutFullReload()
    {
        await WorkbenchPage.GotoAsync(_page, _fx.BaseAddress);

        var loads = 0;
        _page.Load += (_, _) => loads++;
        var loadsAfterGoto = loads;

        var patch = _page.WaitForResponseAsync(WorkbenchPage.IsPatch);
        await WorkbenchPage.ClickTabAsync(_page, "submissions");
        var response = await patch;

        Assert.Contains("tab=submissions", response.Url, StringComparison.Ordinal);
        Assert.True(
            response.Request.Headers.Keys.Any(k => k.Equals("vx-request", StringComparison.OrdinalIgnoreCase)),
            "Tab click must send VX-Request so the server returns a patch, not a full page.");
        await Assertions.Expect(_page.Locator("#pane h3")).ToHaveTextAsync("Submissions");
        Assert.Contains("tab=submissions", _page.Url, StringComparison.Ordinal);
        Assert.Equal(loadsAfterGoto, loads);
        Assert.Equal(1, await _page.Locator("#pane").CountAsync());
    }

    [Fact]
    public async Task ContactForm_ValidatesThenSubmitsInPlace()
    {
        await WorkbenchPage.GotoAsync(_page, _fx.BaseAddress);
        await WorkbenchPage.Nav(_page, "/contact").ClickAsync();
        await Assertions.Expect(_page.Locator("#contact h3")).ToHaveTextAsync("Contact Engineering Support");

        await _page.Locator("input[name='name']").FillAsync("   ");
        await _page.Locator("textarea[name='message']").FillAsync("   ");
        await _page.GetByRole(AriaRole.Button, new() { Name = "Send Message" }).ClickAsync();
        await Assertions.Expect(_page.Locator("#contact")).ToContainTextAsync("Name and message are required");
        await Assertions.Expect(_page.Locator("#contact-form")).ToBeVisibleAsync();

        await _page.Locator("input[name='name']").FillAsync("Khurram");
        await _page.Locator("textarea[name='message']").FillAsync("Browser journey for vaxel 1.0");
        await _page.GetByRole(AriaRole.Button, new() { Name = "Send Message" }).ClickAsync();
        await Assertions.Expect(_page.Locator("#contact-success")).ToContainTextAsync("Thank you, Khurram!");
        await Assertions.Expect(_page.Locator("#notices")).ToContainTextAsync("Contact message submitted successfully.");
        Assert.Contains("status=success", _page.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Proposal_EditSave_UpdatesCardAndLiveRail()
    {
        await WorkbenchPage.GotoAsync(_page, _fx.BaseAddress);
        await WorkbenchPage.ClickTabAsync(_page, "proposals");
        await Assertions.Expect(_page.Locator("#pane h3")).ToHaveTextAsync("Project Proposals");

        await _page.GetByRole(AriaRole.Link, new() { Name = "Edit Proposal" }).ClickAsync();
        await Assertions.Expect(_page.Locator("#prop-201")).ToContainTextAsync("Edit Proposal #201 Title:");

        var title = _page.Locator("#prop-201 input[name='title']");
        await title.FillAsync("Realtime Agent Architecture (browser)");
        await _page.Locator("#prop-201").GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await Assertions.Expect(_page.Locator("#prop-201")).ToContainTextAsync("Realtime Agent Architecture (browser)");
        await Assertions.Expect(_page.Locator("#notices")).ToContainTextAsync("Proposal updated successfully.");
        await Assertions.Expect(_page.Locator("#live-updates")).ToContainTextAsync("Proposal #201 updated");
    }

    [Fact]
    public async Task Approve_AnonymousRefused_AliceSucceeds()
    {
        await WorkbenchPage.GotoAsync(_page, _fx.BaseAddress);
        await WorkbenchPage.ClickTabAsync(_page, "submissions");
        await _page.Locator("#sub-101").GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
        await Assertions.Expect(_page.Locator("#notices")).ToContainTextAsync("Action Refused");
        await Assertions.Expect(_page.Locator("#sub-101")).ToContainTextAsync("Pending");

        await WorkbenchPage.SignInAsync(_page, _fx.BaseAddress, "Alice");
        await Assertions.Expect(_page.Locator("header")).ToContainTextAsync("Alice (Approver)");

        await WorkbenchPage.ClickTabAsync(_page, "submissions");
        await _page.Locator("#sub-101").GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
        await Assertions.Expect(_page.Locator("#sub-101")).ToContainTextAsync("Approved by Alice");
        await Assertions.Expect(_page.Locator("#notices")).ToContainTextAsync("Submission #101 approved.");
    }

    [Fact]
    public async Task Approve_BobIsRefused()
    {
        await WorkbenchPage.SignInAsync(_page, _fx.BaseAddress, "Bob");
        await Assertions.Expect(_page.Locator("header")).ToContainTextAsync("Bob (Viewer)");

        await WorkbenchPage.ClickTabAsync(_page, "submissions");
        await _page.Locator("#sub-101").GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
        await Assertions.Expect(_page.Locator("#notices")).ToContainTextAsync("Action Refused");
        await Assertions.Expect(_page.Locator("#sub-101")).ToContainTextAsync("Pending");
    }
}
