# Växel — implementation handoff

**Read this first. It is the entry point for whoever builds this.** Everything needed to finish the project is in this folder; nothing depends on the repository it was drafted in.

| | |
|---|---|
| **What** | A server-driven web framework for .NET: Razor renders everything, a small client agent applies HTML patches, signals carry ephemeral UI state |
| **Status** | Specification **v0.3, complete and internally consistent**. **Zero lines of code written.** |
| **Goal** | Feature parity with [Datastar](https://data-star.dev), minus the client expression language, measured by Datastar's own conformance suite |
| **Licence** | MIT intended. Only third-party code: a DOM morph implementation (BSD-2) |
| **Name** | *Växel* — **settled 2026-08-23.** Swedish for *gear/gearbox*, *telephone switchboard*, *small change* and (as spårväxel) *railway points*; the switchboard sense is the intended one. ASCII namespace and package prefix: `Vaxel`. `vaxel` verified free on NuGet and npm on 2026-08-23 |

---

## 1. The four rules — never violate these

Every design question is answered by these. A proposed feature that contradicts one is wrong, however convenient.

**R1 — The server renders everything.** No client templates, no client render tree. HTML comes from Razor: Pages, Views, Partials, ViewComponents, Tag Helpers, statically-rendered Razor Components.

**R2 — Attribute values are data, never code.** No `eval`, no `new Function`, no expression language. This is why the framework exists: it runs under `script-src 'self'` with no `unsafe-eval`. Datastar and Alpine both require `unsafe-eval`; that is the gap Växel fills.

**R3 — Every fragment target is also reachable as a full page.** A patch is an optimisation of a page, never a capability the page lacks. Enforced by the parity harness, not by discipline.

**R4 — No per-user server state.** State lives in the URL (shareable), the server's durable store (domain data), or signals (ephemeral client state). No sockets holding sessions, no server-side component trees between requests.

## 2. Product features — the complete build list

### Server (`Vaxel.AspNetCore`) — the valuable half

- **`IFragmentComposer`** — render any Razor unit to `IHtmlContent`: `PartialAsync`, `ViewAsync`, `PageAsync`, `ComponentAsync<TViewComponent>`, `RazorComponentAsync<T>` (static SSR). Works inside a request and in a background scope; the background scope fails with a *named* error for anything needing `HttpContext` (`Url.Page`, `User`).
- **`Patch` builder / `PatchResult`** — implements `IResult` and `IActionResult`. Modes: `morph` (default), `outer`, `replace`, `inner`, `append`, `prepend`, `before`, `after`, `remove`; `namespace` for svg/mathml; directives `focus`, `scroll`, `title`, `announce`, `push-url`, `replace-url`, `redirect`, `reload`; `Signals(...)`; `Transition(...)`.
- **`Patch.Refused(refusal)`** — refusals are a normal response: rendered notice, non-2xx status, same code path as success.
- **`Vaxel.PageOrPatch(...)`** — one handler serves a page and a fragment; chooses on `VX-Request`. This *is* R3 expressed in code.
- **`[FromSignals]` binding** — deserialises the `VX-Signals` header into a type. Case-insensitive, invariant culture, never throws on unknown or malformed keys. Reading signals marks the response `private, no-store`.
- **Tag Helpers** — Razor never types a raw `vx-` attribute. They validate targets are ids, bound names are declared signals, triggers sit on degradable elements. **They are the seam that makes the client replaceable.**
- **`IPushChannel` + `MapVaxelStream`** — SSE, `PushScope.User/Group/Broadcast`, heartbeat, reconnect, per-identity caps, `IPushBackplane` seam for multi-node.

### Client (`Vaxel.Client`) — three pieces, ~9–10 KB gzip, 12 KB cap

| Piece | ~gzip | Role |
|---|---|---|
| Signal store | 1 KB | `get`/`set`/`patch`/`subscribe` + name-bound DOM updates |
| Agent | 4–5 KB | Triggers, requests (headers, antiforgery, sequencing, `vx-sync`), patch application, directives, focus/scroll/history, indicators, debounce/throttle, SSE |
| Morph | 4 KB | Identity, focus, caret, selection and dirty-input preservation |

Closed attribute vocabulary — full reference in [`docs/04`](docs/04-client-attributes.md). Triggers, regions/morph controls, signal bindings, signal state (persist, url-sync, match-media). **Nothing else. There is no plugin system, and adding one is a design failure, not a feature.**

### Testing (`Vaxel.Testing`)

`VaxelParity.AssertAsync` (the R3 harness), patch assertions (`ShouldPatch`, `ShouldSetSignal`, `ShouldDirect`, `ShouldNotPatch`), `FakeFragmentComposer`, `OpenStreamAsync` for SSE.

## 3. The role of the signal library — read this before touching signals

Signals are **the last mile**. The server owns every derivation; signals carry the answer the final few metres to the DOM without a round trip.

**Reactive, but one edge deep.** Signal → binding. No `computed`, no derivation chains, no diamond problems, no glitch avoidance, no topological ordering. All of that lives on the server.

**Reactivity never produces structure.** Bindings toggle text, class, attribute, style, visibility and input values on elements the server rendered. Creating, removing or reordering elements is a patch — always.

**Signals are never authority.** They are user-visible and user-editable by definition. Bind them, validate them, never authorise with them. The binder is deliberately forgiving so a tampered bag cannot 500 an endpoint.

**One owner per property.** A property is driven by the server *or* by a signal, never both — patches apply first and bindings re-run after, so a binding silently wins. The dev build warns on the conflict.

**Implementation is pluggable.** Default is an in-house ~1 KB store; `@preact/signals-core` (MIT, ~1.5 KB) is a drop-in alternative. **Open decision for the owner** — either is defensible.

*Honest note for whoever builds this:* the binding layer exists for Datastar parity and to avoid round trips on pure-UI toggles. A strictly minimal Växel — fragments only, no signals — is a coherent smaller design. Do not remove it without the owner's ruling, but know that it is the one feature present for parity rather than necessity.

## 4. Parity with Datastar — the target

Full matrix: [`docs/12`](docs/12-parity-with-datastar.md). Scoreboard: [`parity/SCOREBOARD.md`](parity/SCOREBOARD.md).

| Surface | Target |
|---|---|
| Attribute plugins (17) | **13 Full**, 4 by outcome (`computed`, `effect`, `onSignalPatch`, `ref` — the four that exist to run client expressions) |
| Pro attributes (10) | 9 matched, 1 partial (`on-resize` at element level) |
| Actions (4) | 4 matched |
| Watchers / protocol | Full, incl. all patch modes and namespaces |
| Datastar SDK conformance (20 cases) | **16 pass · 4 declined · 0 failing** |

**Two permanent declines**, so nobody re-opens them: client expression authoring, and executing server-sent script (`executeScript`). Both are the same door; both are why this framework exists.

"Parity by outcome" means the same observable result at a different authoring cost — a server round trip, or ~10 lines of island JavaScript. Fixtures record that cost rather than hiding it.

## 5. Testing procedures

Full matrix: [`docs/10`](docs/10-test-matrix.md). Adoption plan: [`docs/13`](docs/13-test-adoption.md).

### Five levels — test at the cheapest one that can observe the behaviour

**U** unit (xUnit, no host) · **H** HTTP (`WebApplicationFactory`) · **D** DOM (jsdom/linkedom fixtures) · **B** browser (Playwright, **against the real server**) · **S** static (bundle greps, analyser).

### The four gates that must be green to release

1. **Parity harness (R3)** — every declared region reachable as a page. Definition of "parity" is exact and in [`docs/07`](docs/07-testing.md): extract by region id → normalise a *named* list → compare structurally. Ids, names, hrefs, ARIA, text and child presence are **never** normalised. Legitimate differences are declared with `.Allowing(Difference…)`, one line a reviewer can question.
2. **CSP suite** — bundle contains no `eval(`, `new Function`, string `setTimeout`; a browser run under the strict policy reports **zero** violations.
3. **Adopted Datastar suite** — their `sdk/test` scripts, **vendored unmodified**, run against `test/Vaxel.Conformance.Host` via the `Vaxel.Datastar` adapter: `./test-all.sh http://localhost:5199`.
4. **Accessibility suite** — focus restoration, `aria-busy`, live-region announcements, and no focus theft on poll- or SSE-driven patches.

### Non-negotiable testing rules

- **Never edit a vendored test into passing.** A case is passing, declined with a stated reason, or a bug. A suite you edit tests your edits.
- **Every bug fix adds a fixture before it adds a fix.** The conformance suite is the specification's executable half.
- **Browser tests drive the real server.** A browser suite that loads fixture HTML tests the fixtures and will pass after the application is deleted.
- **The scoreboard is regenerated, never hand-edited.**
- **A size gate that skips when the bundle is missing is not a gate** — assert the file exists.

## 6. Repository layout to create

```
src/Vaxel.AspNetCore/         composer, patch builder, signals binding, tag helpers, SSE
src/Vaxel.Client/             agent + morph + signal store (TS → static web assets)
src/Vaxel.Testing/            parity harness, patch assertions, fake composer, stream client
src/Vaxel.Datastar/           compatibility adapter — the measuring instrument (see §7)
test/Vaxel.AspNetCore.Tests/  U + H
test/Vaxel.Client.Tests/      D (jsdom) + the conformance fixture runner
test/Vaxel.Browser.Tests/     B (Playwright against a real host)
test/Vaxel.Conformance.Host/  the /test endpoint Datastar's scripts drive
Vaxel.slnx                    .NET 10 solution (not .sln)
conformance/                  our fixtures: attributes/, protocol/, security/, a11y/, regression/
conformance/vendor/datastar/  their sdk/test, unmodified, pinned commit recorded in NOTICE
samples/                      the reference application (see v1.0)
docs/                         framework contract (was spec/)
specs/                        implementation packets (requirements, design, tasks)
cookbook/ parity/             recipes and scoreboard
```

## 7. Milestones — done means the scoreboard moved

**v0.1 — the server half.** Composer, patch builder, `PageOrPatch`, refusals, `[FromSignals]`, Tag Helpers, `Vaxel.Testing`. **No client of our own** — drive the protocol with htmx + a morph extension, configured by the Tag Helpers. *Done when:* the cookbook's four recipes work end to end and the parity harness passes over all their regions.

**v0.1.5 — the measuring instrument.** `Vaxel.Datastar` adapter + conformance host, so their 20 cases run against us unmodified. Promoted deliberately: a scoreboard on day one is worth more than the same work later, and it proves the server API does not depend on our own client. *Done when:* **16 pass, 4 declined, 0 failing.**

**v0.2 — the agent.** Signal store, agent, morph. Conformance, CSP and accessibility suites. htmx becomes optional, then unnecessary. *Done when:* every attribute in [`docs/04`](docs/04-client-attributes.md) has fixtures, the CSP run is clean, and the bundle is under 12 KB gzip.

**v0.3 — push.** SSE endpoint, `IPushChannel`, heartbeat, reconnect, caps, backplane seam, hosting notes for common proxies. *Done when:* recipe 04's tests pass, including reconnect-after-drop.

**v0.4 — ergonomics.** Signal schema + Tag Helper validation; Roslyn analyser (authorising from signals, triggers on non-degradable elements, targets that are not ids, patch targets with no page route); dev overlay showing last patch, ignored patches, signal diff.

**v0.5 — parity closure.** Every row in [`docs/12`](docs/12-parity-with-datastar.md) has a fixture and a score; Datastar's example gallery is 100 % expressible; any `Cannot` is closed or promoted into the spec as a stated limitation.

**v1.0 — commitment.** Protocol frozen at version 1; semver; a real reference application (not a to-do list: authorisation, forms, an editing surface, a governed action); benchmarks against a plain MVC baseline.

## 8. Working agreements for whoever implements this

1. **Spec first.** [`docs/`](docs/README.md) is the contract. If the code needs to differ, change `docs/` in the same commit and add a CHANGELOG entry saying what was wrong — a contract that quietly drifts teaches nobody. Implement from [`specs/`](specs/README.md); do not put user stories in `docs/`.
2. **The three scope questions.** Needs client string evaluation? Out. Needs server state between requests? Out. Doable in twenty lines of island JavaScript by the application? Then let the application do it.
3. **The failure mode is completeness, not error.** Every framework in this space that grew a plugin system, a component abstraction and a client router ended up competing with React on React's terms and losing.
4. **Credit properly.** Vendored code carries its licence and a **real 40-character commit SHA** in `NOTICE`. Never an abbreviated or invented hash.
5. **Estimates are labelled as estimates** until a gate measures them. Every KB figure in this specification is currently borrowed from comparable libraries.

## 9. Open decisions the owner must make

| # | Decision | Options | Default if unanswered |
|---|---|---|---|
| 1 | Signal store | In-house ~1 KB · `@preact/signals-core` | In-house |
| 2 | Morph | Vendor Idiomorph (BSD-2) · write ~200 lines | Vendor Idiomorph |
| 3 | ~~Name~~ | **Resolved 2026-08-23: Växel / `Vaxel`.** *Singular* was evaluated and rejected — NuGet id and npm package both taken, the GitHub org belongs to the SINGULAR computer algebra system (GPL, RPTU Kaiserslautern), and singular.net is an active commercial software trademark | — |
| 4 | Repository | Standalone OSS · private infrastructure | Decide before v0.5 |
| 5 | First consumer | Which real application proves it | Required before v1.0 |

## 10. Honest risk register

- **Nothing is implemented.** Every guarantee here — morph preserving caret and selection, dirty-input arbitration, the size budget, popstate feeling instant — is an assertion until a browser has an opinion. The server half is the part I would bet on; the wire and client halves should be expected to take another revision or two.
- **Morph correctness is where this category of library lives or dies.** Budget accordingly; it is not the easy part.
- **The parity harness must be usable.** If the normalisation rules prove impractical in real applications, the invariant erodes into an ignored test. Watch it during v0.1 and revise the rules rather than the invariant.
- **SSE under managed platforms and proxies** is operational work, not code work. Verify early on the intended deployment target.
- **Two hypermedia mechanisms during v0.1–v0.2** (htmx and, later, the agent). Never let both be primary for the same surface.

## 11. Document index

| Doc | Settles |
|---|---|
| [README](README.md) | The pitch, the goal, the thirty-second example |
| [CHANGELOG](CHANGELOG.md) | What changed between spec versions **and why it was wrong** |
| [docs/01 Principles](docs/01-principles.md) | The four rules; what signals are and are not; non-goals |
| [docs/02 Architecture](docs/02-architecture.md) | Parts, lifecycle, morph, dirty input, nested regions, islands, what ships |
| [docs/03 Protocol](docs/03-protocol.md) | Headers, bodies, patch documents, concurrency, history, SSE, caching, errors |
| [docs/04 Client attributes](docs/04-client-attributes.md) | The closed vocabulary, ordering, one-owner rule, a11y obligations |
| [docs/05 .NET API](docs/05-dotnet-api.md) | Packages, DI, composer, builder, binding, Tag Helpers, push, hosting |
| [docs/06 Security](docs/06-security.md) | CSP, antiforgery, signal trust boundary, sanitisation, limits |
| [docs/07 Testing](docs/07-testing.md) | Strategy; the exact definition of parity |
| [docs/08 Roadmap](docs/08-roadmap.md) | Milestones, scope discipline, fair comparison, licence |
| [docs/09 Gap analysis](docs/09-datastar-gap-analysis.md) | Every Datastar attribute and our position on it |
| [docs/10 Test matrix](docs/10-test-matrix.md) | Every feature, its level, and what a failure looks like |
| [docs/11 Datastar reuse](docs/11-datastar-reuse.md) | What to vendor, converge on, decline, credit |
| [docs/12 Parity](docs/12-parity-with-datastar.md) | The full inventory and scoring rules |
| [docs/13 Test adoption](docs/13-test-adoption.md) | Taking their 20 cases as our scoreboard |
| [cookbook/01–04](cookbook/) | Search-as-you-type · contact form · tabs/rail/inline edit · live updates |
| [parity/SCOREBOARD.md](parity/SCOREBOARD.md) | Where we actually are |
| [specs/](specs/README.md) | Implementation packets: requirements, design, tasks per slice |

**Read the contract at [docs/01](docs/01-principles.md), then [docs/03](docs/03-protocol.md) and [docs/05](docs/05-dotnet-api.md). Implement from [specs/](specs/README.md), starting with [v0.1-composer](specs/v0.1-composer/requirements.md), against [cookbook/02](cookbook/02-contact-form.md).**
