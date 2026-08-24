using Microsoft.AspNetCore.Mvc;

namespace Vaxel.AspNetCore.Tests.Fixtures.ComposerHost.ViewComponents;

public sealed class BadgeViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(string label)
    {
        ViewBag.Label = label;
        return View();
    }
}
