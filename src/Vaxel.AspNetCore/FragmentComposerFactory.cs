using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Vaxel;

internal sealed class FragmentComposerFactory : IFragmentComposerFactory
{
    private readonly IServiceScopeFactory _scopeFactory;

    public FragmentComposerFactory(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public IFragmentComposer Create(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return new FragmentComposer(httpContext.RequestServices, httpContext);
    }

    public IFragmentComposer CreateBackground()
    {
        var scope = _scopeFactory.CreateScope();
        var http = BackgroundHttpContext.Create(scope.ServiceProvider);
        return new FragmentComposer(http.RequestServices, http, ownedScope: scope);
    }
}
