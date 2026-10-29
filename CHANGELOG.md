# Changelog

The specification is versioned because it is the contract. Every entry says what changed **and why it was wrong**, because a spec that quietly improves teaches nobody.

## v2.0.0-rc2 — 2026-10-29

### `v2.0-bugs` — StowDB Explorer `v2.0.0-rc1` adoption fixes & ergonomic completions

- **B1 — `vx-persist` + `vx-signals-if-missing` on the same element preserves stored values across reload.** In `2.0.0-rc1`, `bindTree()` evaluated `vx-signals-if-missing` (which calls `savePersisted()`) before calling `loadPersisted()`, overwriting `localStorage` / `sessionStorage` with the default seed whenever both attributes sat on `<body>`. `bindTree()` now calls `loadPersisted()` immediately after registering `vx-persist` / `vx-persist-session` / `vx-url-sync` keys and before evaluating `vx-signals-if-missing`. *(docs/04 § Signal state)*
- **B2 — `VxSignalsTagHelper` and `VxSignalsIfMissingTagHelper` accept dynamic Razor `@json` strings.** Previously neither tag helper bound a C# `string?` property, causing Razor to HTML-encode quotes (`&quot;`) into `output.Attributes` before `VxSignalJson.Validate` parsed the JSON. Both tag helpers now bind `[HtmlAttributeName(...)] public string?` and validate the raw unencoded JSON string. *(docs/05 § Tag Helpers)*
- **B3 — `vx-sse` dynamic morph discovery and immediate `pagehide` disconnect.** `initSse()` now runs inside `bindTree()` so `[vx-sse]` regions morphed into the DOM open their `EventSource` connection (and removed `[vx-sse]` regions close it), and `window` `pagehide` closes the active `EventSource` immediately so rapid full-page reloads do not exhaust `MaxConnectionsPerIdentity`. *(docs/04 § Regions, morph and lifecycle)*
- **B4 — `<form vx-get>` with non-submit `vx-on` still intercepts `submit`.** The delegated `submit` (and `click`) listener previously bailed out whenever an element carried any `vx-on` attribute. It now checks whether `vx-on` declares an unscoped `submit` (or `click`) source, so `<form vx-get vx-on="input | debounce 150 | distinct">` still intercepts Enter-key / button form submissions as Vaxel patch requests. *(docs/04 § Trigger chains)*
- **S1–S4 — Multi-chain `vx-on` (`;`), declarative dialog/focus sinks (`showModal #id`, `close [#id]`, `focus #id`), `dataset.<key>` in expressions, and `VaxelPushOptions.ResolveIdentity`.** `vx-on` now accepts `;`-separated chains and `#id`-validated DOM sinks (`showModal #id`, `close`, `focus #id`); expressions can read `dataset.<ident>` from the bound element; and `MapVaxelStream` consults `VaxelPushOptions.ResolveIdentity` for session- or custom-authenticated streams. *(docs/04, docs/05)*

## v2.0.0-rc1 — 2026-10-29

### `v2.0-push-pipelines` (P3) — server push pipelines over `IAsyncEnumerable<T>` & bounded `Bus<T>`

