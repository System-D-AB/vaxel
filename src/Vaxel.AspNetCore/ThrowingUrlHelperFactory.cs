using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Vaxel;

internal sealed class ThrowingUrlHelperFactory : IUrlHelperFactory
{
    public IUrlHelper GetUrlHelper(ActionContext context) => new ThrowingUrlHelper(context);

    private sealed class ThrowingUrlHelper : IUrlHelper
    {
        public ThrowingUrlHelper(ActionContext actionContext) => ActionContext = actionContext;

        public ActionContext ActionContext { get; }

        public string? Action(UrlActionContext actionContext) => throw Create();

        public string? Content(string? contentPath) => contentPath;

        public bool IsLocalUrl(string? url) => false;

        public string? Link(string? routeName, object? values) => throw Create();

        public string? RouteUrl(UrlRouteContext routeContext) => throw Create();

        private static VaxelFragmentContextException Create()
            => new("Url.Page");
    }
}
