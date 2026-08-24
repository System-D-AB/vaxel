# 09 — Gap analysis against Datastar's attribute reference

Datastar's [attribute reference](https://data-star.dev/reference/attributes) is the most complete statement of what this category of library does. This document walks **every** attribute it defines and records Växel's position: *has it*, *adding it* (closed in this revision of the spec), or *deliberately omitted* with the reason.

The dividing line is R2 — attribute values are data, never code. An attribute whose value is a JavaScript expression cannot be adopted without `Function()`. An attribute whose value is a *name*, a *literal*, a *duration*, a *selector* or a *media query* can be adopted exactly, and most of Datastar's genuinely useful surface turns out to be the latter.

## Free attributes

| Datastar | Purpose | Växel |
|---|---|---|
| `data-attr` | Set attributes from expressions | **Has** — `vx-attr:<name>="signal"`. Object form omitted (needs expressions); repeat the attribute instead |
| `data-bind` | Two-way form binding | **Has** — `vx-bind`. **Adding** `vx-bind-event` (which events sync) and `vx-bind-prop` (bind a property rather than value). File-to-base64 omitted: files ride normal multipart, which streams and does not bloat the signal bag |
| `data-class` | Toggle classes | **Has** — `vx-class:<name>="signal"` |
| `data-computed` | Client-side derived signals | **Omitted.** This is the expression language. The server computes derivations and patches the answer — the reduction that buys strict CSP |
| `data-effect` | Run an expression when signals change | **Omitted** (arbitrary JS). Islands subscribe to `vx:signals-changed` |
| `data-ignore` | Skip processing this subtree | **Adding** — `vx-ignore` (+ `vx-ignore-self`). Needed wherever third-party or user-authored HTML might contain `vx-` attributes |
| `data-ignore-morph` | Skip morphing this subtree | **Has** — `vx-preserve` (same behaviour, different name) |
| `data-indicator` | Signal that is true during a request | **Upgrading** — `vx-indicator="saving"` now *sets a signal*, so any element anywhere can react via `vx-attr:disabled="saving"`. Previously it only marked one target busy |
| `data-init` | Run an expression on insertion | **Partial by design** — `vx-signals` seeds values at first paint, `vx-trigger-load` fetches on insertion. **Adding** `vx-trigger-load-delay`. Running arbitrary code on init stays out |
| `data-json-signals` | Debug view of the signal bag | **Adding** — `vx-debug-signals` (development builds only) |
| `data-on` | Event listener running an expression | **Has, narrowed** — `vx-on` selects which event fires the declared request; `vx-signal-set:<event>` sets a literal. **Adding modifiers**: `prevent`, `stop`, `capture`, `passive`, `once`, `window`, `document`, `outside`, `delay`. Running an expression stays out |
| `data-on-intersect` | Fire on viewport intersection | **Has** — `vx-trigger-visible`. **Adding** `vx-trigger-visible-threshold` and `-exit` |
| `data-on-interval` | Fire on a timer | **Has** — `vx-poll` (1 s floor) |
| `data-on-signal-patch` (+`-filter`) | React to signal changes | **Omitted as an attribute** (runs JS); the `vx:signals-changed` event carries the changed keys for islands |
| `data-preserve-attr` | Keep named attributes across a morph | **Adding** — `vx-preserve-attr="open class"`. Without it, `<details open>` and island-added classes are silently reverted by every patch |
| `data-ref` | Put an element reference in a signal | **Omitted** — only useful to expressions; islands use `querySelector` |
| `data-show` | Toggle visibility | **Has** — `vx-show="signal"` |
| `data-signals` | Patch signals into state | **Has** — `vx-signals='{…}'` seed and `<vx-signals>` from the server. **Adding** `vx-signals-if-missing` (seed without clobbering a value the user already changed) |
| `data-style` | Set inline styles from expressions | **Adding, restricted** — `vx-style:<prop>="signal"`, applied through CSSOM. Documented as a sharp tool: prefer `vx-class`, never feed it untrusted values |
| `data-text` | Bind text content | **Has** — `vx-text="signal"` |

## Pro attributes

| Datastar | Purpose | Växel |
|---|---|---|
| `data-persist` | Signals in local/session storage | **Adding** — `vx-persist="railOpen theme"` (+ `vx-persist-session`). Explicit name list rather than regex filters; storing everything by default is how secrets leak into `localStorage` |
| `data-query-string` | Sync signals with the query string | **Adding** — `vx-url-sync="tab filter"` (+ `vx-url-sync-history`). This *reinforces* R3: state that belongs in the URL ends up in the URL, so the page route can render it |
| `data-match-media` | Signal from a media query | **Adding** — `vx-match-media:is-narrow="(max-width: 60rem)"`. The value is a media query string, not code; it gives responsive layout switching with no JS authoring |
| `data-scroll-into-view` | Scroll on update, with placement | **Upgrading** — the `scroll` directive gains `smooth`/`instant`, block and inline placement, and `focus` |
| `data-view-transition` | Set `view-transition-name` | **Adding** — `vx-transition-name="literal"` (literal only; the per-patch `transition="view"` already exists) |
| `data-custom-validity` | Expression-based form validity | **Omitted** — validity is a server judgement rendered as a notice; a client-side rule that disagrees with the server is a bug generator |
| `data-animate` | Reactive animation | **Omitted** — CSS transitions and the View Transition API cover the cases; anything more is an island |
| `data-on-raf` | Every animation frame | **Omitted** — an island's job |
| `data-on-resize` | Element resize | **Omitted for now.** `vx-match-media` covers layout breakpoints; element-level resize is an island until a real case appears |
| `data-replace-url` | Replace the URL from an expression | **Has** — server-side `replace-url` directive, plus `vx-url-sync` on the client |

## Cross-cutting things Datastar specifies that Växel had not

Three of these are more important than any individual attribute:

1. **Evaluation order.** Datastar states it: depth-first through the DOM, left-to-right within an element. Växel must state the same for binding application and for patch application, or two implementations will differ on which of two bindings wins.
2. **Attribute aliasing.** Datastar ships a `data-star-*` variant for conflicts and supports custom prefixes at bundle time. Växel adopts a configurable prefix (`vx-` default), which also matters for embedding a Växel app inside a host page that already uses another library.
3. **Error reporting with context.** Datastar logs runtime errors with a URL explaining the specific mistake. Växel should do the same — an unknown signal name, a target that is not an id, a trigger on a non-degradable element are all detectable, and a link beats a stack trace.

Also adopted: `__case` handling as a *rule* rather than a modifier — Växel signal names are camelCase on the wire and in bindings, full stop, because there is no expression syntax where a kebab name would be ambiguous.

## What the comparison shows

Of Datastar's 23 free attributes, Växel now covers **19** in spirit. The four omissions — `data-computed`, `data-effect`, `data-on-signal-patch`, `data-ref` — are all the same omission wearing different clothes: they exist to run expressions on the client. Everything else in Datastar's surface turns out to be declarative configuration that a data-only vocabulary expresses just as well.

Of the pro attributes, the three genuinely load-bearing ones for an application shell — persistence, query-string sync and media queries — are adopted, because none of them needs an evaluator.

The honest summary: **Datastar's attribute design is excellent and Växel should track it closely.** The single divergence is the expression layer, and the cost of that divergence is that the server must answer questions the client could otherwise answer locally — a round trip, in exchange for a policy with no `unsafe-eval` in it and one place where truth lives.
