# 08 — Roadmap, comparison, and scope discipline

## Milestones

**Parity is the through-line.** Every milestone below is scored against [Datastar's conformance suite and plugin inventory](13-test-adoption.md); a milestone is not done because its code exists, but because its numbers moved in [`parity/SCOREBOARD.md`](../parity/SCOREBOARD.md).

**v0.1 — the server half (useful alone).**
`IFragmentComposer` over Partial · View · ViewComponent · static Razor Component; `Patch`/`PatchResult`; `PageOrPatch`; refusal builder; `[FromSignals]` binding; Tag Helpers; `Vaxel.Testing` with the parity harness and patch assertions. **No client of our own** — the protocol is driven by htmx plus a morph extension, configured by the Tag Helpers. Everything a consuming application writes at v0.1 survives every later version, because views only ever see Tag Helpers.

**v0.1.5 — the measuring instrument.** `Vaxel.Datastar` adapter plus the `/test` conformance host, so Datastar's 20 cases run against us unmodified. Promoted from post-v1.0 because a scoreboard on day one is worth more than the same work later, and it proves the server API does not depend on our own client. Target: **16 pass, 4 declined, 0 failing**.

**v0.2 — the agent.** The client implementing [04](04-client-attributes.md) — signal store, agent and morph, ~9–10 KB gzip against a 12 KB cap; the conformance suite; the CSP and accessibility suites. htmx becomes optional and then unnecessary.

**v0.3 — push.** SSE endpoint, `IPushChannel`, in-process transport, heartbeat and reconnect, per-identity caps, hosting notes for the common reverse proxies and managed platforms.

**v0.4 — ergonomics.** Signal schema with Tag Helper validation; a Roslyn analyser for the sharp edges (authorising from signals, a trigger on a non-degradable element, a target that is not an id, a patch target with no server-rendered page route); dev-mode overlay showing the last patch, ignored patches and the signal diff.

**v0.5 — parity closure.** Every row in [12](12-parity-with-datastar.md) has a fixture and a score; the example corpus is 100 % expressible; any `Cannot` is either closed or promoted into the specification as a stated limitation.

**v1.0 — commitment.** Protocol frozen at version 1; semantic versioning; documented upgrade path; a real reference application (not a to-do list — something with authorisation, forms, a designer-like surface and a governed action); benchmarks against a plain MVC baseline.

**Deliberately unscheduled:** multi-document tab state, client routing, offline, an interactive component model, a CLI, a project template beyond a single `dotnet new` sample. Each of these is where a small framework becomes a big one.

## Scope discipline

Three questions gate every proposed feature:

1. **Does it require evaluating a string on the client?** Then it is out (R2), no matter how convenient.
2. **Does it require state on the server between requests?** Then it is out (R4) — that is Blazor Server's bargain, and applications that want it should take that bargain directly.
3. **Can the application do it in twenty lines of its own JavaScript in an island?** Then it should, and the framework should stay out of the way.

The failure mode for a project like this is not being wrong; it is being *complete*. Every framework in this space that grew a plugin system, a component abstraction and a client router ended up competing with React on React's terms and losing.

## How it compares

| | Växel | Datastar | htmx (+Alpine) | Hotwire | Blazor Server | Blazor WASM |
|---|---|---|---|---|---|---|
| Rendering | server only | server + client bindings | server (+client via Alpine) | server | server | client |
| Client expressions | **none** | `Function()` | none (Alpine: `Function()`) | Stimulus (JS classes) | n/a | n/a |
| Runs under `script-src 'self'` | **yes** | no (`unsafe-eval`) | htmx alone yes; Alpine needs the CSP build | yes | yes | yes |
| Per-user server state | none | none | none | none | circuit + socket | none |
| Client payload | ~9–10 KB (signals + agent + morph) | ~11 KB | ~17 KB (+Alpine ~15 KB) | ~40 KB | small | multi-MB runtime |
| Build step | none | none | none | typical | none | required |
| .NET integration | native (Tag Helpers, ViewComponents, model binding) | SDK, young | community helpers | DIY | first-party | first-party |
| State model | signals as advisory input | signals | none | none | component tree | component tree |
| Partial updates | patch documents | SSE patches | swaps | frames/streams | diffs over socket | local |

Where each is the better choice, stated fairly: **Blazor Server** when a genuinely stateful, highly interactive app can accept a socket per user; **Blazor WASM** when offline or near-native interactivity matters more than payload; **htmx alone** when swapping is all that is needed and no state model is wanted; **Datastar** when its expressiveness is worth `unsafe-eval`; **Hotwire** on Rails, where the integration already exists. **Växel** when the app is server-rendered .NET, the CSP must be strict, and the team would rather write C# than a client framework.

## Provenance

The concepts are Datastar's — signals, server-patched fragments, one attribute vocabulary — with its expression layer removed and .NET's composition model put in its place. Prior art also includes htmx (swap semantics and the hypermedia argument), Hotwire/Turbo (frames and streams), Unpoly (progressive enhancement as a contract), Phoenix LiveView and Laravel Livewire (server-driven UI, with the stateful bargain we decline), and Alpine's CSP build (restricting a grammar to escape `eval`). Where this specification borrows a name or a behaviour, it says so.

## Licence and governance

MIT. Protocol and conformance suite versioned independently of the packages, so a third-party agent or a non-.NET server can implement the same wire format. Breaking protocol changes require a major version and a fallback path — the agent must degrade to full-page navigation rather than misapply a document it does not understand.

## Status of this document

Draft specification. Nothing is implemented. The intended first consumer is a real administrative application with authorisation, forms and a designer-like editing surface; the specification should be revised by what that consumer discovers, and no v1.0 should be declared before it ships on it.
