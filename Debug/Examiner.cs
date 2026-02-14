// ============================================================================
// Examiner — Dual-polarity filtering with fluent builder + Map
// ============================================================================
//
// Parallel to ScriptExaminer. Same Dump/Compare operations, but with separate
// blacklist AND whitelist hashsets per filter dimension, plus a new Map operation
// for getting a structural overview of a GameObject hierarchy.
//
// Quick start:
//
//   // Verbose dump (blacklist keeps the dangerous stuff out)
//   Examiner.Dump(myObject, new Examiner.Settings().WithBlacklist().ForDump());
//
//   // Sparse dump (whitelist only lets known-good properties through)
//   Examiner.Dump(myObject, new Examiner.Settings().WithWhitelist().ForDump());
//
//   // Strict: blacklist kills bad stuff, whitelist gates what survives
//   Examiner.Dump(myObject, Examiner.Settings.Strict().ForDump());
//
//   // Map a hierarchy (shows component tree + property legend)
//   Examiner.Map(someGameObject);
//
// How filtering works:
//
//   1. Blacklist pass: each ACTIVE blacklist is checked. Match = DENY, stop.
//   2. Whitelist pass: if ANY whitelist is active, at least ONE must match.
//      If NO whitelists are active, everything that survived the blacklist is allowed.
//   3. Survived both = allowed.
//
// There are 8 filter dimensions, each with a blacklist AND whitelist hashset (16 total).
// All 16 always contain reasonable defaults. The FilterFlags enum controls which
// ones are actually enforced — inactive hashsets are ignored, not empty.
//
// Builder verbs:
//   With*     — replace entire hashset contents
//   Merge*    — add entries into existing hashset
//   Strip*    — remove entries from existing hashset
//
// Type matching uses IsAssignableFrom for hierarchy — whitelisting MonoBehaviour
// will match all its subclasses.
// ============================================================================

using Il2Cpp;
using MelonLoader;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace MehToolBox;

/// <summary>
/// Dual-polarity examiner for any object — UnityEngine.Object, Il2Cpp types, plain C#, arrays, etc.
/// Dump/Compare operations with dual blacklist+whitelist filtering, plus a Map operation for
/// getting a structural overview of a GameObject hierarchy.
/// </summary>
public static class Examiner
{
    /// <summary>
    /// Controls which of the 16 filter hashsets are actually enforced.
    /// Each bit activates one hashset. Inactive hashsets are ignored, not cleared.
    /// </summary>
    [Flags]
    public enum FilterFlags : ushort
    {
        None = 0,

        ExamineBlacklistTypes         = 1 << 0,
        ExamineWhitelistTypes         = 1 << 1,
        ExamineBlacklistNames         = 1 << 2,
        ExamineWhitelistNames         = 1 << 3,
        ExamineBlacklistTypeTypePairs = 1 << 4,
        ExamineWhitelistTypeTypePairs = 1 << 5,
        ExamineBlacklistTypeNamePairs = 1 << 6,
        ExamineWhitelistTypeNamePairs = 1 << 7,

        RecurseBlacklistTypes         = 1 << 8,
        RecurseWhitelistTypes         = 1 << 9,
        RecurseBlacklistNames         = 1 << 10,
        RecurseWhitelistNames         = 1 << 11,
        RecurseBlacklistTypeTypePairs = 1 << 12,
        RecurseWhitelistTypeTypePairs = 1 << 13,
        RecurseBlacklistTypeNamePairs = 1 << 14,
        RecurseWhitelistTypeNamePairs = 1 << 15,

        AllExamineBlacklists = ExamineBlacklistTypes | ExamineBlacklistNames
                             | ExamineBlacklistTypeTypePairs | ExamineBlacklistTypeNamePairs,
        AllExamineWhitelists = ExamineWhitelistTypes | ExamineWhitelistNames
                             | ExamineWhitelistTypeTypePairs | ExamineWhitelistTypeNamePairs,
        AllRecurseBlacklists = RecurseBlacklistTypes | RecurseBlacklistNames
                             | RecurseBlacklistTypeTypePairs | RecurseBlacklistTypeNamePairs,
        AllRecurseWhitelists = RecurseWhitelistTypes | RecurseWhitelistNames
                             | RecurseWhitelistTypeTypePairs | RecurseWhitelistTypeNamePairs,

        AllBlacklists = AllExamineBlacklists | AllRecurseBlacklists,
        AllWhitelists = AllExamineWhitelists | AllRecurseWhitelists,
        All = 0xFFFF,
    }

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

    /// <summary>
    /// Fluent-builder settings for Examiner.
    /// All 16 hashsets are populated with sensible defaults on construction.
    /// No flags are active until you call WithBlacklist(), WithWhitelist(), etc.
    /// </summary>
    public class Settings
    {
    /// <summary>Which filter hashsets are actually enforced. None by default.</summary>
        public FilterFlags ActiveFilters = FilterFlags.None;

        /// <summary>What gets printed during Dump/Compare. Set by ForDump()/ForCompare().</summary>
        public ReportFlags ReportFlags = ReportFlags.None;

        /// <summary>Max recursion depth for Dump/Compare property traversal.</summary>
        public int MaxDepth = 5;

        /// <summary>Max items to iterate in enumerables before truncating.</summary>
        public int MaxEnumerableItems = 50;

        /// <summary>Max depth for Map's GameObject tree traversal.</summary>
        public int MaxTreeDepth = 10;

        /// <summary>When true, Map prunes branches that have no matching components.</summary>
        public bool PruneEmptyBranches = true;

        /// <summary>Optional description printed in Dump/Compare headers.</summary>
        public string Description = "";

        /// <summary>Console for MelonLogger output, Json for JSON string output.</summary>
        public OutputFormat OutputFormat = OutputFormat.Console;


        #region Hashset Fields — always populated with defaults

        // --- Examine Blacklists (things to never read values from) ---

        internal HashSet<Type> mExamineBlacklistTypes = new()
        {
            typeof(IntPtr), typeof(UIntPtr),
            typeof(CancellationToken), typeof(CancellationTokenSource),
            typeof(IEnumerable), typeof(Matrix4x4)
        };

