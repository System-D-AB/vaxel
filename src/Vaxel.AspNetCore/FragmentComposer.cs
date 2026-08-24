using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Vaxel;

internal sealed class FragmentComposer : IFragmentComposer, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly HttpContext _httpContext;
    private readonly IServiceScope? _ownedScope;

    public FragmentComposer(
        IServiceProvider services,
        HttpContext httpContext,
        IServiceScope? ownedScope = null)
    {
        _services = services;
        _httpContext = httpContext;
        _ownedScope = ownedScope;
    }

    public async Task<IHtmlContent> PartialAsync(string name, object? model = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var helper = CreateHtmlHelper(model);
        try
        {
            return await helper.PartialAsync(name, model);
        }
        catch (InvalidOperationException ex) when (IsMissingView(ex))
        {
            throw new VaxelFragmentNotFoundException(name);
        }
    }

    public async Task<IHtmlContent> ViewAsync(string name, object? model = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var view = FindView(name, isMainPage: false) ?? FindView(name, isMainPage: true);
        if (view is null)
        {
            throw new VaxelFragmentNotFoundException(name);
        }

        return await RenderViewAsync(view, model);
    }

    public async Task<IHtmlContent> PageAsync(string pagePath, object? model = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pagePath);
        var view = FindPageView(pagePath);
        if (view is null)
        {
            throw new VaxelFragmentNotFoundException(pagePath);
        }

        return await RenderViewAsync(view, model);
    }

    public Task<IHtmlContent> ComponentAsync<TViewComponent>(object? arguments = null)
        => InvokeComponentAsync(typeof(TViewComponent), name: null, arguments);

    public Task<IHtmlContent> ComponentAsync(string name, object? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return InvokeComponentAsync(type: null, name, arguments);
    }

    public async Task<IHtmlContent> RazorComponentAsync<TComponent>(object? parameters = null)
        where TComponent : IComponent
    {
        var loggerFactory = _services.GetRequiredService<ILoggerFactory>();
        await using var renderer = new HtmlRenderer(_services, loggerFactory);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(ToParameterView(parameters));
            return output.ToHtmlString();
        });
        return new HtmlString(html);
    }

    public void Dispose() => _ownedScope?.Dispose();

    private async Task<IHtmlContent> InvokeComponentAsync(Type? type, string? name, object? arguments)
    {
        var helper = _services.GetRequiredService<IViewComponentHelper>();
        var viewContext = CreateViewContext(model: null, TextWriter.Null);
        ((IViewContextAware)helper).Contextualize(viewContext);
        try
        {
            if (type is not null)
            {
                return await helper.InvokeAsync(type, arguments);
            }

            return await helper.InvokeAsync(name!, arguments);
        }
        catch (InvalidOperationException ex) when (IsMissingViewComponent(ex))
        {
            throw new VaxelFragmentNotFoundException(type?.Name ?? name!);
        }
    }

    private async Task<IHtmlContent> RenderViewAsync(IView view, object? model)
    {
        await using var writer = new StringWriter();
        var viewContext = CreateViewContext(model, writer);
        await view.RenderAsync(viewContext);
        return new HtmlString(writer.ToString());
    }

    private IView? FindView(string name, bool isMainPage)
    {
        var engine = _services.GetRequiredService<ICompositeViewEngine>();
        var result = engine.FindView(GetActionContext(), name, isMainPage);
        return result.Success ? result.View : null;
    }

    private IView? FindPageView(string pagePath)
    {
        var engine = _services.GetRequiredService<ICompositeViewEngine>();
        var actionContext = GetActionContext();
        foreach (var candidate in PageCandidates(pagePath))
        {
            var get = engine.GetView(executingFilePath: null, candidate, isMainPage: false);
            if (get.Success)
            {
                return get.View;
            }

            var find = engine.FindView(actionContext, candidate, isMainPage: false);
            if (find.Success)
            {
                return find.View;
            }
        }

        return null;
    }

    private IEnumerable<string> PageCandidates(string pagePath)
    {
        yield return pagePath;

        var trimmed = pagePath.TrimStart('~');
        if (!trimmed.StartsWith('/'))
        {
            trimmed = "/" + trimmed;
        }

        yield return trimmed;
        var withExtension = trimmed.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + ".cshtml";
        yield return withExtension;

        var pagesRoot = (_services.GetService<IOptions<RazorPagesOptions>>()?.Value.RootDirectory ?? "/Pages")
            .TrimEnd('/');
        if (!withExtension.StartsWith(pagesRoot + "/", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(withExtension, pagesRoot, StringComparison.OrdinalIgnoreCase))
        {
            yield return pagesRoot + withExtension;
        }
    }

    private IHtmlHelper CreateHtmlHelper(object? model)
    {
        var helper = _services.GetRequiredService<IHtmlHelper>();
        ((IViewContextAware)helper).Contextualize(CreateViewContext(model, TextWriter.Null));
        return helper;
    }

    private ViewContext CreateViewContext(object? model, TextWriter writer)
    {
        var actionContext = GetActionContext();
        var metadataProvider = _services.GetService<IModelMetadataProvider>() ?? new EmptyModelMetadataProvider();
        var viewData = new ViewDataDictionary(metadataProvider, new ModelStateDictionary())
        {
            Model = model
        };
        var tempDataProvider = _services.GetRequiredService<ITempDataProvider>();
        var tempData = new TempDataDictionary(_httpContext, tempDataProvider);
        return new ViewContext(
            actionContext,
            NullView.Instance,
            viewData,
            tempData,
            writer,
            new HtmlHelperOptions());
    }

    private ActionContext GetActionContext()
    {
        var routeData = _httpContext.GetRouteData() ?? new Microsoft.AspNetCore.Routing.RouteData();
        if (!routeData.Values.ContainsKey("controller"))
        {
            routeData.Values["controller"] = "Composer";
        }

        if (!routeData.Values.ContainsKey("action"))
        {
            routeData.Values["action"] = "Fragment";
        }

        return new ActionContext(_httpContext, routeData, new ActionDescriptor());
    }

    private static ParameterView ToParameterView(object? parameters)
    {
        if (parameters is null)
        {
            return ParameterView.Empty;
        }

        if (parameters is ParameterView view)
        {
            return view;
        }

        if (parameters is IDictionary<string, object?> dict)
        {
            return ParameterView.FromDictionary(dict);
        }

        var mapped = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in parameters.GetType().GetProperties())
        {
            mapped[property.Name] = property.GetValue(parameters);
        }

        return ParameterView.FromDictionary(mapped);
    }

    private static bool IsMissingView(InvalidOperationException ex)
        => ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase);

    private static bool IsMissingViewComponent(InvalidOperationException ex)
        => ex.Message.Contains("ViewComponent", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase);

    private sealed class NullView : IView
    {
        public static readonly NullView Instance = new();

        public string Path => "/";

        public Task RenderAsync(ViewContext context) => Task.CompletedTask;
    }
}
