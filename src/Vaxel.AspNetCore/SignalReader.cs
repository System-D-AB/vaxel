using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Vaxel;

/// <summary>
/// Default implementation of <see cref="ISignalReader"/> reading from the request headers.
/// </summary>
public sealed class SignalReader : ISignalReader
{
    internal const string SignalsReadItemKey = "Vaxel.SignalsRead";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly VaxelOptions _options;
    private Dictionary<string, JsonElement>? _parsedSignals;
    private bool _initialized;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public SignalReader(IHttpContextAccessor httpContextAccessor, IOptions<VaxelOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        ArgumentNullException.ThrowIfNull(options);
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value;
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            _parsedSignals = [];
            return;
        }

        // Mark that signals have been read on this request
        httpContext.Items[SignalsReadItemKey] = true;

        var headers = httpContext.Request.Headers;

        // Check if signals were omitted due to size cap
        if (headers.TryGetValue("VX-Signals-Omitted", out var omitted) && omitted == "1")
        {
            _parsedSignals = [];
            return;
        }

        if (!headers.TryGetValue(_options.SignalsHeaderName, out var signalValues) ||
            string.IsNullOrWhiteSpace(signalValues.ToString()))
        {
            _parsedSignals = [];
            return;
        }

        var rawHeader = signalValues.ToString().Trim();

        // Enforce maximum header bytes limit
        if (System.Text.Encoding.UTF8.GetByteCount(rawHeader) > _options.MaxSignalsBytes)
        {
            _parsedSignals = [];
            return;
        }

        try
        {
            _parsedSignals = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rawHeader, JsonOptions)
                ?? [];
        }
        catch
        {
            // Silently ignore malformed JSON per spec requirements
            _parsedSignals = [];
        }
    }

    public bool TryGet<T>(string name, [NotNullWhen(true)] out T? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        EnsureInitialized();

        value = default;
        if (_parsedSignals is null || _parsedSignals.Count == 0)
        {
            return false;
        }

        foreach (var (key, element) in _parsedSignals)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    value = JsonSerializer.Deserialize<T>(element.GetRawText(), JsonOptions);
                    return value is not null;
                }
                catch
                {
                    return false;
                }
            }
        }

        return false;
    }

    public T? Get<T>(string name, T? defaultValue = default)
    {
        return TryGet<T>(name, out var value) ? value : defaultValue;
    }

    public IReadOnlyDictionary<string, object?> All()
    {
        EnsureInitialized();
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (_parsedSignals is not null)
        {
            foreach (var (k, v) in _parsedSignals)
            {
                result[k] = v;
            }
        }
        return result;
    }
}
