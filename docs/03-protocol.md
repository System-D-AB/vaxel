# 03 — Protocol

The wire format is HTML. A patch response is a document the browser can parse with its own parser, and that a human can read in devtools without a decoder.

Protocol version: `1`. Negotiated by header; a client and server that disagree on the major version must fall back to full-page navigation rather than guess.

## Request

| Header | Value | Meaning |
|---|---|---|
| `VX-Request` | `1` | This is an agent request; respond with a patch document, not a page |
| `VX-Protocol` | `1` | Protocol major version the agent speaks |
| `VX-Target` | `#pane` | The trigger's declared target, advisory — the server decides |
| `VX-Url` | `/apps/a_1?tab=overview` | The document's current URL, so the server can compute history |
| `VX-Signals` | compact JSON | **The signal bag. Always a header, for every method** |
| `VX-Sequence` | `17` | Monotonic per trigger; lets the agent discard a stale response |
| *(antiforgery)* | token | Header name is the application's configured one |

### Signals travel in a header. Always.

*Revision v0.2 — this replaced a design where GET put signals in a `vx-signals` query parameter, which contradicted the caching rule below: a query parameter **is** part of the cache key, so every distinct signal bag would have minted its own cache entry.*

```
VX-Signals: {"tab":"submissions","filter":"kyc"}
```

One rule for every method, and the request body is then free to be whatever ASP.NET already understands.

If the bag exceeds `MaxSignalsBytes` (default 8 KB) the agent omits it and sets `VX-Signals-Omitted: 1`. Endpoints must therefore treat signals as **advisory** input — never as required input, and never as the only source of something the response needs.

### Bodies are ordinary

*Revision v0.2 — this replaced a `{"signals":…,"values":…}` JSON envelope, which silently broke `[BindProperty]`, `ModelState` and per-field validation messages: Razor Pages binds forms from form-encoding, not from a JSON body.*

| Trigger | Content type | Why |
|---|---|---|
| A `<form>` | `application/x-www-form-urlencoded` | ASP.NET binding, validation and `ModelState` work untouched |
| A `<form>` with `enctype="multipart/form-data"` | `multipart/form-data` | Files stream instead of being base64'd |
| A non-form trigger with `vx-vals-*` | `application/x-www-form-urlencoded` | Same binder, same code, one less shape to test |
| An explicit `vx-encoding="json"` | `application/json` | For endpoints that genuinely want `[FromBody]` |

The framework's promise is that .NET's own machinery keeps working; a bespoke envelope would have broken exactly the part developers use most.

### Concurrency

*Revision v0.2 — previously unspecified, which left the most common real-world race undefined.*

Every request carries `VX-Sequence`, monotonic per trigger. The agent applies a response only if its sequence is the newest seen for that trigger; older ones are discarded after `vx:after-apply` reports them. Per-trigger policy is set with `vx-sync`:

| Policy | Behaviour |
|---|---|
| `replace` (default) | A new trigger aborts the in-flight request and supersedes it — what a search box wants |
| `queue` | Requests run in order, one at a time — what a sequence of edits wants |
| `drop` | While one is in flight, further triggers are ignored — what a submit button wants |
| `abort` | The new trigger aborts and does **not** send — an explicit cancel control |

Two *different* triggers patching one region are not sequenced against each other; last response wins. Where that matters, the server should patch both regions from whichever request it handles, so the DOM is consistent by construction rather than by client bookkeeping.

An SSE patch never supersedes an in-flight request's target: if a push arrives for a region with a pending request, it is applied, and the pending response then overwrites it. The server is the arbiter of truth in both cases.

## Response

Status codes are ordinary. `200` for an applied patch, `4xx`/`5xx` for refusals and faults — a non-2xx response still carries a patch document, so failures render like everything else.

| Header | Meaning |
|---|---|
| `VX-Protocol: 1` | Server's protocol major version |
| `Content-Type: text/vnd.vaxel-patch+html` | Distinguishes a patch document from a page (`text/html` is accepted for tolerant clients) |

Body — top-level `<vx-patch>` elements, optionally one `<vx-signals>` and one `<vx-directive>`:

```html
<vx-patch target="#pane" mode="morph">
  <section id="pane" vx-region>…</section>
</vx-patch>

<vx-patch target="#tab-strip" mode="morph">
  <nav id="tab-strip">…</nav>
</vx-patch>

<vx-signals>{"tab":"submissions","draftSeq":149}</vx-signals>

<vx-directive push-url="/apps/a_1?tab=submissions"
              focus="#filter"
              title="Submissions — Acme"
              announce="Submissions loaded, 24 rows" />
```

### `<vx-patch>`

| Attribute | Required | Values |
|---|---|---|
| `target` | yes | a single `#id` selector |
| `mode` | no | `morph` (default) · `outer` · `replace` · `inner` · `append` · `prepend` · `before` · `after` · `remove` |
| `namespace` | no | `html` (default) · `svg` · `mathml` — the parser context for the fragment |
| `transition` | no | `none` (default) · `view`; `transition-scope` names a selector to scope the transition to |

Mode meanings, because two pairs are easy to confuse:

