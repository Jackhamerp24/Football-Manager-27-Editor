using Fm27Editor.App;

public sealed class GraphicsIndexTests
{
    [Fact]
    public void Candidates_KeepConfigOrder_AndMatchKeysIgnoringCase()
    {
        string root = Path.Combine(Path.GetTempPath(), "fm27-index-test-" + Guid.NewGuid().ToString("N"));
        string normal = Path.Combine(root, "a-normal");
        string small = Path.Combine(root, "b-small");
        Directory.CreateDirectory(normal);
        Directory.CreateDirectory(small);
        try
        {
            File.WriteAllText(Path.Combine(normal, "config.xml"), """
                <record><list>
                  <record from="big.png" to="graphics/pictures/club/7/logo"/>
                  <record from="other.png" to="graphics/pictures/club/8/logo"/>
                </list></record>
                """);
            File.WriteAllText(Path.Combine(small, "config.xml"), """
                <record><list>
                  <record from="tiny.png" to="graphics/pictures/club/7/logo"/>
                </list></record>
                """);
            var index = GraphicsIndex.Scan(root);

            // Small configs sort last, so the normal file comes first; lookup ignores case.
            Assert.Equal(
                new[] { Path.Combine(normal, "big.png"), Path.Combine(small, "tiny.png") },
                index.Paths("GRAPHICS/pictures/club/7/logo").ToArray());
            Assert.True(index.Contains("Graphics/Pictures/Club/8/Logo"));
            Assert.Equal(3, index.Count);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Scan_MissingFolder_IsEmpty()
    {
        var index = GraphicsIndex.Scan(Path.Combine(Path.GetTempPath(), "fm27-missing-" + Guid.NewGuid().ToString("N")));
        Assert.Equal(0, index.Count);
        Assert.False(index.Contains("graphics/pictures/club/1/logo"));
    }

    [Fact]
    public void LruCache_EvictsLeastRecentlyUsed()
    {
        var cache = new LruCache<string, int>(2);
        cache.Add("a", 1);
        cache.Add("b", 2);
        Assert.True(cache.TryGetValue("a", out _));  // a is now most recent
        cache.Add("c", 3);                           // evicts b
        Assert.False(cache.TryGetValue("b", out _));
        Assert.True(cache.TryGetValue("a", out int a) && a == 1);
        Assert.True(cache.TryGetValue("c", out int c) && c == 3);
        Assert.Equal(2, cache.Count);
    }
}
