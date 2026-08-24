# 07 — Testing

A server-driven framework earns its keep by being testable without a browser. Almost everything here is an HTTP test.

## The page-parity invariant (R3)

> Every fragment target must also be reachable as a full page render.

`Vaxel.Testing` provides the harness, so the invariant is checked rather than asserted in prose:

```csharp
[Fact]
public async Task Every_region_is_reachable_as_a_page()
    => await VaxelParity.AssertAsync(client, routes: new[]
    {
        Route.Get("/apps/a_1?tab=submissions", region: "#pane"),
        Route.Get("/apps/a_1?tab=overview",    region: "#pane"),
        Route.Post("/apps/a_1?handler=SelectTab", region: "#tab-strip"),
    });
```

### What parity means, exactly

*Revision v0.2 — "normalised for whitespace and the attributes the agent adds" was too vague to implement, and this is the invariant the whole design rests on.*

For each route the harness issues the request twice — once with `VX-Request: 1`, once without — then compares **only the named region**, extracted from each response by its `id`:

1. **Extract.** From the page response, the subtree of `#region`. From the patch response, the content of the `<vx-patch target="#region">` element. A patch that never targets the region fails immediately.
2. **Normalise**, in this order: collapse insignificant whitespace between elements; sort attributes by name; drop attributes in the reserved `vx-runtime-*` namespace (state the agent writes: `aria-busy`, `vx-busy`, dirtiness markers); drop comment nodes; normalise self-closing forms and attribute quoting.
3. **Compare** the resulting trees for structural equality: same elements, same order, same attributes, same text.

Deliberately **not** normalised — a difference in any of these is a real failure, not noise:

- element ids, `name`s, `href`s and form `action`s;
- ARIA attributes, `role`, `tabindex`;
- text content, including empty-state wording;
- the presence or absence of a child element.

**Legitimate differences are declared, not inferred.** A region whose page render genuinely differs — a first-paint skeleton, a server-timing comment, an absolute URL where the patch emits a relative one — declares it:

```csharp
Route.Get("/apps/a_1?tab=submissions", region: "#pane")
     .Allowing(Difference.TextIn("#render-time"))
```

Each allowance is a line someone has to write and a reviewer can question. A harness that guesses what is noise eventually excuses the bug it was meant to catch.

A route that renders content reachable *only* through a patch fails, and that failure is the point.

This one test protects deep links, the back button, no-JS operation, crawlability and screen-reader navigation, none of which anyone remembers to test individually.

## Patch assertions

```csharp
var patch = await client.PatchAsync("/apps/a_1?handler=AddField", signals: new { tab = "pages" });

patch.ShouldHaveStatus(200);
patch.ShouldPatch("#canvas").WithMode(SwapMode.Morph).ContainingElement("[data-field='full_name']");
patch.ShouldPatch("#status-strip");
patch.ShouldSetSignal("draftSeq", 149);
patch.ShouldDirect(d => d.Focus == "#field-full_name" && d.PushUrl!.Contains("/pages/"));
patch.ShouldNotPatch("#rail");
```

The parser is the same one the agent uses conceptually — patch documents are ordinary HTML, so assertions read like HTML, not like JSON paths.

## Refusal contract

```csharp
patch.ShouldHaveStatus(409)
     .ShouldPatch("#notices")
     .ContainingText("publish.stale_draft")
     .ContainingText("Review the draft and raise a new proposal");
```

The framework asserts the *shape* (a refusal renders into a region, carries a status, announces itself); the application asserts the words.

## Composer tests

`FakeFragmentComposer` records what was rendered without spinning the Razor engine, so handler logic can be unit-tested:

```csharp
var composer = new FakeFragmentComposer()
    .Returning("_Canvas", "<section id='canvas'>stub</section>");

var result = await page.OnPostAddFieldAsync("a_1", new ShellSignals());

composer.ShouldHaveRendered(Partial("_Canvas"), Component<StatusStrip>());
```

Integration tests use the real composer through `WebApplicationFactory`, which is where the Razor units and their models are actually exercised.

## Agent conformance suite

The client agent is specified, so it is conformance-tested rather than trusted. The suite is a set of fixture pairs — an input document plus a patch document plus the expected resulting DOM — runnable against any implementation of this spec:

```
conformance/
  001-morph-preserves-focus/{before.html, patch.html, expected.html, assert.json}
  002-append-mode/…
  003-remove-missing-target-is-ignored/…
  004-signals-merge-and-delete/…
  005-directive-order-patches-then-focus/…
  006-preserve-subtree-untouched/…
  007-unknown-mode-is-an-error/…
  008-protocol-mismatch-falls-back/…
```

`assert.json` covers what the DOM diff cannot: which events fired, where focus landed, what the live region announced, whether a fallback navigation occurred.

Two suites are non-negotiable for a release:

- **CSP suite** — the shipped bundle contains no `eval(`, no `new Function(`, no string `setTimeout`/`setInterval`; a browser test loads a page under the strict policy from [06](06-security.md) with CSP violation reporting on and asserts zero reports.
- **Accessibility suite** — after a patch, focus is where the spec says; `aria-busy` is set and cleared; the live region announces; a poll- or SSE-driven patch never steals focus.

## Browser tests

Kept deliberately few, because everything above is cheaper. What genuinely needs a browser:

- morph behaviour under real focus, caret and IME conditions;
- the View Transition path;
- SSE reconnection after a dropped connection;
- keyboard navigation through a patched shell.

The rule for the framework's own suite, and the recommendation for applications: **browser tests drive the real server.** A browser suite that loads fixture HTML tests the fixtures, and will pass after the application is deleted.

## What a consuming application should test

1. The parity harness over every region (one test, whole app).
2. Handler logic with the fake composer (fast, no Razor).
3. Two or three end-to-end journeys in a real browser against the real server.
4. Its own refusal wording and design-system rendering.

Notably absent: tests for the swap mechanics, focus restoration, history or signal merging. Those belong to the framework's conformance suite, and an application that finds itself testing them has found a framework bug.
