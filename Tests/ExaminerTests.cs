// ============================================================================
// ExaminerTests — In-game console commands for Examiner
// ============================================================================
//
// Single console command: "examine <subcommand> [args...]"
//
// Subcommands:
//   help                       — List available subcommands
//   dump [depth]               — Blacklist dump of player Transform
//   dump_json [depth]          — JSON dump of player Transform
//   compare                    — Compare player Transform vs first child
//   map                        — Map the player GameObject hierarchy
//   test <name>                — Run a named integration test
//   test all                   — Run all integration tests
//   test help                  — List available test names
//
// Tests:
//   circular        — circular reference / self-reference (visited set guard)
//   depth           — 50-deep chain with MaxDepth=3 (stack overflow guard)
//   enumerable      — 200-item list with MaxEnumerableItems=5 (truncation)
//   nulls           — null properties, null list elements, Dump(null)
//   plain           — pure C# object with nested types, no Unity
//   filter_bl       — blacklist filtering, before/after comparison
//   filter_wl       — whitelist filtering, sparse + single-property
//   filter_strict   — dual-polarity (blacklist + whitelist together)
//   json            — JSON output, validates structure
//   components      — Component[] array from real player GameObject
//   compare         — compare player Transform vs child (real diffs)
//   compare_self    — compare object to itself (zero diffs expected)
//   map             — map real player hierarchy + Component entry point
//   hierarchy       — IsAssignableFrom matching (MonoBehaviour subclass)
//   error_recovery  — property getter that throws, verify continuation
// ============================================================================

using Il2Cpp;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace MehToolBox;

[HarmonyPatch(typeof(ConsoleManager), nameof(ConsoleManager.Initialize))]
internal class ExaminerTests
{
    const string CommandName = "examine";

    static readonly Dictionary<string, Action<IList<string>>> sCommandMap = new()
    {
        { "help",      ProcessHelp },
        { "dump",      ProcessDump },
        { "dump_json", ProcessDumpJson },
        { "compare",   ProcessCompare },
        { "map",       ProcessMap },
        { "test",      ProcessTest },
    };

    static readonly Dictionary<string, Action<IList<string>>> sTestMap = new()
    {
        { "circular",        TestCircularReference },
        { "depth",           TestMaxDepthGuard },
        { "enumerable",      TestEnumerableTruncation },
        { "nulls",           TestNullHandling },
        { "plain",           TestPlainObject },
        { "filter_bl",       TestBlacklistFiltering },
        { "filter_wl",       TestWhitelistFiltering },
        { "filter_strict",   TestStrictFiltering },
        { "json",            TestJsonOutput },
        { "components",      TestComponentArray },
        { "compare",         TestCompareTransforms },
        { "compare_self",    TestCompareSameObject },
        { "map",             TestMapHierarchy },
        { "hierarchy",       TestTypeHierarchyMatch },
        { "error_recovery",  TestPropertyErrorRecovery },
    };

    static void Postfix()
    {
        uConsole.RegisterCommand(CommandName, new Action(OnCommand));
    }

    static void OnCommand()
    {
        List<string> args = ParseArgs();
        string? subcommand = GetNextArg(args);

        if (subcommand == null)
        {
            Log("Usage: examine <subcommand> [args...]. Try 'examine help'.");
            return;
        }

        if (!sCommandMap.TryGetValue(subcommand, out Action<IList<string>> handler))
        {
            Log($"Unknown subcommand: {subcommand}. Try 'examine help'.");
            return;
        }

        handler(args);
    }

    #region Arg Parsing

    static List<string> ParseArgs()
    {
        List<string> args = new();
        for (int i = 0; i < 100; i++)
        {
            try
            {
                string arg = uConsole.GetString();
                if (string.IsNullOrEmpty(arg)) break;
                args.Add(arg);
            }
            catch
            {
                break;
            }
        }
        return args;
    }

    static string? GetNextArg(IList<string> args)
    {
        if (args.Count == 0) return null;
        string arg = args[0];
        args.RemoveAt(0);
        return arg;
    }

