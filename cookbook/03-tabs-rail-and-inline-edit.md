# Recipe 03 — A tabbed shell, a filterable rail, and inline editing

Three patterns that usually justify a SPA, done with server rendering.

---

## A. Tabs

```html
<nav id="tab-strip" role="tablist">
  @foreach (var t in Model.Tabs)
  {
    <a role="tab" id="tab-@t.Key"
       aria-selected="@(t.Key == Model.Selected ? "true" : "false")"
       asp-page="/Apps/Detail" asp-route-id="@Model.AppId" asp-route-tab="@t.Key"
       vx-get vx-target="#pane">@t.Title</a>
  }
</nav>

<section id="pane" vx-region role="tabpanel" aria-labelledby="tab-@Model.Selected">
  @await Html.PartialAsync(Model.PanePartial, Model.PaneModel)
</section>
```

```csharp
public async Task<IResult> OnGetAsync(string id, string tab = "overview", CancellationToken ct = default)
{
    Load(id, tab);

    return Vaxel.PageOrPatch(HttpContext,
        page:  () => Page(),
        patch: async () => Patch.Ok()
            .Replace("#pane",      await _fragments.PartialAsync(PanePartial, PaneModel))
            .Replace("#tab-strip", await _fragments.ComponentAsync<TabStrip>(new { appId = id, selected = tab }))
            .Title($"{AppName} — {TabTitle}")
            .PushUrl(Url.Page("/Apps/Detail", new { id, tab })!)
            .Announce($"{TabTitle} tab"));
}
```

**Why two patches.** The pane changes *and* the strip's `aria-selected` moves. Sending both keeps the selected state server-computed — there is no client rule deciding which tab looks active, so the DOM cannot disagree with the URL.

Every tab is a real link with a real `href`. Middle-click opens it in a new tab, the back button walks the tabs, a screen-reader user tabs through them, and with JavaScript off it is an ordinary multi-page app. That is the whole trick: **the tab is a URL, and the patch is an optimisation of loading it.**

### Keeping the pane's own state

A tab that contains a scrolled table can keep its scroll on return by giving the scroll container an `id` and letting the morph restore it — the agent records and restores scroll for `[vx-region]` and any element with `id` inside it.

---

## B. A rail that filters as you type

```html
<aside id="rail" vx-region>
  <form method="get" asp-page="/Apps/Index" vx-get vx-target="#rail"
        vx-on="input" vx-debounce="150" vx-url-sync="filter">
    <label for="filter" class="sr-only">Filter applications</label>
    <input id="filter" name="filter" type="search" value="@Model.Filter" vx-bind="filter" />
  </form>

  @foreach (var group in Model.Groups)
  {
    <h3>@group.Name</h3>
    <ul>
      @foreach (var item in group.Items)
      {
        <li>
          <a asp-page="@item.Page" asp-route-id="@item.Id" vx-get vx-target="#pane"
             class="@(item.Selected ? "is-selected" : null)">
            @item.Title <span class="mono">@item.Suffix</span>
          </a>
        </li>
      }
    </ul>
  }
</aside>
```

The filter is applied **in C#**, over the same nav model the page uses. There is no client-side list, no duplicated matching rule, and no divergence between "what the filter shows" and "what the server thinks exists" — which is where client-side filtering quietly breaks permissions (items the user may not open must not appear, and only the server knows that).

`vx-url-sync="filter"` puts `?filter=kyc` in the URL without a history entry per keystroke, so the filtered rail is shareable and reload-safe.

**Cost:** one request per 150 ms pause, returning a fragment of a few kilobytes. If that ever becomes a problem, the answer is a `Cache-Control` on the rail route, not a client-side filter.

---

## C. Inline editing a title

```html
<h1 id="app-title" vx-region>
  <span>@Model.Name</span>
  <a asp-page-handler="EditTitle" asp-route-id="@Model.Id"
     vx-get vx-target="#app-title" aria-label="Rename">✎</a>
</h1>
```

The edit affordance is a link. Clicking it patches the heading into a form:

```csharp
public async Task<IResult> OnGetEditTitleAsync(string id)
    => Patch.Ok()
        .Replace("#app-title", await _fragments.PartialAsync("_TitleEditor", await Load(id)))
        .Focus("#title-input");
```

