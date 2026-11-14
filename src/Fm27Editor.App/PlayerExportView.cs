using Avalonia.Platform.Storage;
using Fm27Editor.Core.Players;
using System.Text;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    static readonly FilePickerFileType AttributeJson = new("JSON") { Patterns = ["*.json"] };
    static readonly FilePickerFileType AttributeCsv = new("CSV") { Patterns = ["*.csv"] };

    async Task ExportPlayerAttributesAsync(PlayerRecord player)
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = AppLanguage.Get("Ui_Export_attributes"), SuggestedFileName = $"{player.Id}-attributes.json", DefaultExtension = "json",
                FileTypeChoices = [AttributeJson, AttributeCsv]
            });
            if (file is null) return;
            string content = PlayerAttributeFile.Export(player, file.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteAsync(content);
            status.Text = AppLanguage.T("Attributes exported");
        }
        catch (Exception ex) { status.Text = string.Format(AppLanguage.T("Export failed: {0}"), ex.Message); }
    }

    async Task ImportPlayerAttributesAsync(PlayerRecord player)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = AppLanguage.Get("Ui_Import_attributes"), AllowMultiple = false, FileTypeFilter = [AttributeJson, AttributeCsv] });
            if (files.Count == 0) return;
            await using var stream = await files[0].OpenReadAsync();
            if (stream.CanSeek && stream.Length > 100_000) throw new FormatException("File too large");
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            string content = await reader.ReadToEndAsync();
            var values = PlayerAttributeFile.Parse(content, files[0].Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
            if (selected?.Address != player.Address) throw new InvalidOperationException("Player changed while choosing the file");
            if (!drafts.TryGetValue(player.Address, out var draft)) drafts[player.Address] = draft = new PlayerDraft(player);
            PlayerAttributeFile.Stage(draft, values);
            RenderEditor();
            int unknown = Enum.GetValues<PersonalityAttribute>().Count(a => values[$"Personality.{a}"] == 0);
            status.Text = unknown == 0 ? $"{draft.Count} unsaved change(s)" : $"{draft.Count} unsaved change(s); {unknown} unknown personality value(s) skipped";
        if (draft.Count > 0) SetActiveTabDirty(true);
        }
        catch (Exception ex) { status.Text = string.Format(AppLanguage.T("Import failed: {0}"), ex.Message); }
    }

    async Task ExportPlayersAsync(bool json)
    {
        if (module is not PlayersModule || grid.CollectionView is not { } view) return;
        var players = view.OfType<PlayerRecord>().ToArray();
        string extension = json ? "json" : "csv";
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = AppLanguage.Get("Ui_Export_players"),
                SuggestedFileName = $"players-{backend.GameDate?.ToString("yyyy-MM-dd") ?? "export"}.{extension}",
                DefaultExtension = extension,
                FileTypeChoices = [new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = [$"*.{extension}"] }]
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            await Task.Run(() =>
            {
                if (json) PlayerExport.WriteJson(stream, players);
                else PlayerExport.WriteCsv(stream, players);
            });
            status.Text = string.Format(AppLanguage.T("Exported {0:N0} players"), players.Length);
        }
        catch (Exception ex) { status.Text = string.Format(AppLanguage.T("Export failed: {0}"), ex.Message); }
    }
}
