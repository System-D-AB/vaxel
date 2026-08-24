using System.Net;
using Microsoft.AspNetCore.Razor.TagHelpers;
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
    [InlineData("foo()")]
    [InlineData("a + b")]
    public void ExpressionValue_Rejected(string invalidExpression)
    {
        var tagHelper = new VxBindTagHelper { VxText = invalidExpression };
        var context = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("span", [], (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        var ex = Assert.Throws<VaxelTagHelperException>(() => tagHelper.Process(context, output));
        Assert.Contains("Per Rule R2", ex.Message);
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
}
