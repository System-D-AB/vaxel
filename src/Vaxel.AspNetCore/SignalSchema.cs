using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Vaxel;

/// <summary>
/// Static type lattice for signal schema validation of expressions.
/// </summary>
public enum ExprValueType
{
    Any = 0,
    Number,
    String,
    Bool,
    List,
    Object,
    Null,
}

/// <summary>
/// Defines the allowed signal names for compile/render time validation.
/// </summary>
public interface ISignalSchema
{
    string TypeName { get; }
    IReadOnlySet<string> AllowedSignals { get; }
    bool IsAllowed(string name);
    ExprValueType GetSignalType(string name) => ExprValueType.Any;
}

public sealed class SignalSchema<T> : ISignalSchema
{
    private readonly HashSet<string> _allowed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ExprValueType> _types = new(StringComparer.OrdinalIgnoreCase);

    public string TypeName => typeof(T).Name;
    public IReadOnlySet<string> AllowedSignals => _allowed;

    public SignalSchema()
    {
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in properties)
        {
            var camelCaseName = JsonNamingPolicy.CamelCase.ConvertName(prop.Name);
            var exprType = MapClrType(prop.PropertyType);
            _allowed.Add(camelCaseName);
            _allowed.Add(prop.Name);
            _types[camelCaseName] = exprType;
            _types[prop.Name] = exprType;
        }
    }

    public bool IsAllowed(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return _allowed.Contains(name.Trim());
    }

    public ExprValueType GetSignalType(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return ExprValueType.Any;
        return _types.TryGetValue(name.Trim(), out var t) ? t : ExprValueType.Any;
    }

    internal static ExprValueType MapClrType(Type clrType)
    {
        var t = Nullable.GetUnderlyingType(clrType) ?? clrType;
        if (t == typeof(bool)) return ExprValueType.Bool;
        if (t == typeof(byte) || t == typeof(sbyte) ||
            t == typeof(short) || t == typeof(ushort) ||
            t == typeof(int) || t == typeof(uint) ||
            t == typeof(long) || t == typeof(ulong) ||
            t == typeof(float) || t == typeof(double) ||
            t == typeof(decimal))
        {
            return ExprValueType.Number;
        }
        if (t == typeof(string) || t == typeof(char)) return ExprValueType.String;
        if (t == typeof(object)) return ExprValueType.Any;
        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(t)) return ExprValueType.List;
        return ExprValueType.Object;
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
