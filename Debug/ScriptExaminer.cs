using Il2Cpp;
using MelonLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MehToolBox;

/// <summary>
/// Unified tool for examining UnityEngine.Object instances.
/// Dump(a) to inspect a single object, Compare(a, b) to compare two objects.
/// Both operations use the same atomic methods for determining recursion and examination rules.
/// </summary>
public static class ScriptExaminer
{
    public enum OutputFormat
    {
        Console,
        Json
    }

    [Flags]
    public enum ReportFlags : uint
    {
        None = 0U,
        NullMismatch = 1U << 0,
        BothNull = 1U << 1,
        ReferenceDifferent = 1U << 2,
        ValueDifferent = 1U << 3,
        ReferenceEqual = 1U << 4,
        ValueEqual = 1U << 5,
        LengthMismatch = 1U << 6,
        MaxDepth = 1U << 7,
        Error = 1U << 8,

        // Dump-specific flags
        DumpValue = 1U << 9,
        DumpNull = 1U << 10,

        DefaultCompare = NullMismatch | ValueDifferent | LengthMismatch,
        DefaultDump = DumpValue | DumpNull | MaxDepth | Error,
    }

    [Flags]
    internal enum ErrorState : uint
    {
        None = 0U,
        LeftSide = 1U << 0,
        RightSide = 1U << 1,
        Both = LeftSide | RightSide,
    }

    public class ScriptExaminerSettings
    {
        public ReportFlags ReportFlags = ReportFlags.DefaultCompare;
        public int MaxDepth = 5;
        public int MaxEnumerableItems = 50;
        public bool WarnOnMaxDepthReached = false;
        public string OptionalDescription = "";
        public OutputFormat OutputFormat = OutputFormat.Console;

        /// <summary>
        /// Do not perform value comparisons/examination on properties of these types
        /// </summary>
        public HashSet<Type> NoExamineTypes = new HashSet<Type>
        {
            typeof(IntPtr),
            typeof(UIntPtr),
            typeof(CancellationToken),
            typeof(CancellationTokenSource),
            typeof(IEnumerable),
            typeof(Matrix4x4)
        };

        /// <summary>
        /// Do not perform value comparisons/examination on properties with these names
        /// </summary>
        public HashSet<string> NoExamineNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Pointer",
            "Token",
            "_source",
            "name",
            "parent",
            "m_AssetGUID",
            ""
        };

        /// <summary>
        /// Do not perform value comparisons/examination on properties with these types when the instances being examined are of these declaring types
        /// </summary>
        public HashSet<(Type declaringType, Type propertyType)> NoExamineTypeTypePairs = new HashSet<(Type, Type)>
        {
        };

        /// <summary>
        /// Do not perform value comparisons/examination on properties with these names when the instances being examined are of these declaring types
        /// </summary>
        public HashSet<(Type declaringType, string propName)> NoExamineTypeNamePairs = new HashSet<(Type, string)>
        {
        };

        /// <summary>
        /// Do not recursively examine property values of object instances of these data types
        /// </summary>
        public HashSet<Type> NoRecurseTypes = new HashSet<Type>
        {
            typeof(IntPtr),
            typeof(UIntPtr),
            typeof(CancellationToken),
            typeof(CancellationTokenSource),
            typeof(Transform),
            typeof(MeshRenderer),
            typeof(MeshFilter),
        };

