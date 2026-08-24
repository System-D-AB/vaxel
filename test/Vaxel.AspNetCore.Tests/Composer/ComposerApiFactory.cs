using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Vaxel.AspNetCore.Tests.Composer;

public sealed class ComposerApiFactory : WebApplicationFactory<global::Vaxel.AspNetCore.Tests.Fixtures.ComposerHost.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(GetProjectDirectory());
        builder.UseEnvironment("Development");
    }

    private static string GetProjectDirectory()
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
