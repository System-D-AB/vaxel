# 10 — The test matrix: how every part of this is tested

[07 — Testing](07-testing.md) gives the strategy. This document is the checklist: for **every** attribute, protocol element and guarantee in the specification, what proves it, at which level, and what a failure looks like.

Five levels, cheapest first. A behaviour is tested at the cheapest level that can actually observe it — testing a swap mode in a browser is as wasteful as testing focus restoration in an HTTP test is impossible.

| Level | Runner | Speed | Proves |
|---|---|---|---|
| **U** Unit | xUnit, no host | ms | Builders, parsers, binders, serialisers |
| **H** HTTP | `WebApplicationFactory` | ms | Endpoints, patch documents, parity, refusals, security headers |
| **D** DOM | jsdom/linkedom + fixtures | ms | Attribute semantics, morph, signals, directive application |
| **B** Browser | Playwright, real server | s | Focus, caret, IME, scroll, view transitions, SSE reconnection, a11y |
| **S** Static | Analyser / build script | ms | Bundle contains no `eval`; docs and code agree |

---

## 1. The four rules

| Rule | Level | Test |
|---|---|---|
| R1 server renders everything | S | No client file contains a template literal producing HTML from data; the agent's only DOM authorship is `parseFromString` on a server document plus attribute writes |
| R2 attributes are data | S + B | Bundle grep: no `eval(`, `new Function`, `setTimeout("…")`, `innerHTML =` from a non-server string. Browser: page under `script-src 'self'` reports **zero** CSP violations |
| R3 every target is also a page | H | `VaxelParity.AssertAsync` over every declared region — the single most valuable test in the suite |
| R4 no per-user server state | U + H | No framework service is registered as a singleton holding per-connection state except the push registry; a second request on a fresh connection renders identically |

---

## 2. Client attributes ([04](04-client-attributes.md))

### Triggers

| Attribute | Level | What is asserted | Failure looks like |
|---|---|---|---|
| `vx-get` / `vx-post` / `vx-put` / `vx-patch` / `vx-delete` | D | Correct method, URL, headers (`VX-Request`, `VX-Target`, `VX-Url`), antiforgery token attached to non-GET | Silent 403s in production |
| `vx-on` | D | Default event per element type; explicit override wins | A select that only fires on click |
| `vx-target` / `vx-swap` | D | Sent as advisory headers; server response still wins | Client and server disagree on the target |
| `vx-vals-*` | D | Literals become ordinary body fields; never interpreted | An injected value executing |
| `vx-include` | D | Named form's fields merged | Partial submissions |
| `vx-confirm` | D | No request when declined; text used verbatim | Destructive action without a prompt |
| `vx-indicator` | D | Signal true on send, false on settle **and on error** | A spinner that never stops |
| `vx-disable` | D | Disabled during flight; re-enabled on error; **double-submit produces one request** | Duplicate orders |
| `vx-debounce` / `-leading` / `vx-throttle` / `vx-delay` | D | Timing with a fake clock; leading/trailing edges | Request storms |
| `vx-once` | D | Second trigger does nothing | — |
| `vx-prevent` / `vx-stop` / `vx-capture` / `vx-passive` | D | Listener options and default-prevention | Links navigating away mid-request |
| `vx-window` / `vx-document` | D | Listener attached to the right scope; removed when the element is morphed away | Leaked listeners after navigation |
| `vx-outside` | D | Fires only for events outside the element | Panels that never close, or close on their own clicks |
| `vx-trigger-load` (+delay) | D | Fires on insertion, including insertion *by a patch* | Lazy regions that stay empty after a swap |
| `vx-trigger-visible` (+threshold/exit/once) | B | IntersectionObserver thresholds | Infinite scroll firing at the wrong time |
| `vx-poll` | D | Interval respected; 1 s floor enforced; stops when the element is removed | A tab burning battery forever |
| `vx-sync` (`replace`/`queue`/`drop`/`abort`) | D | Each policy under two overlapping triggers; the in-flight request is really aborted | Two responses fighting over one region |
| `VX-Sequence` staleness | D | A slow first response arriving after a fast second is discarded and reported | The older answer wins and the screen lies |
| `vx-encoding="json"` | D + H | JSON body only when asked; form encoding otherwise | `[BindProperty]` silently binding nothing |
| Degradation rule | S | Analyser: a trigger on a non-degradable element fails the build | An app that breaks without JS |

### Regions, morph, lifecycle

