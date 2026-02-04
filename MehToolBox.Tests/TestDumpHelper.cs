using System.Collections;
using System.Reflection;

namespace MehToolBox.Tests;

public static class TestDumpHelper
{
    public class DumpSettings
    {
        public int MaxDepth { get; set; } = 5;
        public int MaxEnumerableItems { get; set; } = 50;
        public HashSet<string> NoExamineNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public static string DumpToJson(object? obj, DumpSettings? settings = null)
    {
        if (obj == null) return "null";

        settings ??= new DumpSettings();
        var jsonBuilder = new JsonDumpBuilder();
        var visited = new HashSet<object>();
        var type = obj.GetType();

        jsonBuilder.BeginObject(type.Name, type.Name);
        DumpObject(obj, type, 0, visited, settings, jsonBuilder);
        jsonBuilder.EndObject();

        return jsonBuilder.ToJson();
    }

    static void DumpObject(object obj, Type declaringType, int depth, HashSet<object> visited, DumpSettings settings, JsonDumpBuilder jsonBuilder)
    {
        if (obj == null) return;

        if (depth >= settings.MaxDepth)
        {
            jsonBuilder.AddMaxDepth("...");
            return;
        }

        if (!IsValueType(obj.GetType()))
        {
            if (visited.Contains(obj)) return;
            visited.Add(obj);
        }

        if (obj is IEnumerable enumerable && obj is not string)
        {
            int index = 0;
            foreach (var item in enumerable)
            {
                if (index >= settings.MaxEnumerableItems)
                {
                    jsonBuilder.AddTruncated(settings.MaxEnumerableItems);
                    break;
                }

                if (item != null)
                {
                    var itemType = item.GetType();
                    if (IsValueType(itemType))
                    {
                        jsonBuilder.AddValue(null!, itemType.Name, item);
                    }
                    else
                    {
                        jsonBuilder.BeginObject(null!, itemType.Name);
                        DumpObject(item, itemType, depth + 1, visited, settings, jsonBuilder);
                        jsonBuilder.EndObject();
                    }
                }
                else
                {
                    jsonBuilder.AddNull(null!, "null");
                }
                index++;
            }
            return;
        }

        var type = obj.GetType();
        var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);

        foreach (var prop in props)
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
            if (settings.NoExamineNames.Contains(prop.Name)) continue;

            object? value = null;
            bool hadError = false;

            try
            {
                value = prop.GetValue(obj);
            }
            catch (Exception ex)
            {
                hadError = true;
                jsonBuilder.AddError(prop.Name, prop.PropertyType.Name, ex.Message);
            }

            if (hadError) continue;

            if (value == null)
            {
                jsonBuilder.AddNull(prop.Name, prop.PropertyType.Name);
                continue;
            }

            if (IsValueType(prop.PropertyType))
            {
                jsonBuilder.AddValue(prop.Name, prop.PropertyType.Name, value);
            }
            else if (value is IEnumerable innerEnumerable && value is not string)
            {
                jsonBuilder.BeginArray(prop.Name);
                DumpObject(value, prop.DeclaringType!, depth + 1, visited, settings, jsonBuilder);
                jsonBuilder.EndArray();
            }
            else
            {
                jsonBuilder.BeginObject(prop.Name, value.GetType().Name);
                DumpObject(value, prop.DeclaringType!, depth + 1, visited, settings, jsonBuilder);
                jsonBuilder.EndObject();
            }
        }
    }

    static bool IsValueType(Type type)
    {
        if (type.IsPrimitive) return true;
        if (type.IsEnum) return true;
        if (type == typeof(string)) return true;
        if (type == typeof(decimal)) return true;
        return false;
    }
}
