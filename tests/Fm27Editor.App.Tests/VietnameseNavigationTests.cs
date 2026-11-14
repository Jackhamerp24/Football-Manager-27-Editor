using Avalonia.Controls;
using Avalonia.LogicalTree;
using Fm27Editor.App;

namespace Fm27Editor.App.Tests;

[Collection("Language settings")]
public sealed class VietnameseNavigationTests
{
    [Fact]
    public void Translated_tab_keeps_original_navigation_key_and_tracks_pending_save()
    {
        var file = Path.Combine(Path.GetTempPath(), "fm27-vi-" + Guid.NewGuid().ToString("N"), "language.txt");
        var oldPath = AppLanguage.SettingPathOverride;
        var oldLanguage = AppLanguage.Current;
        AppLanguage.SettingPathOverride = file;
        try
        {
            AppLanguage.Save("vi");
            AppLanguage.Load();
            AvaloniaTestThread.Run(() =>
            {
                var save = new Button { Content = "Save changes", IsVisible = false, IsEnabled = false };
                var rail = new ContentControl();
                var host = SectionHost.Build("vi-navigation-test", [
                    new SectionHost.Section("Information", "Information", new TextBlock()),
                    new SectionHost.Section("Relationships", "Relationships", new StackPanel { Children = { save } })
                ], "Relationships", null, rail);
                var window = new Window { Content = new StackPanel { Children = { rail, host } } };
                window.Show();
                try
                {
                    Assert.Equal("Relationships", SectionHost.Selected("vi-navigation-test"));
                    save.IsEnabled = true;
                    var labels = rail.GetLogicalDescendants().OfType<ListBox>().Single().Items.Cast<object>().Select(i => i.ToString()).ToArray();
                    Assert.Equal(["Thông tin", "Mối quan hệ *"], labels);
                }
                finally { window.Close(); }
            });
        }
        finally
        {
            AppLanguage.Save(oldLanguage);
            AppLanguage.Load();
            AppLanguage.SettingPathOverride = oldPath;
            Directory.Delete(Path.GetDirectoryName(file)!, true);
        }
    }
}
