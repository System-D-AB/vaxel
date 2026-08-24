using Microsoft.AspNetCore.Html;
using Vaxel;
using Xunit;

namespace Vaxel.AspNetCore.Tests.PatchTests;

public sealed class PatchBuilderUnitTests
{
    [Fact]
    public async Task Replace_EmitsMorph()
    {
        var result = Patch.Ok()
            .Replace("#pane", new HtmlString("<section id=\"pane\">content</section>"))
            .Build();

        var html = await result.ToHtmlAsync();

        Assert.Contains("<vx-patch target=\"#pane\" mode=\"morph\">", html);
        Assert.Contains("<section id=\"pane\">content</section>", html);
        Assert.Contains("</vx-patch>", html);
    }

    [Fact]
    public async Task ReplaceHard_EmitsReplace()
    {
        var result = Patch.Ok()
            .ReplaceHard("#widget", new HtmlString("<div id=\"widget\">new widget</div>"))
            .Build();

        var html = await result.ToHtmlAsync();

        Assert.Contains("<vx-patch target=\"#widget\" mode=\"replace\">", html);
        Assert.Contains("<div id=\"widget\">new widget</div>", html);
    }

    [Fact]
    public async Task Remove_HasNoContent()
    {
        var result = Patch.Ok()
            .Remove("#row-42")
            .Build();

        var html = await result.ToHtmlAsync();

        Assert.Contains("<vx-patch target=\"#row-42\" mode=\"remove\"></vx-patch>", html);
        Assert.DoesNotContain("<vx-patch target=\"#row-42\" mode=\"remove\"> ", html);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("pane")]
    [InlineData(".class-name")]
    [InlineData("#pane #sub")]
    [InlineData("div > span")]
    public void Target_MustBeId(string invalidTarget)
    {
        Assert.Throws<VaxelTargetException>(() =>
        {
            Patch.Ok().Replace(invalidTarget, new HtmlString("<p>test</p>"));
        });
    }

    [Fact]
    public async Task Signals_CamelCase()
    {
        var result = Patch.Ok()
            .Signals(new { TabName = "submissions", DraftSeq = 149, IsActive = true })
            .Build();

        var html = await result.ToHtmlAsync();

        Assert.Contains("<vx-signals>{\"tabName\":\"submissions\",\"draftSeq\":149,\"isActive\":true}</vx-signals>", html);
    }

    [Fact]
    public async Task Signals_CannotBreakOutOfElement()
    {
        var malicious = new { Payload = "</vx-signals><script>alert('xss')</script>" };
        var result = Patch.Ok()
            .Signals(malicious)
            .Build();

        var html = await result.ToHtmlAsync();

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("\\u003C/vx-signals\\u003E", html);
    }

    [Fact]
    public async Task Directive_AtMostOne()
    {
        var result = Patch.Ok()
            .Focus("#name-input")
            .Title("New Title")
            .Announce("Saved successfully")
            .PushUrl("/items/42")
            .Build();

        var html = await result.ToHtmlAsync();

        var firstIndex = html.IndexOf("<vx-directive", StringComparison.Ordinal);
        var lastIndex = html.LastIndexOf("<vx-directive", StringComparison.Ordinal);

        Assert.True(firstIndex >= 0);
        Assert.Equal(firstIndex, lastIndex);
        Assert.Contains("focus=\"#name-input\"", html);
        Assert.Contains("title=\"New Title\"", html);
        Assert.Contains("announce=\"Saved successfully\"", html);
        Assert.Contains("push-url=\"/items/42\"", html);
    }

    [Fact]
    public async Task Redirect_DoesNotHttpRedirect()
    {
        var result = Patch.Ok()
            .Redirect("/login")
            .Build();

        var html = await result.ToHtmlAsync();

        Assert.Equal(200, result.StatusCode);
        Assert.Contains("<vx-directive redirect=\"/login\" />", html);
    }

    [Fact]
    public async Task Namespace_Svg()
    {
        var result = Patch.Ok()
            .Replace("#icon", new HtmlString("<path d=\"M0 0\" />"))
            .InNamespace(VaxelNamespace.Svg)
            .Build();

        var html = await result.ToHtmlAsync();

        Assert.Contains("<vx-patch target=\"#icon\" mode=\"morph\" namespace=\"svg\">", html);
    }

    [Fact]
    public async Task Transition_View()
    {
        var result = Patch.Ok()
            .Replace("#gallery", new HtmlString("<div>images</div>"))
            .Transition()
            .Build();

        var html = await result.ToHtmlAsync();

        Assert.Contains("<vx-patch target=\"#gallery\" mode=\"morph\" transition=\"view\">", html);
    }

    [Fact]
    public async Task Refused_409_AppendsNotice()
    {
        var refusal = new Refusal("publish.stale_draft", "The draft moved on after this proposal was raised.");
        var result = Patch.Refused(refusal)
            .Into("#notices", new HtmlString("<div class=\"alert\">Draft stale</div>"))
            .Focus("#notices")
            .Build();

        var html = await result.ToHtmlAsync();

        Assert.Equal(409, result.StatusCode);
        Assert.Contains("<vx-patch target=\"#notices\" mode=\"append\">", html);
        Assert.Contains("<div class=\"alert\">Draft stale</div>", html);
        Assert.Contains("<vx-directive focus=\"#notices\" />", html);
    }

    [Fact]
    public async Task AllSwapModes_EmitCorrectModeString()
    {
        var result = Patch.Ok()
            .Outer("#t1", new HtmlString("<span>1</span>"))
            .Inner("#t2", new HtmlString("<span>2</span>"))
            .Append("#t3", new HtmlString("<span>3</span>"))
            .Prepend("#t4", new HtmlString("<span>4</span>"))
            .Before("#t5", new HtmlString("<span>5</span>"))
            .After("#t6", new HtmlString("<span>6</span>"))
            .Build();

        var html = await result.ToHtmlAsync();

        Assert.Contains("target=\"#t1\" mode=\"outer\"", html);
        Assert.Contains("target=\"#t2\" mode=\"inner\"", html);
        Assert.Contains("target=\"#t3\" mode=\"append\"", html);
        Assert.Contains("target=\"#t4\" mode=\"prepend\"", html);
        Assert.Contains("target=\"#t5\" mode=\"before\"", html);
        Assert.Contains("target=\"#t6\" mode=\"after\"", html);
    }

    [Fact]
    public async Task ScrollDirective_EmitsAttributes()
    {
        var result = Patch.Ok()
            .Scroll("#bottom", behavior: "smooth", block: "center", inline: "nearest", focus: true)
            .Build();

        var html = await result.ToHtmlAsync();

        Assert.Contains("scroll=\"#bottom\"", html);
        Assert.Contains("scroll-behavior=\"smooth\"", html);
        Assert.Contains("scroll-block=\"center\"", html);
        Assert.Contains("scroll-inline=\"nearest\"", html);
        Assert.Contains("scroll-focus=\"1\"", html);
    }

    [Fact]
    public async Task NoDirectives_EmitsNoDirectiveElement()
    {
        var result = Patch.Ok()
            .Replace("#pane", new HtmlString("<p>test</p>"))
            .Build();

        var html = await result.ToHtmlAsync();

        Assert.DoesNotContain("<vx-directive", html);
    }

    [Fact]
    public async Task NoSignals_EmitsNoSignalsElement()
    {
        var result = Patch.Ok()
            .Replace("#pane", new HtmlString("<p>test</p>"))
            .Build();

        var html = await result.ToHtmlAsync();

        Assert.DoesNotContain("<vx-signals", html);
    }
}
