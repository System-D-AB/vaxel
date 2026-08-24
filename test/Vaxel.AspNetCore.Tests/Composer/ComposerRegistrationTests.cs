using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Vaxel;

namespace Vaxel.AspNetCore.Tests.Composer;

public sealed class ComposerRegistrationTests
{
    [Fact]
    public void AddVaxel_Resolves_Composer_Inside_Request()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvc();
        services.AddVaxel();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };

        var composer = scope.ServiceProvider.GetRequiredService<IFragmentComposer>();
        var factory = scope.ServiceProvider.GetRequiredService<IFragmentComposerFactory>();

        Assert.NotNull(composer);
        Assert.NotNull(factory);
    }

    [Fact]
    public void AddVaxel_DoesNotRegisterInteractiveBlazor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvc();
        services.AddVaxel();

        Assert.DoesNotContain(
            services,
            d => d.ServiceType.FullName is { } name
                 && (name.Contains("Circuits.Circuit", StringComparison.Ordinal)
                     || name.Contains("InteractiveServer", StringComparison.Ordinal)
                     || name.Contains("InteractiveWebAssembly", StringComparison.Ordinal)));
    }
}
