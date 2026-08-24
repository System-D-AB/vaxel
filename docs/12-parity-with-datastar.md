# 12 — Parity with Datastar: the goal, the inventory, the scoreboard

**Goal (owner ruling, 2026-08-23): Växel matches Datastar feature for feature.** Not "inspired by", not "a subset with different priorities" — an application a team could build on Datastar must be buildable on Växel, with the same capabilities available to it.

The single constraint that shapes *how* a feature is matched: Växel does not evaluate strings on the client (R2). So parity is measured on **outcome**, not syntax. For every Datastar feature, this document names the Växel construct that achieves the same user-visible result, and where the construct is a server round trip instead of a client evaluation, it says so plainly rather than claiming equivalence it does not have.

Progress is measured, not asserted: [`parity/SCOREBOARD.md`](../parity/SCOREBOARD.md) carries the current counts, and [13 — Test adoption](13-test-adoption.md) explains where the tests come from.

---

## 1. Attribute plugins (17 in `library/src/plugins/attributes`)

| # | Datastar | Växel construct | Parity | How it is proven |
|---|---|---|---|---|
| 1 | `attr` | `vx-attr:<name>="signal"` | **Full** | Fixture per attribute type incl. boolean attributes |
| 2 | `bind` | `vx-bind` (+ `-event`, `-prop`) | **Full** | Fixtures: text, number, checkbox, radio, select, multi-select, file |
| 3 | `class` | `vx-class:<name>="signal"` | **Full** | Fixture; server-rendered classes not clobbered |
| 4 | `computed` | **Server-computed signal**, patched via `<vx-signals>` | **Outcome, +1 round trip** | Fixture pair: a derived value updates after the response. *Client-local derivation with zero latency is not matched — by design* |
| 5 | `effect` | Island subscribing to `vx:signals-changed` | **Outcome, via island** | Fixture: island receives changed keys |
| 6 | `indicator` | `vx-indicator="signal"` | **Full** | Signal true on send, false on settle and on error |
| 7 | `init` | `vx-signals` seed + `vx-trigger-load` (+ delay) | **Full** | Fixtures for both halves |
| 8 | `jsonSignals` | `vx-debug-signals` | **Full** (dev builds) | Absent from production bundle |
| 9 | `on` | `vx-on` + modifier set (`prevent`, `stop`, `capture`, `passive`, `once`, `window`, `document`, `outside`, `delay`, `debounce`, `throttle`) | **Full for triggering**; arbitrary expression bodies are `vx-signal-set` (literal) or a request | Fixture per modifier |
| 10 | `onIntersect` | `vx-trigger-visible` (+ `-threshold`, `-exit`, `-once`) | **Full** | Browser fixture per threshold |
| 11 | `onInterval` | `vx-poll` (1 s floor) | **Full** | Fixture; stops when element removed |
| 12 | `onSignalPatch` (+ filter) | `vx:signals-changed` event with changed keys | **Outcome, via island** | Fixture; filtering is a `.filter()` in the island |
| 13 | `ref` | `querySelector` in an island | **Outcome, via island** | Documented pattern, no framework surface |
| 14 | `show` | `vx-show="signal"` | **Full** | Fixture incl. the exact falsy set |
| 15 | `signals` (+ `ifmissing`) | `vx-signals`, `vx-signals-if-missing`, `<vx-signals only-if-missing>` | **Full** | Merge, delete-by-null, no-clobber fixtures |
| 16 | `style` | `vx-style:<prop>="signal"` (CSSOM) | **Full** | Fixture; never concatenated into a `style` string |
| 17 | `text` | `vx-text="signal"` | **Full** | Fixture; value never parsed as HTML |

**13 full, 4 by outcome.** All four "by outcome" are the same thing: Datastar runs an expression on the client, Växel either has the server answer or hands it to ~10 lines of island JavaScript. An application can do everything; the *authoring* differs.

## 2. Pro attributes (10)

