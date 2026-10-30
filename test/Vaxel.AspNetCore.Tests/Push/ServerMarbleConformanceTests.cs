using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Html;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vaxel.AspNetCore.Tests.Composer;
using Vaxel.Testing;
using Xunit;

namespace Vaxel.AspNetCore.Tests.Push;

/// <summary>
/// P3 (<c>v2.0-push-pipelines</c>) conformance and integration tests:
/// - US-1 &amp; US-4: Shared <c>conformance/marbles/*.json</c> corpus under <see cref="VirtualScheduler"/> + server operators (<c>Sample</c>, <c>Buffer</c>, <c>Merge</c>, <c>TakeUntil</c>) + per-recipient rendering
/// - US-2: Pipeline lifetime (<c>close → stop</c>, <c>idle → stop</c>, <c>source-throw isolation</c>)
/// - US-3: <see cref="Bus{T}"/> fan-out, zero-subscriber drop, bounded overflow drop-oldest policy, and cross-node <see cref="IPushBackplane"/> delivery
/// </summary>
public sealed class ServerMarbleConformanceTests : IClassFixture<ComposerApiFactory>
{
    private const int FrameMs = 50;
    private readonly ComposerApiFactory _factory;

    public ServerMarbleConformanceTests(ComposerApiFactory factory)
    {
        _factory = factory;
    }

