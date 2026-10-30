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

## Core Features

### 1. Render-Time Validated Signal Expressions (Zero `eval`)

`vx-show`, `vx-class:*`, `vx-attr:*`, `vx-text` (with `@{ … }` interpolation), and `vx-on` `filter`/`set` stages accept pure expressions over signals.
- **Server validation at render time:** `VxBindTagHelper` and `VxOnTagHelper` parse expressions in C# and type-check them against your registered `AddSignalSchema<T>()` model. Syntax errors, unknown functions, unknown signals, type mismatches, or forbidden attributes (`href`, `src`, `action`, `formaction`, `style`, `on*`) throw `VaxelTagHelperException` with the offending character span during Razor rendering.
- **Client AST execution without `eval`:** The browser agent evaluates the parsed AST over the auto-tracking signal store and a 20-function pure whitelist — with zero `eval()`, zero `new Function()`, and own-property guards blocking `__proto__`/`constructor`/`prototype`.

### 2. Declarative `vx-on` Event Stream Pipelines

Compose DOM events, `window`/`document`/`outside` scopes, `interval <ms>`, `visible`, and `load` through stream operators (`debounce`, `throttle`, `delay`, `once`, `distinct`, `key`, `filter <expr>`, `prevent`, `stop`) into either an HTTP request or `set <signal> = <expr>`:

```html
<div vx-signals='{"qty":1,"price":25,"saving":false,"q":"","open":false}'>
    <p vx-text="Preview total: $@{ qty * price }"></p>
    <button type="submit" vx-attr:disabled="qty <= 0 || saving">Save</button>

    <input name="q" vx-bind="q"
           vx-get="/search" vx-target="#results"
           vx-on="input | debounce 200 | distinct" />

    <div vx-show="open" vx-on="keydown window | key Escape | set open = false">…</div>
</div>
```

### 3. Server Push Pipelines & Bounded `Bus<T>`

Compose `IAsyncEnumerable<T>` sources and in-process/backplane-enabled `IBus<T>` topics into per-recipient SSE patches with zero Rx.NET dependencies:

```csharp
push.From(orderBus.Subscribe(ct))
    .Throttle(TimeSpan.FromMilliseconds(250))
    .Select(async (evt, recipient, composer, token) =>
        Patch.Ok().Replace($"#order-{evt.Id}", await composer.PartialAsync("_OrderRow", evt)))
    .To(PushScope.Group("kitchen"), ct);
```

### 4. View Transitions & Top Progress Bar

Add `vx-transition` to regions (and `vx-transition-name` to shared elements) for smooth View Transition API / CSS `.vx-swapping` & `.vx-settling` animations, and configure the built-in top-of-viewport loading bar with `vx-progress`, `vx-indicator`, and `vx-disable`.

### 5. Layout-Shell Navigation (`vx-boost`, `IVaxelRequest`, `FullPagePaths`)

Adopt Vaxel across an entire MVC or Razor Pages layout shell using `vx-boost` + `vx-target="#content"` + `vx-boost-exclude`, scoped `IVaxelRequest` (`IsPatch`, `IsHistoryRestore`, `Target`, `ShellPatches`), and `VaxelOptions.FullPagePaths`.

## Security & CSP

vaxel is designed from the ground up to run under strict Content Security Policies with **zero `unsafe-eval`**, **zero `new Function()`**, and **zero string timers**:

```http
Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self';
```

## Documentation

Architecture, protocol, and cookbook: [docs/](https://github.com/System-D-AB/vaxel/tree/master/docs).