- **`IPushChannel.From<T>(IAsyncEnumerable<T>)` pipeline builder with zero Rx.NET dependencies.** In 1.x, `IPushChannel.PushAsync(scope, patch)` only handled one-shot imperatives; continuous server sources (database change streams, `ChannelReader<T>`, background queues) required ad-hoc loops, manual timers, and bespoke cancellation plumbing. `push.From(source)` adds a hand-rolled operator chain over `IAsyncEnumerable<T>` — `Debounce`, `Throttle`, `Delay`, `Once`, `Sample`, `Buffer`, `DistinctUntilChanged`, `Where`, `Merge`, `TakeUntil`, and `Select` — driven by `IVaxelScheduler` (`SystemScheduler` in production, `VirtualScheduler` in tests) without taking a dependency on `System.Reactive` or `System.Linq.Async`. The five operators shared with `vx-on` (`debounce`, `throttle`, `delay`, `once`, `distinct`) pass the exact same `conformance/marbles/*.json` fixtures on both client and server. *(docs/05 § Push, parity/SCOREBOARD.md, cookbook/04, cookbook/06)*
- **Per-recipient rendering and deterministic pipeline lifetime.** `.Select((item, recipient, composer, ct) => ...)` and `.To(PushScope, ct)` evaluate and render the `PatchBuilder` **once per recipient** inside a `BackgroundHttpContext` scope, preventing accidental cross-user HTML broadcasts. Pipelines stop and dispose their upstream `IAsyncEnumerator<T>` when a targeted user disconnects, when a scope has zero active connections for `VaxelPushOptions.IdleStopPeriod` (default 30 s), or when the caller's `CancellationToken` cancels; a faulting source throws `VaxelPushPipelineException` (and invokes `VaxelPushOptions.OnPipelineError`) without affecting other pipelines or HTTP requests. *(docs/02 § Server push, docs/05 § Pipeline lifetime, docs/06 § Server push)*
- **Bounded `Bus<T>` (`IBus<T>` / `AddVaxelBus<T>()`).** Domain handlers can publish typed events to `IBus<T>` without knowing who is listening. Each subscriber gets an isolated bounded `Channel<T>` (default capacity 256, `BusDropPolicy.DropOldest`) so a slow consumer never blocks publishers or causes unbounded memory growth; publishing with zero subscribers is a no-op; and when an `IPushBackplane` is registered, published items fan out across nodes with origin-node deduplication. *(docs/05 § Domain event bus, cookbook/06)*

### `v2.0-triggers` (P2) — declarative `vx-on` event stream pipelines & `takeUntil` morph disposal

- **13 standalone trigger-modifier attributes replaced by one `vx-on` chain (`source | operator... | sink`).** The 1.0 draft scattered trigger timing, scopes, and event options across a dozen unimplemented attributes (`vx-debounce`, `vx-throttle`, `vx-delay`, `vx-once`, `vx-prevent`, `vx-stop`, `vx-window`, `vx-document`, `vx-outside`, `vx-trigger-load`, `vx-trigger-visible`, `vx-poll`, `vx-signal-set:*`) whose composition order was undefined. `vx-on` expresses the pipeline left-to-right in the order events flow (`vx-on="input | debounce 200 | distinct"`, `vx-on="keydown window | key Escape | set open = false"`). Sources cover DOM events with optional `window` / `document` / `outside` scopes, `interval <ms>` (with a 1000 ms floor), `visible` (`IntersectionObserver`), and `load`. Operators cover `debounce`, `throttle`, `delay`, `once`, `distinct`, `key`, `filter <expr>`, `prevent`, and `stop`; sinks are the element's HTTP verb (or enclosing form's verb) by default, or `set <ident> = <expr>` writing into the P0 signal store via the P1 expression engine. *(docs/04 § Trigger chains)*
- **Region lifetime (`takeUntil` the next morph).** When a morph or `replace`/`remove` patch removes a subtree from the document, the agent disposes all reactive binding effects, clears active `vx-on` timers, detaches `window`/`document`/`outside` listeners and `IntersectionObserver` instances, and aborts + abandons any in-flight HTTP request initiated by a trigger inside the removed subtree so a superseded screen can never overwrite the new one. *(docs/02 § Nested regions and lifetime, docs/03 § Concurrency)*
- **Render-time chain validation (`VxOnTagHelper`) and marble conformance (`conformance/marbles/`).** `VxOnTagHelper` validates every `vx-on` chain at render time — checking source syntax, operator names, numeric literal arguments, and `filter` / `set` expressions against the P1 expression parser and registered `ISignalSchema` (augmented with event properties `key`, `value`, `checked`) — and throws `VaxelTagHelperException` with the offending character span. `conformance/marbles/` proves the five time/stream operators (`debounce`, `throttle`, `delay`, `once`, `distinct`) against 50 ms frame marble diagrams and verifies zero leaked timers/listeners across 1000 morph cycles. *(parity/SCOREBOARD.md, cookbook/01)*

