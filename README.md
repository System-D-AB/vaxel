# vaxel

Reactive, hypermedia-driven web apps for .NET.

You write Razor Pages, Views, Partials, ViewComponents, and Razor Components. The server sends HTML; a small agent patches it into the page, reactive signals and closed expressions handle client-side UI state, and declarative event pipelines compose time on both the client and server — with zero `unsafe-eval`.

## What it is

- **Hypermedia & Razor** — the server sends HTML that names its targets (`#pane`, `#results`, `#contact`). Every byte of HTML comes from Razor: Pages, Views, Partials, ViewComponents, and static Razor Components.
- **Reactive signals & closed expressions** — an auto-tracking signal store and closed AST expression engine on the client, parsed and type-checked against your C# signal schema at render time on the server.
- **A small client agent** — intercepts links and forms, runs `vx-on` event pipelines (`debounce`, `throttle`, `distinct`, `filter`, `set`), morphs HTML in place while preserving focus and dirty inputs, updates history, and listens to one SSE channel.
- **Server push pipelines & `Bus<T>`** — compose `IAsyncEnumerable<T>` streams and bounded domain buses into real-time SSE patches with the same `Debounce`, `Throttle`, and `DistinctUntilChanged` vocabulary.

## Install

Apps reference **`Vaxel.AspNetCore`**. That package pulls in the agent and serves `/_vaxel/vaxel.js`.

```xml
<PackageReference Include="Vaxel.AspNetCore" Version="2.0.0-rc5" />
```

```csharp
builder.Services.AddRazorPages();
builder.Services.AddVaxel();

var app = builder.Build();
app.UseStaticFiles();
app.UseVaxel();
app.MapRazorPages();
app.MapVaxelStream("/_vaxel/stream"); // optional SSE
```

CSP stays `script-src 'self'`.

## 1. Client-side reactivity: signals, expressions, and `vx-on` pipelines

Start in HTML with `vx-signals`, expression bindings (`vx-text`, `vx-show`, `vx-class:*`, `vx-attr:*`), two-way `vx-bind`, and `vx-on` event pipelines:

```html
<div vx-signals='{"qty":1,"price":25,"saving":false,"q":"","open":false}'>
  <!-- Pure expressions over signals -->
  <p vx-text="Total: $@{ qty * price }"></p>
  <button type="submit" vx-attr:disabled="qty <= 0 || saving">Save</button>

  <!-- Two-way input binding + debounced search pipeline -->
  <input name="q" vx-bind="q"
         vx-get="/search" vx-target="#results"
         vx-on="input | debounce 200 | distinct" />

  <!-- Keyboard shortcut writing directly to a signal -->
  <div vx-show="open" vx-on="keydown window | key Escape | set open = false">
    Details drawer
  </div>
</div>

<script src="/_vaxel/vaxel.js"></script>
```

### How expressions work (server-validated at render time, client AST-interpreted, zero `eval`)

1. **Render-time validation on the server:** When Razor renders `vx-show`, `vx-class:*`, `vx-attr:*`, `vx-text`, or a `vx-on` `filter`/`set` stage, the Tag Helpers (`VxBindTagHelper` and `VxOnTagHelper`) parse the expression in C# (Pratt parser, 512-char / 32-depth caps) and validate it against your registered `AddSignalSchema<T>()` model. Syntax errors, unknown functions, unknown signal names, static type mismatches (e.g., `vx-show="tabs > 1"` when `Tabs` is a list), or forbidden attributes (`href`, `src`, `action`, `formaction`, `style`, `on*`) throw `VaxelTagHelperException` with the exact character span before HTML leaves the server.
2. **Zero-eval execution on the client:** In the browser, `vaxel.js` evaluates the expression by walking a finite AST over the auto-tracking signal bag and a 20-function pure whitelist (`abs`, `min`, `max`, `len`, `trim`, `contains`, `toLowerCase`, …). It never calls `eval()`, `new Function()`, or `window`, blocks `__proto__`/`constructor`/`prototype` via own-property lookup, and re-runs a binding only when a signal it actually read changes. Both the C# validator and JS interpreter are verified against a shared **312-case conformance corpus**.

