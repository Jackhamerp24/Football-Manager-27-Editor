using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace Fm27Editor.App.Tests;

/// <summary>
/// FMRTE stars the child tab that holds unsaved edits, not only the entity tab. Child tabs with their own pending edits
/// keep a hidden "Save changes" button that the shell's Save presses; its IsEnabled must star that child tab.
/// </summary>
public class SectionDirtyStarTests
{
    static string[] Labels(ContentControl rail) =>
        rail.GetLogicalDescendants().OfType<ListBox>().Single().Items.Cast<object>().Select(i => i.ToString()!).ToArray();

    [Fact]
    public void A_child_tab_with_an_enabled_hidden_save_gets_a_star_and_loses_it_when_disabled()
    {
        AvaloniaTestThread.Run(() =>
        {
            var save = new Button { Content = "Save changes", IsVisible = false, IsEnabled = false };
            var rail = new ContentControl();
            var host = SectionHost.Build("startest", [
                new SectionHost.Section("Information", "Information", new TextBlock()),
                new SectionHost.Section("Relationships", "Relationships", new StackPanel { Children = { save } }),
            ], "Relationships", null, rail);
            var window = new Window { Content = new StackPanel { Children = { rail, host } } };
            window.Show();
            try
            {
                Assert.Equal(["Information", "Relationships"], Labels(rail));
                save.IsEnabled = true;
                Assert.Equal(["Information", "Relationships *"], Labels(rail));
                save.IsEnabled = false;
                Assert.Equal(["Information", "Relationships"], Labels(rail));
            }
            finally { window.Close(); }
        });
    }

    /// <summary>The page's own edits (Information fields) star their tab through MarkDirty; the rail must be found right after Build.</summary>
    [Fact]
    public void MarkDirty_stars_the_named_tab_of_a_freshly_built_rail()
    {
        AvaloniaTestThread.Run(() =>
        {
            var rail = new ContentControl();
            SectionHost.Build("startest3", [
                new SectionHost.Section("Information", "Information", new TextBlock()),
                new SectionHost.Section("Contract", "Contract", new TextBlock()),
            ], "Information", null, rail);
            Assert.Equal("Information", SectionHost.Selected("startest3"));
            SectionHost.MarkDirty("startest3", ["Contract"]);
            Assert.Equal(["Information", "Contract *"], Labels(rail));
        });
    }

    [Fact]
    public void A_visible_button_named_save_is_not_a_pending_edit()
    {
        AvaloniaTestThread.Run(() =>
        {
            var save = new Button { Content = "Save", IsEnabled = false };
            var rail = new ContentControl();
            var host = SectionHost.Build("startest2", [
                new SectionHost.Section("Unhappiness", "Unhappiness", new StackPanel { Children = { save } }),
            ], "Unhappiness", null, rail);
            var window = new Window { Content = new StackPanel { Children = { rail, host } } };
            window.Show();
            try
            {
                save.IsEnabled = true;
                Assert.Equal(["Unhappiness"], Labels(rail));
            }
            finally { window.Close(); }
        });
    }
}
