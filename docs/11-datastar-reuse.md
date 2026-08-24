# 11 — Taking advantage of Datastar

Datastar is MIT-licensed, its protocol is documented, and its design is the direct ancestor of this one. Reinventing what it has already settled would be vanity. This document records what to reuse, what to converge on, what to reject, and how to credit it.

## 1. Reuse outright

| Thing | Why | How |
|---|---|---|
| **Idiomorph** (BSD-2) — the morph implementation Datastar and htmx both lean on | Morph correctness is where these libraries live or die; a proven implementation beats ours | Vendor it, pin the version, record the licence in `NOTICE`. Replace only if a measured need appears |
| **The SSE framing decisions** — event names, `data:` line structure, heartbeat, `Last-Event-ID` | Solved, and interoperable | Adopt shape; keep our HTML-document payload |
| **Patch modes** | Their list is more complete than ours was | Adopted below |
| **Modifier taxonomy** — debounce/throttle leading and trailing, intersect thresholds, listener scopes | Battle-worn naming; no reason to differ | Adopted in [04](04-client-attributes.md) |
| **Error-reporting style** — runtime errors that link to the page explaining the mistake | Genuinely better developer experience than a stack trace | Adopted as a requirement |
| **Their example gallery as a benchmark corpus** | An honest way to check whether a no-expression vocabulary can express real screens | See §4 |

## 2. Protocol gaps their SSE reference exposed

Reading Datastar's [SSE events reference](https://data-star.dev/reference/sse_events) found three genuine holes in ours. All three are now closed in [03 — Protocol](03-protocol.md):

1. **`before` and `after` modes.** We had `append`/`prepend` (inside the target) but no way to insert a *sibling*. Adding a row after a specific row needed a wrapper element that existed only to be a target.
2. **`replace` distinct from `morph`.** We conflated them under `outer`. They are different operations: morph preserves identity and focus; replace deliberately does not — sometimes you *want* the subtree destroyed (a widget being torn down, a form being reset).
3. **`namespace` (svg / mathml).** HTML parsed without a namespace hint cannot be patched into an `<svg>`; the elements come out as HTML elements with the right names and render as nothing. Any application with an inline chart or icon system hits this on day one.

Also adopted: `onlyIfMissing` on server signal patches (we had the client-side seed form only), and a view-transition *scope* selector rather than a bare boolean.

## 3. Converge where convergence is free

Where a choice is arbitrary, match Datastar so that knowledge, documentation and mental models transfer:

- Mode names (`outer`, `inner`, `replace`, `append`, `prepend`, `before`, `after`, `remove`).
- Signal deletion by `null`.
- Signals as camelCase on the wire.
- Duration syntax in modifiers (`500ms`, `1s`).
- "Signals are visible and user-modifiable; validate on the backend" — the same warning, in the same place, because it is the mistake everyone makes once.

Divergence should cost something. Where it does not, we match.

## 4. Their examples as our acceptance corpus

Datastar publishes a large example gallery. Each example is a question this specification must answer: *can a vocabulary with no client expressions express this screen?*

The exercise is a scored review, kept in `conformance/corpus/`:

| Verdict | Meaning | Action |
|---|---|---|
| **Same** | Expressible with the same number of attributes | Note it; add a fixture |
| **Server round trip** | Expressible, but a decision moves to the server | Note the cost in ms and bytes; add a fixture |
| **Island** | Needs ~20 lines of ordinary JS (animation, canvas, drag) | Fine by design; document the island |
| **Cannot** | Not expressible at all | **A finding.** Either the vocabulary is missing something adoptable, or the specification must state the limitation honestly |

That last row is why the exercise matters: it is the cheapest available test of whether removing the expression layer removed something people actually need. Any "cannot" that turns out to be common is grounds for revisiting the whole design — not for quietly shipping a `Function()`.

## 5. Datastar compatibility mode (optional, later)

A Växel server could speak Datastar's SSE protocol — `datastar-patch-elements` / `datastar-patch-signals` with their data-line keys — behind a flag. That would let a team drive a Växel .NET backend with the Datastar client, accepting `unsafe-eval` in exchange for its expressiveness.

Arguments for: the .NET server half is the valuable half, and this doubles its addressable users; it also proves the server API is not entangled with our own client.

Arguments against: two protocols to test, and a default that quietly reintroduces the thing this framework exists to avoid.

**Position (revised 2026-08-23, with the parity goal set):** build it **first**, as a measuring instrument — it turns their 20 conformance cases into our scoreboard on day one and proves the server API is independent of our own client. It remains a separate package (`Vaxel.Datastar`), never in the core, never the default, documented with the CSP consequence stated first. The core protocol must still never be shaped by what would make the adapter easier. See [13 — Test adoption](13-test-adoption.md).

## 6. What not to take

- **The expression layer.** `data-computed`, `data-effect`, `data-on-signal-patch`, `data-ref` and expression-valued attributes. This is the whole divergence; see [09](09-datastar-gap-analysis.md).
- **`data-persist` and `data-query-string` semantics with regex filters.** We adopt the features with explicit name lists instead: "persist everything matching `/foo/`" is how a token ends up in `localStorage`.
- **`data-custom-validity`.** Validity is a server judgement; a client rule that disagrees with the server generates bugs shaped like arguments.
- **Their free/pro split.** Not a technical decision, and not ours to copy.

## 7. Credit

The README, the specification and the package descriptions state plainly that the design derives from Datastar, with a link. Where a behaviour is adopted, the section says so. Where code is vendored, `NOTICE` carries the licence and the pinned commit — a real 40-character SHA, verifiable, not a plausible-looking prefix.

Prior art acknowledged alongside it: htmx (swap semantics and the hypermedia argument), Hotwire/Turbo (frames and streams), Unpoly (progressive enhancement as a contract), Phoenix LiveView and Laravel Livewire (server-driven UI, with the stateful bargain declined), and Alpine's CSP build (restricting a grammar to escape `eval`).
