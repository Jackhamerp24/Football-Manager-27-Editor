using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Fm27Editor.App;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App.Tests;

[Collection("Language settings")]
public sealed class VietnameseSearchTests
{
    [Fact]
    public void Contract_page_is_translated_without_changing_filter_values()
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
                var dialog = new AttributeFilterDialog(new PlayerFilter
                {
                    ContractType = ContractType.PartTime,
                    Clauses = [new ContractClauseCriterion(0, true, 1000000, null)]
                }, [], null, []);
                Assert.Equal("Tìm kiếm nâng cao", dialog.Title);
                var tab = dialog.GetLogicalDescendants().OfType<Button>().Single(b => b.Content is TextBlock t && t.Text == "Hợp đồng");
                tab.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                var text = dialog.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
                Assert.Contains("ĐIỀU KHOẢN GIẢI PHÓNG", text);
                Assert.Contains("Phí giải phóng tối thiểu", text);
                Assert.Equal(ContractType.PartTime, dialog.ResultContractType);
                Assert.Equal(1000000, Assert.Single(dialog.ResultClauses).MinimumValue);
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
