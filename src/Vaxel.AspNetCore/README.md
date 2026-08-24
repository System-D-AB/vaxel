# Vaxel.AspNetCore

ASP.NET Core integration and server hypermedia primitives for **vaxel**.

**Repository:** [github.com/System-D-AB/vaxel](https://github.com/System-D-AB/vaxel)

## Installation

```bash
dotnet add package Vaxel.AspNetCore
```

> **Note:** Referencing `Vaxel.AspNetCore` automatically pulls in `Vaxel.Client` as a transitive dependency. No `npm`, `node`, or client-side build steps are required.

## Quick Start

### 1. Register Services and Middleware

In `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddVaxel(options =>
{
    options.Push.HeartbeatSeconds = 20;
    options.Push.MaxConnectionsPerIdentity = 4;
});

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.UseVaxel();

app.MapRazorPages();
app.MapVaxelStream("/_vaxel/stream");

app.Run();
```

### 2. Add Layout Script Tag

In `_Layout.cshtml`:

```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <title>My Application</title>
    <vaxel-antiforgery />
</head>
<body vx-sse="/_vaxel/stream">
    <section id="pane" vx-region>
        @RenderBody()
    </section>
    <script src="/_vaxel/vaxel.js"></script>
</body>
</html>
```

### 3. Handle Page vs Patch

```csharp
public async Task<IActionResult> OnGetAsync()
{
    return await Vaxel.PageOrPatch(HttpContext,
        page: () => Task.FromResult<IActionResult>(Page()),
        patch: async () =>
        {
            var fragment = await _composer.PartialAsync("_Content", Model);
            return Patch.Ok().Replace("#pane", fragment);
        });
}
```

## Security & CSP

vaxel is designed from the ground up to run under strict Content Security Policies with **zero `unsafe-eval`**, **zero `new Function()`**, and **zero string timers**:

```http
Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self';
```

## Documentation

Architecture, protocol, and cookbook: [docs/](https://github.com/System-D-AB/vaxel/tree/master/docs).