## 2. Server-side rendering: Razor Pages, Components, and live push

When a user clicks a link, submits a form, or triggers a `vx-on` pipeline, the server renders the next UI with `IFragmentComposer` (Partials, Views, ViewComponents, or Razor Components) and returns a patch document. `Vaxel.PageOrPatch` serves both a full page on direct navigation and a fragment patch for the agent:

```html
<section id="results" vx-region>
  @await Html.PartialAsync("_SearchResults", Model.Results)
</section>
```

```csharp
using Vaxel;

public async Task<IResult> OnGetAsync(string? q)
{
    Results = await _store.SearchAsync(q);

    return await Vaxel.PageOrPatch(HttpContext,
        page: () => Task.FromResult<IResult>(Page()),
        patch: async () => Patch.Ok()
            .Replace("#results", await _composer.PartialAsync("_SearchResults", Results))
            .ReplaceUrl($"/search?q={Uri.EscapeDataString(q ?? "")}"));
}
```

For real-time updates, compose `IAsyncEnumerable<T>` streams or a bounded `IBus<T>` into SSE patches using the same stream operators on the server:

```csharp
push.From(orderBus.Subscribe(ct))
    .Throttle(TimeSpan.FromMilliseconds(250))
    .Select(async (evt, recipient, composer, token) =>
        Patch.Ok().Replace($"#order-{evt.Id}", await composer.PartialAsync("_OrderRow", evt)))
    .To(PushScope.Group("kitchen"), ct);
```

## 3. Transitions, top progress bar, and loading indicators

Vaxel includes built-in support for smooth region transitions (via the View Transitions API and CSS swap/settle classes), a GitHub/NProgress-style top loading bar (`#vx-progress`), and reactive in-flight indicators (`vx-indicator`, `vx-disable`).

### Top loading bar (`vx-progress`) & in-flight indicators (`vx-indicator`, `vx-disable`)

Every Vaxel request automatically drives a fixed top-of-viewport progress bar (`#vx-progress`) with a default `100 ms` delay threshold so instant local responses never flicker, trickling toward `90%` while in flight and completing to `100%` when the response arrives. You can also bind a boolean signal via `vx-indicator="<signal>"` or disable specific controls via `vx-disable="#id"`:

```html
<body vx-signals-if-missing='{"querying":false}'>
  <!-- Customize the progress bar threshold ("0" / "immediate", "<ms>", or "off"),
       toggle the 'querying' signal, and disable #run-btn while in flight -->
  <form vx-get="/query" vx-target="#results"
        vx-progress="100"
        vx-indicator="querying"
        vx-disable="#run-btn">
    <button id="run-btn" type="submit"
            vx-text="querying ? 'Running…' : 'Run query'">Run query</button>
  </form>

  <!-- Any element on the page can react to the indicator signal -->
  <section id="results" vx-region vx-class:is-loading="querying">
    @await Html.PartialAsync("_QueryResults", Model.Results)
  </section>
</body>
```

Theme the top progress bar with CSS custom properties:

```css
:root {
  --vx-progress-color: #2563eb;
  --vx-progress-height: 3px;
}
```

### Region & shared-element transitions (`vx-transition`, `vx-transition-name`)

Add `vx-transition` to a `[vx-region]` (or to `<body>` / a trigger) to wrap DOM patches in `document.startViewTransition(...)` when supported (automatically respecting `prefers-reduced-motion: reduce`), and use `vx-transition-name="<custom-ident>"` to morph shared elements smoothly between states:

```html
<main id="pane" vx-region vx-transition>
  <article id="doc-card" vx-transition-name="active-doc">
    <h2>@Model.Title</h2>
    <p>@Model.Summary</p>
  </article>
</main>
```

