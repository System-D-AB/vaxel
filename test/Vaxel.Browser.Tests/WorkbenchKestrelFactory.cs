using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Vaxel.Browser.Tests;

/// <summary>
/// Workbench on real Kestrel so Playwright can open a TCP URL (TestServer is in-process only).
/// </summary>
public sealed class WorkbenchKestrelFactory : WebApplicationFactory<global::Workbench.Program>
{
    private IHost? _kestrel;

    public Uri ServerAddress
    {
        get
        {
            _ = CreateDefaultClient();
            return ClientOptions.BaseAddress
                ?? throw new InvalidOperationException("Kestrel did not publish a server address.");
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(FindWorkbenchRoot());
        builder.UseEnvironment("Development");
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var testHost = builder.Build();

        builder.ConfigureWebHost(webHost =>
        {
            webHost.UseKestrel();
            webHost.UseUrls("http://127.0.0.1:0");
        });

        _kestrel = builder.Build();
        _kestrel.Start();

        var addresses = _kestrel.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrel has no IServerAddressesFeature.");

        var url = addresses.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("Kestrel published no listen address.");

        ClientOptions.BaseAddress = new Uri(url.TrimEnd('/') + "/");

        testHost.Start();
        return testHost;
    }

    public override async ValueTask DisposeAsync()
    {
        if (_kestrel is not null)
        {
            await _kestrel.StopAsync();
            _kestrel.Dispose();
            _kestrel = null;
        }

        await base.DisposeAsync();
    }

    private static string FindWorkbenchRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "samples", "Workbench", "Workbench.csproj");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate)!;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find samples/Workbench above {AppContext.BaseDirectory}.");
    }
}