    static bool TryParseInt(string? value, out int result)
    {
        result = 0;
        return value != null && int.TryParse(value, out result);
    }

    #endregion


    #region Scene Guards

    static bool TryGetPlayerTransform(out Transform transform)
    {
        transform = GameManager.GetPlayerTransform();
        if (transform != null) return true;

        Log("Not in a game session — load into a scene first.");
        return false;
    }

    static bool TryGetPlayerObject(out GameObject playerObj)
    {
        playerObj = GameManager.GetPlayerObject();
        if (playerObj != null) return true;

        Log("Not in a game session — load into a scene first.");
        return false;
    }

    #endregion


    #region Top-Level Subcommands

    static void ProcessHelp(IList<string> args)
    {
        Log($"--- {CommandName} subcommands ---");
        foreach (string key in sCommandMap.Keys)
            Log($"  {key}");
        Log("");
        Log("Use 'examine test help' for available test names.");
    }

    // examine dump [depth]
    static void ProcessDump(IList<string> args)
    {
        if (!TryGetPlayerTransform(out Transform transform)) return;

        Examiner.Settings settings = new Examiner.Settings()
            .WithBlacklist()
            .ForDump();

        string? depthArg = GetNextArg(args);
        if (TryParseInt(depthArg, out int depth))
        {
            settings.WithMaxDepth(depth);
            Log($"MaxDepth overridden to {depth}");
        }

        Examiner.Dump(transform, settings);
    }

    // examine dump_json [depth]
    static void ProcessDumpJson(IList<string> args)
    {
        if (!TryGetPlayerTransform(out Transform transform)) return;

        Examiner.Settings settings = new Examiner.Settings()
            .WithBlacklist()
            .ForDump()
            .WithOutputFormat(Examiner.OutputFormat.Json);

        string? depthArg = GetNextArg(args);
        if (TryParseInt(depthArg, out int depth))
        {
            settings.WithMaxDepth(depth);
            Log($"MaxDepth overridden to {depth}");
        }

        Examiner.Dump(transform, settings);
    }

    // examine compare
    static void ProcessCompare(IList<string> args)
    {
        if (!TryGetPlayerObject(out GameObject playerObj)) return;

        Transform playerTransform = playerObj.transform;
        if (playerTransform.childCount < 1)
        {
            Log("Player has no children — can't find two Transforms to compare.");
            return;
        }

        Transform childTransform = playerTransform.GetChild(0);
        Log($"Comparing player Transform vs child '{childTransform.name}'");

        Examiner.Settings settings = new Examiner.Settings()
            .WithBlacklist()
            .ForCompare();

        Examiner.Compare(playerTransform, childTransform, settings);
    }

    // examine map
    static void ProcessMap(IList<string> args)
    {
        if (!TryGetPlayerObject(out GameObject playerObj)) return;

        Examiner.Settings settings = new Examiner.Settings()
            .WithWhitelist()
            .ForMap();

        Examiner.Map(playerObj, settings);
    }

    // examine test <name|all|help>
    static void ProcessTest(IList<string> args)
    {
        string? testName = GetNextArg(args);

        if (testName == null || testName == "help")
        {
            Log("--- available tests ---");
            foreach (string key in sTestMap.Keys)
                Log($"  {key}");
            Log("  all");
            return;
        }

        if (testName == "all")
        {
            Log("=== Running all tests ===");
            foreach (KeyValuePair<string, Action<IList<string>>> entry in sTestMap)
            {
                Log($"--- {entry.Key} ---");
                entry.Value(args);
            }
            Log("=== All tests complete ===");
            return;
        }

        if (!sTestMap.TryGetValue(testName, out Action<IList<string>> test))
        {
            Log($"Unknown test: {testName}. Try 'examine test help'.");
            return;
        }

        test(args);
    }

    #endregion


    #region Tests