```html
@* _TitleEditor.cshtml *@
<h1 id="app-title" vx-region>
  <form method="post" asp-page-handler="RenameApp" asp-route-id="@Model.Id"
        vx-post vx-target="#app-title" vx-indicator="renaming">
    @Html.AntiForgeryToken()
    <input id="title-input" name="name" value="@Model.Name" required
           vx-on="keydown" vx-key="Escape" vx-get="?handler=CancelRename" />
    <button type="submit" vx-attr:disabled="renaming">Save</button>
    <a asp-page-handler="CancelRename" asp-route-id="@Model.Id" vx-get vx-target="#app-title">Cancel</a>
  </form>
</h1>
```

```csharp
public async Task<IResult> OnPostRenameAppAsync(string id, string name, CancellationToken ct)
{
    var result = await _apps.RenameAsync(id, name, ct);

    if (result.Refused)
        return Patch.Status(422)
            .Replace("#app-title", await _fragments.PartialAsync("_TitleEditor", result.Model))
            .Append("#notices",    await _fragments.PartialAsync("_Notice", result.Refusal))
            .Focus("#title-input");

    return Patch.Ok()
        .Replace("#app-title",  await _fragments.PartialAsync("_Title", result.Model))
        .Replace("#rail",       await _fragments.ComponentAsync<Rail>(new { selected = id }))  // the name changed there too
        .Focus("#app-title")
        .Announce($"Renamed to {result.Model.Name}");
}
```

**The pattern to notice:** one action patched *two* regions, because renaming an app changes both the heading and the rail entry. In a client-state framework you would keep a store in sync; here the server, which knows what a rename affects, simply says so. Consistency is a property of the response rather than of a reducer.

### `contenteditable` editing

If you want the heading itself editable rather than swapped for an input, the morph guarantee is what makes it viable: patches to *other* regions never disturb the node the user is typing in, and `vx-preserve-attr` keeps attributes an island added. Commit on blur with `vx-on="blur"` and send the text with `vx-vals-*`, or let a small island read the node and post it. Either way the value is validated and re-rendered by the server.

---

## What all three share

1. **Every interactive thing is a link or a form.** No `<div>` with a click handler, so keyboard and screen-reader behaviour is the platform's, not ours.
2. **Selected/active/filtered state is computed server-side** and arrives as rendered HTML, so the DOM cannot disagree with the URL or with permissions.
3. **One action may patch several regions.** That is how consistency is maintained without a client store.
4. **Everything degrades.** Turn off JavaScript and all three are ordinary page loads.

## Testing these

```csharp
[Fact]
public async Task Selecting_a_tab_patches_pane_and_strip_and_pushes_the_url()
{
    var patch = await Client.PatchAsync("/Apps/Detail?id=a_1&tab=submissions");

    patch.ShouldPatch("#pane").ContainingElement("table");
    patch.ShouldPatch("#tab-strip").ContainingAttribute("#tab-submissions", "aria-selected", "true");
    patch.ShouldDirect(d => d.PushUrl!.EndsWith("tab=submissions") && d.Title!.Contains("Submissions"));
}

[Fact]
public async Task Rename_patches_both_the_heading_and_the_rail()
{
    var patch = await Client.PatchPostAsync("/Apps/Detail?handler=RenameApp&id=a_1", values: new { name = "Renamed" });

    patch.ShouldPatch("#app-title").ContainingText("Renamed");
    patch.ShouldPatch("#rail").ContainingText("Renamed");
}

[Fact]
public async Task Rail_filter_never_lists_an_app_the_actor_may_not_open()
{
    var patch = await AsReadOnlyUser().PatchAsync("/Apps?filter=secret");
    patch.ShouldPatch("#rail").NotContainingText("Secret project");
}

[Fact]
public Task All_three_regions_are_reachable_as_pages()
    => VaxelParity.AssertAsync(Client,
        Route.Get("/Apps/Detail?id=a_1&tab=submissions", region: "#pane"),
        Route.Get("/Apps?filter=kyc",                    region: "#rail"),
        Route.Get("/Apps/Detail?id=a_1",                 region: "#app-title"));
```

The third test is the one a client-side filter cannot pass without duplicating authorisation into the browser.
