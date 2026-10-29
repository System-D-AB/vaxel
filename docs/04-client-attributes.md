# 04 — Client attributes

The vocabulary is **closed**: an agent implementing this spec supports these attributes and no others. Every value is data — a URL, a selector, a signal name, a duration, a literal string, a media query — or a **pure expression over signals** in binding attributes (R2). Nothing is evaluated as JavaScript (`eval` or `Function()`).

Attributes are written in Razor through Tag Helpers ([05 — .NET API](05-dotnet-api.md)); the raw names below are the contract between the Tag Helpers and the agent.

**Prefix.** `vx-` by default, configurable at bundle time (`data-vx-` for validator-strict documents, or a custom prefix when embedding inside a host page that uses another library).

**Order.** Attributes are processed depth-first through the DOM, and left-to-right within an element. Patches apply in document order; directives apply after all patches. Two bindings writing the same property is a last-one-wins race and the agent reports it in development.

**Naming.** Signal names are camelCase everywhere — wire, seeds, bindings.

**Status (the `1.0` column).** This vocabulary is the full contract. The shipped 1.0 agent does not implement all of it yet; the `1.0` column states what is in the bundle today so a view is not designed around an inert attribute. The remaining triggers and modifiers are scheduled for `v1.1-triggers`; a conformance test (`VocabularyHonestyTests`) fails if the bundle and this column disagree.

- ✅ — implemented in the 1.0 agent.
- ⬜ — documented, **not in 1.0** (planned for v1.1).
- 🖥 — honoured server-side (Tag Helper / test kit), no agent behaviour.

---

## Triggers

