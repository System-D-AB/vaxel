# Recipe 01 — Search as you type, with suggestions

**What the user does:** types in a box; a list of suggestions appears under it after they pause; arrow keys and Enter work; the browser's back button still does the right thing; with JavaScript off, pressing Enter searches.

**What is client-side:** debouncing the keystrokes, sending the request, morphing the result list in, showing a spinner. Nothing else. The matching, ranking, highlighting and empty-state wording are all C#.

---

## The markup

```html
@* Pages/Search/Index.cshtml *@
<form id="search-form" method="get" asp-page="/Search/Index" vx-get vx-target="#results"
      vx-on="input" vx-debounce="200" vx-indicator="searching" vx-url-sync="q">
  <label for="q">Search customers</label>
  <input id="q" name="q" type="search" value="@Model.Query"
         autocomplete="off" role="combobox" aria-expanded="@(Model.HasResults ? "true" : "false")"
         aria-controls="results" vx-bind="q" />

  <span vx-show="searching" aria-hidden="true" class="spinner"></span>
  <button type="submit">Search</button>
</form>

<div id="results" vx-region role="listbox" aria-label="Suggestions">
  @await Html.PartialAsync("_Suggestions", Model.Suggestions)
</div>
```

Read what that says: it is a **real GET form**. Without the agent, Enter submits it and the page renders with results — that is the R3 invariant paying for itself. With the agent, `vx-on="input"` plus `vx-debounce="200"` turns each pause into a request whose response replaces `#results` only.

`vx-indicator="searching"` sets a *signal*, so the spinner is `vx-show="searching"` and could equally be a disabled button elsewhere on the page. `vx-url-sync="q"` keeps `?q=` in the address bar without a history entry per keystroke — so a reload, a share or a bookmark all reproduce the screen.

## The partial

```html
@* Pages/Search/_Suggestions.cshtml *@
@model IReadOnlyList<SuggestionVm>

@if (Model.Count == 0)
{
  <p class="empty">No customers match. Try a name, an email or a customer number.</p>
}
else
{
  <ul>
  @foreach (var s in Model)
  {
    <li role="option" id="opt-@s.Id">
      <a asp-page="/Customers/Detail" asp-route-id="@s.Id" vx-get vx-target="#pane">
        @* Highlighting is computed server-side: the client never parses or matches anything *@
        @foreach (var run in s.Runs) { if (run.Hit) { <mark>@run.Text</mark> } else { @run.Text } }
      </a>
      <span class="meta">@s.CustomerNumber · @s.City</span>
    </li>
  }
  </ul>
}
```

## The handler

```csharp
public sealed class IndexModel : PageModel
{
    private readonly ICustomerSearch _search;
    private readonly IFragmentComposer _fragments;

    public string Query { get; private set; } = "";
    public IReadOnlyList<SuggestionVm> Suggestions { get; private set; } = [];
    public bool HasResults => Suggestions.Count > 0;

    public async Task<IResult> OnGetAsync(string? q, CancellationToken ct)
    {
        Query = (q ?? "").Trim();
        Suggestions = Query.Length < 2 ? [] : await _search.SuggestAsync(Query, take: 8, ct);

        return Vaxel.PageOrPatch(HttpContext,
            page:  () => Page(),
            patch: async () => Patch.Ok()
                .Replace("#results", await _fragments.PartialAsync("_Suggestions", Suggestions))
                .Signals(new { hasResults = HasResults })
                .Announce(Suggestions.Count switch
                {
                    0 => "No suggestions",
                    1 => "1 suggestion",
                    var n => $"{n} suggestions"
                }));
    }
}
```

One handler, two shapes. `PageOrPatch` chooses by the presence of `VX-Request`; the search logic is written once.

## The wire

Request (after a 200 ms pause on "acme no"):

```
GET /Search?q=acme%20no
VX-Request: 1
VX-Target: #results
VX-Sequence: 12
VX-Signals: {"q":"acme no","searching":true}
```

The query string carries `q` because `vx-url-sync` put it there — it is shareable state. The signal bag rides in a header, so it never becomes part of the cache key.

Response:

```html
<vx-patch target="#results" mode="morph">
  <div id="results" vx-region role="listbox" aria-label="Suggestions">
    <ul><li role="option" id="opt-c_18"><a …><mark>Acme No</mark>rdics AB</a>…</li>…</ul>
  </div>
</vx-patch>
<vx-signals>{"hasResults":true}</vx-signals>
<vx-directive announce="6 suggestions" />
```

Morph matters here: the user is still typing. Assigning `innerHTML` on the *parent* would be fine, but morphing `#results` means the input never loses focus or caret position even if a future version of the page nests them differently — and repeated results keep their DOM identity, so no flicker and no lost scroll in a long list.

## Keyboard behaviour

Arrow keys and Enter over a listbox are pure client behaviour with no server involvement, so they are an **island** — about fifteen lines:

```js
// wwwroot/js/combobox.js  (mounted on #search-form, which carries vx-preserve on nothing —
// the island only listens, it does not own DOM, so morphs are safe)
document.getElementById('search-form').addEventListener('keydown', e => {
  if (!['ArrowDown','ArrowUp','Enter','Escape'].includes(e.key)) return;
  const opts = [...document.querySelectorAll('#results [role=option] a')];
  if (!opts.length) return;
  const at = opts.findIndex(o => o === document.activeElement);
  if (e.key === 'ArrowDown') { e.preventDefault(); (opts[at + 1] ?? opts[0]).focus(); }
  if (e.key === 'ArrowUp')   { e.preventDefault(); (opts[at - 1] ?? opts.at(-1)).focus(); }
  if (e.key === 'Escape')    { document.getElementById('q').focus(); }
});
```

This is the framework's philosophy in one example: the *transport* is declarative, the *keyboard convention* is a few lines of ordinary JavaScript, and the *product logic* is C#.

## Edge cases the framework handles

| Case | Behaviour |
|---|---|
| Fast typist outruns the network | `vx-debounce` coalesces; `vx-sync="replace"` (the default) aborts the in-flight request; `VX-Sequence` discards any stale response that still arrives |
| User clears the box | `q` is empty → server returns the empty-state partial. No special-casing on the client |
| Server 500 | `vx:error` fires, the region is left alone, the agent falls back to native form submission if the trigger was the submit button |
| JavaScript disabled | The form submits, the page renders with results |
| Screen reader | `announce` posts the result count to the polite live region; focus never moves while typing |

## How to test it

```csharp
[Fact]
public async Task Suggestions_patch_only_the_results_region()
{
    var patch = await Client.PatchAsync("/Search?q=acme");

    patch.ShouldHaveStatus(200)
         .ShouldPatch("#results").WithMode(SwapMode.Morph).ContainingElement("[role=option]");
    patch.ShouldSetSignal("hasResults", true);
    patch.ShouldNotPatch("#search-form");        // the input must never be re-rendered under the user
}

[Fact]
public async Task Short_query_returns_the_empty_state_not_an_error()
    => (await Client.PatchAsync("/Search?q=a"))
        .ShouldHaveStatus(200)
        .ShouldPatch("#results").ContainingText("No customers match");

[Fact]
public Task Search_page_renders_the_same_results_without_the_agent()
    => VaxelParity.AssertAsync(Client, Route.Get("/Search?q=acme", region: "#results"));
```

The third test is the important one: it proves the page and the patch agree, which is what makes the no-JS path, the deep link and the shared URL real rather than aspirational.
