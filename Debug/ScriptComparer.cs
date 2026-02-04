using Il2Cpp;
using MelonLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MehToolBox;

public static class ScriptComparer
{
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

        Default = NullMismatch | ValueDifferent | LengthMismatch,
    }

    [Flags]
    internal enum ErrorState : uint
    {
        None = 0U,
        LeftSide = 1U << 0,
        RightSide = 1U << 1,
        Both = LeftSide | RightSide,
    }

    public class ScriptComparerSettings
    {
        public ReportFlags ReportFlags = ReportFlags.Default;
        public int MaxDepth = 5;
        public bool WarnOnMaxDepthReached = false;
        public string OptionalDescription = "";

        /// <summary>
        /// Do not perform value comparisons between properties of these types
        /// </summary>
        public HashSet<Type> NoCompareTypes = new HashSet<Type>
        {
            typeof(IntPtr),
            typeof(UIntPtr),
            typeof(CancellationToken),
            typeof(CancellationTokenSource),
            typeof(IEnumerable),
            typeof(Matrix4x4)
        };

        /// <summary>
        /// Do not perform value comparisons between properties with these names
        /// </summary>
        public HashSet<string> NoCompareNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
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
        /// Do not perform value comparisons between properties with these types when the instances being compared are of these declaring types
        /// </summary>
        public HashSet<(Type declaringType, Type propertyType)> NoCompareTypeTypePairs = new HashSet<(Type, Type)>
        {
        };


        /// <summary>
        /// Do not perform value comparisons between properties with these names when the instances being compared are of these declaring types
        /// </summary>
        public HashSet<(Type declaringType, string propName)> NoCompareTypeNamePairs = new HashSet<(Type, string)>
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
        /// Do not recursively example property values of object instances of these declaring types when the properties have these names
        /// </summary>
        public HashSet<(Type declaringType, string propName)> NoRecurseTypeNamePairs = new HashSet<(Type, string)>
        {

        };
    }

    /// <summary>
    /// Recursively compares two UnityEngine.Object instances of identical type. Stays true to the contract of the declaring type at all times - no wandering into uneven data structures.
    /// </summary>
    /// <param name="settings">Optional settings construct with reasonable preset parameters.</param>
    /// 
    public static void ReportDifferences<T>(
        T a,
        T b,
        ScriptComparerSettings? settings = null)
        where T : UnityEngine.Object
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

        if (settings == null)
        {
            settings = new ScriptComparerSettings();
        }

        MelonLogger.Msg("");
        MelonLogger.Msg($"ReportDifferences between {a.name} and {b.name} ({a.GetType()})");
        if (!string.IsNullOrEmpty(settings.OptionalDescription))
        {
            MelonLogger.Msg(settings.OptionalDescription);
        }
        MelonLogger.Msg("");

        var visited = new HashSet<(object, object)>();
        CompareObject(a, b, a.GetType(), a.GetType().Name, 0, visited, settings);
    }


    internal static readonly Dictionary<Type, List<PropertyInfo>> TypeToPropertyCache = new Dictionary<Type, List<PropertyInfo>>();


    internal static bool ShouldCompareValue(Type declaringType, PropertyInfo prop, ScriptComparerSettings settings)
    {
        return !settings.NoCompareTypes.Contains(prop.PropertyType)
                && !settings.NoCompareNames.Contains(prop.Name)
                && !settings.NoCompareTypeTypePairs.Contains((declaringType, prop.PropertyType))
                && !settings.NoCompareTypeNamePairs.Contains((declaringType, prop.Name));
    }

    internal static bool ShouldCompareObject(Type declaringType, PropertyInfo prop, ScriptComparerSettings settings)
    {
        return !IsValueType(prop)
                && !settings.NoRecurseTypes.Contains(prop.PropertyType)
                && !settings.NoRecurseNames.Contains(prop.Name)
                && !settings.NoRecurseTypeTypePairs.Contains((declaringType, prop.PropertyType))
                && !settings.NoRecurseTypeNamePairs.Contains((declaringType, prop.Name));
    }


    internal static bool IsValueType(PropertyInfo prop)
    {
        if (prop.PropertyType.IsPrimitive) return true;
        if (prop.PropertyType.IsEnum) return true;
        if (prop.PropertyType == typeof(string)) return true;
        if (prop.PropertyType == typeof(decimal)) return true;
        if (prop.PropertyType == typeof(Quaternion)) return true;
        if (prop.PropertyType == typeof(Vector2)) return true;
        if (prop.PropertyType == typeof(Vector3)) return true;
        if (prop.PropertyType == typeof(Vector4)) return true;
        if (prop.PropertyType == typeof(Matrix4x4)) return true;
        return false;
    }


    internal static void CompareObject<T>(T a, T b, Type declaringType, string path, int depth, HashSet<(object, object)> visited, ScriptComparerSettings settings)
    {
        if (depth >= settings.MaxDepth)
        {
            MaybeReportMaxDepth(path, settings);
            return;
        }

        if (visited.Contains((a, b)) || visited.Contains((b, a)))
        {
            return;
        }

        visited.Add((a, b));

        if (a is IEnumerable aEnum && b is IEnumerable bEnum)
        {
            foreach (Type noRecurseType in settings.NoRecurseTypes)
            {
                if (IsEnumerableOfType(aEnum, noRecurseType) || IsEnumerableOfType(bEnum, noRecurseType))
                {
                    // do not explore enumerables of no-recurse types
                    return;
                }
            }
            var enumeratorA = aEnum.GetEnumerator();
            var enumeratorB = bEnum.GetEnumerator();
            int index = 0;

            while (true)
            {
                bool hasNextA = enumeratorA.MoveNext();
                bool hasNextB = enumeratorB.MoveNext();

                if (!hasNextA && !hasNextB) break; // done
                if (hasNextA != hasNextB)
                {
                    MaybeReportLengthMismatch(path, settings);
                    break;
                }

                CompareObject(enumeratorA.Current, enumeratorB.Current, enumeratorA.Current?.GetType() ?? typeof(object), $"{path}[{index}]", depth + 1, visited, settings);
                index++;
            }

            return;
        }

        var type = a.GetType();
        if (!TypeToPropertyCache.TryGetValue(type, out var props))
        {
            props = new List<PropertyInfo>(type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            TypeToPropertyCache[type] = props;
        }

        foreach (var prop in props)
        {
            if (!prop.CanRead)
            {
                continue;
            }

            bool shouldCompareValue = ShouldCompareValue(declaringType, prop, settings);
            bool shouldCompareObject = ShouldCompareObject(declaringType, prop, settings);

            if (!shouldCompareValue && !shouldCompareObject)
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
                MaybeReportError($"({errorState}) {path}", prop, settings);
            }


            if (shouldCompareValue)
            {
                CompareValue(valueA, valueB, path, prop, settings);
            }

            if (shouldCompareObject && NeitherNull(valueA, valueB))
            {
                CompareObject(valueA, valueB, prop.DeclaringType, $"{path}.{prop.Name}", depth + 1, visited, settings);
            }
        }
    }


    internal static void CompareValue<T>(T valueA, T valueB, string path, PropertyInfo prop, ScriptComparerSettings settings)
    {
        if (NullMismatch(valueA, valueB))
        {
            MaybeReportNullMismatch(valueA, valueB, path, prop, settings);
            return;
        }
        if (BothNull(valueA, valueB))
        {
            MaybeReportBothNull(path, prop, settings);
            return;
        }
        if (IsValueType(prop))
        {
            if (!valueA.Equals(valueB))
            {
                MaybeReportValueDifferent(valueA, valueB, path, prop, settings);
            }
            else
            {
                MaybeReportValueEqual(path, prop, settings);
            }
            return;
        }
        if (!ReferenceEquals(valueA, valueB))
        {
            MaybeReportReferenceDifferent(valueA, valueB, path, prop, settings);
        }
        else
        {
            MaybeReportReferenceEqual(path, prop, settings);
        }
    }

    internal static bool NullMismatch<T>(T valueA, T valueB) => (!SafeUnityNull(valueA) && SafeUnityNull(valueB)) || (SafeUnityNull(valueA) && !SafeUnityNull(valueB));
    internal static bool BothNull<T>(T valueA, T valueB) => SafeUnityNull(valueA) && SafeUnityNull(valueB);
    internal static bool NeitherNull<T>(T valueA, T valueB) => !SafeUnityNull(valueA) && !SafeUnityNull(valueB);
    internal static bool SafeUnityNull<T>(T value) => (value as object) == null;

    internal static void MaybeReportNullMismatch<T>(T valueA, T valueB, string path, PropertyInfo prop, ScriptComparerSettings settings) => MaybeReportWithValue(valueA, valueB, path, prop, settings, ReportFlags.NullMismatch);
    internal static void MaybeReportBothNull(string path, PropertyInfo prop, ScriptComparerSettings settings) => MaybeReport(path, prop, settings, ReportFlags.BothNull);
    internal static void MaybeReportReferenceDifferent<T>(T valueA, T valueB, string path, PropertyInfo prop, ScriptComparerSettings settings) => MaybeReportWithValue(valueA, valueB, path, prop, settings, ReportFlags.ReferenceDifferent);
    internal static void MaybeReportReferenceEqual(string path, PropertyInfo prop, ScriptComparerSettings settings) => MaybeReport(path, prop, settings, ReportFlags.ReferenceEqual);
    internal static void MaybeReportValueDifferent<T>(T valueA, T valueB, string path, PropertyInfo prop, ScriptComparerSettings settings) => MaybeReportWithValue(valueA, valueB, path, prop, settings, ReportFlags.ValueDifferent);
    internal static void MaybeReportValueEqual(string path, PropertyInfo prop, ScriptComparerSettings settings) => MaybeReport(path, prop, settings, ReportFlags.ValueEqual);
    internal static void MaybeReportLengthMismatch(string path, ScriptComparerSettings settings) => MaybeReport(path, null, settings, ReportFlags.LengthMismatch);
    internal static void MaybeReportMaxDepth(string path, ScriptComparerSettings settings) => MaybeReport(path, null, settings, ReportFlags.MaxDepth);
    internal static void MaybeReportError(string path, PropertyInfo prop, ScriptComparerSettings settings) => MaybeReport(path, prop, settings, ReportFlags.Error);


    internal static void MaybeReport(string path, PropertyInfo prop, ScriptComparerSettings settings, ReportFlags toCheck)
    {
        if (!settings.ReportFlags.HasFlag(toCheck))
        {
            return;
        }
        string message = ConstructMessage(path, toCheck);
        if (prop != null)
        {
            message = AppendWithPropertyName(prop, message);
        }
        Report(toCheck, message);
    }

    internal static void MaybeReportWithValue<T>(T valueA, T valueB, string path, PropertyInfo prop, ScriptComparerSettings settings, ReportFlags toCheck)
    {
        if (!settings.ReportFlags.HasFlag(toCheck))
        {
            return;
        }
        string message = ConstructMessage(path, toCheck);
        if (prop != null)
        {
            message = AppendWithPropertyName(prop, message);
        }
        message = AppendWithValue(valueA, valueB, message);
        Report(toCheck, message);

    }

    internal static string ConstructMessage(string path, ReportFlags toCheck) => $"[{toCheck}]".PadRight(25) + $" {path}";
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

    internal static bool IsEnumerableOfType(IEnumerable enumerable, Type elementType)
    {
        if (enumerable == null || elementType == null)
            return false;

        // Get the IEnumerable<> interfaces implemented by the runtime type
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
}

