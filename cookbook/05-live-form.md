# Recipe 05 — A live form with instant character counter, dirty state, and schema-checked expressions

**What the user does:** types a name and message; sees the live character counter update on every keystroke (`142 / 500 characters`), an "Unsaved changes" badge appear when the fields differ from the saved baseline, and the Send button enable only when both fields are non-empty and within the 500-character budget. With JavaScript disabled, the form submits normally and the server validates the same rules.

**What is client-side:** two-way `vx-bind` on the inputs plus pure, single-edge expressions over signals (`vx-text`, `vx-show`, `vx-class:*`, `vx-attr:disabled`) evaluated by the closed AST interpreter under `script-src 'self'`. Authoritative validation and persistence remain C# on the server.

---

## The signal schema

Registering the schema lets `VxBindTagHelper` type-check every expression at render time:

```csharp
public sealed record ContactSignals(
    string Name = "",
    string Message = "",
    string SavedName = "",
    string SavedMessage = "",
    bool Saving = false
);

// Program.cs
builder.Services.AddVaxel().AddSignalSchema<ContactSignals>();
```

If a view author mistypes `vx-show="len(mesage) > 500"` or writes a type mismatch like `vx-show="message > 500"`, Razor rendering throws `VaxelTagHelperException` with the offending character span before the page ever ships.

## The markup

```html
@* Pages/Contact.cshtml *@
<section id="contact" vx-region
         vx-signals='@Json.Serialize(new {
             name = Model.Name,
             message = Model.Message,
             savedName = Model.Name,
             savedMessage = Model.Message,
             saving = false
         })'>
  <form method="post" action="/contact" vx-post vx-target="#contact" vx-indicator="saving" id="contact-form">
    @Html.AntiForgeryToken()

    <div class="field">
      <label for="contact-name">Name</label>
      <input id="contact-name" type="text" name="name" value="@Model.Name"
             vx-bind="name" required />
    </div>

    <div class="field">
      <label for="contact-message">Message</label>
      <textarea id="contact-message" name="message" rows="4"
                vx-bind="message" required>@Model.Message</textarea>
      <small id="contact-char-count"
             vx-text="@@{ len(message) } / 500 characters"
             vx-class:is-over-limit="len(message) > 500">@((Model.Message ?? "").Length) / 500 characters</small>
    </div>

    <p id="contact-dirty-hint"
       vx-show="name != savedName || message != savedMessage"
       hidden>Unsaved changes</p>

    <button type="submit"
            vx-attr:disabled="trim(name) == '' || trim(message) == '' || len(message) > 500 || saving">
      Send Message
    </button>
  </form>
</section>
```

Why each piece is shaped this way:

- **Initial text matches the initial signal seed.** `<small>` renders `@(Model.Message.Length) / 500 characters` on the server and binds `vx-text="@{ len(message) } / 500 characters"` for the client. There is no layout shift on first paint, and the no-JS page has the right count too.
- **Fail-closed boolean bindings.** If an expression ever refuses at runtime, `vx-show`, `vx-class:*`, and boolean `vx-attr:*` treat the refusal as `false` and emit `vx:error` — a broken expression never silently enables a restricted control.
- **Zero `unsafe-eval`.** The expression interpreter walks a parsed AST over the signal store's auto-tracking `Signal` nodes (`name` and `message`), re-running `#contact-char-count` only when `message` changes and `#contact-dirty-hint` when `name` or `message` changes.

## How to test it

```csharp
[Fact]
public Task Contact_live_form_page_and_patch_agree()
    => VaxelParity.AssertAsync(_client, "/contact", "/contact", "pane");

[Fact]
public void Typo_in_expression_fails_at_render_time()
{
    var options = Options.Create(new VaxelOptions
    {
        SignalSchema = new SignalSchema<ContactSignals>()
    });
    var helper = new VxBindTagHelper(options)
    {
        VxShow = "message > 500" // string compared to number -> expressions.type
    };

    var ex = Assert.Throws<VaxelTagHelperException>(() => helper.Process(context, output));
    Assert.Contains("expressions.type", ex.Message);
}
```
