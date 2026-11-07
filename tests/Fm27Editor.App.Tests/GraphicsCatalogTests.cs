using Fm27Editor.App;

public sealed class GraphicsCatalogTests
{
    [Fact]
    public void IndexUsesResourceIdsAndCustomFileNames()
    {
        string root = Path.Combine(Path.GetTempPath(), "fm27-graphics-test-" + Guid.NewGuid().ToString("N"));
        string pack = Path.Combine(root, "logos");
        Directory.CreateDirectory(pack);
        try
        {
            File.WriteAllText(Path.Combine(pack, "config.xml"), """
                <record><list id="maps">
                  <record from="TCM1_680" to="graphics/pictures/club/680/logo"/>
                  <record from="bruno" to="graphics/pictures/person/43124203/portrait"/>
                  <record from="regenerated" to="graphics/pictures/person/r-2002054286/portrait"/>
                  <record from="../escape" to="graphics/pictures/club/999/logo"/>
                </list></record>
                """);
            var index = GraphicsCatalog.Scan(root);
            Assert.Equal(Path.Combine(pack, "TCM1_680"), Assert.Single(index["graphics/pictures/club/680/logo"]));
            Assert.Equal(Path.Combine(pack, "bruno"), Assert.Single(index["graphics/pictures/person/43124203/portrait"]));
            Assert.Equal(Path.Combine(pack, "regenerated"), Assert.Single(index["graphics/pictures/person/2002054286/portrait"]));
            Assert.False(index.ContainsKey("graphics/pictures/club/999/logo"));
        }
        finally { Directory.Delete(root, true); }
    }
}