| Attribute | Level | What is asserted |
|---|---|---|
| `vx-region` | H | Declared regions are all covered by the parity harness |
| `vx-preserve` | D + B | Subtree identity survives a morph that would otherwise replace it; an island's state (a chart instance) is intact |
| `vx-preserve-attr` | D | `<details open>` stays open; a class added by an island survives |
| Dirty input wins | D + B | A field edited during a request keeps its value, caret and selection; a non-dirty field takes the server's value; `replace` mode resets dirtiness | Typed work vanishing mid-form — the classic complaint |
| `vx-overwrite-dirty` | D | The server's value wins for that element only | A normalised value that never appears |
| Nested regions | D + H | Inner region morphed with the outer; focus scopes to the innermost; parity holds for both | A fragment reachable nowhere as a page |
| `vx-ignore` / `vx-ignore-self` | D | No attribute in the subtree is processed; children still processed for `-self` |
| `vx-transition-name` | B | `view-transition-name` applied; graceful where the API is absent |
| `vx-sse` | B | Exactly one channel per document |

### Signal bindings

| Attribute | Level | What is asserted |
|---|---|---|
| `vx-text` | D | Text set, **not** parsed as HTML — a value of `<b>x</b>` renders literally |
| `vx-show` | D | `hidden` toggled; falsy set is `false`, `null`, `0`, `""` and undefined, and nothing else |
| `vx-class:<n>` | D | Class added/removed; classes the server rendered are not clobbered |
| `vx-attr:<n>` | D | Set, and **removed** on `null`/`false`; `disabled`/`checked` handled as boolean attributes |
| `vx-style:<p>` | D | Applied via CSSOM; value never concatenated into a `style` string |
| `vx-bind` (+`-event`, `-prop`) | D | Two-way for text, number, checkbox, radio, select, multi-select; type preserved (a number stays a number) |
| `vx-signal-set:<event>` | D | Literal assignment only; a value containing `=` or JSON does not become a nested write |
| `vx-signals` / `-if-missing` | D | Seed merges shallow; `-if-missing` does not clobber; `null` deletes |
| `vx-persist` / `-session` | D | Only named signals stored; storage unavailable (private mode, quota) degrades silently |
| `vx-url-sync` (+`-history`) | D + B | Query string updated; `replaceState` by default; back button restores signals **and** the server-rendered region |
| `vx-match-media:<n>` | B | Signal flips on a media change |
| `vx-debug-signals` | S | Absent from the production bundle |

### Ordering and prefixing

| Guarantee | Level | Test |
|---|---|---|
| Depth-first, left-to-right binding order | D | Two bindings writing one property resolve deterministically; the agent warns |
| One owner per property | D | A patch and a binding driving the same property: bindings win, and the development build warns | A server decision silently overridden |
| Patch order = document order; directives last | D | A patch that adds `#x` followed by `focus="#x"` works |
| Configurable prefix | D | Same fixture passes with `vx-`, `data-vx-` and a custom prefix |

---

## 3. Protocol ([03](03-protocol.md))

| Element | Level | What is asserted |
|---|---|---|
| Request headers | D | All present and correctly valued; antiforgery only on state-changing methods |
| Signals in `VX-Signals` header | D + H | Same header for every method; round-trips; never appears in the URL or the body |
| Form bodies bind natively | H | `[BindProperty]`, `ModelState` and validation attributes behave exactly as without the framework |
| `no-store` when signals are read | H | Response reading signals is not cacheable; one that does not read them still is |
| Signals over 8 KB | D + H | Agent omits and flags; **endpoint still succeeds** |
| Multipart | H | Native encoding preserved; signals in header; file streams |
| Modes: `morph`, `outer`, `replace`, `inner`, `append`, `prepend`, `before`, `after`, `remove` | D | One fixture pair each |
| `namespace` (svg / mathml) | D | Fragment parsed in the right namespace and patched inside an `<svg>` |
| Missing target | D | Ignored, reported through `vx:after-apply`, no throw |
| Unknown mode | D | Nothing applied from that element; `vx:error` raised |
| `<vx-signals>` merge / delete / only-if-missing | D | Shallow merge; `null` deletes |
| Directives: push-url, replace-url, focus, scroll(+placement), title, announce, redirect, reload | D + B | Applied after patches; redirect stops application; redirect refuses cross-origin |
| Status codes | H | 2xx and non-2xx both carry patch documents and both apply |
| `Vary: VX-Request` | H | Present, so a proxy cannot serve a patch to a page request |
| Protocol version mismatch | D | Falls back to native navigation, does not misapply |
| SSE frames, heartbeat, reconnect, `Last-Event-ID` | B | Reconnect after a forced drop; heartbeat keeps a proxy from closing |
| popstate restore | B + H | Back issues `VX-History: restore`, re-renders from the server, restores synced signals and scroll; a failed restore falls back to full navigation | Back button showing a stale or lying screen |
| Parity definition | U | The normaliser drops exactly the reserved runtime attributes and nothing else; a changed `aria-label`, id or empty-state wording still fails | A harness that excuses the bug it exists to catch |

---

## 4. .NET API ([05](05-dotnet-api.md))

