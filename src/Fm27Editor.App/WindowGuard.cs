namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    /// <summary>Windows that are being prepared or are open, by opener name; a second click on the same button is ignored.</summary>
    readonly HashSet<string> openers = [];
}