During every patch on a `vx-transition` region, Vaxel also toggles `.vx-swapping` (before morph) and `.vx-settling` (for the settle frame after morph) on the target element for pure CSS transitions:

```css
#pane.vx-settling {
  animation: vx-fade-in 160ms ease-out;
}
@keyframes vx-fade-in {
  from { opacity: 0.6; transform: translateY(2px); }
  to   { opacity: 1;   transform: translateY(0); }
}
```

## 4. Layout-shell navigation (`vx-boost`, `IVaxelRequest`, `FullPagePaths`)

In multi-controller MVC or Razor Pages applications, you can adopt Vaxel across an entire layout shell without rewriting existing controllers or annotating every `<a href>` link.

### 1. Configure full-page route exclusions (`FullPagePaths`)

In `Program.cs`, register path prefixes (or a `ForceFullPage` predicate) that use a separate layout or third-party script context. When a patch request hits any of those paths, `UseVaxel()` short-circuits before MVC/Razor executes and returns `<vx-directive reload></vx-directive>`, instructing the browser to perform a clean full-page navigation:

```csharp
builder.Services.AddVaxel(options =>
{
    options.FullPagePaths.Add("/auth");
    options.FullPagePaths.Add("/billing");
});
```

### 2. Boost links and branch `_Layout.cshtml` with `IVaxelRequest`

Add `vx-boost` and `vx-target="#content"` to your shell container so same-origin `<a href>` links automatically issue Vaxel `GET` patch requests (while automatically ignoring Ctrl/Cmd/Shift/Alt/middle-click, `target="_blank"`, `download`, `rel="external"`, cross-origin URLs, `#hash` anchors, and `vx-boost-exclude` prefixes). Inject `IVaxelRequest` in `_Layout.cshtml` to emit only `<vx-patch target="#content">` plus any out-of-band shell patches (`Vx.AddShellPatch(...)`) on patch requests:

```html
@using Vaxel
@inject IVaxelRequest Vx
@{
    var isShellPatch = Vx.IsPatch && (Vx.Target is null or "#content");
}
@if (isShellPatch)
{
    <vx-patch target="#content" swap="inner">@RenderBody()</vx-patch>
    foreach (var shellPatch in Vx.ShellPatches)
    {
        <vx-patch target="@shellPatch.Target" swap="@shellPatch.SwapMode">@shellPatch.Content</vx-patch>
    }
    <vx-directive title="@ViewData["Title"]"
                  push-url="@($"{Context.Request.Path}{Context.Request.QueryString}")"
                  scroll="top"></vx-directive>
}
else
{
    <!DOCTYPE html>
    <html lang="en">
    <head>
        <title>@ViewData["Title"]</title>
    </head>
    <body vx-boost vx-target="#content" vx-boost-exclude="/auth /billing">
        <header id="site-header">@await Html.PartialAsync("_Header")</header>
        <main id="content" vx-region vx-transition>
            @RenderBody()
        </main>
        <script src="/_vaxel/vaxel.js" defer></script>
    </body>
    </html>
}
```

Wait-free post-swap hooks can inspect `e.detail.targets` on `vx:after-apply` to see which selectors were patched:

```js
document.addEventListener("vx:after-apply", (e) => {
  // e.detail === { patchesApplied: 1, targets: ["#content"] }
  if (e.detail.targets.includes("#content")) {
    // Re-initialize page-level widgets inside #content
  }
});
```

## Concepts

