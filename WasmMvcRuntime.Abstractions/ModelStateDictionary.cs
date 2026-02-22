using System.Collections;

namespace WasmMvcRuntime.Abstractions.Mvc;

/// <summary>
/// Represents the state of model binding.
/// </summary>
public class ModelStateDictionary : IEnumerable<KeyValuePair<string, ModelStateEntry>>
{
    private readonly Dictionary<string, ModelStateEntry> _data = new();

    public int Count => _data.Count;
    public bool IsValid => _data.All(x => x.Value.Errors.Count == 0);

    public ModelStateEntry this[string key]
    {
        get
        {
            if (!_data.TryGetValue(key, out var entry))
            {
                entry = new ModelStateEntry();
                _data[key] = entry;
            }
            return entry;
        }
    }

    public void AddModelError(string key, string errorMessage)
    {
        if (!_data.TryGetValue(key, out var entry))
        {
            entry = new ModelStateEntry();
            _data[key] = entry;
        }
        entry.Errors.Add(new ModelError(errorMessage));
    }

    public void AddModelError(string key, Exception exception)
    {
        if (!_data.TryGetValue(key, out var entry))
        {
            entry = new ModelStateEntry();
            _data[key] = entry;
        }
        entry.Errors.Add(new ModelError(exception));
    }

    public bool ContainsKey(string key) => _data.ContainsKey(key);

    public void Clear() => _data.Clear();

    public IEnumerator<KeyValuePair<string, ModelStateEntry>> GetEnumerator() => _data.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Represents an entry in the ModelStateDictionary.
/// </summary>
public class ModelStateEntry
{
    public List<ModelError> Errors { get; } = new();
    public object? RawValue { get; set; }
    public string? AttemptedValue { get; set; }
}

/// <summary>
/// Represents a model error.
/// </summary>
public class ModelError
{
    public string ErrorMessage { get; }
    public Exception? Exception { get; }

    public ModelError(string errorMessage)
    {
        ErrorMessage = errorMessage;
    }

    public ModelError(Exception exception)
    {
        Exception = exception;
        ErrorMessage = exception.Message;
    }
}
