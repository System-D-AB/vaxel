using Microsoft.Playwright;
using Xunit;

namespace Vaxel.Browser.Tests;

[Collection(WorkbenchBrowserCollection.Name)]
public sealed class WorkbenchCspAndA11yTests : IAsyncLifetime
{
    private readonly WorkbenchBrowserFixture _fx;
    private IBrowserContext _context = null!;
    private IPage _page = null!;

    public WorkbenchCspAndA11yTests(WorkbenchBrowserFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _context = await _fx.Browser.NewContextAsync();
        _page = await _context.NewPageAsync();
        await _page.AddInitScriptAsync("""
            window.__vxCsp = [];
            document.addEventListener('securitypolicyviolation', (e) => {
              window.__vxCsp.push({
                directive: e.effectiveDirective,
                blocked: e.blockedURI,
                sample: e.sample
              });
            });
            """);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    [Fact]
    public async Task Workbench_StrictScriptSrc_ReportsNoCspViolations()
    {
        await WorkbenchPage.GotoAsync(_page, _fx.BaseAddress);
        await WorkbenchPage.ClickTabAsync(_page, "settings");
        await Assertions.Expect(_page.Locator("#pane h3")).ToHaveTextAsync("Settings");
        await WorkbenchPage.Nav(_page, "/contact").ClickAsync();
        await Assertions.Expect(_page.Locator("#contact")).ToBeVisibleAsync();

        var violations = await _page.EvaluateAsync<CspViolation[]>("() => window.__vxCsp || []");
        var scriptViolations = violations
            .Where(v => v.Directive.StartsWith("script-src", StringComparison.OrdinalIgnoreCase)
                        || v.Directive.Equals("script-src-elem", StringComparison.OrdinalIgnoreCase)
                        || v.Directive.Equals("script-src-attr", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(
            scriptViolations.Length == 0,
            "script-src CSP violations: " + string.Join("; ", scriptViolations.Select(v => $"{v.Directive} {v.Blocked} {v.Sample}")));
    }

    [Fact]
    public async Task TabClick_RestoresFocusToTrigger_AndClearsAriaBusy()
    {
        await WorkbenchPage.GotoAsync(_page, _fx.BaseAddress);
        var submissions = WorkbenchPage.Nav(_page, "/?tab=submissions");
        await submissions.ClickAsync();
        await Assertions.Expect(_page.Locator("#pane h3")).ToHaveTextAsync("Submissions");
        await Assertions.Expect(submissions).ToBeFocusedAsync();
        Assert.Equal(0, await _page.Locator("[aria-busy='true']").CountAsync());
    }

    [Fact]
    public async Task SsePatch_DoesNotStealFocus()
    {
        var observer = await _context.NewPageAsync();
        await WorkbenchPage.GotoAsync(observer, _fx.BaseAddress);
        var signIn = WorkbenchPage.Nav(observer, "/login");
        await signIn.FocusAsync();
        await Assertions.Expect(signIn).ToBeFocusedAsync();

        await WorkbenchPage.GotoAsync(_page, _fx.BaseAddress);
        await WorkbenchPage.ClickTabAsync(_page, "proposals");
        await _page.GetByRole(AriaRole.Link, new() { Name = "Edit Proposal" }).ClickAsync();
        await _page.Locator("#prop-201 input[name='title']").FillAsync("SSE focus probe");
        await _page.Locator("#prop-201").GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await Assertions.Expect(observer.Locator("#live-updates")).ToContainTextAsync("Proposal #201 updated");
        await Assertions.Expect(signIn).ToBeFocusedAsync();
    }

    private sealed class CspViolation
    {
        public string Directive { get; set; } = "";
        public string Blocked { get; set; } = "";
        public string Sample { get; set; } = "";
    }
}
