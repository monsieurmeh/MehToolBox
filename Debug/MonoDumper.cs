using MelonLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MehToolBox;

public static class MonoDumper
{
    public static void Dump(object obj, int maxDepth = 3)
    {
        DumpInternal(obj, 0, maxDepth, new HashSet<object>());
    }

    internal static void DumpInternal(object obj, int depth, int maxDepth, HashSet<object> visited)
    {
        if (obj == null) return;
        if (depth > maxDepth) return;

        // Prevent cycles
        if (!IsSimple(obj))
        {
            if (visited.Contains(obj)) return;
            visited.Add(obj);
        }

        var type = obj.GetType();
        string indent = new string(' ', depth * 2);

        try
        {
            // Handle iterables (but not strings)
            if (obj is IEnumerable enumerable && obj is not string)
            {
                int i = 0;
                foreach (var item in enumerable)
                {
                    DumpInternal(item, depth + 1, maxDepth, visited);
                    if (++i > 20) break; // hard cap to avoid spam
                }
                return;
            }

            foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                try
                {
                    var val = f.GetValue(obj);
                    MelonLogger.Msg($"{indent}{type.Name}.{f.Name}: {val}");
                    DumpInternal(val, depth + 1, maxDepth, visited);
                }
                catch { }
            }

            foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                try
                {
                    if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                    var val = p.GetValue(obj);
                    MelonLogger.Msg($"{indent}{type.Name}.{p.Name}: {val}");
                    DumpInternal(val, depth + 1, maxDepth, visited);
                }
                catch { }
            }
        }
        catch { }
    }

    static bool IsSimple(object obj)
    {
        var t = obj.GetType();
        return t.IsPrimitive
            || obj is string
            || obj is decimal
            || obj is Vector2
            || obj is Vector3
            || obj is Quaternion;
    }
}