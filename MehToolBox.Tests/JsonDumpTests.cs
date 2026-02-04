using System.Text.Json;
using Xunit;

namespace MehToolBox.Tests;

public class JsonDumpTests
{
    [Fact]
    public void SimpleObject_ReturnsValidJson()
    {
        var obj = new SimpleObject { Name = "Test", Value = 42, IsActive = true };
        var json = TestDumpHelper.DumpToJson(obj);

        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("SimpleObject", out var root));

        Assert.Equal("Test", GetValue<string>(root, "Name"));
        Assert.Equal(42, GetValue<int>(root, "Value"));
        Assert.True(GetValue<bool>(root, "IsActive"));
    }

    [Fact]
    public void EmptyStrings_PreservedNotNull()
    {
        var obj = new SimpleObject { Name = "", Value = 0, IsActive = false };
        var json = TestDumpHelper.DumpToJson(obj);

        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("SimpleObject", out var root));

        Assert.Equal("", GetValue<string>(root, "Name"));
        Assert.Equal(0, GetValue<int>(root, "Value"));
        Assert.False(GetValue<bool>(root, "IsActive"));
    }

    [Fact]
    public void NestedObject_AllLevelsAccessible()
    {
        var obj = new NestedObject
        {
            Description = "Parent",
            Inner = new SimpleObject { Name = "Child", Value = 100, IsActive = false }
        };
        var json = TestDumpHelper.DumpToJson(obj);
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("NestedObject", out var root));

        Assert.Equal("Parent", GetValue<string>(root, "Description"));
        Assert.True(root.TryGetProperty("Inner", out var inner));
        Assert.Equal("Child", GetValue<string>(inner, "Name"));
        Assert.Equal(100, GetValue<int>(inner, "Value"));
    }

    [Fact]
    public void NestedObject_NullInner_MarkedAsNull()
    {
        var obj = new NestedObject { Description = "Lonely", Inner = null };
        var json = TestDumpHelper.DumpToJson(obj);
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("NestedObject", out var root));

        Assert.Equal("Lonely", GetValue<string>(root, "Description"));
        Assert.True(root.TryGetProperty("Inner", out var inner));
        Assert.Equal(JsonValueKind.Null, inner.GetProperty("$value").ValueKind);
    }

    [Fact]
    public void Array_EmptyList_OutputsEmptyArray()
    {
        var obj = new ObjectWithArray { Title = "Empty", Items = new List<string>(), Numbers = Array.Empty<int>() };
        var json = TestDumpHelper.DumpToJson(obj);
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("ObjectWithArray", out var root));

        Assert.True(root.TryGetProperty("Items", out var items));
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.Equal(0, items.GetArrayLength());
    }

    [Fact]
    public void Array_AllElementsPresent()
    {
        var obj = new ObjectWithArray
        {
            Title = "List Test",
            Items = new List<string> { "First", "Second", "Third" },
            Numbers = new[] { 10, 20, 30 }
        };
        var json = TestDumpHelper.DumpToJson(obj);

        Assert.Contains("First", json);
        Assert.Contains("Second", json);
        Assert.Contains("Third", json);
        Assert.Contains("10", json);
        Assert.Contains("20", json);
        Assert.Contains("30", json);
    }

    [Fact]
    public void NullValues_DistinguishedFromMissingValues()
    {
        var obj = new ObjectWithNulls { NullableString = null, NullableObject = null, NonNullableInt = 42 };
        var json = TestDumpHelper.DumpToJson(obj);
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("ObjectWithNulls", out var root));

        Assert.True(root.TryGetProperty("NullableString", out var nullStr));
        Assert.Equal(JsonValueKind.Null, nullStr.GetProperty("$value").ValueKind);
        Assert.True(root.TryGetProperty("NullableObject", out var nullObj));
        Assert.Equal(JsonValueKind.Null, nullObj.GetProperty("$value").ValueKind);
        Assert.Equal(42, GetValue<int>(root, "NonNullableInt"));
    }

    [Fact]
    public void MaxDepth_TruncatesAtBoundary_IncludesUpToLimit()
    {
        var obj = DeeplyNestedObject.CreateWithDepth(10);
        var settings = new TestDumpHelper.DumpSettings { MaxDepth = 3 };
        var json = TestDumpHelper.DumpToJson(obj, settings);

        Assert.Contains("Level0", json);
        Assert.Contains("Level1", json);
        Assert.Contains("Level2", json);
        Assert.DoesNotContain("Level3", json);
        Assert.DoesNotContain("Level9", json);
        Assert.Contains("$maxDepth", json);
    }

    [Fact]
    public void MaxDepth_ZeroDepth_OnlyMarker()
    {
        var obj = DeeplyNestedObject.CreateWithDepth(5);
        var settings = new TestDumpHelper.DumpSettings { MaxDepth = 0 };
        var json = TestDumpHelper.DumpToJson(obj, settings);

        Assert.Contains("$maxDepth", json);
        Assert.DoesNotContain("Level0", json);
    }

    [Fact]
    public void CircularReference_DirectSelfReference_NoInfiniteLoop()
    {
        var obj = new CircularReferenceObject { Name = "Root" };
        obj.Self = obj;

        var json = TestDumpHelper.DumpToJson(obj);

        Assert.Contains("Root", json);
        var occurrences = json.Split("Root").Length - 1;
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void CircularReference_IndirectCycle_NoInfiniteLoop()
    {
        var a = new CircularReferenceObject { Name = "NodeA" };
        var b = new CircularReferenceObject { Name = "NodeB" };
        a.Other = b;
        b.Other = a;

        var json = TestDumpHelper.DumpToJson(a);

        Assert.Contains("NodeA", json);
        Assert.Contains("NodeB", json);
        var occurrencesA = json.Split("NodeA").Length - 1;
        var occurrencesB = json.Split("NodeB").Length - 1;
        Assert.Equal(1, occurrencesA);
        Assert.Equal(1, occurrencesB);
    }

    [Fact]
    public void NestedArray_ObjectsRetainAllProperties()
    {
        var obj = new ObjectWithNestedArray
        {
            Name = "Container",
            Objects = new List<SimpleObject>
            {
                new() { Name = "First", Value = 1, IsActive = true },
                new() { Name = "Second", Value = 2, IsActive = false }
            }
        };
        var json = TestDumpHelper.DumpToJson(obj);

        Assert.Contains("First", json);
        Assert.Contains("Second", json);
        Assert.Contains("\"$value\": 1", json);
        Assert.Contains("\"$value\": 2", json);
        Assert.Contains("\"$value\": true", json);
        Assert.Contains("\"$value\": false", json);
    }

    [Fact]
    public void NullObject_ReturnsLiteralNull()
    {
        var json = TestDumpHelper.DumpToJson(null);
        Assert.Equal("null", json);
    }

    [Fact]
    public void MaxEnumerableItems_TruncatesAtBoundary_IncludesUpToLimit()
    {
        var obj = new ObjectWithArray
        {
            Title = "Big List",
            Items = Enumerable.Range(1, 100).Select(i => $"Item{i}").ToList()
        };
        var settings = new TestDumpHelper.DumpSettings { MaxEnumerableItems = 5 };
        var json = TestDumpHelper.DumpToJson(obj, settings);

        Assert.Contains("Item1", json);
        Assert.Contains("Item5", json);
        Assert.DoesNotContain("Item6", json);
        Assert.DoesNotContain("Item100", json);
        Assert.Contains("$truncated", json);
    }

    [Fact]
    public void MaxEnumerableItems_ExactlyAtLimit_NoTruncationMarker()
    {
        var obj = new ObjectWithArray
        {
            Title = "Exact",
            Items = new List<string> { "A", "B", "C", "D", "E" }
        };
        var settings = new TestDumpHelper.DumpSettings { MaxEnumerableItems = 5 };
        var json = TestDumpHelper.DumpToJson(obj, settings);

        Assert.Contains("A", json);
        Assert.Contains("E", json);
        Assert.DoesNotContain("$truncated", json);
    }

    [Fact]
    public void SpecialCharacters_InStrings_Preserved()
    {
        var obj = new SimpleObject { Name = "Test\twith\nnewlines\"and quotes", Value = 0, IsActive = false };
        var json = TestDumpHelper.DumpToJson(obj);

        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("SimpleObject", out var root));
        var name = GetValue<string>(root, "Name");
        Assert.Contains("\t", name);
        Assert.Contains("\n", name);
        Assert.Contains("\"", name);
    }

    [Fact]
    public void LargeNumbers_NotTruncated()
    {
        var obj = new SimpleObject { Name = "Big", Value = int.MaxValue, IsActive = true };
        var json = TestDumpHelper.DumpToJson(obj);

        Assert.Contains(int.MaxValue.ToString(), json);
    }

    static T? GetValue<T>(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var prop)) return default;
        if (!prop.TryGetProperty("$value", out var value)) return default;

        if (typeof(T) == typeof(string)) return (T)(object)value.GetString()!;
        if (typeof(T) == typeof(int)) return (T)(object)value.GetInt32();
        if (typeof(T) == typeof(bool)) return (T)(object)value.GetBoolean();
        return default;
    }
}
