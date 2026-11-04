using Avalonia.Platform.Storage;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    async Task ExportPlayersAsync(bool json)
    {
        if (module is not PlayersModule || grid.CollectionView is not { } view) return;
        var players = view.OfType<PlayerRecord>().ToArray();
        string extension = json ? "json" : "csv";
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export players",
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
            status.Text = $"Exported {players.Length:N0} players";
        }
        catch (Exception ex) { status.Text = $"Export failed: {ex.Message}"; }
    }
}
