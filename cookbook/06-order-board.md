# Recipe 06 — Live order board: `Bus<T>` and `push.From(...)` pipelines

A kitchen or operations board watches orders move from **Preparing** to **Ready**. Multiple stations trigger state transitions; every open kitchen display updates in real time without polling, while any station that refreshes or opens the URL cold sees the exact same board (Rule R3).

---

## The markup

```html
<body vx-sse="/_vaxel/stream"
      vx-on="keydown window | key k | filter ctrlKey || metaKey | prevent | showModal #station-switcher; keydown window | key / | filter !typing | prevent | focus #order-filter">
  <main class="board" vx-signals='{"filterStation": "all"}'>
    <header>
      <h1>Kitchen Order Board</h1>
      <span id="board-summary" class="badge">@Model.Preparing.Count preparing · @Model.Ready.Count ready</span>
    </header>

    <div class="columns">
      <section id="orders-preparing" vx-region aria-label="Orders preparing">
        @await Html.PartialAsync("_OrderColumn", Model.Preparing)
      </section>

      <section id="orders-ready" vx-region aria-label="Orders ready">
        @await Html.PartialAsync("_OrderColumn", Model.Ready)
      </section>
    </div>
  </main>
</body>
```

Each card in `_OrderColumn.cshtml` is a standard `<form>` with `vx-post`:

```html
@model IReadOnlyList<OrderCard>
<ul class="order-list">
  @foreach (var order in Model)
  {
    <li id="order_@order.Id" class="order-card"
        vx-show="filterStation == 'all' || filterStation == '@order.Station'">
      <strong>#@order.Number</strong> · @order.Summary
      @if (order.Status == OrderStatus.Preparing)
      {
        <form method="post"
              action="/Orders?handler=MarkReady&amp;id=@order.Id"
              vx-post="/Orders?handler=MarkReady&amp;id=@order.Id"
              vx-target="#orders-preparing">
          <button type="submit">Mark ready</button>
        </form>
      }
    </li>
  }
</ul>
```

---

## Publishing domain events to `IBus<T>`

Register a bounded typed bus in `Program.cs`:

```csharp
builder.Services.AddVaxel(o => o.Push.IdleStopPeriod = TimeSpan.FromSeconds(30));
builder.Services.AddVaxelBus<OrderBoardChanged>(capacity: 256, BusDropPolicy.DropOldest);
```

When a cook marks an order ready, the Razor Page handler commits the change, publishes `OrderBoardChanged` to `IBus<OrderBoardChanged>` (without knowing or caring who is watching), and returns a `PageOrPatch` response for the caller:

```csharp
public sealed record OrderBoardChanged(string OrderId, OrderStatus NewStatus);

public sealed class OrdersModel(
    IOrderStore store,
    IBus<OrderBoardChanged> bus,
    IFragmentComposer fragments) : PageModel
{
    public IReadOnlyList<OrderCard> Preparing { get; private set; } = [];
    public IReadOnlyList<OrderCard> Ready { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        (Preparing, Ready) = await store.GetColumnsAsync(User, ct);
    }

    public async Task<IActionResult> OnPostMarkReadyAsync(string id, CancellationToken ct)
    {
        await store.MarkReadyAsync(id, ct);
        bus.Publish(new OrderBoardChanged(id, OrderStatus.Ready));

        (Preparing, Ready) = await store.GetColumnsAsync(User, ct);
        var patch = Patch.Ok()
            .Replace("#orders-preparing", await fragments.PartialAsync("_OrderColumn", Preparing))
            .Replace("#orders-ready", await fragments.PartialAsync("_OrderColumn", Ready))
            .Inner("#board-summary", $"{Preparing.Count} preparing · {Ready.Count} ready")
            .Announce($"Order {id} is ready");

        return this.PageOrPatch(patch, redirectToPage: "/Orders");
    }
}
```

---

## Streaming the board with `push.From(...)`

A background worker subscribes to `IBus<OrderBoardChanged>`, coalesces bursts with `DistinctUntilChanged` and `Throttle(250 ms)`, renders per recipient inside a `BackgroundHttpContext` scope, and pushes to the `"kitchen"` group:

```csharp
public sealed class OrderBoardPushWorker(
    IBus<OrderBoardChanged> bus,
    IPushChannel push,
    IOrderStore store) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        push.From(bus.Subscribe(stoppingToken))
            .DistinctUntilChanged(e => (e.OrderId, e.NewStatus))
            .Throttle(TimeSpan.FromMilliseconds(250))
            .Select(async (evt, recipient, composer, ct) =>
            {
                // Rendered per recipient: station leads only see orders permitted for their role/tenant.
                var (preparing, ready) = await store.GetColumnsForUserAsync(recipient.UserId, ct);

                return Patch.Ok()
                    .Replace("#orders-preparing", await composer.PartialAsync("_OrderColumn", preparing))
                    .Replace("#orders-ready", await composer.PartialAsync("_OrderColumn", ready))
                    .Inner("#board-summary", $"{preparing.Count} preparing · {ready.Count} ready")
                    .Announce($"Order {evt.OrderId} moved to {evt.NewStatus}");
            })
            .To(PushScope.Group("kitchen"), stoppingToken);
}
```

### Why this holds the invariants

1. **Zero Rx.NET, one mental model.** `DistinctUntilChanged`, `Throttle`, and `Select` on the server use the exact same operator semantics and shared `conformance/marbles/` fixtures as `vx-on` on the client.
2. **Per-recipient rendering.** The `.Select((evt, recipient, composer, ct) => ...)` selector runs once per matching recipient in a `BackgroundHttpContext` scope. Two kitchen displays with different station permissions never share pre-rendered HTML.
3. **Bounded memory and clean lifetime.** `Bus<T>` uses a bounded `Channel<T>` per subscriber (`DropOldest` default) and drops immediately when zero subscribers are listening. If every `"kitchen"` display closes its tab, the scope becomes idle and cancels the upstream subscription after `IdleStopPeriod` (default 30 s).

---

## Testing: push stream + Rule R3 page parity

Every region updated by the order board (`#orders-preparing` and `#orders-ready`) must satisfy `VaxelParity.AssertAsync` — a fresh `GET /Orders` after an order moves to Ready produces the exact same region DOM as the morphed patch:

```csharp
[Fact]
public async Task Marking_order_ready_pushes_to_kitchen_and_preserves_page_parity()
{
    using var display = await Client.OpenStreamAsync("/_vaxel/stream", as: KitchenDisplay);

    // Trigger state transition as line cook
    var patchResponse = await Client.PatchPostAsync("/Orders?handler=MarkReady&id=104", as: LineCook);
    patchResponse.ShouldPatch("#orders-preparing");
    patchResponse.ShouldPatch("#orders-ready");

    // Kitchen display receives the throttled push frame
    var pushed = await display.NextPatchAsync(TimeSpan.FromSeconds(3));
    pushed.ShouldPatch("#orders-preparing");
    pushed.ShouldPatch("#orders-ready").ContainingText("#104");

    // Rule R3: fresh GET /Orders matches the patched regions structurally
    await VaxelParity.AssertAsync(
        Client,
        pageUrl: "/Orders",
        patchRequest: new HttpRequestMessage(HttpMethod.Post, "/Orders?handler=MarkReady&id=104"),
        regionIds: ["#orders-preparing", "#orders-ready"]);
}
```