        /// <summary>
        /// Do not recursively examine property values of object instances assigned to these property names
        /// </summary>
        public HashSet<string> NoRecurseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Pointer",
            "Token",
            "_source",
            "gameObject",
        };

        /// <summary>
        /// Do not recursively examine property values of object instances of these declaring types when the properties have these propertyTypes
        /// </summary>
        public HashSet<(Type declaringType, Type propertyType)> NoRecurseTypeTypePairs = new HashSet<(Type, Type)>
        {
        };

        /// <summary>
        /// Do not recursively examine property values of object instances of these declaring types when the properties have these names
        /// </summary>
        public HashSet<(Type declaringType, string propName)> NoRecurseTypeNamePairs = new HashSet<(Type, string)>
        {
        };

        /// <summary>
        /// Creates settings configured for dump operations
        /// </summary>
        public static ScriptExaminerSettings ForDump(int maxDepth = 5, int maxEnumerableItems = 50)
        {
            return new ScriptExaminerSettings
            {
                ReportFlags = ReportFlags.DefaultDump,
                MaxDepth = maxDepth,
                MaxEnumerableItems = maxEnumerableItems,
            };
        }

        /// <summary>
        /// Creates settings configured for compare operations
        /// </summary>
        public static ScriptExaminerSettings ForCompare(int maxDepth = 5)
        {
            return new ScriptExaminerSettings
            {
                ReportFlags = ReportFlags.DefaultCompare,
                MaxDepth = maxDepth,
            };
        }
    }


    #region Public API

    /// <summary>
    /// Dumps a single UnityEngine.Object instance, recursively examining its properties.
    /// Respects OutputFormat setting - Console prints to MelonLogger, Json prints JSON string.
    /// </summary>
    public static void Dump<T>(T obj, ScriptExaminerSettings? settings = null) where T : UnityEngine.Object
    {
        if (obj == null)
        {
            MelonLogger.Error("Cannot dump null UnityEngine.Object.");
            return;
        }

        settings ??= ScriptExaminerSettings.ForDump();
        var visited = new HashSet<object>();
        var type = obj.GetType();

        if (settings.OutputFormat == OutputFormat.Json)
        {
            var jsonBuilder = new JsonDumpBuilder();
            jsonBuilder.BeginObject(type.Name, type.Name);
            DumpObject(obj, type, type.Name, 0, visited, settings, jsonBuilder);
            jsonBuilder.EndObject();

            MelonLogger.Msg($"JSON Dump of {obj.name} ({type}):");
            MelonLogger.Msg(jsonBuilder.ToJson());
            return;
        }

        MelonLogger.Msg("");
        MelonLogger.Msg($"Dump of {obj.name} ({type})");
        if (!string.IsNullOrEmpty(settings.OptionalDescription))
        {
            MelonLogger.Msg(settings.OptionalDescription);
        }
        MelonLogger.Msg("");

        DumpObject(obj, type, type.Name, 0, visited, settings);
    }

    /// <summary>
    /// Compares two UnityEngine.Object instances of identical type, reporting differences.
    /// </summary>
    public static void Compare<T>(T a, T b, ScriptExaminerSettings? settings = null) where T : UnityEngine.Object
    {
        if (a == null || b == null)
        {
            MelonLogger.Error("Cannot compare null UnityEngine.Object.");
            return;
        }

        if (a.GetType() != b.GetType())
        {
            MelonLogger.Error("Must compare identical UnityEngine.Object types.");
            return;
        }

        settings ??= ScriptExaminerSettings.ForCompare();

        MelonLogger.Msg("");
        MelonLogger.Msg($"Compare between {a.name} and {b.name} ({a.GetType()})");
        if (!string.IsNullOrEmpty(settings.OptionalDescription))
        {
            MelonLogger.Msg(settings.OptionalDescription);
        }
        MelonLogger.Msg("");

        var visited = new HashSet<(object, object)>();
        CompareObjects(a, b, a.GetType(), a.GetType().Name, 0, visited, settings);
    }

    /// <summary>
    /// Dumps any object to a JSON string, recursively examining its properties.
    /// </summary>
    public static string DumpToJson(object obj, ScriptExaminerSettings? settings = null)
    {
        if (obj == null) return "null";

        settings ??= ScriptExaminerSettings.ForDump();
        settings.OutputFormat = OutputFormat.Json;

        var jsonBuilder = new JsonDumpBuilder();
        var visited = new HashSet<object>();
        var type = obj.GetType();

        jsonBuilder.BeginObject(type.Name, type.Name);
        DumpObject(obj, type, type.Name, 0, visited, settings, jsonBuilder);
        jsonBuilder.EndObject();

        return jsonBuilder.ToJson();
    }

    /// <summary>
    /// Dumps a UnityEngine.Object to a JSON string, recursively examining its properties.
    /// </summary>
    public static string DumpToJson<T>(T obj, ScriptExaminerSettings? settings = null) where T : UnityEngine.Object
    {
        if (obj == null) return "null";
        return DumpToJson((object)obj, settings);
    }

    #endregion


    #region Internal API

    internal static readonly Dictionary<Type, List<PropertyInfo>> TypeToPropertyCache = new Dictionary<Type, List<PropertyInfo>>();

    /// <summary>
    /// Determines if a property's value should be examined (for both dump and compare operations)
    /// </summary>
    internal static bool ShouldExamineValue(Type declaringType, PropertyInfo prop, ScriptExaminerSettings settings)
    {
        return !settings.NoExamineTypes.Contains(prop.PropertyType)
                && !settings.NoExamineNames.Contains(prop.Name)
                && !settings.NoExamineTypeTypePairs.Contains((declaringType, prop.PropertyType))
                && !settings.NoExamineTypeNamePairs.Contains((declaringType, prop.Name));
    }

    /// <summary>
    /// Determines if recursion should occur into a property's value (for both dump and compare operations)
    /// </summary>
    internal static bool ShouldRecurse(Type declaringType, PropertyInfo prop, ScriptExaminerSettings settings)
    {
        return !IsValueType(prop)
                && !settings.NoRecurseTypes.Contains(prop.PropertyType)
                && !settings.NoRecurseNames.Contains(prop.Name)
                && !settings.NoRecurseTypeTypePairs.Contains((declaringType, prop.PropertyType))
                && !settings.NoRecurseTypeNamePairs.Contains((declaringType, prop.Name));
    }

    /// <summary>
    /// Determines if a property represents a simple value type that should not be recursed into
    /// </summary>
    internal static bool IsValueType(PropertyInfo prop)
    {
        return IsValueType(prop.PropertyType);
    }

    /// <summary>
    /// Determines if a type is a simple value type that should not be recursed into
    /// </summary>
    internal static bool IsValueType(Type type)
    {
        if (type.IsPrimitive) return true;
        if (type.IsEnum) return true;
        if (type == typeof(string)) return true;
        if (type == typeof(decimal)) return true;
        if (type == typeof(Quaternion)) return true;
        if (type == typeof(Vector2)) return true;
        if (type == typeof(Vector3)) return true;
        if (type == typeof(Vector4)) return true;
        if (type == typeof(Matrix4x4)) return true;
        return false;
    }

    /// <summary>
    /// Gets cached properties for a type
    /// </summary>
    internal static List<PropertyInfo> GetProperties(Type type)
    {
        if (!TypeToPropertyCache.TryGetValue(type, out var props))
        {
            props = new List<PropertyInfo>(type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            TypeToPropertyCache[type] = props;
        }
        return props;
    }

    /// <summary>
    /// Checks if an enumerable contains elements of a specific type
    /// </summary>
    internal static bool IsEnumerableOfType(IEnumerable enumerable, Type elementType)
    {
        if (enumerable == null || elementType == null)
            return false;

        var interfaces = enumerable.GetType().GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        foreach (var iface in interfaces)
        {
            Type ifaceElementType = iface.GetGenericArguments()[0];
            if (ifaceElementType == elementType)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if an enumerable should be skipped based on NoRecurseTypes
    /// </summary>
    internal static bool ShouldSkipEnumerable(IEnumerable enumerable, ScriptExaminerSettings settings)
    {
        foreach (Type noRecurseType in settings.NoRecurseTypes)
        {
            if (IsEnumerableOfType(enumerable, noRecurseType))
            {
                return true;
            }
        }
        return false;
    }

    #endregion


    #region Null Checking Utilities

    internal static bool SafeUnityNull<T>(T value) => (value as object) == null;
    internal static bool NullMismatch<T>(T a, T b) => (!SafeUnityNull(a) && SafeUnityNull(b)) || (SafeUnityNull(a) && !SafeUnityNull(b));
    internal static bool BothNull<T>(T a, T b) => SafeUnityNull(a) && SafeUnityNull(b);
    internal static bool NeitherNull<T>(T a, T b) => !SafeUnityNull(a) && !SafeUnityNull(b);

    #endregion


    #region Dump Implementation

    internal static void DumpObject(object obj, Type declaringType, string path, int depth, HashSet<object> visited, ScriptExaminerSettings settings, JsonDumpBuilder? jsonBuilder = null)
    {
        if (SafeUnityNull(obj))
        {
            return;
        }

        if (depth >= settings.MaxDepth)
        {
            if (jsonBuilder != null)
            {
                jsonBuilder.AddMaxDepth(GetLastPathSegment(path));
            }
            else
            {
                MaybeReport(path, null, settings, ReportFlags.MaxDepth, depth);
            }
            return;
        }

        // Prevent cycles for reference types
        if (!IsValueType(obj.GetType()))
        {
            if (visited.Contains(obj)) return;
            visited.Add(obj);
        }

        // Handle enumerables
        if (obj is IEnumerable enumerable && obj is not string)
        {
            if (ShouldSkipEnumerable(enumerable, settings))
            {
                return;
            }

            int index = 0;
            foreach (var item in enumerable)
            {
                if (index >= settings.MaxEnumerableItems)
                {
                    if (jsonBuilder != null)
                    {
                        jsonBuilder.AddTruncated(settings.MaxEnumerableItems);
                    }
                    else
                    {
                        MelonLogger.Msg($"{EmptyFlagPrefix}{Indent(depth)}... (truncated at {settings.MaxEnumerableItems} items)");
                    }
                    break;
                }

                if (jsonBuilder != null && item != null)
                {
                    var itemType = item.GetType();
                    if (IsValueType(itemType))
                    {
                        jsonBuilder.AddValue(null!, itemType.Name, item);
                    }
                    else
                    {
                        jsonBuilder.BeginObject(null!, itemType.Name);
                        DumpObject(item, itemType, $"{path}[{index}]", depth + 1, visited, settings, jsonBuilder);
                        jsonBuilder.EndObject();
                    }
                }
                else if (jsonBuilder != null && item == null)
                {
                    jsonBuilder.AddNull(null!, "null");
                }
                else if (item == null)
                {
                    if (settings.ReportFlags.HasFlag(ReportFlags.DumpNull))
                    {
                        MelonLogger.Msg($"{EmptyFlagPrefix}{Indent(depth + 1)}{path}[{index}]: null");
                    }
                }
                else if (IsValueType(item.GetType()))
                {
                    if (settings.ReportFlags.HasFlag(ReportFlags.DumpValue))
                    {
                        MelonLogger.Msg($"{EmptyFlagPrefix}{Indent(depth + 1)}{path}[{index}] ({item.GetType().Name}): {item}");
                    }
                }
                else
                {
                    DumpObject(item, item.GetType(), $"{path}[{index}]", depth + 1, visited, settings, jsonBuilder);
                }
                index++;
            }
            return;
        }

        var type = obj.GetType();
        var props = GetProperties(type);

        foreach (var prop in props)
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
            {
                continue;
            }

            bool shouldExamine = ShouldExamineValue(declaringType, prop, settings);
            bool shouldRecurse = ShouldRecurse(declaringType, prop, settings);

            if (!shouldExamine && !shouldRecurse)
            {
                continue;
            }

            object? value = null;
            bool hadError = false;

            try
            {
                value = prop.GetValue(obj);
            }
            catch (Exception ex)
            {
                hadError = true;
                if (jsonBuilder != null)
                {
                    jsonBuilder.AddError(prop.Name, prop.PropertyType.Name, ex.Message);
                }
                else
                {
                    MaybeReport($"{path}.{prop.Name}", prop, settings, ReportFlags.Error, depth);
                }
            }

            if (hadError)
            {
                continue;
            }

            if (shouldExamine)
            {
                DumpValue(value, path, prop, depth, settings, jsonBuilder);
            }

            if (shouldRecurse && !SafeUnityNull(value))
            {
                if (jsonBuilder != null)
                {
                    // Check if it's an array/list
                    if (value is IEnumerable innerEnumerable && value is not string)
                    {
                        jsonBuilder.BeginArray(prop.Name);
                        DumpObject(value, prop.DeclaringType!, $"{path}.{prop.Name}", depth + 1, visited, settings, jsonBuilder);
                        jsonBuilder.EndArray();
                    }
                    else
                    {
                        jsonBuilder.BeginObject(prop.Name, value!.GetType().Name);
                        DumpObject(value, prop.DeclaringType!, $"{path}.{prop.Name}", depth + 1, visited, settings, jsonBuilder);
                        jsonBuilder.EndObject();
                    }
                }
                else
                {
                    DumpObject(value, prop.DeclaringType!, $"{path}.{prop.Name}", depth + 1, visited, settings, jsonBuilder);
                }
            }
        }
    }

    internal static void DumpValue(object? value, string path, PropertyInfo prop, int depth, ScriptExaminerSettings settings, JsonDumpBuilder? jsonBuilder = null)
    {
        if (SafeUnityNull(value))
        {
            if (jsonBuilder != null)
            {
                jsonBuilder.AddNull(prop.Name, prop.PropertyType.Name);
            }
            else if (settings.ReportFlags.HasFlag(ReportFlags.DumpNull))
            {
                MelonLogger.Msg($"{EmptyFlagPrefix}{Indent(depth)}{path}.{prop.Name} ({prop.PropertyType.Name}): null");
            }
            return;
        }

        if (jsonBuilder != null)
        {
            jsonBuilder.AddValue(prop.Name, prop.PropertyType.Name, value);
        }
        else if (settings.ReportFlags.HasFlag(ReportFlags.DumpValue))
        {
            MelonLogger.Msg($"{EmptyFlagPrefix}{Indent(depth)}{path}.{prop.Name} ({prop.PropertyType.Name}): {value}");
        }
    }

    internal static string GetLastPathSegment(string path)
    {
        int lastDot = path.LastIndexOf('.');
        if (lastDot >= 0)
        {
            return path.Substring(lastDot + 1);
        }
        return path;
    }

    internal const int FlagPrefixWidth = 21; // Accommodates longest flag [ReferenceDifferent]
    internal static string EmptyFlagPrefix => new string(' ', FlagPrefixWidth);
    internal static string Indent(int depth) => new string(' ', depth * 2);

    #endregion


    #region Compare Implementation

    internal static void CompareObjects<T>(T a, T b, Type declaringType, string path, int depth, HashSet<(object, object)> visited, ScriptExaminerSettings settings)
    {
        if (depth >= settings.MaxDepth)
        {
            MaybeReport(path, null, settings, ReportFlags.MaxDepth, depth);
            return;
        }

        if (a == null && b == null) //this shouldn't be happening, but somehow it is...
            return;

        if (visited.Contains((a, b)) || visited.Contains((b, a)))
        {
            return;
        }

        visited.Add((a, b));

        // Handle enumerables
        if (a is IEnumerable aEnum && b is IEnumerable bEnum)
        {
            if (ShouldSkipEnumerable(aEnum, settings) || ShouldSkipEnumerable(bEnum, settings))
            {
                return;
            }

            var enumeratorA = aEnum.GetEnumerator();
            var enumeratorB = bEnum.GetEnumerator();
            int index = 0;

            while (true)
            {
                bool hasNextA = enumeratorA.MoveNext();
                bool hasNextB = enumeratorB.MoveNext();

                if (!hasNextA && !hasNextB) break;
                if (hasNextA != hasNextB)
                {
                    MaybeReport(path, null, settings, ReportFlags.LengthMismatch, depth);
                    break;
                }

                CompareObjects(enumeratorA.Current, enumeratorB.Current, enumeratorA.Current?.GetType() ?? typeof(object), $"{path}[{index}]", depth + 1, visited, settings);
                index++;
            }

            return;
        }

        var type = a != null ? a.GetType() : b.GetType();
        var props = GetProperties(type);

        foreach (var prop in props)
        {
            if (!prop.CanRead)
            {
                continue;
            }

            bool shouldExamine = ShouldExamineValue(declaringType, prop, settings);
            bool shouldRecurse = ShouldRecurse(declaringType, prop, settings);

            if (!shouldExamine && !shouldRecurse)
            {
                continue;
            }

            object valueA = null;
            object valueB = null;
            ErrorState errorState = ErrorState.None;

            try
            {
                valueA = prop.GetValue(a);
            }
            catch
            {
                errorState |= ErrorState.LeftSide;
            }

            try
            {
                valueB = prop.GetValue(b);
            }
            catch
            {
                errorState |= ErrorState.RightSide;
            }

            if (errorState != ErrorState.None)
            {
                MaybeReport($"({errorState}) {path}", prop, settings, ReportFlags.Error, depth);
            }

            if (shouldExamine)
            {
                CompareValues(valueA, valueB, path, prop, settings, depth);
            }

            if (shouldRecurse && NeitherNull(valueA, valueB))
            {
                CompareObjects(valueA, valueB, prop.DeclaringType, $"{path}.{prop.Name}", depth + 1, visited, settings);
            }
        }
    }

    internal static void CompareValues<T>(T valueA, T valueB, string path, PropertyInfo prop, ScriptExaminerSettings settings, int depth)
    {
        if (NullMismatch(valueA, valueB))
        {
            MaybeReportWithValue(valueA, valueB, path, prop, settings, ReportFlags.NullMismatch, depth);
            return;
        }
        if (BothNull(valueA, valueB))
        {
            MaybeReport(path, prop, settings, ReportFlags.BothNull, depth);
            return;
        }
        if (IsValueType(prop))
        {
            if (!valueA.Equals(valueB))
            {
                MaybeReportWithValue(valueA, valueB, path, prop, settings, ReportFlags.ValueDifferent, depth);
            }
            else
            {
                MaybeReport(path, prop, settings, ReportFlags.ValueEqual, depth);
            }
            return;
        }
        if (!ReferenceEquals(valueA, valueB))
        {
            MaybeReportWithValue(valueA, valueB, path, prop, settings, ReportFlags.ReferenceDifferent, depth);
        }
        else
        {
            MaybeReport(path, prop, settings, ReportFlags.ReferenceEqual, depth);
        }
    }

    #endregion


    #region Reporting

    internal static void MaybeReport(string path, PropertyInfo? prop, ScriptExaminerSettings settings, ReportFlags toCheck, int depth)
    {
        if (!settings.ReportFlags.HasFlag(toCheck))
        {
            return;
        }
        string message = ConstructMessage(path, toCheck, depth);
        if (prop != null)
        {
            message = AppendWithPropertyName(prop, message);
        }
        Report(toCheck, message);
    }

    internal static void MaybeReportWithValue<T>(T valueA, T valueB, string path, PropertyInfo prop, ScriptExaminerSettings settings, ReportFlags toCheck, int depth)
    {
        if (!settings.ReportFlags.HasFlag(toCheck))
        {
            return;
        }
        string message = ConstructMessage(path, toCheck, depth);
        if (prop != null)
        {
            message = AppendWithPropertyName(prop, message);
        }
        message = AppendWithValue(valueA, valueB, message);
        Report(toCheck, message);
    }

    internal static string ConstructMessage(string path, ReportFlags toCheck, int depth) => $"[{toCheck}]".PadRight(FlagPrefixWidth) + Indent(depth) + path;
    internal static string AppendWithPropertyName(PropertyInfo prop, string message) => $"{message}.{prop.Name} ({prop.PropertyType.Name})";
    internal static string AppendWithValue<T>(T valueA, T valueB, string message) => $"{message}: {valueA} vs {valueB}";

    internal static void Report(ReportFlags toCheck, string message)
    {
        switch (toCheck)
        {
            case ReportFlags.Error:
                MelonLogger.Error(message);
                return;
            case ReportFlags.MaxDepth:
            case ReportFlags.LengthMismatch:
                MelonLogger.Warning(message);
                return;
            default:
                MelonLogger.Msg(message);
                return;
        }
    }

    #endregion
}
