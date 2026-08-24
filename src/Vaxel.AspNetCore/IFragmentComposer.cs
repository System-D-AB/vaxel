using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Components;

namespace Vaxel;

public interface IFragmentComposer
{
    Task<IHtmlContent> PartialAsync(string name, object? model = null);

    Task<IHtmlContent> ViewAsync(string name, object? model = null);

    Task<IHtmlContent> ComponentAsync<TViewComponent>(object? arguments = null);

    Task<IHtmlContent> ComponentAsync(string name, object? arguments = null);

    Task<IHtmlContent> RazorComponentAsync<TComponent>(object? parameters = null)
        where TComponent : IComponent;

    Task<IHtmlContent> PageAsync(string pagePath, object? model = null);
}
