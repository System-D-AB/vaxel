using Microsoft.AspNetCore.Authentication.Cookies;
using Vaxel;
using Workbench.Signals;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.Cookie.Name = "Vaxel.Workbench.Auth";
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ApproverOnly", policy => policy.RequireRole("Approver"));
});

builder.Services.AddRazorPages();
builder.Services.AddVaxel(options =>
{
    options.Push.HeartbeatSeconds = 20;
    options.Push.MaxConnectionsPerIdentity = 4;
    options.Push.AllowAnonymous = true;
});
builder.Services.AddSignalSchema<WorkbenchSignals>();

var app = builder.Build();

app.UseDeveloperExceptionPage();

// Strict CSP header
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("Content-Security-Policy",
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self'; object-src 'none'; base-uri 'self';");
    await next();
});

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseVaxel();

app.MapRazorPages();
app.MapVaxelStream("/_vaxel/stream");

// Inline Edit: Proposal Cancel
app.MapGet("/proposals/{id:int}/cancel", async (int id, HttpContext http, IFragmentComposer composer) =>
{
    var model = (Id: id, Title: "Proposal A: Realtime Agent Architecture", Author: "Alice (Engineering)");
    return await global::Vaxel.Vaxel.PageOrPatch(http,
        page: () => Task.FromResult<IResult>(Results.Redirect($"/?tab=proposals")),
        patch: async () =>
        {
            var fragment = await composer.PartialAsync("_ProposalItem", model);
            return Patch.Ok().Replace($"#prop-{id}", fragment);
        });
});

// Inline Edit: Proposal Save
app.MapPost("/proposals/{id:int}/save", async (int id, HttpContext http, IFragmentComposer composer, IPushChannel push) =>
{
    var form = await http.Request.ReadFormAsync();
    var title = form["title"].ToString();
    if (string.IsNullOrWhiteSpace(title)) title = "Untitled Proposal";

    var model = (Id: id, Title: title, Author: $"{http.User.Identity?.Name ?? "Alice"} (Saved)");

    // Broadcast update notification to live-updates rail using Razor partial
    var liveUpdateFragment = await composer.PartialAsync("_LiveUpdate", ($"⚡ Proposal #{id} updated: {title}", "#38bdf8"));
    _ = push.PushAsync(PushScope.Broadcast(), Patch.Ok().Replace("#live-updates", liveUpdateFragment));

    var itemFragment = await composer.PartialAsync("_ProposalItem", model);
    var noticeFragment = await composer.PartialAsync("_Notice", ("Proposal updated successfully.", false));

    return await global::Vaxel.Vaxel.PageOrPatch(http,
        page: () => Task.FromResult<IResult>(Results.Redirect($"/?tab=proposals")),
        patch: () => Task.FromResult<IResult>(Patch.Ok()
            .Replace($"#prop-{id}", itemFragment)
            .Into("#notices", noticeFragment)));
});

// Governed Action: Submission Approve
app.MapPost("/submissions/{id:int}/approve", async (int id, HttpContext http, IFragmentComposer composer, IPushChannel push) =>
{
    var isApprover = http.User.IsInRole("Approver");
    if (!isApprover)
    {
        var refusalNotice = await composer.PartialAsync("_Notice", ("Action Refused: You must have the Approver role to approve budget submissions. (Sign in as Alice)", true));
        // Governed Refusal per Rule R4
        return Patch.Refused("User does not hold the Approver role.", StatusCodes.Status403Forbidden)
            .Into("#notices", refusalNotice);
    }

    var approverName = http.User.Identity?.Name ?? "Approver";
    var model = (Id: id, Title: "Quarterly Budget Request", Approver: approverName);

    // Push broadcast event using Razor partial
    var liveUpdateFragment = await composer.PartialAsync("_LiveUpdate", ($"✔ Submission #{id} approved by {approverName}", "#34d399"));
    _ = push.PushAsync(PushScope.Broadcast(), Patch.Ok().Replace("#live-updates", liveUpdateFragment));

    var rowFragment = await composer.PartialAsync("_SubmissionApprovedRow", model);
    var successNotice = await composer.PartialAsync("_Notice", ($"Submission #{id} approved.", false));

    return await global::Vaxel.Vaxel.PageOrPatch(http,
        page: () => Task.FromResult<IResult>(Results.Redirect($"/?tab=submissions")),
        patch: () => Task.FromResult<IResult>(Patch.Ok()
            .Replace($"#sub-{id}", rowFragment)
            .Into("#notices", successNotice)));
});

app.Run();

namespace Workbench
{
    public partial class Program;
}