| Term | Meaning |
|---|---|
| **Page** | A full HTML document. What you get with no JavaScript, a refresh, or a shared link. Ordinary Razor. |
| **Patch** | An HTML fragment document (`<vx-patch target="#pane">…</vx-patch>`). Same Razor as the page, wrapped so the agent can swap only that region. A patch is an optimisation of a page, never a capability the page lacks. |
| **Region** | An element with an id and `vx-region` (`#pane`, `#results`). It is a patch target **and** it must exist on the full page for that URL. |
| **Fragment** | The inner HTML of a region — a Razor Partial, View, ViewComponent, or Razor Component rendered by `IFragmentComposer`. |
| **Agent** | The small script at `/_vaxel/vaxel.js`. It intercepts `vx-*` links and forms, sends `VX-Request: 1`, morphs the patch into the DOM, and updates the URL. |
| **Morph** | Identity-preserving DOM update. Matching ids keep the node; focus, caret, and dirty inputs survive when they should. |
| **Signal** | A named JSON value on the client (`tab`, `railOpen`). Sent with every agent request in the `VX-Signals` header. Bindings can show, hide, format text, or two-way bind it using signal names or pure expressions over signals. Signals are never authority — do not authorise from them. |
| **PageOrPatch** | One handler, two branches: full `Page()` when the browser navigated; `Patch.Ok()` when the agent asked. That is how a region stays reachable as a page. |
| **Trigger** | A real `<a href>` or `<form>` with `vx-get` / `vx-post`. Without the agent it navigates normally. |
| **Refusal** | `Patch.Refused` — a normal non-2xx patch (notice in `#notices`), not a thrown exception. |
| **Stream** | One SSE connection (`vx-sse`) for server push into regions. |

State lives in three places: the **URL** (shareable), the server's **store** (domain data), and **signals** (ephemeral UI).

## Attributes

The vocabulary is closed. Every value is data — a URL, a selector, a signal name, a duration, a literal — or a **pure expression over signals** in binding attributes (`vx-show`, `vx-class:*`, `vx-attr:*`, `vx-text`) and `vx-on` `filter`/`set` stages. Nothing is evaluated as JavaScript (`eval` or `Function()`). Razor writes these through Tag Helpers; the names below are what the agent reads. Exact semantics live in [docs/04](docs/04-client-attributes.md).

A trigger belongs on an `<a href>`, a `<form>`, or a `<button>` inside a form, except `load` / `visible` / `interval` / `window` / `document` listeners.

**Status.** ✅ implemented in the shipped agent · ⬜ documented, not yet implemented · 🖥 server-side only. See [docs/04](docs/04-client-attributes.md).

### Triggers

| Attribute | Status | |
|---|---|---|
| `vx-get` `vx-post` `vx-put` `vx-patch` `vx-delete` | ✅ | Issue that method. On `<a>` / `<form>`, defaults to `href` / `action`. A `vx-get` form sends its fields as the query string. |
| `vx-boost` `vx-boost-exclude` | ✅ | Delegated same-origin `<a href>` interception on a container (`<body vx-boost vx-target="#content" vx-boost-exclude="/auth /checkout">`). |
| `vx-on` | ✅ | Declarative event stream pipeline (`source \| operator... \| sink`). Defaults when omitted: `click` on `<a>`/`<button>`, `submit` on `<form>`. |
| `vx-target` | ✅ | Advisory `#id` sent as `VX-Target`. The server decides what it patches. |
| `vx-swap` | ⬜ | Advisory swap mode for the response. |
| `vx-vals-*` | ✅ | Extra request field, like a hidden input. Literals only: `vx-vals-tab="submissions"`. |
| `vx-include` | ⬜ | Also send the fields of this `#id` form or container. |
| `vx-confirm` | ✅ | Native confirm before sending. Plain text, never markup. |
| `vx-indicator` | ✅ | Signal set `true` (or `#id` toggled with `.vx-loading` / `aria-busy`) while this request is in flight. |
| `vx-disable` | ✅ | Disable this element (or `#id`) while in flight, preserving pre-existing `disabled` state. |
| `vx-progress` | ✅ | Top-of-viewport progress bar (`#vx-progress`) while in flight (`100 ms` default delay, `0`/`immediate`, or `off`). |
| `vx-sync` | ✅ | Concurrent requests: `replace` (default), `queue`, `drop`, `abort`. |
| `vx-encoding` | ✅ | `json` — send `application/json` instead of form encoding. |