| Mode | Effect |
|---|---|
| `morph` | Merge into the target, preserving identity, focus, caret and scroll. The default, and what you want for anything the user might be interacting with |
| `outer` | Morph the target element itself, attributes included |
| `replace` | Destroy the target and put the fragment in its place — deliberately losing state (tearing a widget down, resetting a form) |
| `inner` | Morph the target's children only |
| `append` / `prepend` | Insert inside the target, at the end / start |
| `before` / `after` | Insert as a sibling of the target — how a row lands next to a specific row without inventing a wrapper to aim at |
| `remove` | Delete the target. Carries no content |

`namespace` matters more than it looks: HTML parsed without a namespace hint cannot be patched into an `<svg>` — the nodes come out as HTML elements with the right names and render as nothing at all.

`remove` carries no content. Unknown modes are a protocol error: the agent applies nothing from that element and reports it (see Errors).

A patch whose target is not present in the document is **ignored, not an error** — regions legitimately differ between screens. The agent reports ignored patches through an event so tests can catch drift.

### `<vx-signals>`

One JSON object, merged into the store (shallow, key by key). `null` deletes a key. Values must be JSON scalars, arrays or objects — never functions, never strings that the client will interpret.

`<vx-signals only-if-missing>` patches only keys the store does not already hold — so re-rendering a shell cannot clobber a filter box the user has since typed into.

### `<vx-directive>`

| Attribute | Effect |
|---|---|
| `push-url` | `history.pushState` to this URL |
| `replace-url` | `history.replaceState` |
| `focus` | Move focus to this `#id` after applying |
| `scroll` | `#id` to scroll into view, or `top`. Optional placement: `scroll-behavior` (`smooth` · `instant`), `scroll-block` / `scroll-inline` (`start` · `center` · `end` · `nearest`), `scroll-focus` (`1` to focus after scrolling) |
| `title` | Set `document.title` |
| `announce` | Text for the polite live region the agent maintains |
| `redirect` | Full navigation to this URL (the agent stops applying) |
| `reload` | `1` — full page reload (used after sign-out, version bumps) |

At most one `<vx-directive>` per document. Directives apply after all patches.

## Refusals

A refusal is a normal response with a non-2xx status and a rendered notice:

```html
<vx-patch target="#notices" mode="append">
  <div class="notice notice--blocking" role="alert">
    <code>publish.stale_draft</code>
    <p>The draft moved on after this proposal was raised.</p>
    <p>Review the draft and raise a new proposal.</p>
  </div>
</vx-patch>
<vx-directive focus="#notices" announce="Request refused: the draft moved on." />
```

The framework provides the builder; the application provides the partial. Refusal *rendering* is the application's design; refusal *shape* is the framework's contract.

## History and popstate

*Revision v0.2 — `push-url` was specified with no statement of what happens when the user goes back.*

On `popstate` the agent:

1. Restores the signals it persisted with that history entry (the values of any `vx-url-sync` names, read back from the URL).
2. Issues a `GET` for the entry's URL with `VX-Request: 1` and `VX-History: restore`.
3. Applies the returned patches, then restores the scroll position recorded for that entry.

The server sees an ordinary request for an ordinary URL, so the same handler serves forward navigation, a back button, a refresh and a shared link — one code path, which is the invariant again.

Two rules make this behave:

- **A history entry must be renderable on its own.** `push-url` to a URL whose page route does not exist is a defect the parity harness catches.
- **No client-side snapshot cache.** Restoring from a cached DOM is how stale screens and lost authorisation states happen; a round trip is cheap and correct. Applications that want the speed should cache on the server, where invalidation is possible.

If the restore request fails, the agent performs a full navigation to that URL rather than leaving the user on a screen whose address bar lies.

## Server-Sent Events

`GET /…/stream` with `Accept: text/event-stream`. Each frame's `data` is a patch document, identical in format to a response body:

```
id: 42
event: vx-patch
data: <vx-patch target="#queue-count" mode="inner"><span id="queue-count">3</span></vx-patch>

: heartbeat
```

- `event: vx-patch` — apply the document.
- `event: vx-reload` — the agent performs a full reload (deploy, protocol bump).
- Comment heartbeats at a configurable interval (default 20 s) keep proxies from closing the stream.
- The agent reconnects with jittered backoff and sends `Last-Event-ID`; the server may replay or ignore it, but must not assume the client saw anything.

## Caching

`GET` patch responses are cacheable exactly like pages: `ETag` and `Cache-Control` behave normally, and `Vary: VX-Request` distinguishes a patch from a page at the same URL. `POST` responses are never cached.

**Signals are never a cache key** — and because they now travel in a header rather than the URL, that statement is enforceable rather than aspirational. Two consequences:

- The framework marks a response `Cache-Control: private, no-store` **automatically** when the handler actually read signals (`[FromSignals]` bound, or `ISignalReader` touched). A response that varies by signals is therefore never served to someone with a different bag. Applications may override deliberately.
- State that *should* be cacheable and shareable belongs in the URL, not in signals. `vx-url-sync` exists precisely to move it there, and the parity invariant then guarantees the URL renders it.

## Errors

Transport failure, an unparseable body, a protocol-version mismatch, or an unknown mode: the agent applies nothing from the offending document, emits `vx:error` with a reason, and — if the trigger came from a real link or form — falls back to native navigation so the user still gets somewhere. Silent failure is a protocol violation.
