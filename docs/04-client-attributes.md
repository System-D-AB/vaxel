# 04 — Client attributes

The vocabulary is **closed**: an agent implementing this spec supports these attributes and no others. Every value is data — a URL, a selector, a signal name, a duration, a literal string, a media query. Nothing is parsed as an expression, and there is no escape syntax that would make one (R2).

Attributes are written in Razor through Tag Helpers ([05 — .NET API](05-dotnet-api.md)); the raw names below are the contract between the Tag Helpers and the agent.

**Prefix.** `vx-` by default, configurable at bundle time (`data-vx-` for validator-strict documents, or a custom prefix when embedding inside a host page that uses another library).

**Order.** Attributes are processed depth-first through the DOM, and left-to-right within an element. Patches apply in document order; directives apply after all patches. Two bindings writing the same property is a last-one-wins race and the agent reports it in development.

**Naming.** Signal names are camelCase everywhere — wire, seeds, bindings. There is no case-conversion modifier because there is no expression syntax that would make a name ambiguous.

---

## Triggers

| Attribute | Value | Notes |
|---|---|---|
| `vx-get` | URL | Issue a GET. On `<a>`, defaults to the element's `href` |
| `vx-post` | URL | POST. On `<form>`, defaults to `action`; also `vx-put`, `vx-patch`, `vx-delete` |
| `vx-on` | event name | Which event fires the request. Defaults: `click` on `<a>`/`<button>`, `submit` on `<form>`, `change` on `<select>`/checkbox/radio, `input` on text inputs |
| `vx-target` | `#id` | Advisory target sent as `VX-Target`; the server decides what it patches |
| `vx-swap` | mode | Advisory default mode for the response |
| `vx-vals-*` | literal | `vx-vals-tab="submissions"` contributes a `tab=submissions` field to the request body, exactly as a hidden input would. Literals only |
| `vx-include` | `#id` | Also send the fields of this form or container |
| `vx-confirm` | text | Native confirm before sending. Plain text, never markup |
| `vx-indicator` | signal name | Sets that signal `true` while a request from this trigger is in flight, `false` after — so **any** element can react through `vx-attr` / `vx-class` / `vx-show`, not just a nearby spinner |
| `vx-disable` | — or `#id` | Disable this element (or that one) while in flight |
| `vx-sync` | policy | How concurrent requests from this trigger behave: `replace` (default) · `queue` · `drop` · `abort`. See [03 — Concurrency](03-protocol.md#concurrency) |
| `vx-encoding` | `json` | Send an `application/json` body instead of form encoding, for endpoints using `[FromBody]` |

### Trigger modifiers

| Attribute | Effect |
|---|---|
| `vx-debounce` / `vx-throttle` | ms; coalesce or rate-limit. `vx-debounce-leading` fires on the leading edge instead |
| `vx-delay` | ms to wait before sending |
| `vx-once` | Fire at most once, then the trigger is inert |
| `vx-prevent` / `vx-stop` | `preventDefault` / `stopPropagation` |
| `vx-capture` / `vx-passive` | Listener options |
| `vx-window` / `vx-document` | Attach the listener to `window`/`document` instead of the element — how a shell binds Escape to close a panel |
| `vx-outside` | Fire when the event occurs *outside* this element — click-away to dismiss |
| `vx-trigger-load` (+ `vx-trigger-load-delay`) | Fire when the element enters the document (lazy regions) |
| `vx-trigger-visible` (+ `-threshold`, `-exit`, `-once`) | Fire on viewport intersection; threshold is a number 0–1 |
| `vx-poll` | ms; repeat while the element exists. 1 s floor. Prefer SSE |

**The degradation rule.** A trigger may only be placed on an element the browser would already action — `<a href>`, `<form>`, or a `<button>` inside a form — except `vx-trigger-load`, `vx-trigger-visible`, `vx-poll` and `vx-window`/`vx-document` listeners, which must target a region that also renders server-side on first paint. There is no attribute that makes a `<div>` clickable; that would break R3 and accessibility in one move.

---

## Regions, morph and lifecycle

| Attribute | Meaning |
|---|---|
| `vx-region` | This element is a patch target. Documents intent; scopes focus restoration; asserted by the test kit |
| `vx-preserve` | Never morph this subtree — an island owns it |
| `vx-preserve-attr` | Space-separated attribute names kept across a morph — `vx-preserve-attr="open class"`. Without this, `<details open>` closes and island-added classes vanish on every patch |
| `vx-ignore` | The agent does not process this element or its descendants at all — for user-authored or third-party HTML that may contain `vx-` attributes |
| `vx-ignore-self` | Ignore this element's own attributes but still process its children |
| `vx-overwrite-dirty` | On an input: let an incoming value replace what the user has typed. Off by default — dirty input wins (see [02 — Morph](02-architecture.md#dirty-input-wins-over-an-incoming-value)) |
| `vx-transition-name` | Literal `view-transition-name` for the View Transition API |
| `vx-sse` | URL — open the document's single Server-Sent Events channel |

---

## Signal bindings

Every binding names **one signal**. No operators, no comparisons, no negation, no dotted paths into computed shapes — if a view needs a condition, the server sends the answer as a signal or a fragment.

| Attribute | Effect |
|---|---|
| `vx-text` | Element's text content becomes the signal's value, stringified |
| `vx-show` | Element is hidden (`hidden` attribute) when the signal is falsy |
| `vx-class:<name>` | Class `<name>` present while the signal is truthy |
| `vx-attr:<name>` | Attribute `<name>` set to the signal's value; removed when `null`/`false` |
| `vx-style:<prop>` | Inline style property set through CSSOM. Sharp tool: prefer `vx-class`, and never bind untrusted values |
| `vx-bind` | Two-way: input's value ⇄ signal. Checkbox binds boolean, number input binds number |
| `vx-bind-event` | Which events sync the binding (default: `input` for text, `change` for the rest) |
| `vx-bind-prop` | Bind an element *property* rather than its value |
| `vx-signal-set:<event>` | On `<event>`, set the named signal to a literal — `vx-signal-set:click="tab=submissions"` |

### Signal state

| Attribute | Effect |
|---|---|
| `vx-signals` | JSON object seeding the store at first paint — `vx-signals='{"tab":"overview","railOpen":true}'` |
| `vx-signals-if-missing` | Same, but does not overwrite a key that already exists (survives a patch that re-renders the shell) |
| `vx-persist` | Space-separated signal names mirrored to `localStorage` — `vx-persist="railOpen theme"` |
| `vx-persist-session` | As above, into `sessionStorage` |
| `vx-url-sync` | Space-separated signal names mirrored into the query string — `vx-url-sync="tab filter"`. `replaceState` by default; `vx-url-sync-history` pushes instead |
| `vx-match-media:<name>` | Sets signal `<name>` from a media query — `vx-match-media:isNarrow="(max-width: 60rem)"` |
| `vx-debug-signals` | Renders the live signal bag into this element. Development builds only; a no-op in production |

**Persistence and URL sync are explicit name lists, never wildcards.** Persisting "everything" is how a session identifier ends up in `localStorage`; syncing "everything" is how a filter box's contents end up in someone's shared link.

`vx-url-sync` is the recommended way to hold shell state, because it reinforces R3: state that belongs in the URL ends up in the URL, so the full page route can render it.

### One owner per property

A property must be owned by **either** the server **or** a signal — never both. If a fragment renders `disabled` and a binding also drives `disabled`, they can disagree: patches apply first, bindings re-run after, so the binding wins silently and the server's intent disappears.

- Server-owned: the property is rendered into the fragment, and no binding names it.
- Signal-owned: the server renders a sensible initial value, and thereafter only the signal changes it.

The development build warns when a patch sets a property that a binding on the same element also drives. In production the rule is the rule: bindings run last.

This is the only real hazard of keeping a reactive layer at all, and it is the price of the last mile.

### Why bindings are this thin

Because the alternative is an expression language, and an expression language is either interpreted (a parser, a grammar, and a second evaluator to keep in parity) or compiled (`Function()`, and `unsafe-eval` forever). The server already knows whether the tab is active, whether the button should be disabled, and what the count is. Sending `{"tabIsSubmissions": true}` is one JSON key; sending `$tab === 'submissions'` is a language.

---

## Events the agent emits

| Event | When | Detail |
|---|---|---|
| `vx:before-request` | Before sending; cancellable | trigger, url, method |
| `vx:before-apply` | Document parsed, before patching | patches, directive |
| `vx:after-apply` | All patches applied and bound | patches, ignored |
| `vx:signals-changed` | After a signal patch or a binding write | changed keys |
| `vx:error` | Transport, parse or protocol failure | reason, response, doc link |
| `vx:sse-state` | Stream connected, dropped, reconnecting | state, attempt |

Islands use `vx:before-apply` / `vx:after-apply` to suspend and resume; nothing else in the framework depends on these events, so an application may ignore them entirely.

## Error reporting

A detectable mistake — an unknown signal name in a binding, a target that is not an id, a trigger on a non-degradable element, an unknown swap mode — is reported through `vx:error` and logged with a link to the section of this specification that explains it. Silence is a bug; a stack trace is a missed teaching opportunity.

## Accessibility obligations of the agent

- Maintain one polite live region; announce `announce` directives through it.
- Restore focus after applying: to the `focus` directive's target if given, else to the trigger if it survives, else to the patched region's first focusable element or the region itself with `tabindex="-1"`.
- Set `aria-busy` on regions being patched, remove it after.
- Never move focus on a poll- or SSE-driven patch — those are not user-initiated.
