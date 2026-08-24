# 05 — .NET API

The half that matters. Everything here is ordinary ASP.NET Core: no source generators, no runtime code emission, no reflection tricks beyond what MVC already does.

## Packages

| Package | Contents | Depends on |
|---|---|---|
| `Vaxel.AspNetCore` | `IFragmentComposer`, `Patch`/`PatchResult`, signal binding, Tag Helpers, SSE endpoint, DI | ASP.NET Core only |
| `Vaxel.Client` | The agent + morph as static assets (`wwwroot/_vaxel/vaxel.js`), served via static web assets | — |
| `Vaxel.Testing` | Patch assertions, the page-parity harness, a fake composer | xUnit-agnostic |

Target frameworks: current LTS and current. No dependency on any client build toolchain.

## Registration

```csharp
builder.Services.AddVaxel(options =>
{
    options.AntiforgeryHeaderName = "X-CSRF";       // default: the app's configured header
    options.SignalsHeaderName     = "VX-Signals";   // signals never travel in the URL or the body
    options.MaxSignalsBytes       = 8 * 1024;
    options.NoStoreWhenSignalsRead = true;
    options.DefaultSwap           = SwapMode.Morph;
    options.Push.HeartbeatSeconds = 20;
});

app.UseVaxel();                  // response marking, protocol negotiation, no per-request state
app.MapVaxelStream("/_vaxel/stream");   // optional: the SSE channel
```

`UseVaxel()` adds no middleware that touches requests it does not own: it sets `Vary: VX-Request`, negotiates the protocol version, and short-circuits nothing.

## Rendering any Razor unit

```csharp
public interface IFragmentComposer
{
    Task<IHtmlContent> PartialAsync(string name, object? model = null);
    Task<IHtmlContent> ViewAsync(string name, object? model = null);
    Task<IHtmlContent> ComponentAsync<TViewComponent>(object? arguments = null);
    Task<IHtmlContent> ComponentAsync(string name, object? arguments = null);
    Task<IHtmlContent> RazorComponentAsync<TComponent>(object? parameters = null)
        where TComponent : IComponent;                       // static SSR: no circuit, no WASM
    Task<IHtmlContent> PageAsync(string pagePath, object? model = null);
}
```

Injectable into Razor Page models, controllers, minimal-API handlers and hosted services alike. Invoking a ViewComponent or a View outside a view context requires a bootstrapped `ViewContext`, `ActionContext` and `TempData` — that plumbing lives here, once, instead of being rediscovered in every project.

Rendering in a **background** context (for SSE pushes from a hosted service) uses the same interface with an explicit `HttpContext`-free scope; the composer refuses, with a clear message, anything that genuinely needs the current request (`Url.Page`, `User`), so the failure is at development time rather than at 3 a.m.

## Building responses

```csharp
public async Task<IResult> OnPostAddFieldAsync(string appId, [FromSignals] ShellSignals ui)
{
    var result = await _ops.DispatchAsync("add_field", …);
    if (result.Refused)
        return Patch.Refused(result.Refusal)
                    .Into("#notices", await Fragments.PartialAsync("_Notice", result.Refusal));

    return Patch.Ok()
        .Replace("#canvas",       await Fragments.PartialAsync("_Canvas", result.Draft))
        .Replace("#status-strip", await Fragments.ComponentAsync<StatusStrip>(new { appId }))
        .Append ("#notices",      await Fragments.PartialAsync("_Notice", Notice.Saved))
        .Signals(new { draftSeq = result.Draft.Seq })
        .Focus("#field-" + result.NewFieldKey)
        .Announce("Field added")
        .PushUrl($"/apps/{appId}/pages/{result.PageId}");
}
```

Builder surface:

| Member | Emits |
|---|---|
| `Patch.Ok()` / `Patch.Status(code)` / `Patch.Refused(refusal)` | response status |
| `.Replace(target, content)` | `mode="morph"` |
| `.Outer` / `.ReplaceHard` / `.Inner` / `.Append` / `.Prepend` / `.Before` / `.After` (target, content) | the corresponding mode |
| `.Remove(target)` | `mode="remove"` |
| `.InNamespace(Svg \| MathMl)` on a patch | `namespace=` for fragments patched inside `<svg>` |
| `.Signals(object \| IDictionary)` | `<vx-signals>` |
| `.Focus` / `.Scroll` / `.Title` / `.Announce` / `.PushUrl` / `.ReplaceUrl` / `.Redirect` / `.Reload` | `<vx-directive>` |
| `.Transition(target)` | `transition="view"` on that patch |

