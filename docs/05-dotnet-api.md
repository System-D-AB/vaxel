# 05 — .NET API

The half that matters. Everything here is ordinary ASP.NET Core: no source generators, no runtime code emission, no reflection tricks beyond what MVC already does.

## Packages

| Package | Contents | Depends on | Install |
|---|---|---|---|
| `Vaxel.AspNetCore` | `IFragmentComposer`, `Patch`/`PatchResult`, signal binding, Tag Helpers, SSE endpoint, DI | ASP.NET Core + `Vaxel.Client` | Required (web apps) |
| `Vaxel.Client` | Pre-compiled agent + morph runtime (`wwwroot/_vaxel/vaxel.js`), static web assets | — | Transitive; do not reference directly |
| `Vaxel.Testing` | Patch assertions, the page-parity harness, fake composer, SSE stream client | `Vaxel.AspNetCore` | Test projects |
| `Vaxel.Analyzers` | Roslyn analyzers and diagnostic rules (`VAXEL001`–`VAXEL003`) | — | Opt-in development dependency (`PrivateAssets="all"`) |
| `Vaxel.Datastar` | Conformance adapter for Datastar SDK test suite | `Vaxel.AspNetCore` | **Not packed in v1.0** (repo-only) |

Target frameworks: current LTS and current (.NET 10). No dependency on any client build toolchain or npm at build/restore time.

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

<span vx-text="Showing @{ count } rows">@Model.Count</span>
<button vx-attr:disabled="name == '' || saving">Save</button>
<div vx-show="tabs.length > 1">…</div>
```

Tag Helpers validate at compile/render time what the agent cannot: that a target is an `#id`, that a trigger sits on a degradable element, and that every expression in `vx-show`, `vx-text`, `vx-class:*`, and `vx-attr:*` parses within the length (≤ 512) and depth (≤ 32) caps, calls only whitelisted functions, and never binds `href`, `src`, `action`, `formaction`, `style`, or `on*`. They are also the seam that lets the agent be replaced — or the same protocol be driven by htmx plus a morph extension — without touching a view.

### Attribute coverage

A Tag Helper claims every `vx-*` attribute the 1.0 agent implements:

| Attribute(s) | Tag Helper |
|---|---|
| `vx-get` `vx-post` `vx-put` `vx-patch` `vx-delete` | `VxTriggerTagHelper` |
| `vx-on` | `VxOnTagHelper` (chain grammar, literal args, `filter`/`set` AST & schema validation) |
| `vx-target` | `VxTargetTagHelper` |
| `vx-region` | `VxRegionTagHelper` |
| `vx-indicator` `vx-disable` | `VxIndicatorTagHelper` |
| `vx-text` `vx-show` `vx-class:*` `vx-attr:*` `vx-bind` | `VxBindTagHelper` |
| `vx-vals-*` | `VxValsTagHelper` |
| `vx-signals` `vx-signals-if-missing` | `VxSignalsTagHelper` / `VxSignalsIfMissingTagHelper` (JSON-object validated) |
| `vx-persist` `vx-persist-session` | `VxPersistTagHelper` / `VxPersistSessionTagHelper` (wildcard rejected) |
| `vx-confirm` `vx-preserve` `vx-sse` | `VxConfirmTagHelper` / `VxPreserveTagHelper` / `VxSseTagHelper` |
| `vaxel-antiforgery` | `VaxelAntiforgeryTagHelper` |

**What a view still writes raw.** `vx-sync`, `vx-encoding`, `vx-style:*`, `vx-url-sync`, `vx-preserve-attr` and `vx-overwrite-dirty` are implemented by the agent but have no dedicated Tag Helper yet; write them as plain attributes.

An optional **signal schema** registers names and types so bindings, `vx-on` `filter` predicates, and `vx-on` `set` sinks are type-checked at render time (and a strongly-typed record can be bound via `[FromSignals]`):

```csharp
builder.Services.AddVaxel(o => o.SignalSchema = new SignalSchema<ShellSignals>());
```

- **With a signal schema registered:** `VxBindTagHelper` and `VxOnTagHelper` validate syntax, the 20-function whitelist, length/depth caps, signal existence, and static operand/return types (`number | string | bool | list | object | null | any`), throwing `VaxelTagHelperException` with the offending character span on any mismatch (e.g., `vx-show="tabs > 1"` where `Tabs` is a list, or a reference to an undeclared signal). In `VxOnTagHelper`, `filter` and `set` expressions additionally have access to the typed event properties `key` (`string`), `value` (`string`), and `checked` (`bool`).
- **Without a signal schema registered:** `VxBindTagHelper` and `VxOnTagHelper` still validate syntax, the function whitelist, forbidden attributes, operator/sink structure, and the length/depth caps (treating user signal types as `any`) — degrading gracefully to less than full schema typing while still catching syntax and whitelist mistakes before deploy.
- **No server evaluation:** the server parses and type-checks only; expression values are computed in exactly one place, the client agent.

