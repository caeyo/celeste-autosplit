using LiveSplit.Model;
using LiveSplit.Model.Comparisons;
using LiveSplit.UI.Components;
using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace CelesteAutosplit.Tests;

// Tests that run the wasm module change process-wide state (working directory and
// ComponentManager.BasePath), so they never run in parallel.
[CollectionDefinition(Name, DisableParallelization = true)]
public class LiveSplitCollection : ICollectionFixture<LiveSplitHost>
{
    public const string Name = "LiveSplit";
}

// Stands in for LiveSplit without its UI. The test output folder plays LiveSplit's
// folder, and a hidden form on an STA thread plays its UI thread, which ASR steps
// the runtime on and which all component calls go through, as in LiveSplit.
public sealed class LiveSplitHost : IDisposable
{
    public string BasePath { get; } = AppDomain.CurrentDomain.BaseDirectory;

    // Scratch folders, kept short and out of the output folder, since .NET
    // Framework doesn't support paths over 260 characters by default.
    public string TempPath { get; } = Path.Combine(Path.GetTempPath(), "celeste-autosplit-tests", Guid.NewGuid().ToString("N"));
    public string MissingWasmBasePath { get; }
    public string CorruptWasmBasePath { get; }

    private readonly Thread uiThread;
    private Form form;

    public LiveSplitHost()
    {
        if (!Environment.Is64BitProcess)
        {
            throw new InvalidOperationException("ASR only runs wasm modules in 64-bit processes.");
        }

        string wasmPath = Path.Combine(BasePath, "Components", CelesteAutosplitComponent.WasmFileName);
        if (!File.Exists(wasmPath))
        {
            throw new InvalidOperationException($"{wasmPath} is missing. Build the wasm module first (cd autosplitter && cargo b --release), then rebuild the tests.");
        }

        // ASR loads Components\x64\asr_capi.dll relative to the working directory.
        Environment.CurrentDirectory = BasePath;
        ComponentManager.BasePath = BasePath;

        MissingWasmBasePath = Path.Combine(TempPath, "missing-wasm");
        Directory.CreateDirectory(Path.Combine(MissingWasmBasePath, "Components"));
        CorruptWasmBasePath = Path.Combine(TempPath, "corrupt-wasm");
        Directory.CreateDirectory(Path.Combine(CorruptWasmBasePath, "Components"));
        File.WriteAllText(Path.Combine(CorruptWasmBasePath, "Components", CelesteAutosplitComponent.WasmFileName), "not a wasm module");

        using var ready = new ManualResetEventSlim();
        uiThread = new Thread(() =>
        {
            form = new Form();
            _ = form.Handle;
            ready.Set();
            Application.Run();
            form.Dispose();
        })
        {
            IsBackground = true,
        };
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        ready.Wait();
    }

    public T OnUiThread<T>(Func<T> func)
    {
        return (T)form.Invoke(func);
    }

    public void OnUiThread(Action action)
    {
        form.Invoke(action);
    }

    public LiveSplitState NewState()
    {
        return new LiveSplitState(new Run(new StandardComparisonGeneratorsFactory()), form, null, null, null);
    }

    public void Dispose()
    {
        form.Invoke(new Action(Application.ExitThread));
        uiThread.Join();

        try
        {
            Directory.Delete(TempPath, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
