using LiveSplit.Model;
using LiveSplit.Model.Comparisons;
using LiveSplit.UI.Components;
using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace CelesteAutosplit.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public class LiveSplitCollection : ICollectionFixture<LiveSplitHost>
{
    public const string Name = "LiveSplit";
}

public sealed class LiveSplitHost : IDisposable
{
    public string BasePath { get; } = AppDomain.CurrentDomain.BaseDirectory;

    public string TempPath { get; } = Path.Combine(Path.GetTempPath(), "celeste-autosplit-tests", Guid.NewGuid().ToString("N"));
    public string MissingWasmBasePath { get; }
    public string CorruptWasmBasePath { get; }

    private readonly Thread uiThread;
    private Form form;

    public LiveSplitHost()
    {
        string wasmPath = Path.Combine(BasePath, "Components", CelesteAutosplitComponent.WasmFileName);
        if (!File.Exists(wasmPath))
        {
            throw new InvalidOperationException($"{wasmPath} is missing. Build the wasm module first (cd autosplitter && cargo b --release), then rebuild the tests.");
        }

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
