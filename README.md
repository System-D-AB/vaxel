# vaxel

Hypermedia-driven web apps for .NET.

**Repository:** [github.com/System-D-AB/vaxel](https://github.com/System-D-AB/vaxel)

You write plain Razor Pages, Views, Partials, and ViewComponents. The server sends HTML; a small agent patches it into the page. There is no client router, no render tree, and no JSON-for-the-frontend. The response *is* the next UI.

SPA-like tabs, forms, and live regions — without React, Blazor, or any other client-heavy framework. No `unsafe-eval`.

```csharp
using Vaxel;

public async Task<IResult> OnPostAsync()
{
    if (!ModelState.IsValid)
    {
        Response.StatusCode = 422;
        return await Vaxel.PageOrPatch(HttpContext,
            page: () => Task.FromResult<IResult>(Page()),
            patch: async () => Patch.Status(422)
                .Replace("#contact", await _composer.PartialAsync("_ContactForm", this)));
    }

    return await Vaxel.PageOrPatch(HttpContext,
        page: () => Task.FromResult<IResult>(Redirect("/contact?sent=1")),
        patch: async () => Patch.Ok()
            .Replace("#contact", await _composer.PartialAsync("_ContactThanks", this))
            .PushUrl("/contact?sent=1"));
}
```

```html
<section id="contact" vx-region>
  <form method="post" action="/contact" vx-post vx-target="#contact">
    <!-- the form works without JavaScript; with the agent it becomes a fragment swap -->
  </form>
</section>
<script src="/_vaxel/vaxel.js"></script>
```

The link or form works with no JavaScript. With the agent loaded, the same Razor still renders — the client only morphs it in, keeps focus, and updates the URL.

## Install

Apps reference **`Vaxel.AspNetCore`**. That package pulls in the agent and serves `/_vaxel/vaxel.js`.

```xml
<PackageReference Include="Vaxel.AspNetCore" Version="1.0.0" />
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

CSP can stay `script-src 'self'`. There is no expression language, so there is no `unsafe-eval`.

## What it is

- **Hypermedia** — the server sends HTML that names its targets (`#pane`, `#contact`). The next screen is a document, not a JSON payload for a client to render.
- **A rendering discipline** — every byte of HTML comes from Razor: Pages, Views, Partials, ViewComponents, static Razor Components.
- **A small client agent** — intercept, request, morph, restore focus, push history, one SSE channel. Not a router, not a virtual DOM.
- **A signal store** — named UI values that travel with requests. Signals hold data, never code.

It is not a SPA, not a component framework, and not a JavaScript framework you write JavaScript in. The SPA-like feel comes from patching HTML in place, with a full page behind every fragment.

## Concepts

| Term | Meaning |
|---|---|
| **Page** | A full HTML document. What you get with no JavaScript, a refresh, or a shared link. Ordinary Razor. |
| **Patch** | An HTML fragment document (`<vx-patch target="#pane">…</vx-patch>`). Same Razor as the page, wrapped so the agent can swap only that region. A patch is an optimisation of a page, never a capability the page lacks. |
| **Region** | An element with an id and `vx-region` (`#pane`, `#contact`). It is a patch target **and** it must exist on the full page for that URL. |
| **Fragment** | The inner HTML of a region, usually a Razor partial. `IFragmentComposer` renders it. |
| **Agent** | The small script at `/_vaxel/vaxel.js`. It intercepts `vx-*` links and forms, sends `VX-Request: 1`, morphs the patch into the DOM, and updates the URL. It is not a client router. |
| **Morph** | Identity-preserving DOM update. Matching ids keep the node; focus, caret, and dirty inputs survive when they should. |
| **Signal** | A named JSON value on the client (`tab`, `railOpen`). Sent with every agent request in the `VX-Signals` header. Bindings can show, hide, or two-way bind it. Signals are data, never expressions, and never authority — do not authorise from them. |
| **PageOrPatch** | One handler, two branches: full `Page()` when the browser navigated; `Patch.Ok()` when the agent asked. That is how a region stays reachable as a page. |
| **Trigger** | A real `<a href>` or `<form>` with `vx-get` / `vx-post`. Without the agent it navigates normally. |
| **Refusal** | `Patch.Refused` — a normal non-2xx patch (notice in `#notices`), not a thrown exception. |
| **Stream** | One SSE connection (`vx-sse`) for server push into regions. No per-user UI tree on the server. |

State lives in three places only: the **URL** (shareable), the server's **store** (domain data), and **signals** (ephemeral UI). The server does not keep a circuit or a component tree between requests.

## Attributes

The vocabulary is closed. Every value is data — a URL, a selector, a signal name, a duration, a literal — never an expression. Razor writes these through Tag Helpers; the names below are what the agent reads. Exact semantics live in [docs/04](docs/04-client-attributes.md).

A trigger belongs on an `<a href>`, a `<form>`, or a `<button>` inside a form, except load / visible / poll / window listeners.

**Status.** The vocabulary is the full contract; the `1.0` column says what the shipped agent implements today. ✅ in 1.0 · ⬜ documented, not in 1.0 (planned for v1.1) · 🖥 server-side only. See [docs/04](docs/04-client-attributes.md).

### Triggers

| Attribute | 1.0 | |
|---|---|---|
| `vx-get` `vx-post` `vx-put` `vx-patch` `vx-delete` | ✅ | Issue that method. On `<a>` / `<form>`, defaults to `href` / `action`. A `vx-get` form sends its fields as the query string. |
| `vx-on` | ⬜ | Event that fires the request (`click`, `submit`, `input`, …). |
| `vx-target` | ✅ | Advisory `#id` sent as `VX-Target`. The server decides what it patches. |
| `vx-swap` | ⬜ | Advisory swap mode for the response. |
| `vx-vals-*` | ✅ | Extra request field, like a hidden input. Literals only: `vx-vals-tab="submissions"`. |
| `vx-include` | ⬜ | Also send the fields of this `#id` form or container. |
| `vx-confirm` | ✅ | Native confirm before sending. Plain text, never markup. |
| `vx-indicator` | ✅ | Signal set `true` while this request is in flight. |
| `vx-disable` | ✅ | Disable this element (or `#id`) while in flight. |
| `vx-sync` | ✅ | Concurrent requests: `replace` (default), `queue`, `drop`, `abort`. |
| `vx-encoding` | ✅ | `json` — send `application/json` instead of form encoding. |

### Trigger modifiers

All trigger modifiers are ⬜ **not in 1.0** (planned for v1.1).

| Attribute | 1.0 | |
|---|---|---|
| `vx-debounce` `vx-throttle` | ⬜ | Coalesce or rate-limit, in ms. `vx-debounce-leading` fires on the leading edge. |
| `vx-delay` | ⬜ | Wait this many ms before sending. |
| `vx-once` | ⬜ | Fire at most once, then the trigger is inert. |
| `vx-prevent` `vx-stop` | ⬜ | `preventDefault` / `stopPropagation`. |
| `vx-capture` `vx-passive` | ⬜ | Listener options. |
| `vx-window` `vx-document` | ⬜ | Attach the listener to `window` / `document` (e.g. Escape to close). |
| `vx-outside` | ⬜ | Fire when the event happens outside this element (click-away). |
| `vx-trigger-load` | ⬜ | Fire when the element enters the document. `vx-trigger-load-delay` waits first. |
| `vx-trigger-visible` | ⬜ | Fire on viewport intersection (`-threshold`, `-exit`, `-once`). |
| `vx-poll` | ⬜ | Repeat while the element exists, in ms (1 s floor). Prefer SSE. |

### Regions and morph

| Attribute | 1.0 | |
|---|---|---|
| `vx-region` | 🖥 | This element is a patch target. Documents intent; scopes focus restoration. |
| `vx-preserve` | ✅ | Never morph this subtree — an island owns it. |
| `vx-preserve-attr` | ✅ | Attribute names kept across a morph: `vx-preserve-attr="open class"`. |
| `vx-ignore` | ⬜ | Skip this element and its descendants (third-party HTML that may contain `vx-*`). |
| `vx-ignore-self` | ⬜ | Ignore this element's own attributes; still process children. |
| `vx-overwrite-dirty` | ✅ | Let an incoming value replace what the user has typed. Off by default. |
| `vx-transition-name` | ⬜ | Literal `view-transition-name` for the View Transition API. |
| `vx-sse` | ✅ | URL of the document's one Server-Sent Events channel. |

### Signal bindings

Every binding names **one** signal. No operators, no comparisons, no dotted paths.

| Attribute | 1.0 | |
|---|---|---|
| `vx-text` | ✅ | Text content becomes the signal's value. |
| `vx-show` | ✅ | Hidden when the signal is falsy. |
| `vx-class:<name>` | ✅ | Class `<name>` present while the signal is truthy. |
| `vx-attr:<name>` | ✅ | Attribute `<name>` set to the signal; removed when `null` / `false`. |
| `vx-style:<prop>` | ✅ | Inline style via CSSOM. Prefer `vx-class`; never bind untrusted values. |
| `vx-bind` | ✅ | Two-way: input value ⇄ signal. |
| `vx-bind-event` | ⬜ | Which events sync `vx-bind` (default `input` / `change`). |
| `vx-bind-prop` | ⬜ | Bind an element property rather than its value. |
| `vx-signal-set:<event>` | ⬜ | On that event, set a signal to a literal: `vx-signal-set:click="tab=submissions"`. |

### Signal state

| Attribute | 1.0 | |
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
| `vx:after-apply` | All patches applied and bound. Islands resume here. |
| `vx:signals-changed` | After a signal patch or a binding write. |
| `vx:error` | Transport, parse, or protocol failure. |
| `vx:sse-state` | Stream connected, dropped, or reconnecting. |

## Sample

```bash
dotnet run --project samples/Workbench/Workbench.csproj
```

The Workbench is a small admin shell: submissions (a role-checked approve), proposals (inline edit + live rail), a contact form, and read-only settings. Sign in as Alice to approve; as Bob to see a refusal.

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
| [04 — Live updates](cookbook/04-live-updates-sse.md) | SSE push per recipient, reconnect |

## Licence

MIT. Source: [github.com/System-D-AB/vaxel](https://github.com/System-D-AB/vaxel). No dependency in the .NET packages beyond ASP.NET Core. The agent's only third-party code is a DOM morph implementation (BSD-2).
