# Recipe 04 — Live updates: a queue that changes because someone else did something

Everything in recipes 01–03 is request/response: the user acts, the server answers. This recipe covers the other half — change the user did **not** initiate. A colleague approves a proposal; a long job finishes; a counter moves.

---

## Opening the channel

One channel per document, on the shell:

```html
<body vx-sse="/_vaxel/stream">
  …
  <span id="queue-count" class="badge">@Model.PendingCount</span>
</body>
```

That is the entire client side.

## Pushing from the server

```csharp
public sealed class ProposalRaisedHandler(IPushChannel push, IFragmentComposer fragments)
{
    public async Task HandleAsync(ProposalRaised e, CancellationToken ct)
    {
        // Render per recipient: two people may see different counts and different rows.
        foreach (var approver in await _directory.ApproversForAsync(e.TenantId, ct))
        {
            var vm = await _queue.ForApproverAsync(approver.Id, ct);

            await push.PushAsync(PushScope.User(approver.Id),
                Patch.Ok()
                    .Inner("#queue-count", await fragments.PartialAsync("_QueueCount", vm.PendingCount))
                    .Prepend("#queue-list", await fragments.PartialAsync("_QueueRow", vm.Newest))
                    .Announce($"New approval waiting: {vm.Newest.Title}"),
                ct);
        }
    }
}
```

**Rendered per recipient, deliberately.** The API shape makes it awkward to broadcast one rendering to people with different permissions, because that is the bug this pattern invites. If two hundred approvers need the same fragment, render once and loop the push — an explicit choice, visible in the code.

## The wire

```
event: vx-patch
id: 8814
data: <vx-patch target="#queue-count" mode="inner"><span id="queue-count" class="badge">4</span></vx-patch>
data: <vx-patch target="#queue-list" mode="prepend"><li id="p_204">…</li></vx-patch>
data: <vx-directive announce="New approval waiting: Source-of-funds for PEPs" />

: heartbeat
```

Identical format to a response body — one parser, one applier, one set of conformance fixtures.

## What the agent does and does not do

| Does | Does not |
|---|---|
| Applies patches and announces politely | Move focus — the user did not ask for this |
| Reconnects with jittered backoff, sends `Last-Event-ID` | Assume the server replayed anything |
| Surfaces `vx:sse-state` so a UI can show "reconnecting…" | Retry forever silently |
| Keeps exactly one channel per document | Open a channel per region |

The focus rule matters: a patch that steals focus while someone is typing is the fastest way to make a live-updating page unusable, so the spec forbids it for push-driven patches.

## Long-running work, without polling

```csharp
public async Task<IResult> OnPostGenerateReportAsync(string appId, CancellationToken ct)
{
    var jobId = await _jobs.EnqueueAsync(new GenerateReport(appId), ct);

    return Patch.Ok()
        .Replace("#report-panel", await _fragments.PartialAsync("_ReportPending", jobId))
        .Announce("Report queued");
}
```

The worker pushes when it finishes:

```csharp
await push.PushAsync(PushScope.User(job.RequestedBy),
    Patch.Ok()
        .Replace("#report-panel", await fragments.PartialAsync("_ReportReady", result))
        .Announce("Report ready to download"));
```

No polling, no client job-state machine, no websocket. If the connection was dropped when the job finished, the panel is stale — and the fix is the invariant: `#report-panel` is also reachable as a page, so a refresh shows the truth.

**When SSE is unavailable** (a proxy that will not stream, a corporate middlebox): degrade to `vx-poll` on the region, at a sane interval. The server code does not change, because the pending partial is the same partial.

## Rendering outside a request

Pushing happens from a hosted service or a message consumer, where there is no `HttpContext`. `IFragmentComposer` works there, but a partial that calls `Url.Page(...)` or reads `User` will fail — deliberately, at development time, with a message naming the offending call rather than a null reference at 3 a.m. Partials meant for push should take everything they need in their model.

## Scaling

The default push transport is in-process: one node, connections held in memory. Multi-node needs `IPushBackplane` (Redis, Postgres `LISTEN/NOTIFY`, a bus). This is a documented limitation with a documented seam, not a surprise discovered at the second instance.

Cost to keep in mind: one open connection per document, not per user — someone with four tabs holds four. The per-identity cap (default 4) exists for that reason, and on platforms that scale to zero, an open stream keeps an instance alive. Push where it earns its keep; use request/response everywhere else.

## Testing

```csharp
[Fact]
public async Task Raising_a_proposal_pushes_a_count_and_a_row_to_approvers_only()
{
    using var stream = await Client.OpenStreamAsync("/_vaxel/stream", as: Approver);

    await Client.PatchPostAsync("/Apps/a_1?handler=ProposePublish", values: new { note = "ready" }, as: Builder);

    var frame = await stream.NextPatchAsync(TimeSpan.FromSeconds(5));
    frame.ShouldPatch("#queue-count").ContainingText("4");
    frame.ShouldPatch("#queue-list").WithMode(SwapMode.Prepend);
    frame.ShouldDirect(d => d.Announce!.Contains("New approval waiting"));
}

[Fact]
public async Task A_user_never_receives_another_users_queue()
{
    using var mine = await Client.OpenStreamAsync("/_vaxel/stream", as: ApproverA);
    await RaiseProposalFor(ApproverB);
    await mine.ShouldReceiveNothingWithin(TimeSpan.FromSeconds(2));
}

[Fact]
public async Task Stream_reconnects_and_the_page_is_correct_after_a_drop()
{
    using var stream = await Client.OpenStreamAsync("/_vaxel/stream", as: Approver);
    stream.Drop();
    await RaiseProposal();

    await stream.ShouldReconnectWithin(TimeSpan.FromSeconds(10));
    // The missed change is recovered by rendering the page, not by replay:
    (await Client.GetAsync("/Approvals")).ShouldContainText("4 waiting");
}
```

`Vaxel.Testing` provides `OpenStreamAsync`, so push is testable without a browser. The one genuinely browser-shaped test — that a reconnect after a laptop sleeps produces a correct screen — belongs in the framework's conformance suite.
