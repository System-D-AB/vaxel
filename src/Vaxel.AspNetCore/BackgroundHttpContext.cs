using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Vaxel;

internal static class BackgroundHttpContext
{
    public static HttpContext Create(IServiceProvider services)
    {
        var http = new DefaultHttpContext();
        var wrapped = new BackgroundServiceProvider(services);
        http.Features.Set<IServiceProvidersFeature>(new ServiceProvidersFeature { RequestServices = wrapped });
        http.User = new ThrowingUserPrincipal();
        http.Items[typeof(IUrlHelper)] = new ThrowingUrlHelperFactory().GetUrlHelper(
            new ActionContext(http, http.GetRouteData() ?? new RouteData(), new ActionDescriptor()));
        http.Request.Method = HttpMethods.Get;
        http.Request.Path = "/";
        http.Request.Host = new HostString("localhost");
        http.Request.Scheme = "http";
        http.Request.PathBase = PathString.Empty;
        return http;
    }

    private sealed class BackgroundServiceProvider : IServiceProvider, ISupportRequiredService
    {
        private readonly IServiceProvider _inner;

        public BackgroundServiceProvider(IServiceProvider inner) => _inner = inner;

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IServiceProvider) || serviceType == typeof(ISupportRequiredService))
            {
                return this;
            }

            if (serviceType == typeof(LinkGenerator))
            {
                return new ThrowingLinkGenerator();
            }

            if (serviceType == typeof(IUrlHelperFactory))
            {
                return new ThrowingUrlHelperFactory();
            }

            return _inner.GetService(serviceType);
        }

        public object GetRequiredService(Type serviceType)
            => GetService(serviceType)
               ?? throw new InvalidOperationException($"Service '{serviceType}' is not registered.");
    }

    private sealed class ThrowingUserPrincipal : ClaimsPrincipal
    {
        public override IIdentity? Identity
            => throw new VaxelFragmentContextException("User");
    }
}
