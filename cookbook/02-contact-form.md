# Recipe 02 — A contact form that submits, validates and cannot double-submit

**What the user does:** fills in a form, presses Send, sees the button disable and a spinner appear, then either the form is replaced by a thank-you panel or the fields come back with server validation messages beside them — without the page reloading and without losing what they typed.

**What is client-side:** disabling the button, showing the indicator, sending the request, morphing the response in. Every validation rule, every message and every anti-abuse decision is C#.

---

## The markup

```html
@* Pages/Contact.cshtml *@
<section id="contact" vx-region>
  @if (Model.Sent)
  {
    <div class="panel panel--affirm" role="status">
      <h2>Thank you — we have your message</h2>
      <p>Reference <code>@Model.Reference</code>. We reply within one working day.</p>
    </div>
  }
  else
  {
    <form method="post" asp-page="/Contact" vx-post vx-target="#contact"
          vx-indicator="sending" vx-disable>
      @Html.AntiForgeryToken()

      <div class="field">
        <label for="name">Your name</label>
        <input id="name" name="Form.Name" value="@Model.Form.Name"
               aria-invalid="@Model.Invalid(nameof(Model.Form.Name))"
               aria-describedby="name-error" required />
        <p id="name-error" class="error">@Model.ErrorFor(nameof(Model.Form.Name))</p>
      </div>

      <div class="field">
        <label for="email">Email</label>
        <input id="email" name="Form.Email" type="email" value="@Model.Form.Email"
               aria-invalid="@Model.Invalid(nameof(Model.Form.Email))"
               aria-describedby="email-error" required />
        <p id="email-error" class="error">@Model.ErrorFor(nameof(Model.Form.Email))</p>
      </div>

      <div class="field">
        <label for="message">Message</label>
        <textarea id="message" name="Form.Message" rows="6"
                  aria-describedby="message-count">@Model.Form.Message</textarea>
        <p id="message-count" class="hint">Up to 2 000 characters.</p>
      </div>

      @* Honeypot: server-side trap, invisible to people, irresistible to bots *@
      <input type="text" name="Form.Website" tabindex="-1" autocomplete="off" class="hp" aria-hidden="true" />

      <button type="submit">
        <span vx-show="sending" class="spinner" aria-hidden="true"></span>
        Send message
      </button>
      <p class="hint" vx-show="sending" role="status">Sending…</p>
    </form>
  }
</section>
```

`vx-disable` on the form disables the submit button for the duration of the request — the double-submit guard that costs nothing. `vx-indicator="sending"` drives both the spinner and the status line from one signal.

Note what is *not* here: no client-side validation rules, no `required` mirroring in JavaScript, no regex for the email. The browser's native `required`/`type=email` gives instant feedback for free; the server is the authority and its messages are the ones rendered.

## The handler

```csharp
public sealed class ContactModel : PageModel
{
    private readonly IContactService _contact;
    private readonly IFragmentComposer _fragments;

    [BindProperty] public ContactForm Form { get; set; } = new();
    public bool Sent { get; private set; }
    public string? Reference { get; private set; }

    public async Task<IResult> OnPostAsync(CancellationToken ct)
    {
        var result = await _contact.SubmitAsync(Form, ct);   // validates, rate-limits, checks the honeypot

        if (result.Refused)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(e.Field, e.Message);

            return Vaxel.PageOrPatch(HttpContext,
                page:  () => Page(),
                patch: async () => Patch.Status(422)
                    .Replace("#contact", await _fragments.PartialAsync("_ContactForm", this))
                    .Focus("#" + result.Errors[0].Field.ToLowerInvariant())
                    .Announce($"{result.Errors.Count} problems to fix"));
        }

        Sent = true;
        Reference = result.Reference;

        return Vaxel.PageOrPatch(HttpContext,
            page:  () => RedirectToPage("/Contact", new { sent = result.Reference }),  // POST-redirect-GET
            patch: async () => Patch.Ok()
                .Replace("#contact", await _fragments.PartialAsync("_ContactSent", this))
                .Focus("#contact")
                .Announce("Message sent")
                .PushUrl(Url.Page("/Contact", new { sent = result.Reference })!));
    }
}
```

Two details worth copying:

