using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Vaxel;

/// <summary>
/// Model binder for deserializing the VX-Signals request header into typed models.
/// </summary>
public sealed class FromSignalsModelBinder : IModelBinder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var httpContext = bindingContext.HttpContext;
        var options = httpContext.RequestServices.GetService<IOptions<VaxelOptions>>()?.Value
            ?? new VaxelOptions();

        // Mark that [FromSignals] participated in binding (signal-dependent endpoint)
        httpContext.Items[SignalReader.SignalsReadItemKey] = true;

        var modelType = bindingContext.ModelType;
        var defaultInstance = CreateDefaultInstance(modelType);

        var headers = httpContext.Request.Headers;

        // Check if signals were omitted by client
        if (headers.TryGetValue("VX-Signals-Omitted", out var omitted) && omitted == "1")
        {
            bindingContext.Result = ModelBindingResult.Success(defaultInstance);
            return Task.CompletedTask;
        }

        if (!headers.TryGetValue(options.SignalsHeaderName, out var signalValues) ||
            string.IsNullOrWhiteSpace(signalValues.ToString()))
        {
            bindingContext.Result = ModelBindingResult.Success(defaultInstance);
            return Task.CompletedTask;
        }

        var rawHeader = signalValues.ToString().Trim();

        // Max signals byte size cap
        if (System.Text.Encoding.UTF8.GetByteCount(rawHeader) > options.MaxSignalsBytes)
        {
            bindingContext.Result = ModelBindingResult.Success(defaultInstance);
            return Task.CompletedTask;
        }

        try
        {
            var result = JsonSerializer.Deserialize(rawHeader, modelType, JsonOptions);
            bindingContext.Result = ModelBindingResult.Success(result ?? defaultInstance);
        }
        catch
        {
            // Never 500 on malformed or incompatible JSON; bind default instance
            bindingContext.Result = ModelBindingResult.Success(defaultInstance);
        }

        return Task.CompletedTask;
    }

    private static object? CreateDefaultInstance(Type type)
    {
        if (type == typeof(string))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize("{}", type, JsonOptions);
        }
        catch
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }
}
