using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Vaxel;

internal sealed class ThrowingLinkGenerator : LinkGenerator
{
    public override string? GetPathByAddress<TAddress>(
        HttpContext httpContext,
        TAddress address,
        RouteValueDictionary values,
        RouteValueDictionary? ambientValues = null,
        PathString? pathBase = default,
        FragmentString fragment = default,
        LinkOptions? options = null)
        => throw Create();

    public override string? GetPathByAddress<TAddress>(
        TAddress address,
        RouteValueDictionary values,
        PathString pathBase = default,
        FragmentString fragment = default,
        LinkOptions? options = null)
        => throw Create();

    public override string? GetUriByAddress<TAddress>(
        HttpContext httpContext,
        TAddress address,
        RouteValueDictionary values,
        RouteValueDictionary? ambientValues = null,
        string? scheme = null,
        HostString? host = null,
        PathString? pathBase = null,
        FragmentString fragment = default,
        LinkOptions? options = null)
        => throw Create();

    public override string? GetUriByAddress<TAddress>(
        TAddress address,
        RouteValueDictionary values,
        string scheme,
        HostString host,
        PathString pathBase = default,
        FragmentString fragment = default,
        LinkOptions? options = null)
        => throw Create();

    private static VaxelFragmentContextException Create()
        => new("Url.Page");
}
