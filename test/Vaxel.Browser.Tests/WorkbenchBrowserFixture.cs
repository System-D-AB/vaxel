using Microsoft.Playwright;
using Xunit;

namespace Vaxel.Browser.Tests;

public sealed class WorkbenchBrowserFixture : IAsyncLifetime
{
    public WorkbenchKestrelFactory Factory { get; } = new();

    public IPlaywright Playwright { get; private set; } = null!;

    public IBrowser Browser { get; private set; } = null!;

    public Uri BaseAddress => Factory.ServerAddress;

    public async Task InitializeAsync()
    {
        var install = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (install != 0)
        {
            throw new InvalidOperationException($"Playwright chromium install exited {install}.");
        }

        _ = Factory.ServerAddress;

        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = Environment.GetEnvironmentVariable("HEADED") != "1"
        });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.CloseAsync();
        }

        Playwright?.Dispose();
        await Factory.DisposeAsync();
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WorkbenchBrowserCollection : ICollectionFixture<WorkbenchBrowserFixture>
{
    public const string Name = "Workbench browser";
}
