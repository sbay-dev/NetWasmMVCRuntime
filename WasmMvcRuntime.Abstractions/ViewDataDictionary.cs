using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace WasmMvcRuntime.Abstractions.Mvc;

/// <summary>
/// Represents a dictionary that contains view data.
/// </summary>
public class ViewDataDictionary : IDictionary<string, object?>
{
    private readonly Dictionary<string, object?> _data = new();
    private readonly ModelStateDictionary _modelState;
    private object? _model;

    public ViewDataDictionary(IModelMetadataProvider metadataProvider, ModelStateDictionary modelState)
    {
        _modelState = modelState;
    }

    public object? Model
    {
        get => _model;
        set => _model = value;
    }

    public ModelStateDictionary ModelState => _modelState;

    public object? this[string key]
    {
        get => _data.TryGetValue(key, out var value) ? value : null;
        set => _data[key] = value;
    }

    public ICollection<string> Keys => _data.Keys;
    public ICollection<object?> Values => _data.Values;
    public int Count => _data.Count;
    public bool IsReadOnly => false;

    public void Add(string key, object? value) => _data.Add(key, value);
    public void Add(KeyValuePair<string, object?> item) => _data.Add(item.Key, item.Value);
    public void Clear() => _data.Clear();
    public bool Contains(KeyValuePair<string, object?> item) => _data.Contains(item);
    public bool ContainsKey(string key) => _data.ContainsKey(key);
    public void CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex) => ((ICollection<KeyValuePair<string, object?>>)_data).CopyTo(array, arrayIndex);
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _data.GetEnumerator();
    public bool Remove(string key) => _data.Remove(key);
    public bool Remove(KeyValuePair<string, object?> item) => ((ICollection<KeyValuePair<string, object?>>)_data).Remove(item);
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out object? value) => _data.TryGetValue(key, out value);
    IEnumerator IEnumerable.GetEnumerator() => _data.GetEnumerator();
}

/// <summary>
/// Dynamic view data object for ViewBag.
/// </summary>
public class DynamicViewData : System.Dynamic.DynamicObject
{
    private readonly Func<ViewDataDictionary> _viewDataFunc;

    public DynamicViewData(Func<ViewDataDictionary> viewDataFunc)
    {
        _viewDataFunc = viewDataFunc;
    }

    public override bool TryGetMember(System.Dynamic.GetMemberBinder binder, out object? result)
    {
        result = _viewDataFunc()[binder.Name];
        return true;
    }

    public override bool TrySetMember(System.Dynamic.SetMemberBinder binder, object? value)
    {
        _viewDataFunc()[binder.Name] = value;
        return true;
    }
}

/// <summary>
/// Provides metadata about model properties.
/// </summary>
public interface IModelMetadataProvider
{
}

/// <summary>
/// Empty implementation of IModelMetadataProvider.
/// </summary>
public class EmptyModelMetadataProvider : IModelMetadataProvider
{
}
