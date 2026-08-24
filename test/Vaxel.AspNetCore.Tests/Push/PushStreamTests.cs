using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Vaxel;
using Vaxel.AspNetCore.Tests.Composer;
using Vaxel.Testing;
using Xunit;

namespace Vaxel.AspNetCore.Tests.Push;

public sealed class PushStreamTests : IClassFixture<ComposerApiFactory>
{
    private readonly ComposerApiFactory _factory;

    public PushStreamTests(ComposerApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Stream_Unauthenticated_Refused()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/_vaxel/stream");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Stream_ContentType_EventStream()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "alice");

        var request = new HttpRequestMessage(HttpMethod.Get, "/_vaxel/stream");
        request.Headers.Accept.ParseAdd("text/event-stream");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/event-stream", response.Content.Headers.ContentType?.ToString());
        Assert.Contains("no-cache", response.Headers.CacheControl?.ToString());
        Assert.Contains("no-transform", response.Headers.CacheControl?.ToString());
        Assert.True(response.Headers.Contains("X-Accel-Buffering"));
    }

    [Fact]
    public async Task Push_User_DoesNotLeakToOtherUser()
    {
        var clientAlice = _factory.CreateClient();
        clientAlice.DefaultRequestHeaders.Add("X-Test-User", "alice");

        var clientBob = _factory.CreateClient();
        clientBob.DefaultRequestHeaders.Add("X-Test-User", "bob");

        await using var streamAlice = await StreamClient.OpenStreamAsync(clientAlice);
        await using var streamBob = await StreamClient.OpenStreamAsync(clientBob);

        var pushChannel = _factory.Services.GetRequiredService<IPushChannel>();

        // Push targeted exclusively to Alice
        await pushChannel.PushAsync(
            PushScope.User("alice"),
            Patch.Ok().Replace("#pane", new Microsoft.AspNetCore.Html.HtmlString("<p>Alice only update</p>")));

        var alicePatch = await streamAlice.NextPatchAsync(TimeSpan.FromSeconds(3));
        Assert.NotNull(alicePatch);
        Assert.Contains("Alice only update", alicePatch);

        // Verify Bob receives nothing
        await streamBob.ShouldReceiveNothingWithinAsync(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task Push_Broadcast_ReachesAll()
    {
        var client1 = _factory.CreateClient();
        client1.DefaultRequestHeaders.Add("X-Test-User", "user_1");

        var client2 = _factory.CreateClient();
        client2.DefaultRequestHeaders.Add("X-Test-User", "user_2");

        await using var stream1 = await StreamClient.OpenStreamAsync(client1);
        await using var stream2 = await StreamClient.OpenStreamAsync(client2);

        var pushChannel = _factory.Services.GetRequiredService<IPushChannel>();

        await pushChannel.PushAsync(
            PushScope.Broadcast(),
            Patch.Ok().Replace("#banner", new Microsoft.AspNetCore.Html.HtmlString("<div>System announcement</div>")));

        var patch1 = await stream1.NextPatchAsync(TimeSpan.FromSeconds(3));
        var patch2 = await stream2.NextPatchAsync(TimeSpan.FromSeconds(3));

        Assert.NotNull(patch1);
        Assert.Contains("System announcement", patch1);

        Assert.NotNull(patch2);
        Assert.Contains("System announcement", patch2);
    }

    [Fact]
    public async Task Push_Group_ReachesRoleMembers()
    {
        var approver = _factory.CreateClient();
        approver.DefaultRequestHeaders.Add("X-Test-User", "manager_1");
        approver.DefaultRequestHeaders.Add("X-Test-Role", "approver");

        var viewer = _factory.CreateClient();
        viewer.DefaultRequestHeaders.Add("X-Test-User", "viewer_1");
        viewer.DefaultRequestHeaders.Add("X-Test-Role", "viewer");

        await using var streamApprover = await StreamClient.OpenStreamAsync(approver);
        await using var streamViewer = await StreamClient.OpenStreamAsync(viewer);

        var pushChannel = _factory.Services.GetRequiredService<IPushChannel>();

        await pushChannel.PushAsync(
            PushScope.Group("approver"),
            Patch.Ok().Replace("#proposals", new Microsoft.AspNetCore.Html.HtmlString("<p>New pending approval</p>")));

        var approverPatch = await streamApprover.NextPatchAsync(TimeSpan.FromSeconds(3));
        Assert.NotNull(approverPatch);
        Assert.Contains("New pending approval", approverPatch);

        await streamViewer.ShouldReceiveNothingWithinAsync(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task Stream_FifthConnection_Refused()
    {
        var streams = new List<StreamClient>();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "heavy_user");

        try
        {
            // Open 4 connections (MaxConnectionsPerIdentity is 4)
            for (int i = 0; i < 4; i++)
            {
                streams.Add(await StreamClient.OpenStreamAsync(client));
            }

            // 5th connection must fail with 429 Too Many Requests
            var fifthRequest = new HttpRequestMessage(HttpMethod.Get, "/_vaxel/stream");
            fifthRequest.Headers.Accept.ParseAdd("text/event-stream");

            var response = await client.SendAsync(fifthRequest);
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
        finally
        {
            foreach (var s in streams)
            {
                await s.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task FakeBackplane_Publish_Called()
    {
        var fakeBackplane = new FakeBackplane();
        var pushChannel = new PushChannel(fakeBackplane);

        await pushChannel.PushAsync(PushScope.User("charlie"), Patch.Ok().Replace("#x", new Microsoft.AspNetCore.Html.HtmlString("<div>test</div>")));

        Assert.True(fakeBackplane.PublishCalled);
        Assert.Equal("vx-patch", fakeBackplane.LastEventName);
        Assert.Contains("<div>test</div>", fakeBackplane.LastData);
    }

    private sealed class FakeBackplane : IPushBackplane
    {
        public bool PublishCalled { get; private set; }
        public string? LastEventName { get; private set; }
        public string? LastData { get; private set; }

        public Task PublishAsync(PushScope scope, string eventName, string data, CancellationToken ct = default)
        {
            PublishCalled = true;
            LastEventName = eventName;
            LastData = data;
            return Task.CompletedTask;
        }

        public void Subscribe(Func<PushScope, string, string, Task> onMessage) { }
    }
}
