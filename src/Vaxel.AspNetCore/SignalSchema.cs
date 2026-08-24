using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Vaxel;

/// <summary>
/// Defines the allowed signal names for compile/render time validation.
/// </summary>
public interface ISignalSchema
{
    string TypeName { get; }
    IReadOnlySet<string> AllowedSignals { get; }
    bool IsAllowed(string name);
}

public sealed class SignalSchema<T> : ISignalSchema
{
    private readonly HashSet<string> _allowed = new(StringComparer.OrdinalIgnoreCase);

    public string TypeName => typeof(T).Name;
    public IReadOnlySet<string> AllowedSignals => _allowed;

    public SignalSchema()
    {
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in properties)
        {
            var camelCaseName = JsonNamingPolicy.CamelCase.ConvertName(prop.Name);
            _allowed.Add(camelCaseName);
            _allowed.Add(prop.Name);
        }
    }

    public bool IsAllowed(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return _allowed.Contains(name.Trim());
    }
}

public static class SignalSchemaServiceCollectionExtensions
{
    /// <summary>
    /// Registers a strongly-typed signal schema for compile/render-time validation of binding names in Tag Helpers.
    /// </summary>
    public static IServiceCollection AddSignalSchema<T>(this IServiceCollection services) where T : class
    {
        ArgumentNullException.ThrowIfNull(services);

        var schema = new SignalSchema<T>();
        services.AddSingleton<ISignalSchema>(schema);
        services.Configure<VaxelOptions>(opt => opt.SignalSchema = schema);

        return services;
    }
}