`PatchResult` implements `IResult` and `IActionResult`, so it returns from minimal APIs, controllers and Page handlers unchanged.

**Page-or-patch.** A handler that must serve both a full page and a fragment writes it once:

```csharp
return Vaxel.PageOrPatch(HttpContext,
    page:  () => Page(),
    patch: () => Patch.Ok().Replace("#pane", Fragments.PartialAsync("_Pane", model)));
```

If `VX-Request` is absent, the page renders — which is exactly the invariant R3 asks for, expressed in code rather than in a convention.

## Reading signals

```csharp
public sealed record ShellSignals(string Tab = "overview", string? Filter = null, bool RailOpen = true);

public Task<IResult> OnGet([FromSignals] ShellSignals ui) { … }
```

A model binder deserialises the `VX-Signals` header into a type. It is `System.Text.Json` with a case-insensitive, camelCase-friendly policy, invariant culture for numbers and dates, and it never throws on unknown or malformed keys — signals are advisory input from an untrusted client, and a rename must not 500 an endpoint. `ISignalReader` is available for dynamic access.

**Signals do not touch the body**, so form posts bind exactly as they always have: `[BindProperty]`, `ModelState`, validation attributes, `TryValidateModel`, `IValidatableObject` — untouched. That is deliberate: an earlier revision wrapped values in a JSON envelope and quietly broke every one of them.

**Reading signals marks the response uncacheable.** When `[FromSignals]` binds or `ISignalReader` is touched, the framework sets `Cache-Control: private, no-store` unless the handler overrides it, because a response that varies by a header that is not in `Vary` is a cache-poisoning bug waiting to happen. State that should be cacheable belongs in the URL — `vx-url-sync` puts it there.

**Security note, restated because it is the one that gets forgotten:** signals are user-authored. Bind them, validate them, never authorise with them. See [06 — Security](06-security.md).

## Tag Helpers

Razor never types a raw `vx-` attribute:

```html
<a vx-page-handler="SelectTab" vx-target="#pane" vx-vals-tab="submissions"
   asp-page="/Apps/Detail" asp-route-appId="@Model.AppId">Submissions</a>

<form vx-post asp-page-handler="AddField" vx-target="#canvas" vx-indicator="#saving">
  <button type="submit">Add field</button>
</form>

<section id="pane" vx-region>…</section>

<span vx-text="draftSeq">@Model.DraftSeq</span>
<button vx-attr:disabled="saving">Save</button>
```

Tag Helpers validate at compile/render time what the agent cannot: that a target is an `#id`, that a bound name is a declared signal (when a signal schema is registered), that a trigger sits on a degradable element. They are also the seam that lets the agent be replaced — or the same protocol be driven by htmx plus a morph extension — without touching a view.

An optional **signal schema** registers names and types so bindings can be checked and a strongly-typed record generated for `[FromSignals]`:

```csharp
builder.Services.AddVaxel().AddSignalSchema<ShellSignals>();
```

## Pushing from the server

```csharp
public interface IPushChannel
{
    Task PushAsync(PushScope scope, PatchDocument document, CancellationToken ct = default);
}

await push.PushAsync(PushScope.User(userId),
    Patch.Ok().Inner("#queue-count", await fragments.PartialAsync("_QueueCount", count)));
```

`PushScope` is `User`, `Group` or `Broadcast`. The default transport is in-process (one node); an abstraction point (`IPushBackplane`) allows Redis, Postgres `LISTEN/NOTIFY` or a service bus without changing calling code. Scale-out without a backplane is a documented limitation, not a silent one.

## Hosting notes

- Response buffering must be disabled on the stream route; the SSE endpoint sets the headers and calls `DisableBuffering` itself, but reverse proxies need their own configuration and the docs must say which for the common ones.
- Idle timeouts on managed platforms terminate long streams; the heartbeat default is chosen to sit under the tightest common value, and it is configurable for the rest.
- Nothing else in the framework holds a connection, so a vaxel app scales to zero exactly like a plain MVC app when no stream is open.
