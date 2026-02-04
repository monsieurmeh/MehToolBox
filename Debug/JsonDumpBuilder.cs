using System.Text.Json;

namespace MehToolBox;

/// <summary>
/// Builds a nested dictionary structure during object traversal for JSON serialization.
/// </summary>
public class JsonDumpBuilder
{
    readonly Dictionary<string, object?> _root = new();
    readonly Stack<(string name, object container)> _stack = new();
    object _current;

    public JsonDumpBuilder()
    {
        _current = _root;
    }

    /// <summary>
    /// Begins a new object node with type metadata.
    /// </summary>
    public void BeginObject(string name, string typeName)
    {
        var obj = new Dictionary<string, object?> { ["$type"] = typeName };
        AddToCurrentContainer(name, obj);
        _stack.Push((name, _current));
        _current = obj;
    }

    /// <summary>
    /// Ends the current object and returns to parent context.
    /// </summary>
    public void EndObject()
    {
        if (_stack.Count > 0)
        {
            var (_, parent) = _stack.Pop();
            _current = parent;
        }
    }

    /// <summary>
    /// Begins a new array node.
    /// </summary>
    public void BeginArray(string name)
    {
        var arr = new List<object?>();
        AddToCurrentContainer(name, arr);
        _stack.Push((name, _current));
        _current = arr;
    }

    /// <summary>
    /// Ends the current array and returns to parent context.
    /// </summary>
    public void EndArray()
    {
        if (_stack.Count > 0)
        {
            var (_, parent) = _stack.Pop();
            _current = parent;
        }
    }

    /// <summary>
    /// Adds a value with type metadata.
    /// </summary>
    public void AddValue(string name, string typeName, object? value)
    {
        var valueObj = new Dictionary<string, object?>
        {
            ["$type"] = typeName,
            ["$value"] = ConvertValue(value)
        };
        AddToCurrentContainer(name, valueObj);
    }

    /// <summary>
    /// Adds a null value with type metadata.
    /// </summary>
    public void AddNull(string name, string typeName)
    {
        var valueObj = new Dictionary<string, object?>
        {
            ["$type"] = typeName,
            ["$value"] = null
        };
        AddToCurrentContainer(name, valueObj);
    }

    /// <summary>
    /// Adds an error marker when property access fails.
    /// </summary>
    public void AddError(string name, string typeName, string errorMessage)
    {
        var valueObj = new Dictionary<string, object?>
        {
            ["$type"] = typeName,
            ["$error"] = errorMessage
        };
        AddToCurrentContainer(name, valueObj);
    }

    /// <summary>
    /// Adds a marker indicating max depth was reached.
    /// </summary>
    public void AddMaxDepth(string name)
    {
        var valueObj = new Dictionary<string, object?> { ["$maxDepth"] = true };
        AddToCurrentContainer(name, valueObj);
    }

    /// <summary>
    /// Adds a marker indicating array was truncated at max items.
    /// </summary>
    public void AddTruncated(int maxItems)
    {
        var valueObj = new Dictionary<string, object?>
        {
            ["$truncated"] = true,
            ["$maxItems"] = maxItems
        };
        AddToCurrentContainer(null, valueObj);
    }

    /// <summary>
    /// Serializes the built structure to a JSON string.
    /// </summary>
    public string ToJson(bool indented = true)
    {
        var options = new JsonSerializerOptions { WriteIndented = indented };
        return JsonSerializer.Serialize(_root, options);
    }

    /// <summary>
    /// Gets the root dictionary for direct manipulation.
    /// </summary>
    public Dictionary<string, object?> GetRoot() => _root;

    void AddToCurrentContainer(string? name, object? value)
    {
        if (_current is Dictionary<string, object?> dict)
        {
            if (name != null) dict[name] = value;
        }
        else if (_current is List<object?> list)
        {
            list.Add(value);
        }
    }

    static object? ConvertValue(object? value)
    {
        if (value == null) return null;

        var type = value.GetType();
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal)) return value;
        if (type.IsEnum) return value.ToString();

        return value.ToString();
    }
}