### Trigger chains (`vx-on`)

Trigger timing, scopes, filtering, and signal writes are expressed as left-to-right pipelines inside `vx-on="source | operator... | sink"`, validated at render time by `VxOnTagHelper` and automatically disposed (`takeUntil`) when a region is morphed out:

| Stage | Status | Examples |
|---|---|---|
| **Sources & scopes** | ✅ | `<event>` (`input`, `click`, `keydown`, …), `<event> window`, `<event> document`, `<event> outside`, `interval <ms>` (1 s floor), `visible`, `load` |
| **Operators** | ✅ | `debounce <ms>`, `throttle <ms>`, `delay <ms>`, `once`, `distinct`, `key <KeyName>`, `filter <expr>`, `prevent`, `stop` |
| **Sinks** | ✅ | Default: fire the element's (or enclosing form's) HTTP verb · Signal write: `set <ident> = <expr>` |

### Regions and morph

| Attribute | Status | |
|---|---|---|
| `vx-region` | 🖥 | This element is a patch target. Documents intent; scopes focus restoration. |
| `vx-preserve` | ✅ | Never morph this subtree — an island owns it. |
| `vx-preserve-attr` | ✅ | Attribute names kept across a morph: `vx-preserve-attr="open class"`. |
| `vx-ignore` | ⬜ | Skip this element and its descendants (third-party HTML that may contain `vx-*`). |
| `vx-ignore-self` | ⬜ | Ignore this element's own attributes; still process children. |
| `vx-overwrite-dirty` | ✅ | Let an incoming value replace what the user has typed. Off by default. |
| `vx-transition` | ✅ | Wrap patches to this region in `document.startViewTransition` and toggle `.vx-swapping` / `.vx-settling`. |
| `vx-transition-name` | ✅ | Literal `view-transition-name` for the View Transition API. |
| `vx-sse` | ✅ | URL of the document's one Server-Sent Events channel. |

### Signal bindings

`vx-text`, `vx-show`, `vx-class:<name>`, and `vx-attr:<name>` accept either a signal name or a **closed expression over signals** (arithmetic, comparisons, `&& || ! ?:`, dotted paths, and a 20-function pure whitelist), validated at render time and evaluated inside an auto-tracking `effect`.

| Attribute | Status | |
|---|---|---|
| `vx-text` | ✅ | `textContent` becomes the expression's value or interpolated string (`"Showing @{ count } rows"`). Never sets `innerHTML`. |
| `vx-show` | ✅ | Shown when the boolean expression is `true`; hidden when `false` or on evaluation refusal. |
| `vx-class:<name>` | ✅ | Class `<name>` present while the boolean expression evaluates to `true`. |
| `vx-attr:<name>` | ✅ | Attribute `<name>` set to the expression's value; removed when `null` / `false`. Refused at render time on `href`, `src`, `action`, `formaction`, `style`, and `on*`. |
| `vx-style:<prop>` | ✅ | Inline style via CSSOM from a single signal name. Prefer `vx-class`; never bind untrusted values. |
| `vx-bind` | ✅ | Two-way: input value ⇄ signal name. |
| `vx-bind-event` | ⬜ | Which events sync `vx-bind` (default `input` / `change`). |
| `vx-bind-prop` | ⬜ | Bind an element property rather than its value. |
| `vx-signal-set:<event>` | ⬜ | Superseded by `vx-on="<event> \| set <name> = <expr>"`. |

### Signal state

