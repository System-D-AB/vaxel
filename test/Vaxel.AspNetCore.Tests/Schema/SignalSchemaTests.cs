using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;
using Vaxel;
using Vaxel.AspNetCore.Tests.Fixtures.ComposerHost;
using Vaxel.TagHelpers;
using Xunit;

namespace Vaxel.AspNetCore.Tests.Schema;

public sealed class SignalSchemaTests
{
    [Fact]
    public void Schema_DiscoversAllPublicProperties()
    {
        var schema = new SignalSchema<ShellSignals>();

        Assert.Equal("ShellSignals", schema.TypeName);
        Assert.True(schema.IsAllowed("tab"));
        Assert.True(schema.IsAllowed("Tab"));
        Assert.True(schema.IsAllowed("railOpen"));
        Assert.True(schema.IsAllowed("RailOpen"));
        Assert.True(schema.IsAllowed("count"));
        Assert.True(schema.IsAllowed("filter"));
        Assert.True(schema.IsAllowed("since"));

        Assert.False(schema.IsAllowed("nonExistentSignal"));
    }

    [Fact]
    public void Schema_UnknownBinding_Fails()
    {
        var options = new VaxelOptions();
        options.SignalSchema = new SignalSchema<ShellSignals>();
        var optionsWrapper = Options.Create(options);

        var tagHelper = new VxBindTagHelper(optionsWrapper)
        {
            VxText = "nonExistentSignal"
        };

        var context = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("span", [], (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        var ex = Assert.Throws<VaxelTagHelperException>(() => tagHelper.Process(context, output));
        Assert.Contains("not defined in registered schema 'ShellSignals'", ex.Message);
    }

    [Fact]
    public void Schema_AllowedBinding_Succeeds()
    {
        var options = new VaxelOptions();
        options.SignalSchema = new SignalSchema<ShellSignals>();
        var optionsWrapper = Options.Create(options);

        var tagHelper = new VxBindTagHelper(optionsWrapper)
        {
            VxText = "tab"
        };

        var context = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("span", [], (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        tagHelper.Process(context, output);
        Assert.Equal("tab", output.Attributes["vx-text"].Value);
    }

    [Fact]
    public void Schema_Absent_AllowsAnyName()
    {
        // When SignalSchema is null, any valid identifier is allowed (v0.1-v0.3 behavior)
        var options = new VaxelOptions();
        var optionsWrapper = Options.Create(options);

        var tagHelper = new VxBindTagHelper(optionsWrapper)
        {
            VxText = "arbitrarySignalName"
        };

        var context = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("span", [], (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        tagHelper.Process(context, output);
        Assert.Equal("arbitrarySignalName", output.Attributes["vx-text"].Value);
    }
}
