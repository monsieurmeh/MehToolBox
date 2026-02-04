namespace MehToolBox.Tests;

public class SimpleObject
{
    public string Name { get; set; } = "";
    public int Value { get; set; }
    public bool IsActive { get; set; }
}

public class NestedObject
{
    public string Description { get; set; } = "";
    public SimpleObject? Inner { get; set; }
}

public class ObjectWithArray
{
    public string Title { get; set; } = "";
    public List<string> Items { get; set; } = new();
    public int[] Numbers { get; set; } = Array.Empty<int>();
}

public class ObjectWithNestedArray
{
    public string Name { get; set; } = "";
    public List<SimpleObject> Objects { get; set; } = new();
}

public class ObjectWithNulls
{
    public string? NullableString { get; set; }
    public SimpleObject? NullableObject { get; set; }
    public int NonNullableInt { get; set; }
}

public class CircularReferenceObject
{
    public string Name { get; set; } = "";
    public CircularReferenceObject? Self { get; set; }
    public CircularReferenceObject? Other { get; set; }
}

public class DeeplyNestedObject
{
    public string Level { get; set; } = "";
    public DeeplyNestedObject? Child { get; set; }

    public static DeeplyNestedObject CreateWithDepth(int depth)
    {
        var root = new DeeplyNestedObject { Level = "Level0" };
        var current = root;
        for (int i = 1; i < depth; i++)
        {
            current.Child = new DeeplyNestedObject { Level = $"Level{i}" };
            current = current.Child;
        }
        return root;
    }
}
