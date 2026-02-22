using Microsoft.AspNetCore.Components;

namespace WasmMvcRuntime.Abstractions.Views;

/// <summary>
/// Base class for Razor views with strongly-typed model support
/// </summary>
/// <typeparam name="TModel">The type of the model</typeparam>
public abstract class RazorViewBase<TModel> : ComponentBase
{
    /// <summary>
    /// Gets or sets the model for this view
    /// </summary>
    [Parameter]
    public TModel? Model { get; set; }

    /// <summary>
    /// Gets or sets the ViewData dictionary
    /// </summary>
    [Parameter]
    public IDictionary<string, object?>? ViewData { get; set; }
}

/// <summary>
/// Base class for Razor views without a model
/// </summary>
public abstract class RazorViewBase : ComponentBase
{
    /// <summary>
    /// Gets or sets the ViewData dictionary
    /// </summary>
    [Parameter]
    public IDictionary<string, object?>? ViewData { get; set; }
}
