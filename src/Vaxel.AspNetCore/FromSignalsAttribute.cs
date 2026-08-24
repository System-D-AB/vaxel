using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Vaxel;

/// <summary>
/// Specifies that a parameter or property should be bound from the <c>VX-Signals</c> request header.
/// <para>
/// <strong>SECURITY NOTICE:</strong> Signals are user-controlled client UI state sent in the request header.
/// They must never be used for authorization, identity, pricing, totals, or security-sensitive server-side decisions.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class FromSignalsAttribute : Attribute, IBindingSourceMetadata, IBinderTypeProviderMetadata
{
    public BindingSource BindingSource => BindingSource.Custom;

    public Type? BinderType => typeof(FromSignalsModelBinder);
}
