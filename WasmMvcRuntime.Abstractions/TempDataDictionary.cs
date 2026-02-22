using System.Collections;
using System.Diagnostics.CodeAnalysis;
using WasmMvcRuntime.Abstractions.Mvc;

namespace WasmMvcRuntime.Abstractions.Mvc;

/// <summary>
/// Represents a dictionary for temporary data that persists only from one request to the next.
/// </summary>
public interface ITempDataDictionary : IDictionary<string, object?>
{
    void Keep();
    void Keep(string key);
    object? Peek(string key);
}

/// <summary>
/// Default implementation of ITempDataDictionary.
/// </summary>
public class TempDataDictionary : ITempDataDictionary
{
    private readonly Dictionary<string, object?> _data = new();
    private readonly HashSet<string> _keysToKeep = new();

    public object? this[string key]
    {
        get
        {
            if (_data.TryGetValue(key, out var value))
            {
                if (!_keysToKeep.Contains(key))
                {
                    _data.Remove(key);
                }
                return value;
            }
            return null;
        }
        set => _data[key] = value;
    }

    public ICollection<string> Keys => _data.Keys;
    public ICollection<object?> Values => _data.Values;
    public int Count => _data.Count;
    public bool IsReadOnly => false;

    public void Keep()
    {
        foreach (var key in _data.Keys)
        {
            _keysToKeep.Add(key);
        }
    }

    public void Keep(string key)
    {
        _keysToKeep.Add(key);
    }

    public object? Peek(string key)
    {
        _data.TryGetValue(key, out var value);
        return value;
    }

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
/// Factory for creating ITempDataDictionary instances.
/// </summary>
public interface ITempDataDictionaryFactory
{
    ITempDataDictionary GetTempData(WasmHttpContext httpContext);
}

/// <summary>
/// Default implementation of ITempDataDictionaryFactory.
/// </summary>
public class TempDataDictionaryFactory : ITempDataDictionaryFactory
{
    public ITempDataDictionary GetTempData(WasmHttpContext httpContext)
    {
        return new TempDataDictionary();
    }
}