## Pushing from the server

```csharp
public interface IPushChannel
{
    IPushPipeline<T> From<T>(IAsyncEnumerable<T> source, IVaxelScheduler? scheduler = null);
    Task PushAsync(PushScope scope, PatchBuilder patch, CancellationToken ct = default);
    Task PushAsync(PushScope scope, string rawPatchHtml, CancellationToken ct = default);
    Task PushReloadAsync(PushScope scope, CancellationToken ct = default);
}

await push.PushAsync(PushScope.User(userId),
    Patch.Ok().Inner("#queue-count", await fragments.PartialAsync("_QueueCount", count)));
```

`PushScope` is `User`, `Group` or `Broadcast`. The default transport is in-process (one node); an abstraction point (`IPushBackplane`) allows Redis, Postgres `LISTEN/NOTIFY` or a service bus without changing calling code. Scale-out without a backplane is a documented limitation, not a silent one.

### Push pipelines (`push.From(source)`)

A server-side `IAsyncEnumerable<T>` source can be composed into scoped SSE pushes using the same operator vocabulary and marble semantics as client `vx-on` chains:

```csharp
await push.From(store.Watch(db, box, indexName))
    .DistinctUntilChanged(s => s.State)
    .Throttle(TimeSpan.FromSeconds(1))
    .Select(async (s, composer, ct) =>
        Patch.Ok().Replace($"#ix-{s.Name}", await composer.PartialAsync("_StateBadge", s)))
    .To(PushScope.User(userId), ct);
```

- **Operators (`IPushPipeline<T>`):** `Debounce(window)`, `Throttle(window)`, `Delay(duration)`, `Once()`, `Sample(interval)`, `Buffer(window)`, `DistinctUntilChanged([keySelector])`, `Where(predicate)`, `Merge(others)`, `TakeUntil(stopSignal | stopToken)`, and `Select(...)`, terminating in `.To(scope, ct)`.
- **Hand-rolled on `IAsyncEnumerable<T>` + `IVaxelScheduler` (no Rx.NET):** time operators run against `IVaxelScheduler` (`SystemScheduler` in production, `VirtualScheduler` in marble tests) so `Vaxel.AspNetCore` keeps zero third-party package dependencies.
- **Per-recipient rendering (`BackgroundHttpContext`):** when `.To(scope, ct)` emits an item, the `.Select(...)` patch factory runs **once per connected recipient** inside its own `BackgroundHttpContext` scope (never rendering a single privileged view and fanning the HTML out). Touching request-coupled state (`HttpContext.User`, `Url.Page`) inside a background render throws `VaxelFragmentContextException` naming the property.
- **Lifetime and fault isolation:**
  - When a single-user recipient (`PushScope.User(id)`) closes their SSE connection, or when any scope has zero active connections for `VaxelPushOptions.IdleStopPeriod` (default `30s`), the pipeline cancels its source enumeration and disposes the underlying subscription.
  - If a source throws, the fault is wrapped in `VaxelPushPipelineException` (and reported to `VaxelPushOptions.OnPipelineError`), stopping only that pipeline without affecting other pipelines or HTTP requests.

### `Bus<T>` (in-process subject with backplane fan-out)

`Bus<T>` (`IBus<T>`) is a server-wide typed message subject so handlers can publish events (`bus.Publish(evt)`) without knowing which pipelines are watching (`push.From(bus.Subscribe(ct))`):

- **Bounded per-subscriber channels:** each subscriber gets a bounded `Channel<T>` (default capacity `64`) with a documented overflow policy (`BusDropPolicy.DropOldest` by default so live boards always render the newest state).
- **Zero-subscriber drop:** when `SubscriberCount == 0`, `Publish` drops events immediately with zero buffering.
- **Multi-node via `IPushBackplane`:** when a backplane is registered, published events propagate across nodes on `IPushBackplane` while deduplicating by node ID so local subscribers never receive a message twice.

## Hosting notes

- Response buffering must be disabled on the stream route; the SSE endpoint sets the headers and calls `DisableBuffering` itself, but reverse proxies need their own configuration and the docs must say which for the common ones.
- Idle timeouts on managed platforms terminate long streams; the heartbeat default is chosen to sit under the tightest common value, and it is configurable for the rest.
- Nothing else in the framework holds a connection, so a vaxel app scales to zero exactly like a plain MVC app when no stream is open.