| Surface | Level | What is asserted |
|---|---|---|
| `PartialAsync` / `ViewAsync` / `PageAsync` | H | Renders identically to the same unit inside a page |
| `ComponentAsync<T>` | H | ViewComponent renders outside a view context, with DI, TempData and `IUrlHelper` available |
| `RazorComponentAsync<T>` | H | Static SSR output matches a page-hosted render |
| Composer without `HttpContext` | U | Throws a *named* error for `Url.Page`/`User`, not a `NullReferenceException` |
| `Patch` builder | U | Emits well-formed documents for every mode and directive; escaping correct |
| `PatchResult` as `IResult` / `IActionResult` | H | Works from minimal APIs, controllers and Page handlers |
| `PageOrPatch` | H | Chooses by `VX-Request`; both branches render the same region (this *is* the parity harness) |
| `[FromSignals]` | U + H | Case-insensitive; unknown keys ignored; malformed JSON binds defaults and **never** 500s |
| Signal schema + Tag Helpers | S | Unknown signal name in a binding fails the build; target that is not an id fails the build |
| `IPushChannel` scopes | H | User/Group/Broadcast reach exactly the intended connections |
| Per-identity stream cap | H | The (cap + 1)-th connection is refused with a clear status |

---

## 5. Security ([06](06-security.md))

| Guarantee | Level | Test |
|---|---|---|
| Runs under `script-src 'self'` | B | Strict CSP + violation reporting; zero reports across the whole cookbook |
| No eval constructs shipped | S | Bundle grep in CI; a PR adding one fails |
| Antiforgery on every state-changing request | D + H | Missing token → agent refuses to send and reports; server rejects a forged post |
| Signals are never authority | H | Endpoint ignores `{"isAdmin":true}`; analyser warns on authorisation-shaped signal names |
| Signals never a cache key | H | Two different bags at one URL do not poison each other |
| Signal seeds escaped | U | A value containing `</script>`, quotes and `&` cannot break out of the attribute |
| Scripts in patches inert by default | D | `<script>` in a fragment does not execute unless opted in |
| Redirect same-origin only | D | Cross-origin redirect directive raises `vx:error` |
| SSE authorisation | H | Unauthenticated connection refused; a client cannot request another user's scope |
| `vx-text` does not render HTML | D | Covered above, restated because it is the classic XSS foothold |

---

## 6. Accessibility

| Guarantee | Level | Test |
|---|---|---|
| Focus after a user-initiated patch | B | Directive target, else surviving trigger, else region |
| Focus **never** moves on poll/SSE patches | B | Type into a field while a push arrives; caret unmoved |
| `aria-busy` set and cleared | D | Including on error paths |
| Live region announces | B | `announce` text reaches the polite region once, not twice |
| Keyboard parity | B | Every cookbook recipe operable by keyboard alone |
| axe-core | B | Zero violations on every cookbook page, before and after a patch |

---

## 7. Performance guardrails

| Budget | Level | Test |
|---|---|---|
| Signals + agent + morph ≤ 12 KB gzip | S | Size gate in CI, itemised per piece so a regression names its cause; the file must exist (a gate that skips when the bundle is missing is not a gate) |
| Morph of a 500-row table < 16 ms | B | Benchmark fixture |
| No listener leaks | D | 1 000 patch cycles; listener and observer counts stable |
| No unbounded signal growth | D | Repeated seeds do not accumulate keys |

---

## 7a. Adopted from Datastar

Not everything here is ours to write. Datastar's `sdk/test` suite — 20 black-box cases driven by shell scripts against a `/test` endpoint — is vendored unmodified and run against the `Vaxel.Datastar` adapter. It covers `patchElements`, `patchSignals`, `removeElements`, `removeSignals`, multi-event framing and reading signals from a body. Their 17 attribute plugins and 4 actions are read as behaviour specifications and ported into our fixtures. See [13 — Test adoption](13-test-adoption.md); scores live in [`parity/SCOREBOARD.md`](../parity/SCOREBOARD.md).

## 8. The conformance suite

Fixture triples — `before.html`, `patch.html`, `expected.html`, plus `assert.json` for what the DOM cannot show (events fired, focus landing, announcements, fallbacks). Any implementation of this protocol can run it, in any language.

```
conformance/
  attributes/…      one directory per attribute, incl. every modifier
  protocol/…        one per mode, header, directive, error path
  security/…        escaping, inert scripts, redirect refusal
  a11y/…            focus, aria-busy, announcements
  regression/…      one per fixed bug, named after the issue
```

**Rule:** every bug fix adds a fixture before it adds a fix. The suite is the specification's executable half — where prose and fixtures disagree, the fixture is the bug report.

## 9. What a consuming application tests

1. Parity over every region — one test, whole app.
2. Handler logic with `FakeFragmentComposer` — fast, no Razor.
3. Refusal wording and design-system rendering — its own contract.
4. Two or three journeys in a real browser against the **real server**.

And what it must *not* test: swap mechanics, focus restoration, history, signal merging. Those belong to the conformance suite; an application testing them has found a framework bug and should report it as one.
