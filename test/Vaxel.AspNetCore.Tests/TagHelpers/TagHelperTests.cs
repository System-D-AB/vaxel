using System.Net;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.TestHost;
using Vaxel;
using Vaxel.AspNetCore.Tests.Composer;
using Vaxel.TagHelpers;
using Xunit;

namespace Vaxel.AspNetCore.Tests.TagHelpers;

public sealed class TagHelperTests : IClassFixture<ComposerApiFactory>
{
    private readonly ComposerApiFactory _factory;
    private readonly HttpClient _client;

    public TagHelperTests(ComposerApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task TagHelperPage_RendersExpectedMarkup()
    {
        var response = await _client.GetAsync("/tag-helper-page");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // 1. Antiforgery meta
        Assert.Contains("<meta name=\"vx-csrf\" content=\"", content);

        // 2. Trigger on anchor with target and vals
        Assert.Contains("<a href=\"/apps/a_1?tab=submissions\"", content);
        Assert.Contains("vx-get", content);
        Assert.Contains("vx-target=\"#pane\"", content);
        Assert.Contains("vx-vals-tab=\"submissions\"", content);

        // 3. Form trigger, indicator and disable
        Assert.Contains("<form method=\"post\" action=\"/contact\"", content);
        Assert.Contains("vx-post", content);
        Assert.Contains("vx-target=\"#contact-form\"", content);
        Assert.Contains("vx-indicator=\"#saving\"", content);
        Assert.Contains("vx-disable", content);

        // 4. Region with id
        Assert.Contains("<section id=\"pane\" vx-region>", content);

        // 5. Reactive bindings
        Assert.Contains("vx-text=\"draftSeq\"", content);
        Assert.Contains("vx-show=\"isVisible\"", content);
        Assert.Contains("vx-class:is-active=\"tabIsActive\"", content);
        Assert.Contains("vx-attr:disabled=\"isSubmitting\"", content);
    }

    [Fact]
    public void Div_VxGet_Fails()
    {
        var tagHelper = new VxTriggerTagHelper();
        var context = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("div", [], (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        var ex = Assert.Throws<VaxelTagHelperException>(() => tagHelper.Process(context, output));
        Assert.Contains("Per Rule R3", ex.Message);
    }

    [Fact]
    public void OnClick_Prohibited()
    {
        var tagHelper = new VxTriggerTagHelper();
        var context = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("a", [new TagHelperAttribute("onclick", "alert('bad')")], (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        var ex = Assert.Throws<VaxelTagHelperException>(() => tagHelper.Process(context, output));
        Assert.Contains("Rule R2 violation", ex.Message);
    }

    [Theory]
    [InlineData("pane")]
    [InlineData(".pane")]
    [InlineData("#pane #child")]
    [InlineData("#pane, #sub")]
    public void Target_MustBeId(string invalidTarget)
    {
        var tagHelper = new VxTargetTagHelper { Target = invalidTarget };
        var context = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("a", [], (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        Assert.Throws<VaxelTagHelperException>(() => tagHelper.Process(context, output));
    }

    [Fact]
    public void Region_RequiresId()
    {
        var tagHelper = new VxRegionTagHelper();
        var context = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("section", [], (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        var ex = Assert.Throws<VaxelTagHelperException>(() => tagHelper.Process(context, output));
        Assert.Contains("must have a non-empty 'id' attribute", ex.Message);
    }

    [Fact]
    public void Vals_Literal()
    {
        var tagHelper = new VxValsTagHelper
        {
            Values = new Dictionary<string, string>
            {
                ["tab"] = "submissions",
                ["query"] = "a > 1" // Literal data, NOT evaluated
            }
        };

        var context = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("a", [], (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        tagHelper.Process(context, output);

        Assert.Equal("submissions", output.Attributes["vx-vals-tab"].Value);
        Assert.Equal("a > 1", output.Attributes["vx-vals-query"].Value);
    }

    [Theory]
    [InlineData("tab == 'submissions'")]
    [InlineData("count > 5")]
    [InlineData("!isActive")]
    [InlineData("a + b")]
    [InlineData("Showing @{ count } rows")]
    [InlineData("tabs.length > 1 ? 'many' : 'one'")]
    public void ValidExpressionValue_Accepted(string validExpression)
    {
        var tagHelper = new VxBindTagHelper { VxText = validExpression };
        var context = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("span", [], (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        tagHelper.Process(context, output);

        Assert.Equal(validExpression, output.Attributes["vx-text"].Value);
    }

    [Theory]
    [InlineData("foo()")]
    [InlineData("2 + + * 3")]
    [InlineData("user[0]")]
    [InlineData("a ?? b")]
    [InlineData("42 + true")]
    [InlineData("Showing @{ } rows")]
    public void InvalidExpressionValue_RejectedWithSpan(string invalidExpression)
    {
        var tagHelper = new VxBindTagHelper { VxText = invalidExpression };
        var context = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("span", [], (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        var ex = Assert.Throws<VaxelTagHelperException>(() => tagHelper.Process(context, output));
        Assert.Contains("span", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpressionExceedingCaps_RejectedAtRenderTime()
    {
        var deepExpr = new string('(', 33) + "1" + new string(')', 33);
        var tagHelper = new VxBindTagHelper { VxShow = deepExpr };
        var context = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("div", [], (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        var ex = Assert.Throws<VaxelTagHelperException>(() => tagHelper.Process(context, output));
        Assert.Contains("expressions.syntax", ex.Message);

        var longExpr = "'" + new string('a', 520) + "'";
        var tagHelperLong = new VxBindTagHelper { VxText = longExpr };
        var exLong = Assert.Throws<VaxelTagHelperException>(() => tagHelperLong.Process(context, output));
        Assert.Contains("expressions.syntax", exLong.Message);
    }

    [Theory]
    [InlineData("href")]
    [InlineData("src")]
    [InlineData("action")]
    [InlineData("formaction")]
    [InlineData("style")]
    [InlineData("onclick")]
    [InlineData("onmouseover")]
    [InlineData("ONLOAD")]
    public void ForbiddenBoundAttributes_RefusedAtRenderTime(string forbiddenAttr)
    {
        var tagHelper = new VxBindTagHelper
        {
            Attributes = new Dictionary<string, string> { [forbiddenAttr] = "targetUrl" }
        };
        var context = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("a", [], (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        var ex = Assert.Throws<VaxelTagHelperException>(() => tagHelper.Process(context, output));
        Assert.Contains("Rule R2", ex.Message);
    }

    [Fact]
    public void Text_EmitsName()
    {
        var tagHelper = new VxBindTagHelper { VxText = "draftSeq" };
        var context = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("span", [], (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        tagHelper.Process(context, output);

        Assert.Equal("draftSeq", output.Attributes["vx-text"].Value);
    }

    [Fact]
    public void ClassColon_Emits()
    {
        var tagHelper = new VxBindTagHelper
        {
            Classes = new Dictionary<string, string> { ["is-active"] = "tabIsActive" }
        };
        var context = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("div", [], (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        tagHelper.Process(context, output);

        Assert.Equal("tabIsActive", output.Attributes["vx-class:is-active"].Value);
    }

    [Fact]
    public async Task CookbookMarkup_NoHxAttributes()
    {
        var response = await _client.GetAsync("/tag-helper-page");
        var content = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("hx-get", content);
        Assert.DoesNotContain("hx-post", content);
        Assert.DoesNotContain("hx-target", content);
        Assert.DoesNotContain("hx-swap", content);
    }

    [Fact]
    public void DriverScript_Exists()
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Vaxel.AspNetCore", "wwwroot", "_vaxel", "vaxel-htmx.js");
        Assert.True(File.Exists(scriptPath) || File.Exists(Path.GetFullPath(scriptPath)));
    }

    // US-7: the antiforgery meta must carry the configured header name in data-header, and the
    // agent reads it so AddAntiforgery(o => o.HeaderName = ...) and vaxel agree.

    // US-6 (T14): the passthrough attributes now have Tag Helpers (the seam docs/05 promises).

    [Theory]
    [InlineData("vx-signals")]
    [InlineData("vx-signals-if-missing")]
    public void Signals_ValidJsonObject_PassesThrough(string attribute)
    {
        var output = ProcessAttributeHelper(attribute, "{\"tab\":\"overview\"}");
        Assert.Equal("{\"tab\":\"overview\"}", output.Attributes[attribute].Value?.ToString());
    }

    [Theory]
    [InlineData("vx-signals")]
    [InlineData("vx-signals-if-missing")]
    public void Signals_InvalidJson_Throws(string attribute)
    {
        Assert.Throws<VaxelTagHelperException>(() => ProcessAttributeHelper(attribute, "{not json"));
    }

    [Theory]
    [InlineData("vx-signals")]
    [InlineData("vx-signals-if-missing")]
    public void Signals_NonObjectJson_Throws(string attribute)
    {
        Assert.Throws<VaxelTagHelperException>(() => ProcessAttributeHelper(attribute, "[1,2,3]"));
    }

    [Theory]
    [InlineData("vx-persist")]
    [InlineData("vx-persist-session")]
    public void Persist_NameList_PassesThrough(string attribute)
    {
        var output = ProcessAttributeHelper(attribute, "railOpen theme");
        Assert.Equal("railOpen theme", output.Attributes[attribute].Value?.ToString());
    }

    [Theory]
    [InlineData("vx-persist")]
    [InlineData("vx-persist-session")]
    public void Persist_Wildcard_Throws(string attribute)
    {
        Assert.Throws<VaxelTagHelperException>(() => ProcessAttributeHelper(attribute, "*"));
    }

    [Fact]
    public void Confirm_And_Preserve_PassThrough()
    {
        Assert.Equal("Delete this?", ProcessAttributeHelper("vx-confirm", "Delete this?").Attributes["vx-confirm"].Value?.ToString());
        Assert.Equal("", ProcessAttributeHelper("vx-preserve", "").Attributes["vx-preserve"].Value?.ToString());
    }

    [Fact]
    public void Sse_Url_PassesThrough()
    {
        var output = ProcessAttributeHelper("vx-sse", "/_vaxel/stream");
        Assert.Equal("/_vaxel/stream", output.Attributes["vx-sse"].Value?.ToString());
    }

    [Fact]
    public void Sse_Empty_Throws()
    {
        Assert.Throws<VaxelTagHelperException>(() => ProcessAttributeHelper("vx-sse", ""));
    }

    private static TagHelperOutput ProcessAttributeHelper(string attribute, string value)
    {
        TagHelper helper = attribute switch
        {
            "vx-signals" => new VxSignalsTagHelper(),
            "vx-signals-if-missing" => new VxSignalsIfMissingTagHelper(),
            "vx-persist" => new VxPersistTagHelper(),
            "vx-persist-session" => new VxPersistSessionTagHelper(),
            "vx-confirm" => new VxConfirmTagHelper(),
            "vx-preserve" => new VxPreserveTagHelper(),
            "vx-sse" => new VxSseTagHelper(),
            _ => throw new ArgumentOutOfRangeException(nameof(attribute)),
        };

        var context = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput(
            "div",
            [new TagHelperAttribute(attribute, value)],
            (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        helper.Process(context, output);
        return output;
    }

    [Fact]
    public void Antiforgery_Meta_CarriesDefaultHeaderName()
    {
        var output = RenderAntiforgeryMeta(new VaxelOptions());

        Assert.Equal("vx-csrf", output.Attributes["name"].Value?.ToString());
        Assert.Equal("X-CSRF", output.Attributes["data-header"].Value?.ToString());
    }

    [Fact]
    public void Antiforgery_Meta_CarriesConfiguredHeaderName()
    {
        var output = RenderAntiforgeryMeta(new VaxelOptions { AntiforgeryHeaderName = "X-My-CSRF" });

        Assert.Equal("X-My-CSRF", output.Attributes["data-header"].Value?.ToString());
    }

    [Theory]
    [InlineData("input | debounce 200 | distinct")]
    [InlineData("keydown window | key Escape | set open = false")]
    [InlineData("visible | once")]
    [InlineData("load | set initialized = true")]
    [InlineData("interval 5000")]
    [InlineData("input | filter len(value) >= 2 && key != 'Escape' | debounce 200")]
    [InlineData("click outside | set menuOpen = false")]
    [InlineData("keydown | key Enter | prevent | stop | set count = count + 1")]
    public void VxOn_ValidChains_Accepted(string validChain)
    {
        var tagHelper = new VxOnTagHelper { VxOn = validChain };
        var context = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("input", [], (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        tagHelper.Process(context, output);

        Assert.Equal(validChain, output.Attributes["vx-on"].Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("input | bogus 100")]
    [InlineData("input | debounce fast")]
    [InlineData("input | debounce -5")]
    [InlineData("interval")]
    [InlineData("interval 0")]
    [InlineData("visible window")]
    [InlineData("click badscope")]
    [InlineData("input | filter 42 + 1")]
    [InlineData("input | filter foo()")]
    [InlineData("input | set open = false | once")]
    [InlineData("input | set 123 = true")]
    [InlineData("input | once 1")]
    [InlineData("input | | distinct")]
    public void VxOn_InvalidChains_RejectedWithSpan(string invalidChain)
    {
        var tagHelper = new VxOnTagHelper { VxOn = invalidChain };
        var context = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("input", [], (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        var ex = Assert.Throws<VaxelTagHelperException>(() => tagHelper.Process(context, output));
        Assert.Contains("span", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VxOn_WithRegisteredSignalSchema_ValidatesFilterAndSetTypes()
    {
        var options = new VaxelOptions
        {
            SignalSchema = new SignalSchema<SampleTriggerSignals>()
        };
        var optsWrapper = Microsoft.Extensions.Options.Options.Create(options);

        // Valid chain against schema + event props (value, key, checked)
        var validHelper = new VxOnTagHelper(optsWrapper)
        {
            VxOn = "input | filter len(value) >= minLen | set query = trim(value)"
        };
        var context = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("input", [], (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        validHelper.Process(context, output);
        Assert.Equal("input | filter len(value) >= minLen | set query = trim(value)", output.Attributes["vx-on"].Value);

        // Unknown signal in set sink
        var unknownTargetHelper = new VxOnTagHelper(optsWrapper)
        {
            VxOn = "click | set nonexistent = 'x'"
        };
        var exUnknown = Assert.Throws<VaxelTagHelperException>(() => unknownTargetHelper.Process(context, output));
        Assert.Contains("nonexistent", exUnknown.Message);

        // Type mismatch in set sink (assigning string to int signal minLen)
        var mismatchHelper = new VxOnTagHelper(optsWrapper)
        {
            VxOn = "click | set minLen = 'not-a-number'"
        };
        var exMismatch = Assert.Throws<VaxelTagHelperException>(() => mismatchHelper.Process(context, output));
        Assert.Contains("expressions.type", exMismatch.Message);
    }

    private sealed record SampleTriggerSignals(int MinLen, string Query, bool Open);

    [Fact]
    public void VxSignals_AcceptsDynamicCSharpJsonString_WithoutHtmlEncodingFailure()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { tabs = Array.Empty<string>(), theme = "auto" });

        var signalsHelper = new VxSignalsTagHelper { Signals = json };
        var ifMissingHelper = new VxSignalsIfMissingTagHelper { SignalsIfMissing = json };

        var ctx = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var out1 = new TagHelperOutput(
            "body",
            [new TagHelperAttribute("vx-signals", new Microsoft.AspNetCore.Html.HtmlString("{&quot;tabs&quot;:[]}"))],
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        var out2 = new TagHelperOutput(
            "body",
            [new TagHelperAttribute("vx-signals-if-missing", new Microsoft.AspNetCore.Html.HtmlString("{&quot;tabs&quot;:[]}"))],
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        signalsHelper.Process(ctx, out1);
        ifMissingHelper.Process(ctx, out2);

        Assert.Equal(json, out1.Attributes["vx-signals"].Value);
        Assert.Equal(json, out2.Attributes["vx-signals-if-missing"].Value);
    }

    [Fact]
    public void VxOn_AcceptsMultiChainAndDialogFocusSinks()
    {
        var options = new VaxelOptions
        {
            SignalSchema = new SignalSchema<SampleTriggerSignals>(),
        };
        var helper = new VxOnTagHelper(Microsoft.Extensions.Options.Options.Create(options))
        {
            VxOn = "click | set query = ''; click | showModal #drop-db; keydown window | key Escape | close #drop-db; keydown | key Enter | focus #search-box",
        };

        var ctx = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("button", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        helper.Process(ctx, output);
        Assert.Contains("showModal #drop-db", output.Attributes["vx-on"].Value?.ToString());
    }

    [Theory]
    [InlineData("click | showModal drop-db")]
    [InlineData("click | focus .not-an-id")]
    [InlineData("click | close invalid-selector")]
    [InlineData("click | showModal #drop-db | debounce 100")]
    public void VxOn_RefusesInvalidDomSinkSelectorOrNonFinalSink(string invalidChain)
    {
        var helper = new VxOnTagHelper { VxOn = invalidChain };
        var ctx = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("button", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        Assert.Throws<VaxelTagHelperException>(() => helper.Process(ctx, output));
    }

    [Fact]
    public void VxBind_AllowsDatasetPropertyAccess_WithRegisteredSignalSchema()
    {
        var options = new VaxelOptions
        {
            SignalSchema = new SignalSchema<SampleTriggerSignals>(),
        };
        var helper = new VxBindTagHelper(Microsoft.Extensions.Options.Options.Create(options));
        helper.Attributes["disabled"] = "query != dataset.match";

        var ctx = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("button", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        helper.Process(ctx, output);
        Assert.Equal("query != dataset.match", output.Attributes["vx-attr:disabled"].Value);
    }

    [Fact]
    public void VxOn_AllowsModifierKeysAndTypingGuard_WithRegisteredSignalSchema()
    {
        var options = new VaxelOptions
        {
            SignalSchema = new SignalSchema<SampleTriggerSignals>(),
        };
        var helper = new VxOnTagHelper(Microsoft.Extensions.Options.Options.Create(options))
        {
            VxOn = "keydown window | key k | filter ctrlKey || metaKey | prevent | showModal #palette; keydown window | key / | filter !typing && !altKey && !shiftKey | prevent | focus #tree-filter"
        };

        var ctx = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("body", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        helper.Process(ctx, output);
        Assert.Contains("filter ctrlKey || metaKey", output.Attributes["vx-on"].Value.ToString());
        Assert.Contains("filter !typing && !altKey && !shiftKey", output.Attributes["vx-on"].Value.ToString());
    }

    [Fact]
    public void VxIndicatorTagHelper_WithRegisteredSignalSchema_ValidatesSignalName()
    {
        var options = new VaxelOptions
        {
            SignalSchema = new SignalSchema<SampleTriggerSignals>(),
        };
        var optsWrapper = Microsoft.Extensions.Options.Options.Create(options);

        // Known signal 'open' passes
        var validHelper = new VxIndicatorTagHelper(optsWrapper) { Indicator = "open" };
        var ctx = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var outValid = new TagHelperOutput("form", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        validHelper.Process(ctx, outValid);
        Assert.Equal("open", outValid.Attributes["vx-indicator"].Value);

        // Selector '#spinner' passes even with schema
        var selHelper = new VxIndicatorTagHelper(optsWrapper) { Indicator = "#spinner" };
        var outSel = new TagHelperOutput("form", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        selHelper.Process(ctx, outSel);
        Assert.Equal("#spinner", outSel.Attributes["vx-indicator"].Value);

        // Unknown signal 'unknownBusy' throws VaxelTagHelperException when schema is registered
        var unknownHelper = new VxIndicatorTagHelper(optsWrapper) { Indicator = "unknownBusy" };
        var outUnknown = new TagHelperOutput("form", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        var ex = Assert.Throws<VaxelTagHelperException>(() => unknownHelper.Process(ctx, outUnknown));
        Assert.Contains("unknownBusy", ex.Message);
    }

    [Fact]
    public void VxIndicatorTagHelper_DisableSelectorAndProgressValidation()
    {
        var ctx = new TagHelperContext([], new Dictionary<object, object>(), "test");

        // S9: vx-disable="#run" passes
        var disableIdHelper = new VxIndicatorTagHelper { Disable = "#run" };
        var outDisableId = new TagHelperOutput("form", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        disableIdHelper.Process(ctx, outDisableId);
        Assert.Equal("#run", outDisableId.Attributes["vx-disable"].Value);

        // S9: vx-disable="not-an-id" throws VaxelTagHelperException
        var badDisableHelper = new VxIndicatorTagHelper { Disable = "run" };
        var outBadDisable = new TagHelperOutput("form", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        Assert.Throws<VaxelTagHelperException>(() => badDisableHelper.Process(ctx, outBadDisable));

        // S11: vx-progress="0", "150", "off", "immediate" pass; invalid string throws
        var progHelper = new VxIndicatorTagHelper { Progress = "150" };
        var outProg = new TagHelperOutput("form", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        progHelper.Process(ctx, outProg);
        Assert.Equal("150", outProg.Attributes["vx-progress"].Value);

        var badProgHelper = new VxIndicatorTagHelper { Progress = "invalid" };
        var outBadProg = new TagHelperOutput("form", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        Assert.Throws<VaxelTagHelperException>(() => badProgHelper.Process(ctx, outBadProg));
    }

    [Fact]
    public void VxTransitionTagHelper_ValidatesTransitionNameAndMode()
    {
        var ctx = new TagHelperContext([], new Dictionary<object, object>(), "test");

        var valid = new VxTransitionTagHelper { TransitionName = "hero-card", Transition = "on" };
        var outValid = new TagHelperOutput("div", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        valid.Process(ctx, outValid);
        Assert.Equal("hero-card", outValid.Attributes["vx-transition-name"].Value);
        Assert.Equal("on", outValid.Attributes["vx-transition"].Value);

        var badName = new VxTransitionTagHelper { TransitionName = "123 invalid" };
        var outBadName = new TagHelperOutput("div", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        Assert.Throws<VaxelTagHelperException>(() => badName.Process(ctx, outBadName));

        var badMode = new VxTransitionTagHelper { Transition = "spin" };
        var outBadMode = new TagHelperOutput("div", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        Assert.Throws<VaxelTagHelperException>(() => badMode.Process(ctx, outBadMode));
    }

    [Fact]
    public void VxBoostTagHelper_ValidatesBoostAndExcludePrefixes()
    {
        var ctx = new TagHelperContext([], new Dictionary<object, object>(), "test");

        var valid = new VxBoostTagHelper { Boost = "on", BoostExclude = "/auth /checkout, /api" };
        var outValid = new TagHelperOutput("body", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        valid.Process(ctx, outValid);
        Assert.Equal("on", outValid.Attributes["vx-boost"].Value);
        Assert.Equal("/auth /checkout, /api", outValid.Attributes["vx-boost-exclude"].Value);

        var badBoost = new VxBoostTagHelper { Boost = "invalid" };
        var outBadBoost = new TagHelperOutput("body", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        Assert.Throws<VaxelTagHelperException>(() => badBoost.Process(ctx, outBadBoost));

        var badExclude = new VxBoostTagHelper { BoostExclude = "relative-path" };
        var outBadExclude = new TagHelperOutput("body", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        Assert.Throws<VaxelTagHelperException>(() => badExclude.Process(ctx, outBadExclude));
    }

    [Fact]
    public void VaxelRequest_ExposesIsPatch_IsHistoryRestore_Target_AndShellPatches()
    {
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.Headers[VaxelProtocol.RequestHeader] = "1";
        httpContext.Request.Headers[VaxelProtocol.ProtocolHeader] = "1";
        httpContext.Request.Headers[VaxelProtocol.HistoryHeader] = "restore";
        httpContext.Request.Headers[VaxelProtocol.TargetHeader] = "#content";

        var accessor = new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = httpContext };
        var vx = new VaxelRequest(accessor);

        Assert.True(vx.IsPatch);
        Assert.True(vx.IsHistoryRestore);
        Assert.Equal("#content", vx.Target);

        vx.AddShellPatch("#badge", "<span id=\"badge\">2</span>");
        Assert.Single(vx.ShellPatches);

        using var sw = new StringWriter();
        vx.ShellPatches[0].WriteTo(sw, System.Text.Encodings.Web.HtmlEncoder.Default);
        Assert.Equal("<vx-patch target=\"#badge\" mode=\"morph\"><span id=\"badge\">2</span></vx-patch>", sw.ToString());
    }

    [Fact]
    public async Task UseVaxel_ShortCircuitsFullPagePaths_AndAddsVaryHeaderOnHtml()
    {
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddVaxel(o =>
        {
            o.FullPagePaths.Add("/auth");
            o.FullPagePaths.Add("/checkout");
        });

        await using var app = builder.Build();
        app.UseVaxel();
        app.MapGet("/checkout/pay", () => Microsoft.AspNetCore.Http.Results.Content("<html>Checkout</html>", "text/html"));
        app.MapGet("/items", () => Microsoft.AspNetCore.Http.Results.Content("<html>Items</html>", "text/html"));
        await app.StartAsync();

        using var client = app.GetTestClient();

        // 1. Patch request to a FullPagePath short-circuits with <vx-directive reload="1" />
        using var reqPatch = new HttpRequestMessage(HttpMethod.Get, "/checkout/pay");
        reqPatch.Headers.Add("VX-Request", "1");
        reqPatch.Headers.Add("VX-Protocol", "1");
        var resPatch = await client.SendAsync(reqPatch);
        var bodyPatch = await resPatch.Content.ReadAsStringAsync();

        Assert.Equal("text/vnd.vaxel-patch+html; charset=utf-8", resPatch.Content.Headers.ContentType?.ToString());
        Assert.Equal("<vx-directive reload=\"1\" />", bodyPatch);
        Assert.Contains("VX-Request", resPatch.Headers.Vary);

        // 2. Full-page HTML response on /items includes Vary: VX-Request
        var resHtml = await client.GetAsync("/items");
        Assert.Contains("VX-Request", resHtml.Headers.Vary);
    }

    private static TagHelperOutput RenderAntiforgeryMeta(VaxelOptions options)
    {
        var antiforgery = new StubAntiforgery();
        var tagHelper = new VaxelAntiforgeryTagHelper(
            antiforgery,
            Microsoft.Extensions.Options.Options.Create(options))
        {
            ViewContext = null
        };

        var context = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput(
            "meta",
            [],
            (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        tagHelper.Process(context, output);
        return output;
    }

    private sealed class StubAntiforgery : Microsoft.AspNetCore.Antiforgery.IAntiforgery
    {
        public Microsoft.AspNetCore.Antiforgery.AntiforgeryTokenSet GetAndStoreTokens(Microsoft.AspNetCore.Http.HttpContext httpContext)
            => new("token", "cookie", "field", "header");

        public Microsoft.AspNetCore.Antiforgery.AntiforgeryTokenSet GetTokens(Microsoft.AspNetCore.Http.HttpContext httpContext)
            => new("token", "cookie", "field", "header");

        public Task<bool> IsRequestValidAsync(Microsoft.AspNetCore.Http.HttpContext httpContext) => Task.FromResult(true);
        public void SetCookieTokenAndHeader(Microsoft.AspNetCore.Http.HttpContext httpContext) { }
        public Task ValidateRequestAsync(Microsoft.AspNetCore.Http.HttpContext httpContext) => Task.CompletedTask;
    }
}