- **The no-JS path keeps POST-redirect-GET.** A refresh after submitting must not resubmit. The patch path achieves the same end with `PushUrl`, so the URL after submitting is the one that would render the thank-you panel — which is exactly what the parity test checks.
- **A validation failure is a 422 that still renders.** Failure and success take the same code path and the same visual language; there is no `catch` block that formats errors differently.

## The wire

Failure:

```
POST /Contact
VX-Request: 1
Content-Type: application/x-www-form-urlencoded
VX-Signals: {"sending":true}
X-CSRF: CfDJ8…

Form.Name=&Form.Email=not-an-email&Form.Message=hi&__RequestVerificationToken=…
```

An ordinary form post. That is the point: `[BindProperty]`, `ModelState`, `[Required]`, `IValidatableObject` and every validation message you already know all keep working, because the agent did not invent an envelope around them. Signals ride in a header, beside the body rather than inside it.

```html
HTTP/1.1 422 Unprocessable Content
Content-Type: text/vnd.vaxel-patch+html

<vx-patch target="#contact" mode="morph">
  <section id="contact" vx-region>… form with values preserved and messages rendered …</section>
</vx-patch>
<vx-directive focus="#name" announce="2 problems to fix" />
```

Because the response is a **morph**, the textarea the user spent two minutes on keeps its scroll position and the caret survives — the fields are re-rendered with the same ids, so morphing matches them rather than replacing them. And any field the user edited *after* pressing Send keeps what they typed: dirty input wins over an incoming value unless the element says `vx-overwrite-dirty`. This is the single behaviour that makes server-rendered forms feel modern instead of feeling like 2004.

## Variations

**Field-level validation on blur.** Add to one field:

```html
<input id="email" name="Form.Email" vx-post="?handler=ValidateField" vx-on="blur"
       vx-target="#email-field" vx-vals-field="Email" />
```

and a handler that validates one field and patches only `#email-field`. Same C# validators, no duplicated rules.

**File attachment.** Give the form `enctype="multipart/form-data"`; the agent sends it natively, signals ride in the `VX-Signals` header, and the upload streams instead of being base64'd into a JSON body.

**Progress for a slow submit.** `vx-indicator` covers "in flight". For genuinely long work (a PDF being generated), submit returns immediately with a job id, and the SSE channel patches the panel when the job finishes — see [Recipe 04](04-live-updates-sse.md).

## Anti-abuse

All server-side, all unchanged from a classic form: the honeypot field, a per-IP rate limit, the antiforgery token (which the agent sends automatically), and a minimum fill time if you want one. The client cannot be trusted to enforce any of it, so it does not pretend to.

## How to test it

```csharp
[Fact]
public async Task Invalid_submission_returns_422_and_re_renders_with_messages()
{
    var patch = await Client.PatchPostAsync("/Contact",
        values: new { Form_Name = "", Form_Email = "nope", Form_Message = "hi" });

    patch.ShouldHaveStatus(422)
         .ShouldPatch("#contact")
             .ContainingText("Enter your name")
             .ContainingAttribute("#email", "value", "nope");     // what they typed survives
    patch.ShouldDirect(d => d.Focus == "#name");
}

[Fact]
public async Task Successful_submission_swaps_in_the_thank_you_and_moves_the_url()
{
    var patch = await Client.PatchPostAsync("/Contact", values: Valid());

    patch.ShouldHaveStatus(200)
         .ShouldPatch("#contact").ContainingText("Thank you");
    patch.ShouldDirect(d => d.PushUrl!.Contains("sent="));
}

[Fact]
public async Task Honeypot_submission_is_accepted_silently_and_stores_nothing()
{
    var patch = await Client.PatchPostAsync("/Contact", values: Valid(website: "http://spam"));

    patch.ShouldHaveStatus(200).ShouldPatch("#contact").ContainingText("Thank you");
    (await Store.CountAsync()).Should().Be(0);
}

[Fact]
public Task Contact_page_renders_the_same_form_without_the_agent()
    => VaxelParity.AssertAsync(Client, Route.Post("/Contact", region: "#contact"));
```

Four HTTP tests and no browser. The one thing worth a browser test is that the caret and scroll position in the textarea survive the failure morph — a conformance concern, so it lives in the framework's own suite rather than in every application that uses it.
