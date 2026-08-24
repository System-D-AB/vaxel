namespace Vaxel;

/// <summary>
/// Thrown when a named Razor unit (partial, view, page, or ViewComponent) cannot be found.
/// </summary>
public sealed class VaxelFragmentNotFoundException : InvalidOperationException
{
    public VaxelFragmentNotFoundException(string fragmentName, IEnumerable<string>? searchedLocations = null)
        : base(BuildMessage(fragmentName, searchedLocations))
    {
        FragmentName = fragmentName;
        SearchedLocations = searchedLocations?.ToArray() ?? [];
    }

    public string FragmentName { get; }

    public IReadOnlyList<string> SearchedLocations { get; }

    private static string BuildMessage(string fragmentName, IEnumerable<string>? searchedLocations)
    {
        var locations = searchedLocations is null ? "" : " Searched: " + string.Join(", ", searchedLocations);
        return $"The fragment '{fragmentName}' was not found.{locations}";
    }
}
