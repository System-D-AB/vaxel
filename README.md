# Växel

**Server-driven web apps for .NET.** Your Razor Pages, Views, Partials and ViewComponents render every pixel; a small client agent swaps the fragments the server sends and keeps a bag of UI state in signals. No SPA, no client templates, no `unsafe-eval`, no build step required.

> Status: **DRAFT SPEC v0.3 — nothing implemented.** See [CHANGELOG](CHANGELOG.md) for what each revision corrected and why. This repository is the specification for a framework, written before the code so the shape can be argued with.
>
> **Name: Växel — settled 2026-08-23.** Swedish, and honestly it carries several everyday meanings: *gear / gearbox* (växellåda) is the most common, then *telephone switchboard* (telefonväxel), *small change*, and *railway points* (usually the compound spårväxel). The switchboard sense is the intended one — a switchboard connects a request to its destination and patches a line through. Package and namespace spelling is ASCII: `Vaxel`.

---

## The idea in thirty seconds

```csharp
public async Task<IResult> OnPostSelectTab(string tab)
{
    var model = await _reader.LoadAsync(tab);

    return Patch.Ok()
        .Replace("#pane",     await Fragments.PartialAsync("_SubmissionsPane", model))
        .Replace("#tab-strip", await Fragments.ComponentAsync<TabStrip>(new { selected = tab }))
        .PushUrl($"/apps/{model.AppId}?tab={tab}");
}
```

```html
<nav id="tab-strip">
  <a href="/apps/a_1?tab=submissions" vx-post="?handler=SelectTab" vx-vals-tab="submissions">Submissions</a>
</nav>
<section id="pane" vx-region>…server-rendered…</section>
```

The link works without JavaScript. With the agent loaded, it becomes a fragment swap that preserves scroll and focus and updates the URL. The server did all the rendering either way.

## What Växel is

- **A rendering discipline** — every byte of HTML comes from .NET. Partials, Views, ViewComponents and statically-rendered Razor Components are all first-class fragment sources.
- **A wire protocol** — responses are HTML documents whose top-level elements say where they go.
- **A small client agent** — intercept, request, morph, indicate, restore focus, push history, hold one SSE channel.
- **A signal store** — a named bag of UI state that travels with requests and is patched by the server. Signals hold *values*, never expressions.

## What Växel is not

- Not a component framework. There is no client-side render tree, no virtual DOM, no template language.
- Not Blazor. No circuit, no server-held UI state, no WASM runtime.
- Not reactive on the client. Derivations are computed on the server, where the data is.
- Not a JavaScript framework you write JavaScript in. Attribute values are data; nothing is ever evaluated.

## The goal

**Feature parity with Datastar**, minus the client expression language. An application a team could build on Datastar must be buildable here, with the same capabilities — measured by [Datastar's own conformance suite](docs/13-test-adoption.md), which is MIT-licensed and which we run unmodified. Progress is a number in [`parity/SCOREBOARD.md`](parity/SCOREBOARD.md), not a claim in a README.

## Why it exists

Datastar showed the shape: signals plus server-patched fragments. But it compiles attribute expressions with `Function()`, so it [requires `unsafe-eval`](https://data-star.dev/reference/security). Alpine has the same requirement, escaped only by a restricted build. Blazor Server keeps per-user state and a socket; Blazor WASM ships a runtime. htmx is excellent at swapping but has no state model, so it is usually paired with a second library that brings the eval back.

Växel takes Datastar's concepts and removes the one thing that costs the security posture: **the client expression language**. Signals bind by name; anything that needs computing is computed where the data already is.

## Building it?

**Start with [HANDOFF.md](HANDOFF.md)** — the self-contained implementation brief: features, the role of the signal library, parity targets, testing procedure, repository layout, milestones with definitions of done, working agreements, open decisions and the risk register.

Implementation is spec-driven. The contract lives in [`docs/`](docs/README.md). Each slice you build has a packet in [`specs/`](specs/README.md) (`requirements.md`, `design.md`, `tasks.md`). Current packet: [v0.1-composer](specs/v0.1-composer/requirements.md).

## Reading order

| Doc | What it settles |
|---|---|
| [01 — Principles](docs/01-principles.md) | Goals, non-goals, and the four rules everything else follows from |
| [02 — Architecture](docs/02-architecture.md) | Parts, request lifecycle, morph, SSE, progressive enhancement |
| [03 — Protocol](docs/03-protocol.md) | The wire: patch documents, signal envelopes, headers, SSE frames |
| [04 — Client attributes](docs/04-client-attributes.md) | The closed vocabulary and its exact semantics |
| [05 — .NET API](docs/05-dotnet-api.md) | Packages, DI, composer, patch builder, model binding, tag helpers |
| [06 — Security](docs/06-security.md) | CSP, antiforgery, the signal trust boundary, sanitisation |
| [07 — Testing](docs/07-testing.md) | The page-parity invariant, the test kit, the conformance suite |
| [08 — Roadmap](docs/08-roadmap.md) | Milestones, scope discipline, comparison, licence |
| [09 — Datastar gap analysis](docs/09-datastar-gap-analysis.md) | Every Datastar attribute, and Växel's position on it |
| [10 — Test matrix](docs/10-test-matrix.md) | Every attribute, protocol element and guarantee, and what proves it |
| [11 — Datastar reuse](docs/11-datastar-reuse.md) | What to vendor, converge on, reject, and credit |
| [12 — Parity with Datastar](docs/12-parity-with-datastar.md) | Every plugin, action and watcher, and the Växel construct that matches it |
| [13 — Test adoption](docs/13-test-adoption.md) | Taking their 20 conformance cases as our scoreboard |

## Cookbook — what this looks like in practice

| Recipe | Shows |
|---|---|
| [01 — Search as you type](cookbook/01-search-as-you-type.md) | Debounced input, suggestion list, URL sync, a 15-line keyboard island |
| [02 — Contact form](cookbook/02-contact-form.md) | Submit, server validation re-rendered in place, double-submit guard, honeypot |
| [03 — Tabs, rail and inline edit](cookbook/03-tabs-rail-and-inline-edit.md) | Tab strip as links, server-side filtering, one action patching two regions |
| [04 — Live updates](cookbook/04-live-updates-sse.md) | SSE push per recipient, long jobs without polling, reconnection |

## Licence

MIT (intended). No dependency in the .NET packages beyond ASP.NET Core; the client agent's only third-party code is a DOM morph implementation (BSD-2) which may be replaced by an in-house one.
