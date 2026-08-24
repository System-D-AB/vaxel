# 01 — Principles

## Goal
Let a .NET developer build an application that *feels* like a modern reactive UI — partial updates, live regions, no full-page flashes — while writing only C# and Razor, and while keeping a strict Content Security Policy.

## The four rules

Everything in this specification is derivable from these. If a proposed feature contradicts one, the feature is wrong.

**R1 — The server renders everything.**
No client-side templates, no client render tree. HTML is produced by Razor: Pages, Views, Partials, ViewComponents, Tag Helpers, or statically-rendered Razor Components. The client's only DOM authorship is applying what the server sent and toggling attributes bound to signals.

**R2 — Attribute values are data, never code.**
No attribute is ever evaluated. There is no expression language on the client, therefore no `Function()`, therefore no `unsafe-eval`. Where a decision needs computing, the server computes it and sends the result — as a fragment or as a signal value.

**R3 — Every fragment target is also reachable as a full page.**
A patch is an optimisation of a page, never a capability the page lacks. This is what makes deep links, the back button, no-JS operation and screen readers work by construction rather than by discipline, and it is enforced by a test (see [07 — Testing](07-testing.md)).

**R4 — The server holds no per-user UI state.**
State lives in one of exactly three places: the URL (anything shareable, bookmarkable or navigable), the server's own durable store (domain data), or signals (ephemeral client state that the client sends and the server may patch). No sockets holding sessions, no server-side component trees between requests.

## What signals are, and are not

A **signal** is a named value held by the client, serialised as JSON, sent with requests, and patchable by the server.

```
signals = { "tab": "submissions", "filter": "kyc", "railOpen": true, "draftSeq": 148 }
```

Signals **are**:
- UI state that would be silly to keep server-side (which panel is open, a filter box's text, a confirmation's armed state);
- request parameters that many endpoints want without re-declaring them;
- a channel for the server to push scalar updates without re-rendering a fragment (`draftSeq` ticks; a counter increments).

Signals **are not**:
- expressions — no signal value is ever interpreted as code;
- authority — the server never trusts a signal for a permission, an identity, a price or a total. Signals are user-editable by definition;
- a render tree — bindings toggle text, attributes, classes and visibility on elements the *server* produced.

**Derivations are computed on the server.** Where Datastar writes `data-show="$count > 3"`, vaxel patches a signal or a fragment because the server already knows the answer. This is the single reduction that removes the expression language, and with it the CSP problem, the parser, the second engine and the parity test suite.

## Non-goals

- **Client-side routing.** Browser navigation is the model. `PushUrl` updates history; the back button does what it always did.
- **Offline.** A vaxel app is online by definition; its rendering authority is on the server.
- **A component model on the client.** If a screen truly needs local component logic (a drag-reorder canvas, a chart, a map), write a small island in ordinary JS or TypeScript and mount it. vaxel gets out of the way and preserves its DOM across morphs.
- **Replacing Blazor.** Static Razor Components are supported as fragment sources; interactive Blazor is a different product with a different bargain.
- **Universal adoption of one swap library.** The client agent is replaceable behind the Tag Helper layer; the protocol is the contract.

## Design stances worth stating

- **Morph by default.** Fragments are merged into the live DOM, not assigned over it, so caret position, text selection, focus, scroll and `contenteditable` survive updates. Replacement is opt-in.
- **Refusals are a first-class response.** An operation that cannot proceed returns a rendered notice with a code, a reason and a remedy, in the same shape as a success. Error handling is not a separate code path or a separate visual language.
- **One long-lived connection at most.** Request/response for everything the user initiates; a single Server-Sent Events channel for change the user did not initiate. This keeps proxies, connection budgets and scale-to-zero deployments boring.
- **No build step required.** A `<script>` tag and Tag Helpers are enough to start. Bundling is available, never mandatory.
- **The protocol is versioned; the client is replaceable.** Views emit Tag Helpers, Tag Helpers emit attributes, the agent reads attributes. Swapping the agent — or driving the same protocol from htmx plus a morph extension — must not touch a single `.cshtml`.
