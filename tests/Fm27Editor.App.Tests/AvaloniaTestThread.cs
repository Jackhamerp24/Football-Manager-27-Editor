using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Fm27Editor.App;

namespace Fm27Editor.App.Tests;

/// <summary>
/// Avalonia controls can only be created on the thread that set Avalonia up. Test methods run on whatever worker thread
/// xunit picks, so the setup used to land on a different thread than some tests ("Call from invalid thread", about one
/// run in eight). One dedicated thread now sets Avalonia up before any test and runs the UI jobs; tests send their UI
/// work to it through <c>Dispatcher.UIThread.Invoke</c>.
/// </summary>
static class AvaloniaTestThread
{
    [ModuleInitializer]
    internal static void Start()
    {
        using var ready = new ManualResetEventSlim();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Program.BuildAvaloniaApp().SetupWithoutStarting(); }
            catch (Exception ex) { failure = ex; }
            finally { ready.Set(); }
            if (failure is not null) return;
            while (true)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(1);
            }
        }) { IsBackground = true, Name = "Avalonia test UI thread" };
        thread.Start();
        ready.Wait();
        if (failure is not null) throw new InvalidOperationException("Avalonia could not be set up for the tests", failure);
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread and rethrows what it throws.</summary>
    public static void Run(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Invoke(action);
    }
}
