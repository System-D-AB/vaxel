# vaxel

Server-driven web apps for .NET. Razor renders every pixel; a small client agent applies HTML patches. No SPA, no client templates, no `unsafe-eval`.

```csharp
using Vaxel;

public async Task<IResult> OnPostAsync()
{
    if (!ModelState.IsValid)
    {
        Response.StatusCode = 422;
        return await Vaxel.PageOrPatch(HttpContext,
            page: () => Task.FromResult<IResult>(Page()),
            patch: async () => Patch.Status(422)
                .Replace("#contact", await _composer.PartialAsync("_ContactForm", this)));
    }

    return await Vaxel.PageOrPatch(HttpContext,
        page: () => Task.FromResult<IResult>(Redirect("/contact?sent=1")),
        patch: async () => Patch.Ok()
            .Replace("#contact", await _composer.PartialAsync("_ContactThanks", this))
            .PushUrl("/contact?sent=1"));
}
```

```html
<section id="contact" vx-region>
  <form method="post" action="/contact" vx-post vx-target="#contact">
    <!-- the form works without JavaScript; with the agent it becomes a fragment swap -->
  </form>
</section>
<script src="/_vaxel/vaxel.js"></script>
```

The link or form works with no JavaScript. With the agent loaded, the server still renders the HTML — the client only morphs it in, keeps focus, and updates the URL.

## Install

Apps reference **`Vaxel.AspNetCore`**. That package pulls in the agent and serves `/_vaxel/vaxel.js`.

```xml
<PackageReference Include="Vaxel.AspNetCore" Version="1.0.0-preview.1" />
```

```csharp
builder.Services.AddRazorPages();
builder.Services.AddVaxel();

var app = builder.Build();
app.UseStaticFiles();
app.UseVaxel();
app.MapRazorPages();
app.MapVaxelStream("/_vaxel/stream"); // optional SSE
```

CSP can stay `script-src 'self'`. There is no expression language, so there is no `unsafe-eval`.

## What it is

- **A rendering discipline** — every byte of HTML comes from Razor: Pages, Views, Partials, ViewComponents, static Razor Components.
- **A wire protocol** — responses are HTML patch documents that name their targets (`#pane`, `#contact`).
- **A small client agent** — intercept, request, morph, restore focus, push history, one SSE channel.
- **A signal store** — named UI values that travel with requests. Signals hold data, never code.

It is not a component framework, not Blazor, and not a JavaScript framework you write JavaScript in.

## Sample

```bash
dotnet run --project samples/Workbench/Workbench.csproj
```

The Workbench is a small admin shell: submissions (a role-checked approve), proposals (inline edit + live rail), a contact form, and read-only settings. Sign in as Alice to approve; as Bob to see a refusal.

## Docs

The contract lives in [`docs/`](docs/README.md). Start with [principles](docs/01-principles.md), then the [protocol](docs/03-protocol.md) and the [.NET API](docs/05-dotnet-api.md).

| Doc | What it settles |
|---|---|
| [01 — Principles](docs/01-principles.md) | Four rules everything else follows from |
| [02 — Architecture](docs/02-architecture.md) | Parts, request lifecycle, morph, SSE |
| [03 — Protocol](docs/03-protocol.md) | Patch documents, headers, SSE frames |
| [04 — Client attributes](docs/04-client-attributes.md) | The closed `vx-*` vocabulary |
| [05 — .NET API](docs/05-dotnet-api.md) | Packages, DI, composer, patch builder, tag helpers |
| [06 — Security](docs/06-security.md) | CSP, antiforgery, the signal trust boundary |
| [07 — Testing](docs/07-testing.md) | Page-parity invariant and the test kit |
| [08 — Roadmap](docs/08-roadmap.md) | Milestones, comparison, licence |
| [09 — Datastar gap analysis](docs/09-datastar-gap-analysis.md) | Every Datastar attribute, and vaxel's position |
| [10 — Test matrix](docs/10-test-matrix.md) | What proves each guarantee |
| [11 — Datastar reuse](docs/11-datastar-reuse.md) | What to vendor, decline, and credit |
| [12 — Parity with Datastar](docs/12-parity-with-datastar.md) | Plugin inventory and scoring |
| [13 — Test adoption](docs/13-test-adoption.md) | Their 20 conformance cases as the scoreboard |

## Cookbook

| Recipe | Shows |
|---|---|
| [01 — Search as you type](cookbook/01-search-as-you-type.md) | Debounced input, suggestion list, URL sync |
| [02 — Contact form](cookbook/02-contact-form.md) | Submit, server validation in place, double-submit guard |
| [03 — Tabs, rail and inline edit](cookbook/03-tabs-rail-and-inline-edit.md) | Tab strip as links, one action patching two regions |
| [04 — Live updates](cookbook/04-live-updates-sse.md) | SSE push per recipient, reconnect |

## Licence

MIT. No dependency in the .NET packages beyond ASP.NET Core. The agent's only third-party code is a DOM morph implementation (BSD-2).
