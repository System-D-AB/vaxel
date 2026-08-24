using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Vaxel.AspNetCore.Tests.Workbench;

public sealed class WorkbenchApiFactory : WebApplicationFactory<global::Workbench.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var contentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "Workbench"));
        builder.UseContentRoot(contentRoot);
        builder.UseEnvironment("Development");
    }
}
