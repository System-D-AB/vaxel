using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Vaxel.AspNetCore.Tests.PatchTests;

/// <summary>
/// Hosts the ComposerHost on real Kestrel with default options so a patch response is written
/// through a stream that disallows synchronous IO (<c>AllowSynchronousIO = false</c>).
/// TestServer allows synchronous IO and therefore cannot observe US-2.
/// </summary>
public sealed class ComposerHostKestrelFactory
    : WebApplicationFactory<global::Vaxel.AspNetCore.Tests.Fixtures.ComposerHost.Program>
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
        builder.UseContentRoot(FindTestProjectRoot());
        builder.UseEnvironment("Development");
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var testHost = builder.Build();

        builder.ConfigureWebHost(webHost =>
        {
            // Default Kestrel: AllowSynchronousIO stays false.
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

    private static string FindTestProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Vaxel.AspNetCore.Tests.csproj")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find Vaxel.AspNetCore.Tests.csproj above {AppContext.BaseDirectory}.");
    }
}
