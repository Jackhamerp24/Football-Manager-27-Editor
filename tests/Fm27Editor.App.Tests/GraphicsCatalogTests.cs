using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
            var index = GraphicsIndex.Scan(root);
            Assert.Equal(Path.Combine(pack, "TCM1_680"), Assert.Single(index.Paths("graphics/pictures/club/680/logo")));
            Assert.Equal(Path.Combine(pack, "bruno"), Assert.Single(index.Paths("graphics/pictures/person/43124203/portrait")));
            Assert.Equal(Path.Combine(pack, "regenerated"), Assert.Single(index.Paths("graphics/pictures/person/2002054286/portrait")));
            Assert.False(index.Contains("graphics/pictures/club/999/logo"));
            Assert.Empty(index.Paths("graphics/pictures/club/999/logo"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public unsafe void AutoCrop_CropsTransparentMargins()
    {
        // 100x100 canvas with a 20x20 colored box at (40, 40)
        var wb = new WriteableBitmap(new PixelSize(100, 100), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = wb.Lock())
        {
            byte* pBase = (byte*)fb.Address;
            int stride = fb.RowBytes;
            for (int y = 40; y < 60; y++)
            {
                byte* row = pBase + y * stride;
                for (int x = 40; x < 60; x++)
                {
                    row[x * 4 + 0] = 0x00; // B
                    row[x * 4 + 1] = 0x00; // G
                    row[x * 4 + 2] = 0xFF; // R
                    row[x * 4 + 3] = 0xFF; // A
                }
            }
        }

        var cropped = GraphicsCatalog.AutoCrop(wb);
        Assert.NotNull(cropped);
        // 20x20 content plus 2px margin on each side = 24x24
        Assert.Equal(24, cropped.PixelSize.Width);
        Assert.Equal(24, cropped.PixelSize.Height);
    }

    [Fact]
    public unsafe void AutoCrop_KeepsOpaqueBitmapUnchanged()
    {
        var wb = new WriteableBitmap(new PixelSize(30, 30), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = wb.Lock())
        {
            byte* pBase = (byte*)fb.Address;
            int stride = fb.RowBytes;
            for (int y = 0; y < 30; y++)
            {
                byte* row = pBase + y * stride;
                for (int x = 0; x < 30; x++)
                {
                    row[x * 4 + 3] = 0xFF;
                }
            }
        }

        var result = GraphicsCatalog.AutoCrop(wb);
        Assert.Equal(30, result.PixelSize.Width);
        Assert.Equal(30, result.PixelSize.Height);
    }

    [Fact]
    public void ResolveFlag_LoadsAuthenticFlags()
    {
        var catalog = new GraphicsCatalog();
        int[] nationIds = [765, 769, 788, 796, 145]; // England, France, Portugal, Spain, Vietnam
        foreach (int id in nationIds)
        {
            var flag = catalog.ResolveFlag(id);
            Assert.NotNull(flag);
            Assert.Equal(25, flag.PixelSize.Width);
            Assert.Equal(18, flag.PixelSize.Height);
        }
    }

    [Fact]
    public void NationFlag_CreatesProperlyConfiguredImage()
    {
        var catalog = new GraphicsCatalog();
        Fm27Editor.App.Tests.AvaloniaTestThread.Run(() =>
        {
            var flagImg = catalog.NationFlag(765, 14); // England
            Assert.NotNull(flagImg);
            Assert.True(flagImg.IsVisible);
            Assert.NotNull(flagImg.Source);
            Assert.Equal(14, flagImg.Height);
            Assert.Equal(19, flagImg.Width); // Round(14 * 25/18) = 19
        });
    }
}
