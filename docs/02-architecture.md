# 02 — Architecture

## Parts

```
┌─ browser ───────────────────────────────────────────────┐
│  document (server-rendered)                             │
│    ├── regions            [id] + vx-region              │
│    ├── triggers           vx-get / vx-post on a / form   │
│    └── bindings           vx-text / vx-class / vx-show   │
│                                                          │
│  agent  (~4 KB)          signals store (~1 KB)           │
│    intercept → request → apply patches → restore focus   │
│    one EventSource for pushed change                     │
└──────────────────────────────────────────────────────────┘
                    │  HTML patch document
                    │  (+ signal envelope)
┌─ ASP.NET Core ────┴──────────────────────────────────────┐
│  endpoint: Razor Page handler / MVC action / minimal API │
│      ↓                                                    │
│  IFragmentComposer → Partial · View · ViewComponent ·     │
│                      static RazorComponent · IHtmlContent │
│      ↓                                                    │
│  PatchBuilder → PatchResult : IResult                     │
│      ↓                                                    │
│  IPushChannel (SSE) for server-initiated change           │
└───────────────────────────────────────────────────────────┘
```

Nothing in the diagram holds state between requests except the browser's DOM, the URL and the signal store.

## Request lifecycle

1. **Trigger.** A `<a href>` or `<form action method>` carries `vx-get`/`vx-post`. Without the agent the browser follows it and gets a full page (R3). With the agent, default navigation is prevented.
2. **Collect.** The agent gathers: the form's fields if the trigger is inside one, any `vx-vals-*` on the trigger, and the current signal bag.
3. **Send.** The signal bag rides in the `VX-Signals` header for every method, so it never enters the URL or the body. The body is whatever ASP.NET already understands: form encoding for a form trigger, `multipart/form-data` when files are present, JSON only on an explicit `vx-encoding="json"`. `VX-Request: 1` marks the request, `VX-Sequence` orders it, and the antiforgery token rides its configured header.
4. **Render.** The endpoint reads signals through model binding, does its work through whatever the application's own service layer is, and composes fragments from any Razor unit.
5. **Respond.** A patch document (see [03 — Protocol](03-protocol.md)): zero or more fragments, each with a target and a mode, plus optional signal patches and directives (`focus`, `push-url`, `title`, `scroll`).
6. **Apply.** The agent checks the response's `VX-Sequence` against the newest it has seen for that trigger and discards a stale one. Otherwise it morphs each fragment into its target (dirty inputs keep their values), applies signal patches, re-binds, restores focus and scroll, updates history, and announces the change to assistive technology if the response says so.

Failure is not special: a refusal renders as a fragment into a notice region with its own status code, and step 6 is unchanged.

## Regions and targets

A **region** is an element with an `id` that the server is willing to replace. Marking it `vx-region` documents that intent, lets the agent scope focus restoration, and lets the test kit assert R3 for it.

Targets are ordinary CSS id selectors. The protocol deliberately does not support arbitrary selectors: a patch addressed to `.row` is ambiguous, cacheable by nobody, and impossible to test. One patch, one id.

## Morph

Fragments are merged into the existing DOM by a morph algorithm (Idiomorph-compatible), not assigned via `innerHTML`. Requirements:

- Elements are matched by `id` first, then by position and tag.
- Focus, caret position and text selection survive in an element that persists.
- `vx-preserve` on an element means: keep this node and its subtree untouched even if the incoming fragment differs — the escape hatch for third-party islands (charts, editors, maps, drag surfaces).
- `vx-preserve-attr` names attributes kept across the merge (`open` on a `<details>`, a class an island added).
- Scroll position of scrollable containers is restored after the merge.
- Morph is the default mode; `outer`, `replace`, `inner`, `append`, `prepend`, `before`, `after`, `remove` are explicit.

### Dirty input wins over an incoming value

*Revision v0.2 — previously undefined, and it is the single most common complaint in this category of library: the server re-renders a form while someone is typing in field three, and their work vanishes.*