        internal HashSet<string> mExamineBlacklistNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Pointer", "Token", "_source", "name", "parent", "m_AssetGUID", ""
        };

        internal HashSet<(Type, Type)> mExamineBlacklistTypeTypePairs = new();
        internal HashSet<(Type, string)> mExamineBlacklistTypeNamePairs = new();

        // --- Examine Whitelists (known-good properties to report on) ---

        internal HashSet<Type> mExamineWhitelistTypes = new()
        {
            typeof(float), typeof(int), typeof(bool), typeof(string),
            typeof(Vector3), typeof(Quaternion), typeof(Enum)
        };

        internal HashSet<string> mExamineWhitelistNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "enabled", "isActiveAndEnabled", "activeSelf",
            "position", "rotation", "localPosition", "localRotation", "localScale",
            "tag", "layer"
        };

        internal HashSet<(Type, Type)> mExamineWhitelistTypeTypePairs = new();
        internal HashSet<(Type, string)> mExamineWhitelistTypeNamePairs = new();

        // --- Recurse Blacklists (things to never descend into) ---

        internal HashSet<Type> mRecurseBlacklistTypes = new()
        {
            typeof(IntPtr), typeof(UIntPtr),
            typeof(CancellationToken), typeof(CancellationTokenSource),
            typeof(Transform), typeof(MeshRenderer), typeof(MeshFilter)
        };

        internal HashSet<string> mRecurseBlacklistNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Pointer", "Token", "_source", "gameObject"
        };

        internal HashSet<(Type, Type)> mRecurseBlacklistTypeTypePairs = new();
        internal HashSet<(Type, string)> mRecurseBlacklistTypeNamePairs = new();

        // --- Recurse Whitelists (the only things we descend into when active) ---

        internal HashSet<Type> mRecurseWhitelistTypes = new()
        {
            typeof(MonoBehaviour)
        };

        internal HashSet<string> mRecurseWhitelistNames = new(StringComparer.OrdinalIgnoreCase);
        internal HashSet<(Type, Type)> mRecurseWhitelistTypeTypePairs = new();
        internal HashSet<(Type, string)> mRecurseWhitelistTypeNamePairs = new();

        #endregion


        #region Flag Activation

        /// <summary>Activate all blacklist filters (OR'd into existing flags).</summary>
        public Settings WithBlacklist()
        {
            ActiveFilters |= FilterFlags.AllBlacklists;
            return this;
        }

        /// <summary>Activate all whitelist filters (OR'd into existing flags).</summary>
        public Settings WithWhitelist()
        {
            ActiveFilters |= FilterFlags.AllWhitelists;
            return this;
        }

        /// <summary>Replace the active flags entirely.</summary>
        public Settings WithFilterFlags(FilterFlags flags)
        {
            ActiveFilters = flags;
            return this;
        }

        /// <summary>Add specific flags (OR'd into existing).</summary>
        public Settings AddFilterFlags(FilterFlags flags)
        {
            ActiveFilters |= flags;
            return this;
        }

        /// <summary>Remove specific flags from the active set.</summary>
        public Settings StripFilterFlags(FilterFlags flags)
        {
            ActiveFilters &= ~flags;
            return this;
        }

        #endregion


        #region Report / Operation Presets

        /// <summary>Configure for a dump operation (ReportFlags = DefaultDump).</summary>
        public Settings ForDump()
        {
            ReportFlags = ReportFlags.DefaultDump;
            return this;
        }

        /// <summary>Configure for a compare operation (ReportFlags = DefaultCompare).</summary>
        public Settings ForCompare()
        {
            ReportFlags = ReportFlags.DefaultCompare;
            return this;
        }

        /// <summary>Configure for a map operation (tree depth, pruning, no report flags).</summary>
        public Settings ForMap(int maxTreeDepth = 10, bool pruneEmpty = true)
        {
            MaxTreeDepth = maxTreeDepth;
            PruneEmptyBranches = pruneEmpty;
            ReportFlags = ReportFlags.None;
            return this;
        }

        #endregion


        #region Hashset Content — Examine Blacklist Types

        /// <summary>Replace the examine blacklist types entirely.</summary>
        public Settings WithExamineBlacklistTypes(params Type[] types)
        {
            mExamineBlacklistTypes = new HashSet<Type>(types);
            return this;
        }

        /// <summary>Add types to the examine blacklist.</summary>
        public Settings MergeExamineBlacklistTypes(params Type[] types)
        {
            mExamineBlacklistTypes.UnionWith(types);
            return this;
        }

        /// <summary>Remove types from the examine blacklist.</summary>
        public Settings StripExamineBlacklistTypes(params Type[] types)
        {
            mExamineBlacklistTypes.ExceptWith(types);
            return this;
        }

        #endregion


        #region Hashset Content — Examine Whitelist Types

        /// <summary>Replace the examine whitelist types entirely.</summary>
        public Settings WithExamineWhitelistTypes(params Type[] types)
        {
            mExamineWhitelistTypes = new HashSet<Type>(types);
            return this;
        }

        /// <summary>Add types to the examine whitelist.</summary>
        public Settings MergeExamineWhitelistTypes(params Type[] types)
        {
            mExamineWhitelistTypes.UnionWith(types);
            return this;
        }

        /// <summary>Remove types from the examine whitelist.</summary>
        public Settings StripExamineWhitelistTypes(params Type[] types)
        {
            mExamineWhitelistTypes.ExceptWith(types);
            return this;
        }

        #endregion


        #region Hashset Content — Examine Blacklist Names

        /// <summary>Replace the examine blacklist names entirely.</summary>
        public Settings WithExamineBlacklistNames(params string[] names)
        {
            mExamineBlacklistNames = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            return this;
        }

        /// <summary>Add names to the examine blacklist.</summary>
        public Settings MergeExamineBlacklistNames(params string[] names)
        {
            foreach (string n in names) mExamineBlacklistNames.Add(n);
            return this;
        }

        /// <summary>Remove names from the examine blacklist.</summary>
        public Settings StripExamineBlacklistNames(params string[] names)
        {
            mExamineBlacklistNames.ExceptWith(names);
            return this;
        }

        #endregion


        #region Hashset Content — Examine Whitelist Names

        /// <summary>Replace the examine whitelist names entirely.</summary>
        public Settings WithExamineWhitelistNames(params string[] names)
        {
            mExamineWhitelistNames = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            return this;
        }

        /// <summary>Add names to the examine whitelist.</summary>
        public Settings MergeExamineWhitelistNames(params string[] names)
        {
            foreach (string n in names) mExamineWhitelistNames.Add(n);
            return this;
        }

        /// <summary>Remove names from the examine whitelist.</summary>
        public Settings StripExamineWhitelistNames(params string[] names)
        {
            mExamineWhitelistNames.ExceptWith(names);
            return this;
        }

        #endregion


        #region Hashset Content — Examine Blacklist TypeType Pairs

        /// <summary>Replace the examine blacklist (declaringType, propertyType) pairs.</summary>
        public Settings WithExamineBlacklistTypeTypePairs(params (Type, Type)[] pairs)
        {
            mExamineBlacklistTypeTypePairs = new HashSet<(Type, Type)>(pairs);
            return this;
        }

        /// <summary>Add (declaringType, propertyType) pairs to the examine blacklist.</summary>
        public Settings MergeExamineBlacklistTypeTypePairs(params (Type, Type)[] pairs)
        {
            mExamineBlacklistTypeTypePairs.UnionWith(pairs);
            return this;
        }

        /// <summary>Remove (declaringType, propertyType) pairs from the examine blacklist.</summary>
        public Settings StripExamineBlacklistTypeTypePairs(params (Type, Type)[] pairs)
        {
            mExamineBlacklistTypeTypePairs.ExceptWith(pairs);
            return this;
        }

        #endregion


        #region Hashset Content — Examine Whitelist TypeType Pairs

        /// <summary>Replace the examine whitelist (declaringType, propertyType) pairs.</summary>
        public Settings WithExamineWhitelistTypeTypePairs(params (Type, Type)[] pairs)
        {
            mExamineWhitelistTypeTypePairs = new HashSet<(Type, Type)>(pairs);
            return this;
        }

        /// <summary>Add (declaringType, propertyType) pairs to the examine whitelist.</summary>
        public Settings MergeExamineWhitelistTypeTypePairs(params (Type, Type)[] pairs)
        {
            mExamineWhitelistTypeTypePairs.UnionWith(pairs);
            return this;
        }

        /// <summary>Remove (declaringType, propertyType) pairs from the examine whitelist.</summary>
        public Settings StripExamineWhitelistTypeTypePairs(params (Type, Type)[] pairs)
        {
            mExamineWhitelistTypeTypePairs.ExceptWith(pairs);
            return this;
        }

        #endregion


        #region Hashset Content — Examine Blacklist TypeName Pairs

        /// <summary>Replace the examine blacklist (declaringType, propName) pairs.</summary>
        public Settings WithExamineBlacklistTypeNamePairs(params (Type, string)[] pairs)
        {
            mExamineBlacklistTypeNamePairs = new HashSet<(Type, string)>(pairs);
            return this;
        }

        /// <summary>Add (declaringType, propName) pairs to the examine blacklist.</summary>
        public Settings MergeExamineBlacklistTypeNamePairs(params (Type, string)[] pairs)
        {
            mExamineBlacklistTypeNamePairs.UnionWith(pairs);
            return this;
        }

        /// <summary>Remove (declaringType, propName) pairs from the examine blacklist.</summary>
        public Settings StripExamineBlacklistTypeNamePairs(params (Type, string)[] pairs)
        {
            mExamineBlacklistTypeNamePairs.ExceptWith(pairs);
            return this;
        }

        #endregion


        #region Hashset Content — Examine Whitelist TypeName Pairs

        /// <summary>Replace the examine whitelist (declaringType, propName) pairs.</summary>
        public Settings WithExamineWhitelistTypeNamePairs(params (Type, string)[] pairs)
        {
            mExamineWhitelistTypeNamePairs = new HashSet<(Type, string)>(pairs);
            return this;
        }

        /// <summary>Add (declaringType, propName) pairs to the examine whitelist.</summary>
        public Settings MergeExamineWhitelistTypeNamePairs(params (Type, string)[] pairs)
        {
            mExamineWhitelistTypeNamePairs.UnionWith(pairs);
            return this;
        }

        /// <summary>Remove (declaringType, propName) pairs from the examine whitelist.</summary>
        public Settings StripExamineWhitelistTypeNamePairs(params (Type, string)[] pairs)
        {
            mExamineWhitelistTypeNamePairs.ExceptWith(pairs);
            return this;
        }

        #endregion


        #region Hashset Content — Recurse Blacklist Types

        /// <summary>Replace the recurse blacklist types entirely.</summary>
        public Settings WithRecurseBlacklistTypes(params Type[] types)
        {
            mRecurseBlacklistTypes = new HashSet<Type>(types);
            return this;
        }

        /// <summary>Add types to the recurse blacklist.</summary>
        public Settings MergeRecurseBlacklistTypes(params Type[] types)
        {
            mRecurseBlacklistTypes.UnionWith(types);
            return this;
        }

        /// <summary>Remove types from the recurse blacklist.</summary>
        public Settings StripRecurseBlacklistTypes(params Type[] types)
        {
            mRecurseBlacklistTypes.ExceptWith(types);
            return this;
        }

        #endregion


        #region Hashset Content — Recurse Whitelist Types

        /// <summary>Replace the recurse whitelist types entirely.</summary>
        public Settings WithRecurseWhitelistTypes(params Type[] types)
        {
            mRecurseWhitelistTypes = new HashSet<Type>(types);
            return this;
        }

        /// <summary>Add types to the recurse whitelist.</summary>
        public Settings MergeRecurseWhitelistTypes(params Type[] types)
        {
            mRecurseWhitelistTypes.UnionWith(types);
            return this;
        }

        /// <summary>Remove types from the recurse whitelist.</summary>
        public Settings StripRecurseWhitelistTypes(params Type[] types)
        {
            mRecurseWhitelistTypes.ExceptWith(types);
            return this;
        }

        #endregion


        #region Hashset Content — Recurse Blacklist Names

        /// <summary>Replace the recurse blacklist names entirely.</summary>
        public Settings WithRecurseBlacklistNames(params string[] names)
        {
            mRecurseBlacklistNames = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            return this;
        }

        /// <summary>Add names to the recurse blacklist.</summary>
        public Settings MergeRecurseBlacklistNames(params string[] names)
        {
            foreach (string n in names) mRecurseBlacklistNames.Add(n);
            return this;
        }

        /// <summary>Remove names from the recurse blacklist.</summary>
        public Settings StripRecurseBlacklistNames(params string[] names)
        {
            mRecurseBlacklistNames.ExceptWith(names);
            return this;
        }

        #endregion


        #region Hashset Content — Recurse Whitelist Names

        /// <summary>Replace the recurse whitelist names entirely.</summary>
        public Settings WithRecurseWhitelistNames(params string[] names)
        {
            mRecurseWhitelistNames = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            return this;
        }

        /// <summary>Add names to the recurse whitelist.</summary>
        public Settings MergeRecurseWhitelistNames(params string[] names)
        {
            foreach (string n in names) mRecurseWhitelistNames.Add(n);
            return this;
        }

        /// <summary>Remove names from the recurse whitelist.</summary>
        public Settings StripRecurseWhitelistNames(params string[] names)
        {
            mRecurseWhitelistNames.ExceptWith(names);
            return this;
        }

        #endregion


        #region Hashset Content — Recurse Blacklist TypeType Pairs

        /// <summary>Replace the recurse blacklist (declaringType, propertyType) pairs.</summary>
        public Settings WithRecurseBlacklistTypeTypePairs(params (Type, Type)[] pairs)
        {
            mRecurseBlacklistTypeTypePairs = new HashSet<(Type, Type)>(pairs);
            return this;
        }

        /// <summary>Add (declaringType, propertyType) pairs to the recurse blacklist.</summary>
        public Settings MergeRecurseBlacklistTypeTypePairs(params (Type, Type)[] pairs)
        {
            mRecurseBlacklistTypeTypePairs.UnionWith(pairs);
            return this;
        }

        /// <summary>Remove (declaringType, propertyType) pairs from the recurse blacklist.</summary>
        public Settings StripRecurseBlacklistTypeTypePairs(params (Type, Type)[] pairs)
        {
            mRecurseBlacklistTypeTypePairs.ExceptWith(pairs);
            return this;
        }

        #endregion


        #region Hashset Content — Recurse Whitelist TypeType Pairs

        /// <summary>Replace the recurse whitelist (declaringType, propertyType) pairs.</summary>
        public Settings WithRecurseWhitelistTypeTypePairs(params (Type, Type)[] pairs)
        {
            mRecurseWhitelistTypeTypePairs = new HashSet<(Type, Type)>(pairs);
            return this;
        }

        /// <summary>Add (declaringType, propertyType) pairs to the recurse whitelist.</summary>
        public Settings MergeRecurseWhitelistTypeTypePairs(params (Type, Type)[] pairs)
        {
            mRecurseWhitelistTypeTypePairs.UnionWith(pairs);
            return this;
        }

        /// <summary>Remove (declaringType, propertyType) pairs from the recurse whitelist.</summary>
        public Settings StripRecurseWhitelistTypeTypePairs(params (Type, Type)[] pairs)
        {
            mRecurseWhitelistTypeTypePairs.ExceptWith(pairs);
            return this;
        }

        #endregion


        #region Hashset Content — Recurse Blacklist TypeName Pairs

        /// <summary>Replace the recurse blacklist (declaringType, propName) pairs.</summary>
        public Settings WithRecurseBlacklistTypeNamePairs(params (Type, string)[] pairs)
        {
            mRecurseBlacklistTypeNamePairs = new HashSet<(Type, string)>(pairs);
            return this;
        }

        /// <summary>Add (declaringType, propName) pairs to the recurse blacklist.</summary>
        public Settings MergeRecurseBlacklistTypeNamePairs(params (Type, string)[] pairs)
        {
            mRecurseBlacklistTypeNamePairs.UnionWith(pairs);
            return this;
        }

        /// <summary>Remove (declaringType, propName) pairs from the recurse blacklist.</summary>
        public Settings StripRecurseBlacklistTypeNamePairs(params (Type, string)[] pairs)
        {
            mRecurseBlacklistTypeNamePairs.ExceptWith(pairs);
            return this;
        }

        #endregion


        #region Hashset Content — Recurse Whitelist TypeName Pairs

        /// <summary>Replace the recurse whitelist (declaringType, propName) pairs.</summary>
        public Settings WithRecurseWhitelistTypeNamePairs(params (Type, string)[] pairs)
        {
            mRecurseWhitelistTypeNamePairs = new HashSet<(Type, string)>(pairs);
            return this;
        }

        /// <summary>Add (declaringType, propName) pairs to the recurse whitelist.</summary>
        public Settings MergeRecurseWhitelistTypeNamePairs(params (Type, string)[] pairs)
        {
            mRecurseWhitelistTypeNamePairs.UnionWith(pairs);
            return this;
        }

        /// <summary>Remove (declaringType, propName) pairs from the recurse whitelist.</summary>
        public Settings StripRecurseWhitelistTypeNamePairs(params (Type, string)[] pairs)
        {
            mRecurseWhitelistTypeNamePairs.ExceptWith(pairs);
            return this;
        }

        #endregion


        #region Scalar Settings

        /// <summary>Set max recursion depth for Dump/Compare.</summary>
        public Settings WithMaxDepth(int depth)
        {
            MaxDepth = depth;
            return this;
        }

        /// <summary>Set max items to iterate in enumerables.</summary>
        public Settings WithMaxEnumerableItems(int count)
        {
            MaxEnumerableItems = count;
            return this;
        }

        /// <summary>Set max tree depth for Map.</summary>
        public Settings WithMaxTreeDepth(int depth)
        {
            MaxTreeDepth = depth;
            return this;
        }

        /// <summary>Enable/disable empty branch pruning for Map.</summary>
        public Settings WithPruneEmptyBranches(bool prune)
        {
            PruneEmptyBranches = prune;
            return this;
        }

        /// <summary>Set an optional description for Dump/Compare headers.</summary>
        public Settings WithDescription(string desc)
        {
            Description = desc;
            return this;
        }

        /// <summary>Set output format (Console or Json).</summary>
        public Settings WithOutputFormat(OutputFormat format)
        {
            OutputFormat = format;
            return this;
        }

        #endregion


        #region Convenience Static Factories

        /// <summary>Shorthand: all blacklists active, nothing else.</summary>
        public static Settings Blacklist() => new Settings().WithBlacklist();

        /// <summary>Shorthand: all whitelists active, nothing else.</summary>
        public static Settings Whitelist() => new Settings().WithWhitelist();

        /// <summary>Shorthand: all blacklists AND whitelists active (most restrictive).</summary>
        public static Settings Strict() => new Settings().WithBlacklist().WithWhitelist();

        #endregion
    }


    #region Public API

    /// <summary>
    /// Dump any object, recursively examining its properties.
    /// Works with UnityEngine.Object, Il2Cpp objects, plain C# objects, arrays, lists, etc.
    /// </summary>
    public static void Dump(object obj, Settings? settings = null)
    {
        if (obj == null)
        {
            MelonLogger.Error("Cannot dump null object.");
            return;
        }

        settings ??= new Settings().WithBlacklist().ForDump();
        HashSet<object> visited = new();
        Type type = obj.GetType();
        string displayName = obj is UnityEngine.Object uo ? uo.name : type.Name;

        if (settings.OutputFormat == OutputFormat.Json)
        {
            JsonDumpBuilder jsonBuilder = new();
            jsonBuilder.BeginObject(type.Name, type.Name);
            DumpObject(obj, type, type.Name, 0, visited, settings, jsonBuilder);
            jsonBuilder.EndObject();

            MelonLogger.Msg($"JSON Dump of {displayName} ({type}):");
            MelonLogger.Msg(jsonBuilder.ToJson());
            return;
        }

        MelonLogger.Msg("");
        MelonLogger.Msg($"Dump of {displayName} ({type})");
        if (!string.IsNullOrEmpty(settings.Description))
            MelonLogger.Msg(settings.Description);
        MelonLogger.Msg("");

        DumpObject(obj, type, type.Name, 0, visited, settings);
    }

    /// <summary>
    /// Compare two objects of the same type, reporting differences.
    /// Works with UnityEngine.Object, Il2Cpp objects, plain C# objects, etc.
    /// </summary>
    public static void Compare(object a, object b, Settings? settings = null)
    {
        if (a == null || b == null)
        {
            MelonLogger.Error("Cannot compare null objects.");
            return;
        }

        if (a.GetType() != b.GetType())
        {
            MelonLogger.Error($"Cannot compare different types: {a.GetType().Name} vs {b.GetType().Name}.");
            return;
        }

        settings ??= new Settings().WithBlacklist().ForCompare();

        string nameA = a is UnityEngine.Object uoA ? uoA.name : a.GetType().Name;
        string nameB = b is UnityEngine.Object uoB ? uoB.name : b.GetType().Name;

        MelonLogger.Msg("");
        MelonLogger.Msg($"Compare between {nameA} and {nameB} ({a.GetType()})");
        if (!string.IsNullOrEmpty(settings.Description))
            MelonLogger.Msg(settings.Description);
        MelonLogger.Msg("");

        HashSet<(object, object)> visited = new();
        CompareObjects(a, b, a.GetType(), a.GetType().Name, 0, visited, settings);
    }

    /// <summary>
    /// Dump any object to a JSON string.
    /// </summary>
    public static string DumpToJson(object obj, Settings? settings = null)
    {
        if (obj == null) return "null";

        settings ??= new Settings().WithBlacklist().ForDump();
        settings.OutputFormat = OutputFormat.Json;

        JsonDumpBuilder jsonBuilder = new();
        HashSet<object> visited = new();
        Type type = obj.GetType();

        jsonBuilder.BeginObject(type.Name, type.Name);
        DumpObject(obj, type, type.Name, 0, visited, settings, jsonBuilder);
        jsonBuilder.EndObject();

        return jsonBuilder.ToJson();
    }

    /// <summary>
    /// Print a structural map of a GameObject hierarchy.
    /// Shows a type legend (component types + their filtered properties) followed by
    /// a tree view of the hierarchy with component annotations.
    /// </summary>
    public static void Map(GameObject root, Settings? settings = null)
    {
        if (root == null)
        {
            MelonLogger.Error("Cannot map null GameObject.");
            return;
        }

        settings ??= new Settings().WithWhitelist().ForMap();

        MapNode tree = BuildMapTree(root, settings, 0);
        if (settings.PruneEmptyBranches)
            PruneEmptyNodes(tree);

        Dictionary<Type, List<PropertyInfo>> legend = CollectTypeLegend(tree, settings);

        MelonLogger.Msg("");
        MelonLogger.Msg($"=== Script Map of {root.name} ===");
        MelonLogger.Msg("");

        RenderLegend(legend);
        MelonLogger.Msg("");
        RenderTree(tree);
    }

    /// <summary>
    /// Map from a component — uses its gameObject as root.
    /// </summary>
    public static void Map(Component component, Settings? settings = null)
    {
        if (component == null)
        {
            MelonLogger.Error("Cannot map null Component.");
            return;
        }

        Map(component.gameObject, settings);
    }

    #endregion


    #region Internal API — Filter Logic

    static readonly Dictionary<Type, List<PropertyInfo>> sTypeToPropertyCache = new();

    /// <summary>
    /// Dual-pass filter: should we read and report this property's value?
    /// Blacklist pass runs first (any match = deny), then whitelist pass
    /// (if any whitelist active, at least one must match).
    /// </summary>
    internal static bool ShouldExamineValue(Type declaringType, PropertyInfo prop, Settings settings)
    {
        FilterFlags active = settings.ActiveFilters;

        // --- Blacklist pass: any active blacklist match → deny ---

        if ((active & FilterFlags.ExamineBlacklistTypes) != 0
            && MatchesTypeHierarchy(prop.PropertyType, settings.mExamineBlacklistTypes))
            return false;

        if ((active & FilterFlags.ExamineBlacklistNames) != 0
            && settings.mExamineBlacklistNames.Contains(prop.Name))
            return false;

        if ((active & FilterFlags.ExamineBlacklistTypeTypePairs) != 0
            && settings.mExamineBlacklistTypeTypePairs.Contains((declaringType, prop.PropertyType)))
            return false;

        if ((active & FilterFlags.ExamineBlacklistTypeNamePairs) != 0
            && settings.mExamineBlacklistTypeNamePairs.Contains((declaringType, prop.Name)))
            return false;

        // --- Whitelist pass: if any active, at least one must match ---

        FilterFlags examineWhitelists = active & FilterFlags.AllExamineWhitelists;
        if (examineWhitelists == FilterFlags.None)
            return true;

        if ((examineWhitelists & FilterFlags.ExamineWhitelistTypes) != 0
            && MatchesTypeHierarchy(prop.PropertyType, settings.mExamineWhitelistTypes))
            return true;

        if ((examineWhitelists & FilterFlags.ExamineWhitelistNames) != 0
            && settings.mExamineWhitelistNames.Contains(prop.Name))
            return true;

        if ((examineWhitelists & FilterFlags.ExamineWhitelistTypeTypePairs) != 0
            && settings.mExamineWhitelistTypeTypePairs.Contains((declaringType, prop.PropertyType)))
            return true;

        if ((examineWhitelists & FilterFlags.ExamineWhitelistTypeNamePairs) != 0
            && settings.mExamineWhitelistTypeNamePairs.Contains((declaringType, prop.Name)))
            return true;

        return false;
    }

    /// <summary>
    /// Dual-pass filter: should we recurse into this property's value?
    /// Value types are never recursed into regardless of settings.
    /// Then blacklist pass, then whitelist pass (same logic as ShouldExamineValue).
    /// </summary>
    internal static bool ShouldRecurse(Type declaringType, PropertyInfo prop, Settings settings)
    {
        if (IsValueType(prop.PropertyType))
            return false;

        FilterFlags active = settings.ActiveFilters;

        // --- Blacklist pass ---

        if ((active & FilterFlags.RecurseBlacklistTypes) != 0
            && MatchesTypeHierarchy(prop.PropertyType, settings.mRecurseBlacklistTypes))
            return false;

        if ((active & FilterFlags.RecurseBlacklistNames) != 0
            && settings.mRecurseBlacklistNames.Contains(prop.Name))
            return false;

        if ((active & FilterFlags.RecurseBlacklistTypeTypePairs) != 0
            && settings.mRecurseBlacklistTypeTypePairs.Contains((declaringType, prop.PropertyType)))
            return false;

        if ((active & FilterFlags.RecurseBlacklistTypeNamePairs) != 0
            && settings.mRecurseBlacklistTypeNamePairs.Contains((declaringType, prop.Name)))
            return false;

        // --- Whitelist pass ---

        FilterFlags recurseWhitelists = active & FilterFlags.AllRecurseWhitelists;
        if (recurseWhitelists == FilterFlags.None)
            return true;

        if ((recurseWhitelists & FilterFlags.RecurseWhitelistTypes) != 0
            && MatchesTypeHierarchy(prop.PropertyType, settings.mRecurseWhitelistTypes))
            return true;

        if ((recurseWhitelists & FilterFlags.RecurseWhitelistNames) != 0
            && settings.mRecurseWhitelistNames.Contains(prop.Name))
            return true;

        if ((recurseWhitelists & FilterFlags.RecurseWhitelistTypeTypePairs) != 0
            && settings.mRecurseWhitelistTypeTypePairs.Contains((declaringType, prop.PropertyType)))
            return true;

        if ((recurseWhitelists & FilterFlags.RecurseWhitelistTypeNamePairs) != 0
            && settings.mRecurseWhitelistTypeNamePairs.Contains((declaringType, prop.Name)))
            return true;

        return false;
    }

    /// <summary>
    /// Check if a type matches any entry in a filter set, using IsAssignableFrom
    /// so that whitelisting MonoBehaviour matches all its subclasses.
    /// </summary>
    internal static bool MatchesTypeHierarchy(Type candidate, HashSet<Type> filterTypes)
    {
        // Fast path: exact match
        if (filterTypes.Contains(candidate))
            return true;

        // Slow path: hierarchy check
        foreach (Type filterType in filterTypes)
        {
            if (filterType.IsAssignableFrom(candidate))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Simplified type-only filter for Map: should this component type be included?
    /// Uses recurse type filters since Map asks "should we look at this component?".
    /// </summary>
    internal static bool ShouldIncludeComponentType(Type componentType, Settings settings)
    {
        FilterFlags active = settings.ActiveFilters;

        if ((active & FilterFlags.RecurseBlacklistTypes) != 0
            && MatchesTypeHierarchy(componentType, settings.mRecurseBlacklistTypes))
            return false;

        if ((active & FilterFlags.RecurseWhitelistTypes) != 0)
        {
            if (!MatchesTypeHierarchy(componentType, settings.mRecurseWhitelistTypes))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Primitives, enums, strings, math types — things you read but don't recurse into.
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
    /// Get cached properties for a type (instance, public + non-public).
    /// </summary>
    internal static List<PropertyInfo> GetProperties(Type type)
    {
        if (!sTypeToPropertyCache.TryGetValue(type, out List<PropertyInfo>? props))
        {
            props = new List<PropertyInfo>(type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            sTypeToPropertyCache[type] = props;
        }
        return props;
    }

    /// <summary>
    /// True if an enumerable holds elements of the given type.
    /// </summary>
    internal static bool IsEnumerableOfType(IEnumerable enumerable, Type elementType)
    {
        if (enumerable == null || elementType == null)
            return false;

        IEnumerable<Type> interfaces = enumerable.GetType().GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        foreach (Type iface in interfaces)
        {
            Type ifaceElementType = iface.GetGenericArguments()[0];
            if (ifaceElementType == elementType)
                return true;
        }

        return false;
    }

    /// <summary>
    /// True if the enumerable holds elements of a recurse-blacklisted type.
    /// </summary>
    internal static bool ShouldSkipEnumerable(IEnumerable enumerable, Settings settings)
    {
        if ((settings.ActiveFilters & FilterFlags.RecurseBlacklistTypes) == 0)
            return false;

        foreach (Type blacklistType in settings.mRecurseBlacklistTypes)
        {
            if (IsEnumerableOfType(enumerable, blacklistType))
                return true;
        }
        return false;
    }

    internal static bool SafeUnityNull<T>(T value) => (value as object) == null;
    internal static bool NullMismatch<T>(T a, T b) => (!SafeUnityNull(a) && SafeUnityNull(b)) || (SafeUnityNull(a) && !SafeUnityNull(b));
    internal static bool BothNull<T>(T a, T b) => SafeUnityNull(a) && SafeUnityNull(b);
    internal static bool NeitherNull<T>(T a, T b) => !SafeUnityNull(a) && !SafeUnityNull(b);

    #endregion


    #region Dump Implementation

    internal static void DumpObject(object obj, Type declaringType, string path, int depth,
        HashSet<object> visited, Settings settings, JsonDumpBuilder? jsonBuilder = null)
    {
        if (SafeUnityNull(obj))
            return;

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

        if (!IsValueType(obj.GetType()))
        {
            if (visited.Contains(obj)) return;
            visited.Add(obj);
        }

        // Handle enumerables
        if (obj is IEnumerable enumerable && obj is not string)
        {
            if (ShouldSkipEnumerable(enumerable, settings))
                return;

            int index = 0;
            foreach (object? item in enumerable)
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
                    Type itemType = item.GetType();
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
                        MelonLogger.Msg($"{EmptyFlagPrefix}{Indent(depth + 1)}{path}[{index}]: null");
                }
                else if (IsValueType(item.GetType()))
                {
                    if (settings.ReportFlags.HasFlag(ReportFlags.DumpValue))
                        MelonLogger.Msg($"{EmptyFlagPrefix}{Indent(depth + 1)}{path}[{index}] ({item.GetType().Name}): {item}");
                }
                else
                {
                    DumpObject(item, item.GetType(), $"{path}[{index}]", depth + 1, visited, settings, jsonBuilder);
                }
                index++;
            }
            return;
        }

        Type type = obj.GetType();
        List<PropertyInfo> props = GetProperties(type);

        foreach (PropertyInfo prop in props)
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                continue;

            bool shouldExamine = ShouldExamineValue(declaringType, prop, settings);
            bool shouldRecurse = ShouldRecurse(declaringType, prop, settings);

            if (!shouldExamine && !shouldRecurse)
                continue;

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
                continue;

            if (shouldExamine)
                DumpValue(value, path, prop, depth, settings, jsonBuilder);

            if (shouldRecurse && !SafeUnityNull(value))
            {
                if (jsonBuilder != null)
                {
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

    internal static void DumpValue(object? value, string path, PropertyInfo prop, int depth,
        Settings settings, JsonDumpBuilder? jsonBuilder = null)
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

    #endregion


    #region Compare Implementation

    internal static void CompareObjects<T>(T a, T b, Type declaringType, string path, int depth,
        HashSet<(object, object)> visited, Settings settings)
    {
        if (depth >= settings.MaxDepth)
        {
            MaybeReport(path, null, settings, ReportFlags.MaxDepth, depth);
            return;
        }

        if (a == null && b == null)
            return;

        if (visited.Contains((a!, b!)) || visited.Contains((b!, a!)))
            return;

        visited.Add((a!, b!));

        // Handle enumerables
        if (a is IEnumerable aEnum && b is IEnumerable bEnum)
        {
            if (ShouldSkipEnumerable(aEnum, settings) || ShouldSkipEnumerable(bEnum, settings))
                return;

            IEnumerator enumeratorA = aEnum.GetEnumerator();
            IEnumerator enumeratorB = bEnum.GetEnumerator();
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

                CompareObjects(enumeratorA.Current, enumeratorB.Current,
                    enumeratorA.Current?.GetType() ?? typeof(object),
                    $"{path}[{index}]", depth + 1, visited, settings);
                index++;
            }

            return;
        }

        Type type = a != null ? a.GetType() : b!.GetType();
        List<PropertyInfo> props = GetProperties(type);

        foreach (PropertyInfo prop in props)
        {
            if (!prop.CanRead)
                continue;

            bool shouldExamine = ShouldExamineValue(declaringType, prop, settings);
            bool shouldRecurse = ShouldRecurse(declaringType, prop, settings);

            if (!shouldExamine && !shouldRecurse)
                continue;

            object? valueA = null;
            object? valueB = null;
            ErrorState errorState = ErrorState.None;

            try { valueA = prop.GetValue(a); }
            catch { errorState |= ErrorState.LeftSide; }

            try { valueB = prop.GetValue(b); }
            catch { errorState |= ErrorState.RightSide; }

            if (errorState != ErrorState.None)
                MaybeReport($"({errorState}) {path}", prop, settings, ReportFlags.Error, depth);

            if (shouldExamine)
                CompareValues(valueA, valueB, path, prop, settings, depth);

            if (shouldRecurse && NeitherNull(valueA, valueB))
                CompareObjects(valueA, valueB, prop.DeclaringType!, $"{path}.{prop.Name}", depth + 1, visited, settings);
        }
    }

    internal static void CompareValues<T>(T valueA, T valueB, string path, PropertyInfo prop,
        Settings settings, int depth)
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
        if (IsValueType(prop.PropertyType))
        {
            if (!valueA!.Equals(valueB))
                MaybeReportWithValue(valueA, valueB, path, prop, settings, ReportFlags.ValueDifferent, depth);
            else
                MaybeReport(path, prop, settings, ReportFlags.ValueEqual, depth);
            return;
        }
        if (!ReferenceEquals(valueA, valueB))
            MaybeReportWithValue(valueA, valueB, path, prop, settings, ReportFlags.ReferenceDifferent, depth);
        else
            MaybeReport(path, prop, settings, ReportFlags.ReferenceEqual, depth);
    }

    #endregion


    #region Map Implementation

    /// <summary>
    /// Tree node for the Map operation. Each node is a GameObject with its
    /// matching component types and child nodes.
    /// </summary>
    internal class MapNode
    {
        internal string Name = "";
        internal List<Type> ComponentTypes = new();
        internal List<MapNode> Children = new();

        internal bool IsEmpty => ComponentTypes.Count == 0 && Children.Count == 0;
    }

    /// <summary>
    /// Walk the GameObject hierarchy, collecting component types that pass filtering.
    /// </summary>
    internal static MapNode BuildMapTree(GameObject go, Settings settings, int depth)
    {
        MapNode node = new() { Name = go.name };

        Component[] components = go.GetComponents<Component>();
        foreach (Component comp in components)
        {
            if (comp == null) continue;
            Type compType = comp.GetType();
            if (ShouldIncludeComponentType(compType, settings))
                node.ComponentTypes.Add(compType);
        }

        if (depth < settings.MaxTreeDepth)
        {
            Transform transform = go.transform;
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child != null && child.gameObject != null)
                    node.Children.Add(BuildMapTree(child.gameObject, settings, depth + 1));
            }
        }

        return node;
    }

    /// <summary>
    /// Recursively remove child nodes that have no components and no children.
    /// Runs bottom-up so pruning cascades correctly.
    /// </summary>
    internal static void PruneEmptyNodes(MapNode node)
    {
        foreach (MapNode child in node.Children)
            PruneEmptyNodes(child);
        node.Children.RemoveAll(c => c.IsEmpty);
    }

    /// <summary>
    /// Walk the tree and collect each distinct component type with its filtered properties.
    /// Each type appears once in the legend, no matter how many GameObjects have it.
    /// </summary>
    internal static Dictionary<Type, List<PropertyInfo>> CollectTypeLegend(MapNode node, Settings settings)
    {
        Dictionary<Type, List<PropertyInfo>> legend = new();
        CollectTypeLegendRecursive(node, settings, legend);
        return legend;
    }

    static void CollectTypeLegendRecursive(MapNode node, Settings settings, Dictionary<Type, List<PropertyInfo>> legend)
    {
        foreach (Type compType in node.ComponentTypes)
        {
            if (legend.ContainsKey(compType))
                continue;

            List<PropertyInfo> allProps = GetProperties(compType);
            List<PropertyInfo> filtered = new();

            foreach (PropertyInfo prop in allProps)
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                    continue;
                if (ShouldExamineValue(compType, prop, settings))
                    filtered.Add(prop);
            }

            legend[compType] = filtered;
        }

        foreach (MapNode child in node.Children)
            CollectTypeLegendRecursive(child, settings, legend);
    }

    /// <summary>
    /// Print the type legend section.
    /// </summary>
    static void RenderLegend(Dictionary<Type, List<PropertyInfo>> legend)
    {
        MelonLogger.Msg("--- Type Legend ---");

        foreach (KeyValuePair<Type, List<PropertyInfo>> entry in legend)
        {
            MelonLogger.Msg($"[{entry.Key.Name}]");
            foreach (PropertyInfo prop in entry.Value)
                MelonLogger.Msg($"  .{prop.Name} ({prop.PropertyType.Name})");
            MelonLogger.Msg("");
        }
    }

    /// <summary>
    /// Print the tree section with box-drawing connectors.
    /// </summary>
    static void RenderTree(MapNode root)
    {
        MelonLogger.Msg("--- Script Tree ---");

        List<string> lines = new();
        RenderTreeNode(root, "", isLast: true, isRoot: true, lines);

        foreach (string line in lines)
            MelonLogger.Msg(line);
    }

    /// <summary>
    /// Recursively render a node and its components/children with proper box-drawing prefixes.
    /// Components and child GameObjects are interleaved as children of the node.
    /// </summary>
    static void RenderTreeNode(MapNode node, string prefix, bool isLast, bool isRoot, List<string> lines)
    {
        // The root node gets no connector, just its name
        if (isRoot)
        {
            lines.Add(node.Name);
        }
        else
        {
            string connector = isLast ? "\u2514\u2500\u2500 " : "\u251C\u2500\u2500 ";
            lines.Add(prefix + connector + node.Name);
        }

        string childPrefix = isRoot ? "" : prefix + (isLast ? "    " : "\u2502   ");

        int totalChildren = node.ComponentTypes.Count + node.Children.Count;
        int index = 0;

        // Render component types as leaf children
        foreach (Type compType in node.ComponentTypes)
        {
            bool childIsLast = (index == totalChildren - 1);
            string connector = childIsLast ? "\u2514\u2500\u2500 " : "\u251C\u2500\u2500 ";
            lines.Add(childPrefix + connector + $"[{compType.Name}]");
            index++;
        }

        // Render child GameObjects
        for (int i = 0; i < node.Children.Count; i++)
        {
            bool childIsLast = (index == totalChildren - 1);
            RenderTreeNode(node.Children[i], childPrefix, childIsLast, false, lines);
            index++;
        }
    }

    #endregion


    #region Reporting

    internal const int FlagPrefixWidth = 21;
    internal static string EmptyFlagPrefix => new string(' ', FlagPrefixWidth);
    internal static string Indent(int depth) => "  ";

    internal static string GetLastPathSegment(string path)
    {
        int lastDot = path.LastIndexOf('.');
        return lastDot >= 0 ? path.Substring(lastDot + 1) : path;
    }

    internal static void MaybeReport(string path, PropertyInfo? prop, Settings settings, ReportFlags toCheck, int depth)
    {
        if (!settings.ReportFlags.HasFlag(toCheck))
            return;
        string message = ConstructMessage(path, toCheck, depth);
        if (prop != null)
            message = AppendWithPropertyName(prop, message);
        Report(toCheck, message);
    }

    internal static void MaybeReportWithValue<T>(T valueA, T valueB, string path, PropertyInfo prop,
        Settings settings, ReportFlags toCheck, int depth)
    {
        if (!settings.ReportFlags.HasFlag(toCheck))
            return;
        string message = ConstructMessage(path, toCheck, depth);
        if (prop != null)
            message = AppendWithPropertyName(prop, message);
        message = AppendWithValue(valueA, valueB, message);
        Report(toCheck, message);
    }

    internal static string ConstructMessage(string path, ReportFlags toCheck, int depth)
        => $"[{toCheck}]".PadRight(FlagPrefixWidth) + Indent(depth) + path;

    internal static string AppendWithPropertyName(PropertyInfo prop, string message)
        => $"{message}.{prop.Name} ({prop.PropertyType.Name})";

    internal static string AppendWithValue<T>(T valueA, T valueB, string message)
        => $"{message}: {valueA} vs {valueB}";

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