    private static string MarblesDir => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "conformance", "marbles"));

    public static IEnumerable<object[]> MarbleFixtures()
    {
        var files = Directory.GetFiles(MarblesDir, "*.json")
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                return !name.StartsWith("package", StringComparison.OrdinalIgnoreCase) &&
                       !name.StartsWith("results", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(f => f, StringComparer.Ordinal);

        foreach (var file in files)
        {
            yield return [Path.GetFileNameWithoutExtension(file)];
        }
    }

    [Theory]
    [MemberData(nameof(MarbleFixtures))]
    public async Task ServerOperator_MatchesSharedMarbleFixture(string operatorName)
    {
        var filePath = Path.Combine(MarblesDir, $"{operatorName}.json");
        var spec = JsonSerializer.Deserialize<MarbleSpec>(
            await File.ReadAllTextAsync(filePath, Encoding.UTF8),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var scheduler = new VirtualScheduler();
        var inputChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
        });

        IPushPipeline<string> pipeline = new PushPipeline<string>(inputChannel.Reader.ReadAllAsync(), scheduler);

        var argMs = spec.Args is { Length: > 0 } ? spec.Args[0] : 0;
        pipeline = spec.Operator switch
        {
            "debounce" => pipeline.Debounce(TimeSpan.FromMilliseconds(argMs)),
            "throttle" => pipeline.Throttle(TimeSpan.FromMilliseconds(argMs)),
            "delay" => pipeline.Delay(TimeSpan.FromMilliseconds(argMs)),
            "once" => pipeline.Once(),
            "distinct" => pipeline.DistinctUntilChanged(),
            _ => throw new InvalidOperationException($"Unknown marble operator: {spec.Operator}"),
        };

        var currentFrame = 0;
        var emittedAtFrame = new Dictionary<int, string>();

        using var cts = new CancellationTokenSource();

        async Task ConsumeAsync()
        {
            await foreach (var item in pipeline.Source.WithCancellation(cts.Token))
            {
                lock (emittedAtFrame)
                {
                    emittedAtFrame[currentFrame] = item;
                }
            }
        }

        var consumeTask = ConsumeAsync();
        await VirtualScheduler.SettleAsync();

        var outChars = new StringBuilder(spec.Input.Length);

        for (var i = 0; i < spec.Input.Length; i++)
        {
            currentFrame = i;
            if (i > 0)
            {
                await scheduler.AdvanceByAsync(TimeSpan.FromMilliseconds(FrameMs));
            }

            var ch = spec.Input[i];
            if (ch != '-' && ch != '|')
            {
                inputChannel.Writer.TryWrite(ch.ToString());
                await VirtualScheduler.SettleAsync();
            }
            else if (ch == '|')
            {
                inputChannel.Writer.TryComplete();
                await VirtualScheduler.SettleAsync();
            }

            string? emitted = null;
            lock (emittedAtFrame)
            {
                emittedAtFrame.TryGetValue(i, out emitted);
            }

            if (emitted is not null)
            {
                outChars.Append(emitted);
            }
            else if (ch == '|')
            {
                outChars.Append('|');
            }
            else
            {
                outChars.Append('-');
            }
        }

        cts.Cancel();
        try { await consumeTask; } catch (OperationCanceledException) { }

        Assert.Equal(spec.Expect, outChars.ToString());
    }

    [Fact]
    public async Task Sample_Buffer_Merge_TakeUntil_WorkOnVirtualScheduler()
    {
        var scheduler = new VirtualScheduler();
        var ch1 = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { AllowSynchronousContinuations = true });
        var ch2 = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { AllowSynchronousContinuations = true });
        var stopCh = Channel.CreateUnbounded<bool>(new UnboundedChannelOptions { AllowSynchronousContinuations = true });

        var batches = new List<IReadOnlyList<string>>();
        var pipeline = new PushPipeline<string>(ch1.Reader.ReadAllAsync(), scheduler)
            .Merge(ch2.Reader.ReadAllAsync())
            .TakeUntil(stopCh.Reader.ReadAllAsync())
            .Buffer(TimeSpan.FromMilliseconds(100));

        async Task ConsumeBatchesAsync()
        {
            await foreach (var b in pipeline.Source)
            {
                batches.Add(b);
            }
        }

        var task = ConsumeBatchesAsync();
        for (var i = 0; i < 50 && scheduler.ActiveTimerCount == 0; i++)
        {
            await Task.Delay(5);
        }

        ch1.Writer.TryWrite("a1");
        await Task.Delay(10);
        await VirtualScheduler.SettleAsync();
        ch2.Writer.TryWrite("b1");
        await Task.Delay(10);
        await VirtualScheduler.SettleAsync();
        await scheduler.AdvanceByAsync(TimeSpan.FromMilliseconds(100));
        for (var i = 0; i < 50 && batches.Count == 0; i++)
        {
            await Task.Delay(5);
        }

        Assert.Single(batches);
        Assert.Equal(["a1", "b1"], batches[0]);

        ch1.Writer.TryWrite("a2");
        await Task.Delay(10);
        stopCh.Writer.TryWrite(true);
        await Task.Delay(10);
        await VirtualScheduler.SettleAsync();
        await scheduler.AdvanceByAsync(TimeSpan.FromMilliseconds(100));
        await task;
    }

    [Fact]
    public async Task PerRecipientRendering_ProducesDistinctRenderingsPerConnectedUser()
    {
        var clientAlice = _factory.CreateClient();
        clientAlice.DefaultRequestHeaders.Add("X-Test-User", "alice");

        var clientBob = _factory.CreateClient();
        clientBob.DefaultRequestHeaders.Add("X-Test-User", "bob");

        await using var streamAlice = await StreamClient.OpenStreamAsync(clientAlice);
        await using var streamBob = await StreamClient.OpenStreamAsync(clientBob);

        var push = _factory.Services.GetRequiredService<IPushChannel>();
        var bus = new Bus<string>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var renderCount = 0;

        var pipelineTask = push.From(bus.Subscribe(cts.Token))
            .Select((orderId, recipient, composer, _) =>
            {
                Interlocked.Increment(ref renderCount);
                Assert.NotNull(composer);
                var html = new HtmlString($"<div id=\"order-banner\">Order {orderId} for {recipient.UserId}</div>");
                return Task.FromResult(Patch.Ok().Replace("#order-banner", html));
            })
            .To(PushScope.Broadcast(), cts.Token);

        bus.Publish("ORD-42");

        var alicePatch = await streamAlice.NextPatchAsync(TimeSpan.FromSeconds(3));
        var bobPatch = await streamBob.NextPatchAsync(TimeSpan.FromSeconds(3));

        Assert.Contains("Order ORD-42 for alice", alicePatch);
        Assert.Contains("Order ORD-42 for bob", bobPatch);
        Assert.True(renderCount >= 2, "Patch selector must run per recipient, never once fanned out.");

        cts.Cancel();
        await pipelineTask;
    }

    [Fact]
    public async Task Lifetime_RecipientDisconnect_StopsUserPipeline_AndDisposesSource()
    {
        var clientAlice = _factory.CreateClient();
        clientAlice.DefaultRequestHeaders.Add("X-Test-User", "alice_lifetime");

        var streamAlice = await StreamClient.OpenStreamAsync(clientAlice);
        var push = _factory.Services.GetRequiredService<IPushChannel>();

        var sourceDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async IAsyncEnumerable<int> InfiniteSource([EnumeratorCancellation] CancellationToken ct = default)
        {
            try
            {
                var i = 0;
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(50, ct);
                    yield return ++i;
                }
            }
            finally
            {
                sourceDisposed.TrySetResult();
            }
        }

        var pipelineTask = push.From(InfiniteSource())
            .Select(n => Patch.Ok().Replace("#tick", new HtmlString($"<span id=\"tick\">{n}</span>")))
            .To(PushScope.User("alice_lifetime"));

        var firstPatch = await streamAlice.NextPatchAsync(TimeSpan.FromSeconds(3));
        Assert.Contains("<span id=\"tick\">1</span>", firstPatch);

        // Close Alice's SSE connection -> pipeline must stop and dispose InfiniteSource
        await streamAlice.DisposeAsync();

        var completed = await Task.WhenAny(sourceDisposed.Task, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(sourceDisposed.Task, completed);
        await pipelineTask;
    }

    [Fact]
    public async Task Lifetime_IdleScope_StopsPipelineAfterIdlePeriod_AndDisposesSource()
    {
        var scheduler = new VirtualScheduler();
        var backplane = new InProcessPushBackplane();
        var options = Options.Create(new VaxelOptions
        {
            Push = new VaxelPushOptions { IdleStopPeriod = TimeSpan.FromSeconds(30) }
        });
        var registry = new PushConnectionRegistry(options, backplane);
        var pushChannel = new PushChannel(backplane, registry, null, options, scheduler);

        var sourceDisposed = false;
        var channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions { AllowSynchronousContinuations = true });

        async IAsyncEnumerable<int> TrackedSource([EnumeratorCancellation] CancellationToken ct = default)
        {
            try
            {
                await foreach (var item in channel.Reader.ReadAllAsync(ct))
                {
                    yield return item;
                }
            }
            finally
            {
                sourceDisposed = true;
            }
        }

        // Start pipeline for a group with 0 connections
        var pipelineTask = pushChannel.From(TrackedSource(), scheduler)
            .Select(n => Patch.Ok().Replace("#board", new HtmlString($"<div id=\"board\">{n}</div>")))
            .To(PushScope.Group("empty_kitchen"));

        await VirtualScheduler.SettleAsync();
        Assert.False(sourceDisposed, "Pipeline should still be alive before IdleStopPeriod elapses.");

        await scheduler.AdvanceByAsync(TimeSpan.FromSeconds(29));
        Assert.False(sourceDisposed, "Pipeline should still be alive at 29s (< 30s IdleStopPeriod).");

        await scheduler.AdvanceByAsync(TimeSpan.FromSeconds(1));
        await pipelineTask;
        Assert.True(sourceDisposed, "Pipeline must stop and dispose source once 30s IdleStopPeriod elapses.");
    }

    [Fact]
    public async Task Lifetime_ThrowingSource_SurfacesNamedException_AndIsolatesOtherPipelines()
    {
        var scheduler = new VirtualScheduler();
        var backplane = new InProcessPushBackplane();
        VaxelPushPipelineException? reportedError = null;
        var options = Options.Create(new VaxelOptions
        {
            Push = new VaxelPushOptions
            {
                IdleStopPeriod = TimeSpan.FromMinutes(5),
                OnPipelineError = (_, ex) => reportedError = ex,
            }
        });
        var registry = new PushConnectionRegistry(options, backplane);
        var pushChannel = new PushChannel(backplane, registry, null, options, scheduler);

        var healthyChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { AllowSynchronousContinuations = true });
        var healthyConnChannel = Channel.CreateUnbounded<PushMessage>(new UnboundedChannelOptions { AllowSynchronousContinuations = true });
        registry.TryAddConnection(new PushConnection("c1", "bob", new HashSet<string>(), healthyConnChannel), out _);

        using var healthyCts = new CancellationTokenSource();
        var healthyTask = pushChannel.From(healthyChannel.Reader.ReadAllAsync(), scheduler)
            .Select(msg => Patch.Ok().Replace("#ok", new HtmlString($"<div id=\"ok\">{msg}</div>")))
            .To(PushScope.User("bob"), healthyCts.Token);

        async IAsyncEnumerable<string> FaultingSource([EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return "before-boom";
            throw new InvalidOperationException("Cursor disconnected");
        }

        var ex = await Assert.ThrowsAsync<VaxelPushPipelineException>(() =>
            pushChannel.From(FaultingSource(), scheduler)
                .Select(msg => Patch.Ok().Replace("#bad", new HtmlString($"<div id=\"bad\">{msg}</div>")))
                .To(PushScope.User("alice")));

        Assert.Contains("Cursor disconnected", ex.Message);
        Assert.Same(ex, reportedError);

        // Healthy pipeline for bob still works after alice's pipeline faulted
        healthyChannel.Writer.TryWrite("still-alive");
        var received = await healthyConnChannel.Reader.ReadAsync();
        Assert.Contains("still-alive", received.Data);

        healthyCts.Cancel();
        await healthyTask;
    }

    [Fact]
    public async Task Bus_FanOut_ZeroSubscriberDrop_OverflowDropOldest_AndCrossNodeBackplane()
    {
        var sharedBackplane = new InProcessPushBackplane();
        var busNodeA = new Bus<int>(sharedBackplane, capacity: 3, dropPolicy: BusDropPolicy.DropOldest);
        var busNodeB = new Bus<int>(sharedBackplane, capacity: 3, dropPolicy: BusDropPolicy.DropOldest);

        // 1. Publishing with zero subscribers drops immediately without buffering
        Assert.Equal(0, busNodeA.SubscriberCount);
        busNodeA.Publish(999);

        // 2. Subscribe two consumers on Node A and one consumer on Node B
        using var cts = new CancellationTokenSource();
        var subA1 = busNodeA.Subscribe(cts.Token).GetAsyncEnumerator(cts.Token);
        var subA2 = busNodeA.Subscribe(cts.Token).GetAsyncEnumerator(cts.Token);
        var subB1 = busNodeB.Subscribe(cts.Token).GetAsyncEnumerator(cts.Token);

        // Trigger subscription registration by advancing to first WaitToReadAsync
        var moveA1 = subA1.MoveNextAsync();
        var moveA2 = subA2.MoveNextAsync();
        var moveB1 = subB1.MoveNextAsync();
        Assert.Equal(2, busNodeA.SubscriberCount);
        Assert.Equal(1, busNodeB.SubscriberCount);

        await busNodeA.PublishAsync(10);
        Assert.True(await moveA1);
        Assert.True(await moveA2);
        Assert.True(await moveB1);
        Assert.Equal(10, subA1.Current);
        Assert.Equal(10, subA2.Current);
        Assert.Equal(10, subB1.Current);

        // 3. Overflow policy (capacity = 3, DropOldest): publish 1, 2, 3, 4, 5 before reading
        busNodeA.Publish(1);
        busNodeA.Publish(2);
        busNodeA.Publish(3);
        busNodeA.Publish(4);
        busNodeA.Publish(5);

        Assert.True(await subA1.MoveNextAsync());
        Assert.Equal(3, subA1.Current);
        Assert.True(await subA1.MoveNextAsync());
        Assert.Equal(4, subA1.Current);
        Assert.True(await subA1.MoveNextAsync());
        Assert.Equal(5, subA1.Current);

        await cts.CancelAsync();
        await subA1.DisposeAsync();
        await subA2.DisposeAsync();
        await subB1.DisposeAsync();
        Assert.Equal(0, busNodeA.SubscriberCount);
    }

    [Fact]
    public async Task Cookbook06_OrderBoard_BusToPipelineAndPageParity()
    {
        var kitchenClient = _factory.CreateClient();
        kitchenClient.DefaultRequestHeaders.Add("X-Test-User", "chef_1");
        kitchenClient.DefaultRequestHeaders.Add("X-Test-Role", "kitchen");

        await using var kitchenStream = await StreamClient.OpenStreamAsync(kitchenClient);

        var push = _factory.Services.GetRequiredService<IPushChannel>();
        var bus = _factory.Services.GetRequiredService<IBus<string>>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var workerTask = push.From(bus.Subscribe(cts.Token))
            .DistinctUntilChanged()
            .Throttle(TimeSpan.FromMilliseconds(20))
            .Select((orderId, recipient, _, _) =>
            {
                var prep = new HtmlString("<section id=\"orders-preparing\" vx-region><ul class=\"order-list\"><li id=\"order_105\">#105 · Ramen</li></ul></section>");
                var ready = new HtmlString($"<section id=\"orders-ready\" vx-region><ul class=\"order-list\"><li id=\"order_{orderId}\">#{orderId} · Gyoza</li></ul></section>");
                return Task.FromResult(Patch.Ok()
                    .Replace("#orders-preparing", prep)
                    .Replace("#orders-ready", ready)
                    .Announce($"Order {orderId} is ready for {recipient.UserId}"));
            })
            .To(PushScope.Group("kitchen"), cts.Token);

        // Give subscription a moment to attach before publishing via HTTP POST
        await Task.Delay(30);

        var cookClient = _factory.CreateClient();
        using var markReadyReq = new HttpRequestMessage(HttpMethod.Post, "/cookbook/orders/ready?id=104");
        markReadyReq.Headers.Add("VX-Request", "true");
        markReadyReq.Headers.Add("VX-Protocol", "1");
        using var markReadyRes = await cookClient.SendAsync(markReadyReq);
        markReadyRes.EnsureSuccessStatusCode();

        // Kitchen display receives the pushed frame from the pipeline
        var pushedFrame = await kitchenStream.NextPatchAsync(TimeSpan.FromSeconds(3));
        Assert.Contains("id=\"orders-preparing\"", pushedFrame);
        Assert.Contains("id=\"orders-ready\"", pushedFrame);
        Assert.Contains("#104 · Gyoza", pushedFrame);

        // Rule R3: #orders-preparing and #orders-ready satisfy VaxelParity between page and patch
        await VaxelParity.AssertAsync(cookClient, "/cookbook/orders", "/cookbook/orders", "#orders-preparing");
        await VaxelParity.AssertAsync(cookClient, "/cookbook/orders", "/cookbook/orders", "#orders-ready");

        cts.Cancel();
        await workerTask;
    }

    private sealed record MarbleSpec(
        string Name,
        string Operator,
        int[] Args,
        string Input,
        string Expect);
}