An input is **dirty** when the user has changed it since it was last rendered or synced. During a morph:

1. A dirty input keeps its value, its caret and its selection. The incoming value is ignored.
2. A non-dirty input takes the incoming value — which is how a server-computed field (a total, a slug, a formatted number) updates.
3. `vx-overwrite-dirty` on an input opts out, for the cases where the server genuinely must win: a value the server normalised, a field reset after a successful submit.
4. Dirtiness resets when the element's own form successfully submits, or when the server sends the element inside a `replace` patch — `replace` means *destroy this*, and the spec honours that.

Checkboxes, radios, selects and `contenteditable` follow the same rule. A file input is never overwritten, because it cannot be.

### Nested regions

A region may contain another region. When a patch targets an outer region:

- inner regions are morphed as part of it, unless they carry `vx-preserve`;
- focus restoration scopes to the **innermost** region that contained focus, not the outer one;
- the parity harness treats each region independently — an inner region must still be reachable as a page on its own, which is what stops a screen from growing a fragment that exists nowhere else.

## What actually ships to the browser

Three pieces, not one. Stating them plainly because "it's just signals" is a claim this framework cannot make:

| Piece | Est. gzip | What it does |
|---|---|---|
| **Signal store** | ~1 KB | `get`/`set`/`patch`/`subscribe`, and the name-bound DOM updates |
| **Agent** | ~4–5 KB | Intercept triggers; build and send requests (headers, antiforgery, sequencing, `vx-sync` policy); parse patch documents; apply patches and directives; focus, scroll and history restoration; indicators; debounce/throttle; the SSE channel |
| **Morph** | ~4 KB | Idiomorph or an in-house equivalent — the merge algorithm that preserves identity, focus, caret and dirty input |
| **Total** | **~9–10 KB** | Budget cap **12 KB**; goal under 10 |

For comparison: Datastar ~11 KB, htmx ~16.6 KB, htmx + Alpine ~32 KB. These are estimates until a bundle exists, and the CI size gate is what turns them into facts.

What is **not** in there, and never will be: an expression evaluator, a template engine, a virtual DOM or reconciler, a component model, a client router, a state manager beyond the signal bag, a build step. Application **islands** (a drag surface, a chart) are the application's own code, not the framework's.

## Signals

The store is a small reactive map: `get`, `set`, `patch`, `subscribe`. Its only DOM job is name-bound updates — `vx-text="draftSeq"`, `vx-show="railOpen"`, `vx-class:is-active="tabIsSubmissions"`, `vx-attr:disabled="saving"`. There is no computation: a binding names a signal, nothing more.

Implementation is pluggable. The default is a ~1 KB store; any library exposing `signal`/`computed`/`effect` (for example `@preact/signals-core`) can back it. Computed signals exist for the agent's internal use, not for authoring — an application never declares a derivation on the client, because R2.

## Server push

One `EventSource` per document, opened by `vx-sse` on the shell. Frames carry the same patch document format as responses. Push is for change the user did not initiate: another user's action, a long-running job finishing, a queue count moving.

Constraints the spec imposes so this stays cheap: one channel per document (not per region); a heartbeat interval that survives proxy idle timeouts; reconnection with jittered backoff and a `Last-Event-ID` resume hint; and a documented expectation that hosts behind buffering proxies must disable response buffering for the stream route.

## Progressive enhancement

Because triggers are real links and forms, the no-agent path is the ordinary web. This is not a compatibility gesture: it is how R3 is verified, how the app remains testable with plain HTTP calls, and how screen-reader and keyboard behaviour stays sane. The spec forbids constructs that cannot degrade — there is no `vx-` attribute that invents a trigger on an element the browser would not have actioned.

## Islands

Where a screen genuinely needs client logic — drag reordering, a text editor, a canvas — the application mounts its own script inside an element marked `vx-preserve`. Växel guarantees the subtree is not morphed away and provides two lifecycle events (`vx:before-apply`, `vx:after-apply`) so the island can suspend and resume. Växel does not wrap, bundle or abstract these islands.