    // ---------------------------------------------------------------
    // circular — Object that references itself. Examiner must not
    // stack overflow; visited set should catch it silently.
    // ---------------------------------------------------------------
    static void TestCircularReference(IList<string> args)
    {
        CircularNode nodeA = new() { Name = "A" };
        CircularNode nodeB = new() { Name = "B" };
        nodeA.Next = nodeB;
        nodeB.Next = nodeA; // cycle: A → B → A

        Examiner.Settings settings = new Examiner.Settings()
            .ForDump()
            .WithMaxDepth(20)
            .WithDescription("test circular — must not infinite-loop on A↔B cycle");

        Log("Dumping circular reference (A↔B). If you see this line but not 'complete', it hung.");
        Examiner.Dump(nodeA, settings);
        Log("PASS circular — survived without hanging.");

        // Also test self-reference
        CircularNode self = new() { Name = "Self" };
        self.Next = self;

        Examiner.Dump(self, settings.WithDescription("test circular — self-referencing object"));
        Log("PASS circular — self-reference survived.");
    }

    // ---------------------------------------------------------------
    // depth — Build a chain 50 levels deep, dump with MaxDepth=3.
    // Verify it stops early and doesn't blow the stack.
    // ---------------------------------------------------------------
    static void TestMaxDepthGuard(IList<string> args)
    {
        DeepChain root = DeepChain.Build(50);

        Examiner.Settings settings = new Examiner.Settings()
            .ForDump()
            .WithMaxDepth(3)
            .WithDescription("test depth — 50-deep chain with MaxDepth=3, expect truncation at depth 3");

        Log("Dumping 50-deep chain with MaxDepth=3...");
        Examiner.Dump(root, settings);
        Log("PASS depth — stopped at MaxDepth, no stack overflow.");

        // Also verify depth=1 produces minimal output
        Examiner.Dump(root, new Examiner.Settings()
            .ForDump()
            .WithMaxDepth(1)
            .WithDescription("test depth — same chain with MaxDepth=1"));
        Log("PASS depth — MaxDepth=1 also survived.");
    }

    // ---------------------------------------------------------------
    // enumerable — Large list with MaxEnumerableItems=5. Verify
    // truncation happens and the full list isn't walked.
    // ---------------------------------------------------------------
    static void TestEnumerableTruncation(IList<string> args)
    {
        List<string> bigList = new();
        for (int i = 0; i < 200; i++)
            bigList.Add($"item_{i}");

        Examiner.Settings settings = new Examiner.Settings()
            .ForDump()
            .WithMaxEnumerableItems(5)
            .WithDescription("test enumerable — 200 items, MaxEnumerableItems=5, expect truncation");

        Log("Dumping 200-item list with MaxEnumerableItems=5...");
        Examiner.Dump(bigList, settings);
        Log("PASS enumerable — truncated without walking all 200.");

        // Array of mixed objects
        object[] mixed = new object[] { "hello", 42, 3.14f, true, new CircularNode { Name = "embedded" } };
        Examiner.Dump(mixed, new Examiner.Settings()
            .ForDump()
            .WithDescription("test enumerable — mixed-type object array"));
        Log("PASS enumerable — mixed object array survived.");
    }

    // ---------------------------------------------------------------
    // nulls — Object graph with null properties, nullable fields,
    // and null array elements. Examiner must not throw NRE.
    // ---------------------------------------------------------------
    static void TestNullHandling(IList<string> args)
    {
        NullHeavyObject obj = new()
        {
            Name = "NullTest",
            NullString = null,
            NullChild = null,
            Items = new List<string?> { "one", null, "three", null },
        };

        Examiner.Settings settings = new Examiner.Settings()
            .ForDump()
            .WithDescription("test nulls — null properties, null list elements");

        Log("Dumping object with null properties and null list elements...");
        Examiner.Dump(obj, settings);
        Log("PASS nulls — no NRE on null properties.");

        // Dump null directly
        Examiner.Dump(null!);
        Log("PASS nulls — Dump(null) handled gracefully.");
    }

    // ---------------------------------------------------------------
    // plain — Pure C# object with nested types, lists, and value
    // types. No Unity dependency. Should work outside a scene.
    // ---------------------------------------------------------------
    static void TestPlainObject(IList<string> args)
    {
        PlainTestObject testObj = new()
        {
            Name = "Widget",
            Score = 99.5f,
            IsActive = true,
            Tags = new List<string> { "alpha", "beta", "gamma" },
            Nested = new PlainTestObject.InnerData { Id = 7, Label = "inner" }
        };

        Examiner.Settings settings = new Examiner.Settings()
            .WithBlacklist()
            .ForDump()
            .WithDescription("test plain — pure C# object, no Unity base");

        Examiner.Dump(testObj, settings);
        Log("PASS plain — plain C# object dumped.");
    }