### `v2.0-expressions` (P1) — closed expression interpreter & render-time schema validation

- **Rule R2 amended: attribute values are data, or pure expressions over signals; never code.** In 1.x, R2 forbade all operators in binding attributes, forcing display facts like `tabs.length > 1`, `name == '' || saving`, or `"Showing @{ count } rows"` through either a server round trip or a redundant boolean signal. `v2.0-expressions` embeds a closed AST interpreter (lifted from Centralen's expression engine) in the client bundle and a matching C# Pratt parser and schema type-checker in `Vaxel.AspNetCore`. Because the interpreter walks a finite AST over the signal bag and a 20-function pure whitelist — with zero `eval`, zero `new Function`, own-property lookup blocking `__proto__`/`constructor`/`prototype`, and 512-char / 32-depth caps — it keeps the `script-src 'self'` / no-`unsafe-eval` guarantee intact. *(docs/01 § R2, docs/04 § Signal bindings, docs/06 § Content Security Policy)*
- **Render-time validation against `AddSignalSchema<T>()` (and graceful degradation without one).** `VxBindTagHelper` parses expressions at render time and throws `VaxelTagHelperException` with the offending character span on syntax errors, unknown functions, length/depth cap violations, or forbidden target attributes (`href`, `src`, `action`, `formaction`, `style`, `on*`). When a signal schema is registered, it also checks signal existence and static operand/return types (`number | string | bool | list | object | null | any`), failing typos and type mismatches (`vx-show="tabs > 1"` where `Tabs` is a list) at build/render time rather than in the user's browser. Evaluation refusals in boolean bindings fail closed (`false`) and emit `vx:error`. *(docs/05 § Tag Helpers, docs/06 § Security)*
- **312-case shared conformance corpus (DivKit Apache-2.0 provenance + Centralen + vaxel security cases).** Both the shipped JS engine (in jsdom) and the C# validator run against `conformance/expressions/`, asserting byte-for-byte canonical S-expression AST equality and matching static refusal codes. *(parity/SCOREBOARD.md)*

### `v2.0-signals-core` (P0) — auto-tracking signal store

- **Auto-tracking `Signal` and `EffectScope` replace the 1.x key-subscription store.** Lifted from Centralen's `runtime/signals.ts`. Each `vx-*` binding now runs inside an auto-tracking `effect` that records the signals it read and re-runs only when one of them changes (`!==`). The 1.x store required manual per-key subscriptions and could not track multi-signal reads needed by v2.0 expressions without maintaining a separate dependency graph. *(docs/02 § Signals)*
- **Private scheduling queue and re-entrancy guarantees.** Signal writes and `patchSignals` enqueue subscribers into a private de-duplicating set guarded by a write-depth counter rather than invoking callbacks synchronously during a write. A multi-key patch runs an effect reading all of those keys once against the completed bag (never against a half-applied bag); diamond paths run once; self-writes settle without recursion; and an effect cycle aborts at 100 passes with a `vx:error`. *(docs/02 § Signals, docs/04 § One owner per property)*
- **Null/absence semantics pinned (delete-on-null) and `window.Vaxel` shim preserved.** Setting or patching a signal to `null`/`undefined` deletes the key from the store, removes it from `vx-persist`/`vx-persist-session` and `vx-url-sync`, and omits it from `VX-Signals` so `[FromSignals]` binds the server default. `window.Vaxel` (`get=peek`, `set`, `patch`, `all`, `subscribe`) remains supported. *(docs/04 § Signal state, docs/06 § Output encoding)*

## v1.1.0 — 2026-10-28 · bug fixes

No wire shape changed; the protocol stays at `VX-Protocol: 1`.

- **Analyser `VAXEL003` scope.** The target-selector check now resolves the invocation's symbol and fires only when the method is on `Vaxel.PatchBuilder`, so `StringBuilder.Append`, `IHeaderDictionary.Append`, `string.Replace` and other same-named methods on unrelated types no longer report a diagnostic; an unresolvable receiver reports nothing. *(docs/05 § Tag Helpers / Analyzers)*
- **Asynchronous patch writes.** A patch document is now written with asynchronous writes, so it completes on a default Kestrel host (`AllowSynchronousIO = false`), including fragments larger than the response writer's buffer. *(docs/05 § Building responses)*
- **Directive values decode on the client.** The agent parses `<vx-directive>` through the DOM parser and reads its attributes with `getAttribute`, so HTML-encoded values round-trip: `push-url="/a?x=1&amp;y=2"` pushes `/a?x=1&y=2` and a `title` of `a &#xB7; b` shows `a · b`. Applies to `push-url`, `replace-url`, `title`, `announce`, `focus`, `scroll` and `redirect`. *(docs/03 § Directives)*
- **`vx-get` forms send their fields.** A `vx-get` form serialises its fields into the query string, merged with any query the action already carries; a non-form GET trigger's `vx-vals-*` are added the same way; a programmatic `requestSubmit()` is intercepted. *(docs/03 § Bodies, docs/04 § Triggers)*
- **Morph of an unrelated view clears the old view.** When the incoming fragment shares no ids with a region's current children, the morph ends with exactly the incoming children; shared ids are kept and the rest removed. *(docs/02 § Morph)*
- **Attribute vocabulary marked by availability.** docs/04, the README and `parity/SCOREBOARD.md` now mark each attribute ✅ implemented in 1.0 · ⬜ not in 1.0 · 🖥 server-side, and `VocabularyHonestyTests` fails if the marks and the shipped bundle disagree. Tag Helpers were added for `vx-signals`, `vx-signals-if-missing`, `vx-persist`, `vx-persist-session`, `vx-confirm`, `vx-preserve` and `vx-sse`; docs/05 lists what a view still writes raw. Implementing the ⬜ attributes is a separate packet (`v1.1-triggers`). *(docs/04, docs/05, parity/SCOREBOARD.md)*
- **Configurable antiforgery header name.** The antiforgery Tag Helper renders `VaxelOptions.AntiforgeryHeaderName` into the `vx-csrf` meta's `data-header`; the agent sends the token under that name, falling back to `X-CSRF` (the documented default) when it is absent, so `AddAntiforgery(o => o.HeaderName = …)` and the agent agree. *(docs/06 § Antiforgery)*

A jsdom regression suite (`conformance/regression/`, run under `dotnet test` from `Vaxel.Client.Tests`) was added for the client-side fixes, per docs/10 §8's fixture convention.

## v1.0 — 2026-08-24 · protocol freeze and reference application

**Protocol 1 is frozen.** Breaking wire changes require a major version bump and a graceful fallback to full navigation.

- **Name in docs and samples is `vaxel`.** The umlaut spelling is not used in the contract, README, cookbook, or Workbench copy. Package and namespace prefix remain `Vaxel`.
- **Implementation handoff** moved to [`specs/HANDOFF.md`](specs/HANDOFF.md). The root README is the public intro with a copy-paste example, not the build brief.
- **Four Product NuGet Packages (`1.0.0`)** — `Vaxel.AspNetCore`, `Vaxel.Client`, `Vaxel.Testing`, and `Vaxel.Analyzers` with MIT licence, distinct package READMEs, and static web assets (`vaxel.js`). Repository: [github.com/System-D-AB/vaxel](https://github.com/System-D-AB/vaxel). `Vaxel.Datastar` is excluded (`IsPackable=false`).
- **Reference Application (`samples/Workbench`)** — fully featured reference application demonstrating tabbed mounted layout, zero-JS progressive degradation, strongly typed signal schema validation, governed action refusals (`Patch.Refused`), and live SSE push.
- **Protocol 1 Frozen (`VX-Protocol: 1`)** — patch document schema (`<vx-patch>`, `<vx-signals>`, `<vx-directive>`), swap modes, headers, and sync policies are locked for v1.x.
- **Performance Benchmarks** — documented in `samples/Workbench/BENCHMARK.md`: ~86%–89% payload size reduction and ~4.5x higher server throughput compared to full page re-renders with constant zero-circuit server memory.
- **Parity Scoreboard Verified** — 100% of the target SDK conformance cases passing (16 pass, 4 declined with stated refusal per Rule R2), all 17 attribute plugins and 10 pro attributes matched, 100% example corpus expressibility with 0 Cannot.

## v0.3.2 — 2026-08-24 · contract folder and implementation packets

**The contract moved from `spec/` to `docs/`.** Implementation user stories must not live in the same tree as the protocol chapters, or agents treat a backlog item as a wire rule.

- **`docs/`** is the framework contract (the former `spec/` numbered chapters). Links in README, `specs/HANDOFF.md`, CHANGELOG and the scoreboard now point here.
- **`specs/`** is the Kiro-shaped implementation tree: one folder per slice, each with `requirements.md`, `design.md` and `tasks.md`. First packet: [`specs/v0.1-composer`](specs/v0.1-composer/requirements.md).
- If implementation discovers the contract was wrong, change `docs/` in the same commit and add an entry here. Do not let a packet's `design.md` become a shadow contract.

## v0.3.1 — 2026-08-23 · naming settled

**The name is vaxel, package prefix `Vaxel`.** Recorded so it is not re-litigated.

- **Etymology corrected.** Earlier revisions described *vaxel* as "a railway switch, and a telephone exchange", which was selective: the most common everyday Swedish reading is **gear / gearbox** (växellåda), then **telephone switchboard**, then **small change**; the railway sense usually needs the compound *spårväxel*. The switchboard reading is the intended one and is now stated alongside the others rather than instead of them.
- **`Singular` evaluated and rejected.** NuGet package id `Singular` is registered; npm `singular` is taken (v2.4.2, "Application module system", published since 2014); `github.com/Singular` belongs to the SINGULAR computer algebra system (GPL, RPTU Kaiserslautern, 4.4.1 released January 2025); and singular.net is an active commercial mobile-attribution company whose SDKs already occupy both registries. Three independent claims on one word.
- **Availability verified 2026-08-23:** `vaxel` returns 404 on both the NuGet registration endpoint and the npm registry — free. The GitHub organisation check returned a gateway error and is **unverified**; confirm before creating the org.

## v0.3 — 2026-08-23

**The goal is now stated: feature parity with Datastar** (owner ruling), measured rather than asserted.

- **[12 — Parity with Datastar](docs/12-parity-with-datastar.md)** — the full inventory read from their repository: 17 attribute plugins, 10 pro attributes, 4 actions, 2 watchers, and the engine/protocol surface. Each row names the vaxel construct that matches it and scores it Full / Outcome / Declined. Result: **13 of 17 attribute plugins at full parity, 4 by outcome** (the four that exist to run client expressions), 9 of 10 pro attributes, all 4 actions.
- **[13 — Test adoption](docs/13-test-adoption.md)** — their `sdk/test` suite (20 cases: 19 GET, 1 POST) is vendored unmodified and run against us. Target **16 pass, 4 declined, 0 failing**; the four declined are `executeScript*`, because executing server-sent script re-opens the door this framework exists to close. Their client library has **no test directory upstream**, so client parity is proven by fixtures derived from their plugin sources.
- **[`parity/SCOREBOARD.md`](parity/SCOREBOARD.md)** — regenerated by the test run, currently all zeroes, which is the honest starting position.
- **`Vaxel.Datastar` promoted from post-v1.0 to v0.1.5** — built early as the measuring instrument that makes their suite runnable against us, and as proof that the server API does not depend on our own client.
- Roadmap restructured so a milestone is done when its scoreboard numbers move, not when its code exists.

## v0.2 — 2026-08-23

A self-review found two internal contradictions and four undefined behaviours. All six are closed.

### Corrections (the spec was wrong)

**C1 — Signals contradicted the caching rule.** GET requests put the signal bag in a `vx-signals` query parameter, while the caching section said "signals must never be a cache key". A query parameter *is* part of the cache key, so every distinct bag would have minted its own cache entry.
→ **Signals now travel in the `VX-Signals` header, for every method.** The rule is enforceable rather than aspirational, and the framework additionally marks a response `private, no-store` when the handler actually read signals. State that should be cacheable and shareable belongs in the URL, which is what `vx-url-sync` is for. *(03, 05, cookbook 01)*

**C2 — The request body broke ASP.NET model binding.** Non-GET requests sent `{"signals":…,"values":…}` as `application/json`. Razor Pages binds forms from form-encoding: that envelope would have silently broken `[BindProperty]`, `ModelState`, validation attributes and per-field error messages — precisely the machinery the framework promises keeps working. Cookbook recipe 02 demonstrated the broken combination.
→ **Form triggers send `application/x-www-form-urlencoded`** (or `multipart/form-data` with files); JSON only on explicit `vx-encoding="json"`. *(03, 05, cookbook 02)*

### Gaps closed (the spec was silent)

**G1 — Concurrency.** Nothing defined what happens when two requests from one trigger overlap, or when responses arrive out of order — the most common real-world race, mentioned once in a cookbook table and specified nowhere.
→ `VX-Sequence` per trigger, stale responses discarded, and a `vx-sync` policy: `replace` (default) · `queue` · `drop` · `abort`. *(03, 04)*

**G2 — History.** `push-url` pushed entries with no statement of what happens on **popstate**.
→ Back issues a `GET` with `VX-History: restore`, the server re-renders, synced signals and scroll are restored, and a failed restore falls back to full navigation. No client-side snapshot cache — restoring from cached DOM is how stale screens and stale authorisation states happen. *(03)*

**G3 — Dirty input during a morph.** Undefined, and it is the single most common complaint about this category of library: the server re-renders a form while someone is typing in field three and their work vanishes.
→ **Dirty input wins over an incoming value**, keeping value, caret and selection; non-dirty inputs take the server's value; `vx-overwrite-dirty` opts out; `replace` mode resets dirtiness because it means *destroy this*. *(02, 04)*

**G4 — Parity was too vague to implement.** "Normalised for whitespace and the attributes the agent adds" cannot be coded, and this invariant is the spine of the design.
→ A three-step definition — extract by region id, normalise a *named* list, compare structurally — plus an explicit list of what is **never** normalised (ids, names, hrefs, ARIA, text, presence of children) and a `.Allowing(Difference…)` mechanism so legitimate differences are declared by a human rather than guessed by a harness. *(07)*

**G5 — Nested regions.** Focus scoping and parity for a region inside a region were undefined. → Specified. *(02)*

### Also in v0.2

- Patch modes gained `before`, `after` and `replace`-distinct-from-`morph`, and a `namespace` for `svg`/`mathml` fragments — three genuine holes found by reading Datastar's SSE reference. Without `namespace`, patching inside an `<svg>` produces HTML elements with the right names that render as nothing. *(03, 11)*
- `<vx-signals only-if-missing>` server-side, so re-rendering a shell cannot clobber a filter the user has since typed into. *(03)*
- Test matrix rows for every item above, each naming what a failure looks like. *(10)*

## v0.1 — 2026-08-23

Initial specification: principles, architecture, protocol, client attributes, .NET API, security, testing, roadmap, Datastar gap analysis and reuse, and four cookbook recipes.

---

### On this file

Six defects in the first draft of a specification is normal; finding them by writing worked examples and a test matrix is the cheap way, because a recipe that cannot be implemented and a test that cannot be written are both symptoms. Expect the protocol to take another revision or two once an implementation exists — the parts most likely to move are the ones added here, since none has yet met a browser.
