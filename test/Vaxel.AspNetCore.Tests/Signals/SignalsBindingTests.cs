using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Vaxel;
using Vaxel.AspNetCore.Tests.Composer;
using Vaxel.AspNetCore.Tests.Fixtures.ComposerHost;
using Xunit;

namespace Vaxel.AspNetCore.Tests.Signals;

public sealed class SignalsBindingTests : IClassFixture<ComposerApiFactory>
{
    private readonly ComposerApiFactory _factory;
    private readonly HttpClient _client;

    public SignalsBindingTests(ComposerApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public void Reader_TryGet_CaseInsensitive()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["VX-Signals"] = "{\"myTab\":\"analytics\",\"COUNT\":42}";
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var options = Options.Create(new VaxelOptions());

        var reader = new SignalReader(accessor, options);

        Assert.True(reader.TryGet<string>("mytab", out var tab1));
        Assert.Equal("analytics", tab1);

        Assert.True(reader.TryGet<string>("MYTAB", out var tab2));
        Assert.Equal("analytics", tab2);

        Assert.True(reader.TryGet<int>("count", out var count));
        Assert.Equal(42, count);
    }

    [Fact]
    public void Reader_Missing_ReturnsFalse()
    {
        var httpContext = new DefaultHttpContext();
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var options = Options.Create(new VaxelOptions());

        var reader = new SignalReader(accessor, options);

        Assert.False(reader.TryGet<string>("nonexistent", out var value));
        Assert.Null(value);
        Assert.Equal("fallback", reader.Get("nonexistent", "fallback"));
    }

    [Fact]
    public async Task Binds_CamelCaseAndInsensitive()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/signals/controller");
        request.Headers.Add("VX-Signals", "{\"tab\":\"submissions\",\"railOpen\":false,\"count\":\"15\"}");

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(content);
        Assert.Equal("submissions", doc.RootElement.GetProperty("tab").GetString());
        Assert.False(doc.RootElement.GetProperty("railOpen").GetBoolean());
        Assert.Equal(15, doc.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task UnknownKeys_Ignored()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/signals/controller");
        request.Headers.Add("VX-Signals", "{\"tab\":\"inbox\",\"unknownField\":\"ignored\",\"anotherUnknown\":123}");

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(content);
        Assert.Equal("inbox", doc.RootElement.GetProperty("tab").GetString());
    }

    [Fact]
    public async Task MalformedJson_DefaultsNot500()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/signals/controller");
        request.Headers.Add("VX-Signals", "{not:valid:json!;;");

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(content);
        // Default values from ShellSignals
        Assert.Equal("overview", doc.RootElement.GetProperty("tab").GetString());
        Assert.True(doc.RootElement.GetProperty("railOpen").GetBoolean());
    }

    [Fact]
    public async Task Omitted_Defaults()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/signals/controller");
        request.Headers.Add("VX-Signals-Omitted", "1");
        request.Headers.Add("VX-Signals", "{\"tab\":\"should_be_ignored\"}");

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(content);
        Assert.Equal("overview", doc.RootElement.GetProperty("tab").GetString());
    }

    [Fact]
    public async Task Oversize_Defaults()
    {
        var oversizeJson = "{\"tab\":\"" + new string('x', 9000) + "\"}";
        var request = new HttpRequestMessage(HttpMethod.Get, "/signals/controller");
        request.Headers.Add("VX-Signals", oversizeJson);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(content);
        Assert.Equal("overview", doc.RootElement.GetProperty("tab").GetString());
    }

    [Fact]
    public async Task FormBody_Untouched()
    {
        var formContent = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("Name", "Alice")
        ]);

        var request = new HttpRequestMessage(HttpMethod.Post, "/signals-page");
        request.Content = formContent;
        request.Headers.Add("VX-Signals", "{\"tab\":\"profile\"}");

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Name:Alice,Tab:profile", content);
    }

    [Fact]
    public async Task QueryString_NotTheBag()
    {
        var response = await _client.GetAsync("/signals/controller?vx-signals={\"tab\":\"from_query\"}");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(content);
        // Did NOT read from query string; defaults to "overview"
        Assert.Equal("overview", doc.RootElement.GetProperty("tab").GetString());
    }

    [Fact]
    public async Task FormField_NotTheBag()
    {
        var formContent = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("Name", "Bob"),
            new KeyValuePair<string, string>("vx-signals", "{\"tab\":\"from_form\"}")
        ]);

        var request = new HttpRequestMessage(HttpMethod.Post, "/signals-page");
        request.Content = formContent;

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Did NOT read from form field vx-signals; defaults to "overview"
        Assert.Equal("Name:Bob,Tab:overview", content);
    }

    [Fact]
    public async Task FromSignals_SetsNoStore()
    {
        var response = await _client.GetAsync("/signals/controller");
        Assert.True(response.Headers.CacheControl?.NoStore == true || response.Headers.ToString().Contains("no-store"));
    }

    [Fact]
    public async Task ReaderTouch_SetsNoStore()
    {
        var response = await _client.GetAsync("/signals/reader");
        Assert.True(response.Headers.CacheControl?.NoStore == true || response.Headers.ToString().Contains("no-store"));
    }

    [Fact]
    public async Task NoSignals_NoNoStore()
    {
        var response = await _client.GetAsync("/plain-route");
        var cacheControl = response.Headers.CacheControl?.ToString();
        Assert.False(cacheControl?.Contains("no-store") == true);
    }

    [Fact]
    public async Task HandlerOverride_Wins()
    {
        var response = await _client.GetAsync("/signals/cache-override");
        var cacheControl = response.Headers.CacheControl?.ToString();
        Assert.Contains("public", cacheControl);
        Assert.Contains("max-age=3600", cacheControl);
    }
}