    // ---------------------------------------------------------------
    // filter_bl — Dump player Transform with blacklist. Then add
    // "position" to the blacklist and dump again — position should
    // disappear from the second output.
    // ---------------------------------------------------------------
    static void TestBlacklistFiltering(IList<string> args)
    {
        if (!TryGetPlayerTransform(out Transform transform)) return;

        Examiner.Settings baseline = new Examiner.Settings()
            .WithBlacklist()
            .ForDump()
            .WithMaxDepth(1)
            .WithDescription("test filter_bl — baseline blacklist dump");

        Log("Baseline blacklist dump (position should appear):");
        Examiner.Dump(transform, baseline);

        Examiner.Settings withBlockedPosition = new Examiner.Settings()
            .WithBlacklist()
            .ForDump()
            .WithMaxDepth(1)
            .MergeExamineBlacklistNames("position", "localPosition", "localRotation")
            .WithDescription("test filter_bl — position/localPosition/localRotation blacklisted");

        Log("With position blacklisted (position should NOT appear):");
        Examiner.Dump(transform, withBlockedPosition);
        Log("PASS filter_bl — compare the two outputs above.");
    }

    // ---------------------------------------------------------------
    // filter_wl — Whitelist-only dump of player Transform. Should
    // produce sparse output with only whitelisted property names.
    // ---------------------------------------------------------------
    static void TestWhitelistFiltering(IList<string> args)
    {
        if (!TryGetPlayerTransform(out Transform transform)) return;

        Examiner.Settings settings = new Examiner.Settings()
            .WithWhitelist()
            .ForDump()
            .WithMaxDepth(1)
            .WithDescription("test filter_wl — only whitelisted types/names should appear");

        Log("Whitelist-only dump (expect only position, rotation, enabled, etc.):");
        Examiner.Dump(transform, settings);

        // Now custom whitelist that allows ONLY 'position'
        Examiner.Settings singleProp = new Examiner.Settings()
            .WithFilterFlags(Examiner.FilterFlags.ExamineWhitelistNames)
            .WithExamineWhitelistNames("position")
            .ForDump()
            .WithMaxDepth(1)
            .WithDescription("test filter_wl — custom whitelist: ONLY 'position'");

        Log("Custom whitelist (ONLY 'position'):");
        Examiner.Dump(transform, singleProp);
        Log("PASS filter_wl — compare sparse vs single-property output.");
    }

    // ---------------------------------------------------------------
    // filter_strict — Both blacklist + whitelist active. Blacklist
    // denies first, then whitelist gates survivors.
    // ---------------------------------------------------------------
    static void TestStrictFiltering(IList<string> args)
    {
        if (!TryGetPlayerTransform(out Transform transform)) return;

        Examiner.Settings settings = Examiner.Settings.Strict()
            .ForDump()
            .WithMaxDepth(1)
            .WithDescription("test filter_strict — dual-polarity: bl denies, then wl gates");

        Log("Strict (blacklist + whitelist) dump:");
        Examiner.Dump(transform, settings);
        Log("PASS filter_strict — dual-polarity produced output without error.");
    }

