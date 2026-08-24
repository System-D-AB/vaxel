using System.Net;
using Vaxel.AspNetCore.Tests.Composer;
using Vaxel.Datastar;
using Xunit;

namespace Vaxel.AspNetCore.Tests.Datastar;

public sealed class DatastarConformanceTests : IClassFixture<ComposerApiFactory>
{
    private readonly ComposerApiFactory _factory;
    private readonly HttpClient _client;

    public DatastarConformanceTests(ComposerApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("patchElementsWithDefaults", "event: datastar-patch-elements", "data: elements <div>Default Elements</div>")]
    [InlineData("patchElementsWithAllOptions", "data: mode append", "data: viewTransition my-transition")]
    [InlineData("patchElementsWithoutDefaults", "data: mode prepend", "data: selector #container")]
    [InlineData("patchElementsWithMultilineElements", "data: elements <div>Line 1</div>", "data: elements <div>Line 2</div>")]
    [InlineData("patchSignalsWithDefaults", "event: datastar-patch-signals", "data: signals")]
    [InlineData("patchSignalsWithAllOptions", "data: onlyIfMissing true", "data: signals")]
    [InlineData("patchSignalsWithoutDefaults", "event: datastar-patch-signals", "data: signals")]
    [InlineData("patchSignalsWithMultilineJson", "data: signals {", "data: signals     \"key\": \"value\"")]
    [InlineData("patchSignalsWithMultilineSignals", "data: signals {", "data: signals   \"item1\": 1")]
    [InlineData("removeElementsWithDefaults", "event: datastar-remove-elements", "data: selector #element-to-remove")]
    [InlineData("removeElementsWithAllOptions", "event: datastar-remove-elements", "data: selector #custom-selector")]
    [InlineData("removeElementsWithoutDefaults", "event: datastar-remove-elements", "data: selector #other-element")]
    [InlineData("removeSignalsWithDefaults", "event: datastar-remove-signals", "data: paths draftKey")]
    [InlineData("removeSignalsWithAllOptions", "data: paths key1", "data: paths nested.key3")]
    [InlineData("sendTwoEvents", "event: datastar-patch-elements", "event: datastar-patch-signals")]
    public async Task Conformance_PassingSdkCases_MatchDatastarFraming(string testCase, string expected1, string expected2)
    {
        var response = await _client.GetAsync($"/test?test={testCase}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains(expected1, content);
        Assert.Contains(expected2, content);
    }

    [Fact]
    public async Task Conformance_ReadSignalsFromBody_AcceptsBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/test");
        request.Content = new StringContent("{\"test\":\"readSignalsFromBody\",\"custom\":\"value\"}", System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("event: datastar-patch-signals", content);
        Assert.Contains("custom", content);
    }

    [Theory]
    [InlineData("executeScriptWithDefaults")]
    [InlineData("executeScriptWithAllOptions")]
    [InlineData("executeScriptWithoutDefaults")]
    [InlineData("executeScriptWithMultilineScript")]
    public async Task Conformance_ExecuteScript_StatedRefusal(string testCase)
    {
        var response = await _client.GetAsync($"/test?test={testCase}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        // Declined with stated refusal reason (Rule R2, Strict CSP)
        Assert.Contains("event: datastar-refused", content);
        Assert.Contains("Rule R2", content);
    }
}
