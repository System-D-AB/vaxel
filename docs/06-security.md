# 06 — Security

Security is the reason this framework exists in the shape it does. The rules below are testable, and [07 — Testing](07-testing.md) says how.

## Content Security Policy

A vaxel application must be able to run under:

```
Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'
```

No `unsafe-eval`, no `unsafe-inline`, no nonces required for the framework's own operation.

This is achievable because of R2: **no attribute value is ever evaluated.** The agent contains no `eval`, no `new Function`, no `setTimeout(string)`, no `innerHTML` assignment from a string that did not come from the server's own parsed document. A conformance test greps the shipped bundle for those constructs and fails the build.

Consequences the framework accepts to keep this: no expression language, no inline event handlers, no `javascript:` URLs, no dynamic script injection, no `style` attributes generated from user input.

## Antiforgery

Every non-GET agent request carries the application's antiforgery token in its configured header. The agent reads the token from a `<meta name="…">` tag rendered by a Tag Helper, so it lives in the layout and not in each page. Token rotation on sign-in is handled by re-rendering the shell (`reload` directive), never by caching a stale token.

The framework refuses to send a state-changing request without a token and reports `vx:error`, rather than sending one and letting the server 403 — the failure is then visible in development instead of only in production logs.

## The signal trust boundary

**Signals are user input. Always. Without exception.**

They are visible in the DOM, editable in devtools, and replayable by any HTTP client. The framework therefore:

- binds them through a model binder that never throws on unknown or malformed keys (a tampered bag must not be a 500);
- refuses to make them a cache key;
- documents, in every place a developer meets them, that authorisation, pricing, identity, totals and permissions are computed server-side from server state.

A framework cannot prevent an application from writing `if (signals.IsAdmin)`. It can make that look as wrong as it is: the binder's XML docs say so, the analyser (roadmap) warns on identifier names matching an authorisation vocabulary, and the sample application demonstrates the correct pattern — the server derives, the client displays.

## Output encoding and sanitisation

Rendering is Razor's, so encoding is Razor's: `@value` encodes, `@Html.Raw` does not, and the framework adds nothing that bypasses either. Two specific obligations:

- **Signal seeds** (`vx-signals='{…}'`) are JSON serialised then HTML-attribute-encoded. The serialiser must escape `<`, `>`, `&` and quotes so a value containing `</script>` or `"` cannot break out of the attribute.
- **Patch documents** are parsed with the browser's own HTML parser into an inert document before being morphed. Script elements arriving in a patch are **not** executed by default; an application that deliberately wants a script in a fragment must opt in per response, and the docs must explain why that is usually the wrong instinct.

## Redirects

The `redirect` directive performs a full navigation. The agent accepts only same-origin URLs and site-relative paths; anything else is an error, not a navigation. Open-redirect protection on the server remains the application's job for the non-agent path.

## Server-Sent Events

- The stream endpoint authenticates and authorises like any other endpoint; a connection is not a bypass.
- `PushScope` is resolved server-side from the connection's identity — a client cannot ask to join another user's scope.
- A push carries rendered HTML for *that* recipient; the framework never broadcasts one rendering of a fragment to scopes with different permissions. The API shape (`PushAsync(scope, document)`) makes the per-scope rendering explicit rather than incidental.
- Streams are capped per identity (configurable) so a client cannot exhaust connections by opening documents.

## Denial of service and limits

| Limit | Default | Why |
|---|---|---|
| Signal bag size | 8 KB | Beyond this the agent omits signals and flags it; endpoints treat signals as optional anyway |
| Patch document size | application's response limits | No framework-specific cap; it is ordinary HTML |
| `vx-poll` minimum interval | 1 s | Prevents an accidental request storm from a typo |
| SSE streams per identity | 4 | A user with several tabs is normal; a script opening hundreds is not |
| Reconnect backoff | jittered, capped | A restarting server must not be thundering-herded |

## Threats the framework does not address

Session fixation, CSRF beyond token validation, rate limiting, tenant isolation, authorisation, secrets handling. These belong to the application; the framework's contribution is that it does not make any of them harder, and that its own request path is indistinguishable from an ordinary ASP.NET Core request path to the middleware that handles them.