    // ---------------------------------------------------------------
    // json — JSON output of player Transform. Parse the result
    // string to verify it's valid JSON.
    // ---------------------------------------------------------------
    static void TestJsonOutput(IList<string> args)
    {
        if (!TryGetPlayerTransform(out Transform transform)) return;

        string json = Examiner.DumpToJson(transform, new Examiner.Settings()
            .WithBlacklist()
            .WithMaxDepth(2));

        bool looksValid = json.StartsWith("{") && json.EndsWith("}") && json.Contains("$type");
        Log($"JSON output length: {json.Length} chars, starts with '{{': {json.StartsWith("{")}, contains $type: {json.Contains("$type")}");

        if (looksValid)
            Log("PASS json — produced valid-looking JSON.");
        else
            Log($"FAIL json — output doesn't look like valid JSON. First 200 chars: {json[..Math.Min(200, json.Length)]}");

        // Also test JSON on a plain object
        string plainJson = Examiner.DumpToJson(new PlainTestObject
        {
            Name = "JsonTest", Score = 1.0f, IsActive = false,
            Tags = new List<string> { "x" },
            Nested = new PlainTestObject.InnerData { Id = 1, Label = "y" }
        });

        Log($"Plain object JSON length: {plainJson.Length} chars");
        Log("PASS json — plain object also produced JSON.");
    }

    // ---------------------------------------------------------------
    // components — Get the player's Component[] array and dump it.
    // Verifies enumerable-of-Unity-objects entry path works.
    // ---------------------------------------------------------------
    static void TestComponentArray(IList<string> args)
    {
        if (!TryGetPlayerObject(out GameObject playerObj)) return;

        Component[] components = playerObj.GetComponents<Component>();
        Log($"Player has {components.Length} components");

        if (components.Length == 0)
        {
            Log("FAIL components — player has zero components, nothing to test.");
            return;
        }

        Examiner.Settings settings = new Examiner.Settings()
            .WithBlacklist()
            .ForDump()
            .WithMaxDepth(1)
            .WithMaxEnumerableItems(5)
            .WithDescription($"test components — first 5 of {components.Length} player components");

        Examiner.Dump(components, settings);
        Log($"PASS components — dumped Component[{components.Length}] without error.");
    }

    // ---------------------------------------------------------------
    // compare — Compare player Transform vs first child Transform.
    // They have different positions, so differences should appear.
    // ---------------------------------------------------------------
    static void TestCompareTransforms(IList<string> args)
    {
        if (!TryGetPlayerObject(out GameObject playerObj)) return;

        Transform playerTransform = playerObj.transform;
        if (playerTransform.childCount < 1)
        {
            Log("SKIP compare — player has no children.");
            return;
        }

        Transform childTransform = playerTransform.GetChild(0);
        Log($"Comparing player Transform vs child '{childTransform.name}'");

        Examiner.Settings settings = new Examiner.Settings()
            .WithBlacklist()
            .ForCompare()
            .WithMaxDepth(1)
            .WithDescription("test compare — should show position/rotation differences");

        Examiner.Compare(playerTransform, childTransform, settings);
        Log("PASS compare — comparison completed without error.");
    }

    // ---------------------------------------------------------------
    // compare_self — Compare an object to itself. Should produce
    // zero differences (validates equality path).
    // ---------------------------------------------------------------
    static void TestCompareSameObject(IList<string> args)
    {
        PlainTestObject obj = new() { Name = "Same", Score = 1.0f, IsActive = true };

        Examiner.Settings settings = new Examiner.Settings()
            .ForCompare()
            .WithDescription("test compare_self — identical objects, expect zero differences");

        Log("Comparing object to itself...");
        Examiner.Compare(obj, obj, settings);
        Log("PASS compare_self — self-comparison completed (should show no diffs).");

        // Also compare two structurally identical but distinct instances
        PlainTestObject clone = new() { Name = "Same", Score = 1.0f, IsActive = true };
        Examiner.Compare(obj, clone, settings.WithDescription("test compare_self — two identical instances"));
        Log("PASS compare_self — identical-but-distinct instances compared.");
    }

    // ---------------------------------------------------------------
    // map — Map the player hierarchy. Validates the collect → legend
    // → tree pipeline on a real GameObject tree.
    // ---------------------------------------------------------------
    static void TestMapHierarchy(IList<string> args)
    {
        if (!TryGetPlayerObject(out GameObject playerObj)) return;

        Log($"Mapping '{playerObj.name}' (childCount={playerObj.transform.childCount})");

        Examiner.Settings settings = new Examiner.Settings()
            .WithWhitelist()
            .ForMap()
            .WithDescription("test map — real player hierarchy");

        Examiner.Map(playerObj, settings);
        Log("PASS map — hierarchy mapped without error.");

        // Also test Map from a Component entry point
        Transform playerTransform = playerObj.transform;
        Examiner.Map(playerTransform, new Examiner.Settings()
            .WithWhitelist()
            .ForMap()
            .WithMaxTreeDepth(2)
            .WithDescription("test map — from Component, MaxTreeDepth=2"));

        Log("PASS map — Component entry point also worked.");
    }

    // ---------------------------------------------------------------
    // hierarchy — Verify IsAssignableFrom matching. Whitelist
    // MonoBehaviour, then dump a real component that derives from it.
    // If the component's properties show up, hierarchy matching works.
    // ---------------------------------------------------------------
    static void TestTypeHierarchyMatch(IList<string> args)
    {
        if (!TryGetPlayerObject(out GameObject playerObj)) return;

        // Get first MonoBehaviour-derived component on the player
        MonoBehaviour? mono = playerObj.GetComponent<MonoBehaviour>();
        if (mono == null)
        {
            Log("SKIP hierarchy — no MonoBehaviour on player object.");
            return;
        }

        Log($"Found MonoBehaviour: {mono.GetType().Name}");

        // Whitelist only MonoBehaviour in recurse types — subclass should still match
        Examiner.Settings settings = new Examiner.Settings()
            .WithFilterFlags(Examiner.FilterFlags.RecurseWhitelistTypes | Examiner.FilterFlags.ExamineWhitelistTypes)
            .WithRecurseWhitelistTypes(typeof(MonoBehaviour))
            .WithExamineWhitelistTypes(typeof(float), typeof(int), typeof(bool), typeof(string))
            .ForDump()
            .WithMaxDepth(1)
            .WithDescription($"test hierarchy — whitelist MonoBehaviour, actual type is {mono.GetType().Name}");

        Examiner.Dump(mono, settings);
        Log("PASS hierarchy — subclass matched MonoBehaviour whitelist via IsAssignableFrom.");
    }

    // ---------------------------------------------------------------
    // error_recovery — Dump an object with a property getter that
    // throws. Examiner should log the error and continue to the
    // next property, not crash entirely.
    // ---------------------------------------------------------------
    static void TestPropertyErrorRecovery(IList<string> args)
    {
        ExplodingObject obj = new() { SafeValue = 42 };

        Examiner.Settings settings = new Examiner.Settings()
            .ForDump()
            .WithDescription("test error_recovery — ExplodingProp getter throws, should not crash");

        Log("Dumping object with a throwing property getter...");
        Examiner.Dump(obj, settings);
        Log("PASS error_recovery — survived property getter exception.");
    }

    #endregion


    #region Utilities

    static void Log(string message)
    {
        MelonLogger.Msg($"[Examiner] {message}");
        uConsole.Log($"[Examiner] {message}");
    }

    #endregion


    #region Test Data

    class CircularNode
    {
        public string Name { get; set; } = "";
        public CircularNode? Next { get; set; }
    }

    class DeepChain
    {
        public string Level { get; set; } = "";
        public DeepChain? Child { get; set; }

        public static DeepChain Build(int depth)
        {
            DeepChain root = new() { Level = "L0" };
            DeepChain current = root;
            for (int i = 1; i < depth; i++)
            {
                current.Child = new DeepChain { Level = $"L{i}" };
                current = current.Child;
            }
            return root;
        }
    }

    class NullHeavyObject
    {
        public string Name { get; set; } = "";
        public string? NullString { get; set; }
        public NullHeavyObject? NullChild { get; set; }
        public List<string?>? Items { get; set; }
    }

    class PlainTestObject
    {
        public string Name { get; set; } = "";
        public float Score { get; set; }
        public bool IsActive { get; set; }
        public List<string> Tags { get; set; } = new();
        public InnerData? Nested { get; set; }

        public class InnerData
        {
            public int Id { get; set; }
            public string Label { get; set; } = "";
        }
    }

    class ExplodingObject
    {
        public int SafeValue { get; set; }
        public string ExplodingProp => throw new InvalidOperationException("This property always throws!");
    }

    #endregion
}