| Attribute | 1.0 | Value | Notes |
|---|---|---|---|
| `vx-get` | ✅ | URL | Issue a GET. On `<a>`, defaults to the element's `href`. A `vx-get` **form** sends its fields as the query string |
| `vx-post` | ✅ | URL | POST. On `<form>`, defaults to `action`; also `vx-put`, `vx-patch`, `vx-delete` |
| `vx-on` | ✅ | chain | Declarative event stream pipeline (`source \| operator... \| sink`). Defaults when omitted: `click` on `<a>`/`<button>`, `submit` on `<form>` |
| `vx-target` | ✅ | `#id` | Advisory target sent as `VX-Target`; the server decides what it patches |
| `vx-swap` | ⬜ | mode | Advisory default mode for the response |
| `vx-vals-*` | ✅ | literal | `vx-vals-tab="submissions"` contributes a `tab=submissions` field — a body field for state-changing methods, a query-string field for GET. Literals only |
| `vx-include` | ⬜ | `#id` | Also send the fields of this form or container |
| `vx-confirm` | ✅ | text | Native confirm before sending. Plain text, never markup |
| `vx-indicator` | ✅ | signal name or `#id` | Sets that signal `true` (or toggles `.vx-loading` / `aria-busy` on `#id`) while a request from this trigger is in flight, `false` after — so **any** element can react through `vx-attr` / `vx-class` / `vx-show`, not just a nearby spinner |
| `vx-disable` | ✅ | — or `#id` | Disable this element (or `#id`) while in flight, preserving any pre-existing `disabled` state |
| `vx-progress` | ✅ | —, `<ms>`, `immediate`, or `off` | Top-of-viewport loading bar (`#vx-progress`, GitHub/NProgress style) while in flight. Defaults to a `100 ms` delay threshold so fast local responses never flicker; `0` / `immediate` starts instantly; `off` opts out |
| `vx-sync` | ✅ | policy | How concurrent requests from this trigger behave: `replace` (default) · `queue` · `drop` · `abort`. See [03 — Concurrency](03-protocol.md#concurrency) |
| `vx-encoding` | ✅ | `json` | Send an `application/json` body instead of form encoding, for endpoints using `[FromBody]` |

### Trigger chains (`vx-on`)

Instead of scattering a dozen separate modifier attributes across an element, `vx-on` expresses the event source, stream operators, and sink as one left-to-right pipeline separated by `|`:

```ebnf
chains    = chain { ";" chain } ;
chain     = source { "|" operator } [ "|" sink ] ;
source    = event [ scope ] | "interval" int | "visible" | "load" ;
scope     = "window" | "document" | "outside" ;
operator  = "debounce" int
          | "throttle" int
          | "delay" int
          | "once"
          | "distinct"
          | "key" keyname
          | "filter" expr
          | "prevent"
          | "stop" ;
sink      = "set" ident "=" expr
          | "showModal" idselector
          | "close" [ idselector ]
          | "focus" idselector ;
```

#### Sources & scopes

| Source / Scope | Status | Meaning |
|---|---|---|
| `<event>` (`input`, `click`, `keydown`, `submit`, `change`, …) | ✅ | Listen for DOM `<event>` on the element (or on `window` / `document` when scoped) |
| `<event> window` / `<event> document` | ✅ | Attach listener to `window` or `document` instead of the element — e.g. `vx-on="keydown window \| key Escape \| set open = false"` |
| `<event> outside` | ✅ | Fire when `<event>` occurs outside this element — click-away to dismiss (`vx-on="click outside \| set menuOpen = false"`) |
| `interval <ms>` | ✅ | Periodic timer while the element remains in the DOM. Enforces a **1000 ms floor** (`interval 200` runs at `1000 ms`). Prefer SSE |
| `visible` | ✅ | Fires when the element intersects the viewport (`IntersectionObserver`), commonly paired with `\| once` for lazy regions |
| `load` | ✅ | Fires once when the element is bound into the document |

#### Stream operators (with 50 ms frame marble semantics)

| Operator | Status | Marble (50 ms/frame) | Effect |
|---|---|---|---|
| `debounce <ms>` | ✅ | `a-b-c----\|` → `------c--\|` (`100`) | Coalesce bursts; emit only after `<ms>` of silence |
| `throttle <ms>` | ✅ | `abc---def\|` → `a-c---d-f\|` (`100`) | Leading-edge rate limit; drop events for `<ms>` after each emission |
| `delay <ms>` | ✅ | `a-b---c--\|` → `--a-b---c\|` (`100`) | Shift each passing event forward in time by `<ms>` |
| `once` | ✅ | `a-b-c----\|` → `a--------\|` | Pass the first reaching event, then silence the pipeline |
| `distinct` | ✅ | `a-a-b-b-a\|` → `a---b---a\|` | Drop consecutive duplicate values (`event.target.value`) |
| `key <KeyName>` | ✅ | — | Pass only when `KeyboardEvent.key === "<KeyName>"` (`Enter`, `Escape`, …; single-character keys match case-insensitively) |
| `filter <expr>` | ✅ | — | Pass only when `<expr>` evaluates to `true` via the P1 expression engine over signals + event `key`, `value`, `checked`, `ctrlKey`, `metaKey`, `altKey`, `shiftKey`, and `typing` (`true` when `event.target` is an `input`, `textarea`, `select`, or `[contenteditable]`) |
| `prevent` / `stop` | ✅ | — | Call `event.preventDefault()` / `event.stopPropagation()` at that stage in the pipeline |

#### Sinks

- **Default sink (request):** when a chain does not end with an explicit sink, a passing event fires the element's HTTP verb (`vx-get`, `vx-post`, `vx-put`, `vx-patch`, `vx-delete`), or its enclosing `<form>`'s HTTP verb when `vx-on` is placed on a form control (`<input name="q" vx-on="input | debounce 200 | distinct">`).
- **Signal sink (`set <ident> = <expr>`):** evaluates `<expr>` through the closed expression engine (with access to all signals plus `dataset.<key>` and event `key`, `value`, `checked`, `ctrlKey`, `metaKey`, `altKey`, `shiftKey`, `typing`) and writes the result into signal `<ident>` (`vx-on="keydown window | key Escape | set open = false"`).
- **Dialog & focus sinks (`showModal <#id>`, `close [<#id>]`, `focus <#id>`):** invoke native `<dialog>.showModal()` (focusing the first interactive control), `<dialog>.close()` (on `<#id>` or the enclosing `<dialog>`), or `el.focus()` on `<#id>`. Multiple chains on one element are separated by `;` (`vx-on="keydown window | key k | filter ctrlKey || metaKey | prevent | showModal #palette; keydown window | key / | filter !typing | prevent | focus #tree-filter"`).

> **Note on 1.0 draft modifier names:** The earlier draft listed standalone modifier attributes (`vx-debounce`, `vx-throttle`, `vx-delay`, `vx-once`, `vx-prevent`, `vx-stop`, `vx-window`, `vx-document`, `vx-outside`, `vx-trigger-load`, `vx-trigger-visible`, `vx-poll`, and `vx-signal-set:*`). All of those are unified into `vx-on` chains; the optional `vx-compat` shim translates legacy standalone attributes into `vx-on`.

**The degradation rule.** A trigger may only be placed on an element the browser would already action — `<a href>`, `<form>`, or a `<button>` inside a form — except `vx-trigger-load`, `vx-trigger-visible`, `vx-poll` and `vx-window`/`vx-document` listeners, which must target a region that also renders server-side on first paint. There is no attribute that makes a `<div>` clickable; that would break R3 and accessibility in one move.

---

## Regions, morph and lifecycle

| Attribute | 1.0 | Meaning |
|---|---|---|
| `vx-region` | 🖥 | This element is a patch target. Documents intent; scopes focus restoration; asserted by the test kit (no agent behaviour of its own) |
| `vx-preserve` | ✅ | Never morph this subtree — an island owns it |
| `vx-preserve-attr` | ✅ | Space-separated attribute names kept across a morph — `vx-preserve-attr="open class"`. Without this, `<details open>` closes and island-added classes vanish on every patch |
| `vx-ignore` | ⬜ | The agent does not process this element or its descendants at all — for user-authored or third-party HTML that may contain `vx-` attributes |
| `vx-ignore-self` | ⬜ | Ignore this element's own attributes but still process its children |
| `vx-overwrite-dirty` | ✅ | On an input: let an incoming value replace what the user has typed. Off by default — dirty input wins (see [02 — Morph](02-architecture.md#dirty-input-wins-over-an-incoming-value)) |
| `vx-transition` | ✅ | Wrap patches to this region (or triggered from this element) in `document.startViewTransition(...)` when supported and `prefers-reduced-motion: reduce` is not active, and toggle `.vx-swapping` / `.vx-settling` CSS transition classes |
| `vx-transition-name` | ✅ | Literal `view-transition-name` (`<custom-ident>`) applied as `el.style.viewTransitionName` for the View Transition API |
| `vx-sse` | ✅ | URL — open the document's single Server-Sent Events channel |

---

## Signal bindings

`vx-show`, `vx-class:<name>`, `vx-attr:<name>`, and `vx-text` accept either a signal name or a **closed expression over signals** (Rule R2). `vx-bind` and `vx-style:<prop>` take a single signal name only. Each binding runs inside a tracked `effect` from the signal store, re-evaluating on **only** the signals its expression actually read.

| Attribute | 1.0 | Effect |
|---|---|---|
| `vx-text` | ✅ | Element's `textContent` becomes the expression's value, or an interpolated string (`"Showing @{ count } rows"`). Never sets `innerHTML` |
| `vx-show` | ✅ | Element is shown when the boolean expression is `true`, and hidden (`hidden` attribute) when `false` or on any evaluation refusal |
| `vx-class:<name>` | ✅ | Class `<name>` present while the boolean expression evaluates to `true` |
| `vx-attr:<name>` | ✅ | Attribute `<name>` set to the expression's value; removed when `null`/`false`. Refused at render time on `href`, `src`, `action`, `formaction`, `style`, and `on*` |
| `vx-style:<prop>` | ✅ | Inline style property set through CSSOM from a single signal name. Sharp tool: prefer `vx-class`, and never bind untrusted values |
| `vx-bind` | ✅ | Two-way: input's value ⇄ signal name. Checkbox binds boolean, number input binds number |
| `vx-bind-event` | ⬜ | Which events sync the binding (default: `input` for text, `change` for the rest) |
| `vx-bind-prop` | ⬜ | Bind an element *property* rather than its value |
| `vx-signal-set:<event>` | ⬜ | On `<event>`, set the named signal to a literal or expression — `vx-signal-set:click="tab='submissions'"` |

### Closed expression grammar

- **Literals:** numbers (`42`, `3.14`), strings (`'…'` or `"…"` with `\n \t \r \\ \' \" \@` escapes), `true`, `false`, `null`.
- **References:** identifiers with dotted member access into the signal bag (`draft.name`, `tabs.length`). Member lookup uses own-property checks (`Object.prototype.hasOwnProperty`) and yields `null` for `__proto__`, `constructor`, and `prototype`.
- **Operators:** arithmetic `+ - * / %`, comparison `== != < <= > >=`, logical `&& || !` (strict boolean operands, short-circuiting), unary `+ -`, ternary `cond ? a : b`, parentheses `( … )`.
- **Functions (20-function pure whitelist):**
  - Math: `abs(n)`, `ceil(n)`, `floor(n)`, `round(n)`, `min(a, b)`, `max(a, b)`
  - String: `len(s)`, `trim(s)`, `toLowerCase(s)`, `toUpperCase(s)`, `contains(s, sub)`, `startsWith(s, prefix)`, `endsWith(s, suffix)`, `substring(s, start[, end])`, `replace(s, target, replacement)`, `encodeUri(s)`
  - Casts: `toBoolean(v)`, `toInteger(v)`, `toNumber(v)`, `toString(v)`
- **Framing:** in `vx-show`, `vx-class:*`, and `vx-attr:*`, the attribute value is the bare expression (`vx-show="tabs.length > 1"`). In `vx-text`, either a bare expression (`vx-text="draftSeq"`) or `@{ … }` interpolation inside text (`vx-text="Showing @{ count } rows"`, with `\@` escaping a literal `@`) is accepted.
- **Caps and refusals:** expressions are capped at **512 characters** and **32 nesting levels**. Any syntax error, unknown function, unknown top-level signal, type mismatch, or division by zero produces a structured refusal (`expressions.*`) and emits `vx:error`; in a boolean binding (`vx-show`, `vx-class:*`, boolean `vx-attr:*`), a refusal evaluates to `false` (disabling/hiding rather than silently enabling).
- **Deliberately excluded:** assignment, `[]` indexing, `??`, lambdas, loops, globals, `window`, DOM access, `new`, and locale/currency/date formatting (`fmt` stays server-rendered).

### Signal state

| Attribute | 1.0 | Effect |
|---|---|---|
| `vx-signals` | ✅ | JSON object seeding the store at first paint — `vx-signals='{"tab":"overview","railOpen":true}'` |
| `vx-signals-if-missing` | ✅ | Same, but does not overwrite a key that already exists (survives a patch that re-renders the shell) |
| `vx-persist` | ✅ | Space-separated signal names mirrored to `localStorage` — `vx-persist="railOpen theme"` |
| `vx-persist-session` | ✅ | As above, into `sessionStorage` |
| `vx-url-sync` | ✅ | Space-separated signal names mirrored into the query string — `vx-url-sync="tab filter"`. `replaceState` by default; `vx-url-sync-history` (⬜ not in 1.0) pushes instead |
| `vx-match-media:<name>` | ⬜ | Sets signal `<name>` from a media query — `vx-match-media:isNarrow="(max-width: 60rem)"` |
| `vx-debug-signals` | ⬜ | Renders the live signal bag into this element. Development builds only; a no-op in production |

**Persistence and URL sync are explicit name lists, never wildcards.** Persisting "everything" is how a session identifier ends up in `localStorage`; syncing "everything" is how a filter box's contents end up in someone's shared link.

`vx-url-sync` is the recommended way to hold shell state, because it reinforces R3: state that belongs in the URL ends up in the URL, so the full page route can render it. Setting or patching a signal to `null` or `undefined` deletes the key from the store, removes its entry from `vx-persist` / `vx-persist-session` storage, deletes its parameter from the `vx-url-sync` query string, and omits the key from the `VX-Signals` header so `[FromSignals]` binds the server model's default.

### One owner per property

A property must be owned by **either** the server **or** a signal — never both. If a fragment renders `disabled` and a binding also drives `disabled`, they can disagree: patches apply first, bindings re-run after, so the binding wins silently and the server's intent disappears.

- Server-owned: the property is rendered into the fragment, and no binding names it.
- Signal-owned: the server renders a sensible initial value, and thereafter only the signal changes it.

The development build warns when a patch sets a property that a binding on the same element also drives. In production the rule is the rule: bindings run last.

This is the only real hazard of keeping a reactive layer at all, and it is the price of the last mile.

### Why bindings use a closed interpreter

Because `eval` and `new Function` break `script-src 'self'`, while forcing every `tabs.length > 1` or `name == '' || saving` display fact through a server round trip or a redundant boolean signal hurts authoring ergonomics. By lifting a small, closed expression interpreter (305+ golden cases shared between the JS evaluator and the C# render-time validator), vaxel closes the Alpine ergonomics gap while keeping zero `unsafe-eval` and failing typos at build/render time when a signal schema is registered.

---

## Events the agent emits

| Event | When | Detail |
|---|---|---|
| `vx:before-request` | Before sending; cancellable | trigger, url, method |
| `vx:before-apply` | Document parsed, before patching | patches, directive |
| `vx:after-apply` | All patches applied and bound | patches, ignored |
| `vx:signals-changed` | After a signal patch or a binding write | changed keys |
| `vx:error` | Trigger, transport, parse, expression, or patch-apply failure | `{ reason, url, target, error, doc }` (`reason: 'trigger' \| 'transport' \| 'apply' \| 'expression' \| 'unknown-mode' \| 'missing-target'`, plus `fallback` on `'apply'`) |
| `vx:sse-state` | Stream connected, dropped, reconnecting | state, attempt |

Islands use `vx:before-apply` / `vx:after-apply` to suspend and resume; nothing else in the framework depends on these events, so an application may ignore them entirely.

## Error reporting

A detectable mistake — an unknown signal name in a binding, a target that is not an id, a trigger on a non-degradable element, an unknown swap mode, a pre-fetch trigger error (`reason: 'trigger'`), or a DOM morph error (`reason: 'apply'`) — is reported through `vx:error` with `{ reason, url, target, error, doc }`. When a pre-fetch exception occurs inside a trigger handler, the agent emits `vx:error` with `reason: 'trigger'` and falls back to native `<form>` submission or `<a href>` navigation. When a morph throws while applying a `<vx-patch>`, the agent retries that patch as a plain child replacement (`fallback: 'replace'`) and continues applying the remaining patches and `<vx-directive>` (`push-url`, `title`, `announce`, `focus`, `scroll`) so the user never sees a dead click. Silence is a bug; a stack trace is a missed teaching opportunity.

## Accessibility obligations of the agent

- Maintain one polite live region; announce `announce` directives through it.
- Restore focus after applying: to the `focus` directive's target if given, else to the trigger if it survives, else to the patched region's first focusable element or the region itself with `tabindex="-1"`.
- Set `aria-busy` on regions being patched, remove it after.
- Never move focus on a poll- or SSE-driven patch — those are not user-initiated.