| Datastar | Växel | Parity |
|---|---|---|
| `persist` | `vx-persist` / `-session` (explicit name lists) | **Full** |
| `query-string` | `vx-url-sync` (+ `-history`) | **Full** |
| `match-media` | `vx-match-media:<name>` | **Full** |
| `scroll-into-view` | `scroll` directive + behaviour/block/inline/focus | **Full** |
| `view-transition` | `vx-transition-name` + per-patch `transition` | **Full** |
| `replace-url` | `replace-url` directive + `vx-url-sync` | **Full** |
| `custom-validity` | Server validation rendered into the field's error region | **Outcome, +1 round trip** |
| `animate` | CSS transitions / View Transitions / island | **Outcome, via CSS or island** |
| `on-raf` | Island | **Outcome, via island** |
| `on-resize` | `vx-match-media` for breakpoints; island for element-level | **Partial** — element-level resize has no framework surface; add if a real case appears |

## 3. Actions (4 in `library/src/plugins/actions`)

| Datastar | What it does | Växel |
|---|---|---|
| `fetch` (`@get`, `@post`, …) | Issues the request from inside an expression | `vx-get` / `vx-post` / … as attributes. **Full** — and one fewer concept, since there is no expression to host the call |
| `peek` | Read a signal without subscribing | Internal to the store; no authoring surface needed. **N/A by construction** |
| `setAll` | Set many signals matching a filter | `<vx-signals>` from the server sets any number at once; client-side, `vx-signal-set` per control. **Full for the outcome**; a wildcard client-side "set everything matching /foo/" is deliberately absent (same reasoning as persist name lists) |
| `toggleAll` | Toggle many signals | Same as above. **Full for the outcome** |

## 4. Watchers, engine and protocol

| Datastar | Växel | Parity |
|---|---|---|
| `patchElements` watcher | `<vx-patch>` application | **Full** — modes matched: `outer`, `inner`, `replace`, `prepend`, `append`, `before`, `after`, `remove`, plus our `morph` default |
| `patchSignals` watcher | `<vx-signals>` | **Full**, incl. `onlyIfMissing` and delete-by-null |
| `executeScript` (SDK event) | **Deliberately absent** | Executing server-sent script is the `unsafe-eval` doorway re-opened. The outcome — "make the client do X" — is covered by patches, signals and directives (`redirect`, `reload`, `focus`, `scroll`, `title`, `announce`). This is the one case where we do not chase parity, and [13](13-test-adoption.md) explains how their tests for it are scored |
| SSE event framing, `id`, `retry` | Same framing; payload is our HTML patch document | **Full** |
| Signal reading from body/query | `VX-Signals` header, every method | **Equivalent**; their `readSignalsFromBody` case is met through the compatibility adapter |
| Namespaces (`svg`, `mathml`) | `namespace` on a patch | **Full** |
| View transition options | `transition` + `transition-scope` | **Full** |
| Aliasing / custom prefix | Configurable prefix | **Full** |
| Plugin system | **Absent by design** — the vocabulary is closed | Not a parity gap; a plugin system is how a closed vocabulary stops being closed |

## 5. What parity does *not* mean

Two things are out of scope permanently, and stating them here prevents an endless chase:

1. **Expression authoring.** `$foo > 3` in an attribute. Every capability it delivers is reachable another way; the syntax is not coming back.
2. **Server-sent script execution.** Same reason, higher stakes.

Everything else on Datastar's surface is either matched today in this specification, or listed above with the construct that matches it.

## 6. Scoring rules

To stop "parity" being a feeling, every row is scored by a test, and the scoreboard counts them:

| Score | Meaning |
|---|---|
| **✅ Full** | A conformance fixture demonstrates the same observable result with the same number of authoring steps |
| **🟡 Outcome** | Same observable result, different authoring cost (a round trip, or an island). The fixture asserts the result *and* records the cost |
| **⛔ Declined** | Deliberately not matched. The fixture asserts that we do **not** do it |
| **❌ Missing** | No construct yet. This is the only score that is a bug |

Today every row is unimplemented, so the scoreboard reads `0 implemented`. That number is the point of having one.
