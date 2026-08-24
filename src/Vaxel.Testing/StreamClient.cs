using System.Threading.Channels;

namespace Vaxel.Testing;

public sealed class StreamClient : IAsyncDisposable
{
    private readonly HttpResponseMessage _response;
    private readonly CancellationTokenSource _cts;
    private readonly Channel<string> _patches;
    private readonly Task _readLoopTask;

    private StreamClient(HttpResponseMessage response, CancellationTokenSource cts, Channel<string> patches, Task readLoopTask)
    {
        _response = response;
        _cts = cts;
        _patches = patches;
        _readLoopTask = readLoopTask;
    }

    public static async Task<StreamClient> OpenStreamAsync(
        HttpClient client,
        string url = "/_vaxel/stream",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("text/event-stream");

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var cts = new CancellationTokenSource();
        var patches = Channel.CreateUnbounded<string>();
        var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        var reader = new StreamReader(stream);

        var readLoop = Task.Run(async () =>
        {
            try
            {
                string? currentEvent = null;
                var currentData = new List<string>();

                while (!cts.Token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cts.Token);
                    if (line is null) break;

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        if (currentEvent is not null && currentData.Count > 0)
                        {
                            var fullData = string.Join("\n", currentData);
                            patches.Writer.TryWrite(fullData);
                        }
                        currentEvent = null;
                        currentData.Clear();
                    }
                    else if (line.StartsWith("event: ", StringComparison.Ordinal))
                    {
                        currentEvent = line["event: ".Length..].Trim();
                    }
                    else if (line.StartsWith("data: ", StringComparison.Ordinal))
                    {
                        currentData.Add(line["data: ".Length..]);
                    }
                }
            }
            catch
            {
                // Normal termination on abort / stream close
            }
            finally
            {
                patches.Writer.TryComplete();
            }
        }, cts.Token);

        return new StreamClient(response, cts, patches, readLoop);
    }

    public async Task<string?> NextPatchAsync(TimeSpan? timeout = null)
    {
        var duration = timeout ?? TimeSpan.FromSeconds(5);
        using var timeoutCts = new CancellationTokenSource(duration);

        try
        {
            return await _patches.Reader.ReadAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    public async Task ShouldReceiveNothingWithinAsync(TimeSpan duration)
    {
        using var timeoutCts = new CancellationTokenSource(duration);
        try
        {
            var patch = await _patches.Reader.ReadAsync(timeoutCts.Token);
            throw new InvalidOperationException($"Expected no push patch, but received: {patch}");
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
    }

    public void DropConnection()
    {
        _cts.Cancel();
        _response.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        DropConnection();
        try
        {
            await _readLoopTask;
        }
        catch
        {
            // Ignore cancellation
        }
    }
}
