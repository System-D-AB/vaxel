using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Vaxel;

/// <summary>
/// Fluent builder for constructing a vaxel patch document.
/// </summary>
public sealed class PatchBuilder : IResult, IActionResult
{
    private readonly List<PatchEntry> _patches = [];
    private readonly DirectiveBag _directives = new();
    private object? _signals;

    public int StatusCode { get; set; } = StatusCodes.Status200OK;
    public Refusal? Refusal { get; private set; }
    public IReadOnlyList<PatchEntry> Patches => _patches;
    public object? SignalValues => _signals;
    internal DirectiveBag Directives => _directives;

    public PatchBuilder(int statusCode = StatusCodes.Status200OK)
    {
        StatusCode = statusCode;
    }

    internal PatchBuilder WithRefusal(Refusal refusal)
    {
        Refusal = refusal;
        StatusCode = refusal.StatusCode;
        return this;
    }

    private PatchBuilder Add(string target, SwapMode mode, IHtmlContent? content)
    {
        VaxelTargetException.ThrowIfInvalid(target);
        _patches.Add(new PatchEntry
        {
            Target = target,
            Mode = mode,
            Content = content
        });
        return this;
    }

    /// <summary>
    /// Morphs the fragment into the target element, preserving identity, focus, and state.
    /// </summary>
    public PatchBuilder Replace(string target, IHtmlContent content) =>
        Add(target, SwapMode.Morph, content);

    /// <summary>
    /// Morphs the target element itself, including its attributes.
    /// </summary>
    public PatchBuilder Outer(string target, IHtmlContent content) =>
        Add(target, SwapMode.Outer, content);

    /// <summary>
    /// Destroys the target element and puts the fragment in its place.
    /// </summary>
    public PatchBuilder ReplaceHard(string target, IHtmlContent content) =>
        Add(target, SwapMode.Replace, content);

    /// <summary>
    /// Morphs the target element's children only.
    /// </summary>
    public PatchBuilder Inner(string target, IHtmlContent content) =>
        Add(target, SwapMode.Inner, content);

    /// <summary>
    /// Appends the fragment as the last child of the target element.
    /// </summary>
    public PatchBuilder Append(string target, IHtmlContent content) =>
        Add(target, SwapMode.Append, content);

    /// <summary>
    /// Prepends the fragment as the first child of the target element.
    /// </summary>
    public PatchBuilder Prepend(string target, IHtmlContent content) =>
        Add(target, SwapMode.Prepend, content);

    /// <summary>
    /// Inserts the fragment as a preceding sibling of the target element.
    /// </summary>
    public PatchBuilder Before(string target, IHtmlContent content) =>
        Add(target, SwapMode.Before, content);

    /// <summary>
    /// Inserts the fragment as a following sibling of the target element.
    /// </summary>
    public PatchBuilder After(string target, IHtmlContent content) =>
        Add(target, SwapMode.After, content);

    /// <summary>
    /// Removes the target element. Carries no content.
    /// </summary>
    public PatchBuilder Remove(string target) =>
        Add(target, SwapMode.Remove, null);

    /// <summary>
    /// Sets the parser namespace for the most recently added patch.
    /// </summary>
    public PatchBuilder InNamespace(VaxelNamespace ns)
    {
        if (_patches.Count == 0)
        {
            throw new InvalidOperationException("InNamespace must be called after adding a patch.");
        }

        _patches[^1].Namespace = ns;
        return this;
    }

    /// <summary>
    /// Opts the target patch into the View Transition API.
    /// </summary>
    public PatchBuilder Transition(string? target = null)
    {
        if (string.IsNullOrEmpty(target))
        {
            if (_patches.Count == 0)
            {
                throw new InvalidOperationException("Transition must be called after adding a patch or with a target selector.");
            }

            _patches[^1].Transition = "view";
        }
        else
        {
            VaxelTargetException.ThrowIfInvalid(target);
            var patch = _patches.FindLast(p => p.Target == target);
            if (patch is not null)
            {
                patch.Transition = "view";
            }
            else
            {
                if (_patches.Count > 0)
                {
                    _patches[^1].Transition = "view";
                }
            }
        }

        return this;
    }

    /// <summary>
    /// Sets the signal bag to patch on the client.
    /// </summary>
    public PatchBuilder Signals(object values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _signals = values;
        return this;
    }

    /// <summary>
    /// Sets the signal bag to patch on the client.
    /// </summary>
    public PatchBuilder Signals(IDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _signals = values;
        return this;
    }

    /// <summary>
    /// Sets the focus directive to move focus to the specified id.
    /// </summary>
    public PatchBuilder Focus(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var target = id.StartsWith('#') ? id : $"#{id}";
        VaxelTargetException.ThrowIfInvalid(target);
        _directives.Focus = target;
        return this;
    }

    /// <summary>
    /// Sets the scroll directive.
    /// </summary>
    public PatchBuilder Scroll(
        string target,
        string? behavior = null,
        string? block = null,
        string? inline = null,
        bool focus = false)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target != "top")
        {
            var t = target.StartsWith('#') ? target : $"#{target}";
            VaxelTargetException.ThrowIfInvalid(t);
            _directives.Scroll = t;
        }
        else
        {
            _directives.Scroll = "top";
        }

        _directives.ScrollBehavior = behavior;
        _directives.ScrollBlock = block;
        _directives.ScrollInline = inline;
        _directives.ScrollFocus = focus;
        return this;
    }

    /// <summary>
    /// Sets document.title.
    /// </summary>
    public PatchBuilder Title(string title)
    {
        _directives.Title = title;
        return this;
    }

    /// <summary>
    /// Sets text for the polite live region announcement.
    /// </summary>
    public PatchBuilder Announce(string text)
    {
        _directives.Announce = text;
        return this;
    }

    /// <summary>
    /// Pushes a new URL to history without navigation.
    /// </summary>
    public PatchBuilder PushUrl(string url)
    {
        _directives.PushUrl = url;
        return this;
    }

    /// <summary>
    /// Replaces the current URL in history without navigation.
    /// </summary>
    public PatchBuilder ReplaceUrl(string url)
    {
        _directives.ReplaceUrl = url;
        return this;
    }

    /// <summary>
    /// Directs the client to navigate to the specified URL.
    /// </summary>
    public PatchBuilder Redirect(string url)
    {
        _directives.Redirect = url;
        return this;
    }

    /// <summary>
    /// Directs the client to perform a full page reload.
    /// </summary>
    public PatchBuilder Reload()
    {
        _directives.Reload = true;
        return this;
    }

    /// <summary>
    /// Appends a refusal notice to the specified target.
    /// </summary>
    public PatchBuilder Into(string target, IHtmlContent content) =>
        Append(target, content);

    /// <summary>
    /// Renders the complete patch document as an HTML string.
    /// </summary>
    public string ToHtml(System.Text.Encodings.Web.HtmlEncoder? encoder = null) =>
        PatchDocumentWriter.Render(_patches, _signals, _directives, encoder);

    /// <summary>
    /// Builds a <see cref="PatchResult"/> from this builder.
    /// </summary>
    public PatchResult Build() => new(this);

    public static implicit operator PatchResult(PatchBuilder builder) => builder.Build();

    Task IResult.ExecuteAsync(HttpContext httpContext) =>
        Build().ExecuteAsync(httpContext);

    Task IActionResult.ExecuteResultAsync(ActionContext context) =>
        Build().ExecuteResultAsync(context);
}
