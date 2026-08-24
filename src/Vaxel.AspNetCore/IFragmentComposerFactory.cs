using Microsoft.AspNetCore.Http;

namespace Vaxel;

public interface IFragmentComposerFactory
{
    IFragmentComposer Create(HttpContext httpContext);

    IFragmentComposer CreateBackground();
}