| Attribute | Status | |
|---|---|---|
| `vx-signals` | ✅ | JSON seed at first paint: `vx-signals='{"tab":"overview"}'`. |
| `vx-signals-if-missing` | ✅ | Same, but do not overwrite a key that already exists. |
| `vx-persist` | ✅ | Signal names mirrored to `localStorage`. Explicit list, never a wildcard. |
| `vx-persist-session` | ✅ | Same, into `sessionStorage`. |
| `vx-url-sync` | ✅ | Signal names mirrored into the query string. `vx-url-sync-history` (⬜) pushes instead of replace. |
| `vx-match-media:<name>` | ⬜ | Set signal `<name>` from a media query. |
| `vx-debug-signals` | ⬜ | Render the live signal bag. Development only; no-op in production. |

### Events

| Event | |
|---|---|
| `vx:before-request` | Before send; cancellable. |
| `vx:before-apply` | Document parsed, before patching. Islands suspend here. |
| `vx:after-apply` | All patches applied and bound (`detail: { patchesApplied, targets }`). Islands resume here. |
| `vx:signals-changed` | After a signal patch or a binding write. |
| `vx:error` | Transport, parse, or protocol failure. |
| `vx:sse-state` | Stream connected, dropped, or reconnecting. |

## Sample

```bash
dotnet run --project samples/Workbench/Workbench.csproj
```

The Workbench is a small admin shell: submissions (a role-checked approve), proposals (inline edit + live rail), a contact form, live form (`cookbook/05`), order board (`cookbook/06`), and read-only settings. Sign in as Alice to approve; as Bob to see a refusal.

## Docs

The contract lives in [`docs/`](docs/README.md). Start with [principles](docs/01-principles.md), then the [protocol](docs/03-protocol.md) and the [.NET API](docs/05-dotnet-api.md).

| Doc | What it settles |
|---|---|
| [01 — Principles](docs/01-principles.md) | Four rules everything else follows from |
| [02 — Architecture](docs/02-architecture.md) | Parts, request lifecycle, morph, SSE |
| [03 — Protocol](docs/03-protocol.md) | Patch documents, headers, SSE frames |
| [04 — Client attributes](docs/04-client-attributes.md) | The closed `vx-*` vocabulary |
| [05 — .NET API](docs/05-dotnet-api.md) | Packages, DI, composer, patch builder, tag helpers |
| [06 — Security](docs/06-security.md) | CSP, antiforgery, the signal trust boundary |
| [07 — Testing](docs/07-testing.md) | Page-parity invariant and the test kit |
| [08 — Roadmap](docs/08-roadmap.md) | Milestones, comparison, licence |
| [09 — Datastar gap analysis](docs/09-datastar-gap-analysis.md) | Every Datastar attribute, and vaxel's position |
| [10 — Test matrix](docs/10-test-matrix.md) | What proves each guarantee |
| [11 — Datastar reuse](docs/11-datastar-reuse.md) | What to vendor, decline, and credit |
| [12 — Parity with Datastar](docs/12-parity-with-datastar.md) | Plugin inventory and scoring |
| [13 — Test adoption](docs/13-test-adoption.md) | Their 20 conformance cases as the scoreboard |

## Cookbook

| Recipe | Shows |
|---|---|
| [01 — Search as you type](cookbook/01-search-as-you-type.md) | Debounced input, suggestion list, URL sync |
| [02 — Contact form](cookbook/02-contact-form.md) | Submit, server validation in place, double-submit guard |
| [03 — Tabs, rail and inline edit](cookbook/03-tabs-rail-and-inline-edit.md) | Tab strip as links, one action patching two regions |
| [04 — Live updates](cookbook/04-live-updates-sse.md) | SSE push per recipient, server pipelines, reconnect |
| [05 — Live form](cookbook/05-live-form.md) | Client expressions over signals, live preview, authoritative server submit |
| [06 — Order board](cookbook/06-order-board.md) | `Bus<T>` domain events and `IPushChannel.From` pipelines |

## Licence

MIT. Source: [github.com/System-D-AB/vaxel](https://github.com/System-D-AB/vaxel). No dependency in the .NET packages beyond ASP.NET Core. The agent's only third-party code is a DOM morph implementation (BSD-2) and expression corpus test fixtures (Apache-2.0).
