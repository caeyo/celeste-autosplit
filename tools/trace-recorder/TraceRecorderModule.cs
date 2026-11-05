using Celeste;
using Celeste.Mod;
using Monocle;
using MonoMod.ModInterop;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace TraceRecorder;

public class TraceRecorderModule : EverestModule
{
    private const string Tag = "TraceRecorder";

    private static readonly string[] TimeFormats = [@"m\:ss\.fff", @"h\:mm\:ss\.fff"];

    private const string Root = "Celeste.Celeste::Instance";

    private static readonly FieldInfo[] InfoFields = typeof(AutoSplitterInfo)
        .GetFields(BindingFlags.Public | BindingFlags.Instance)
        .OrderBy(field => field.MetadataToken)
        .ToArray();

    private string? traceFile;
    private string? tasFile;
    private StreamWriter? trace;
    private int frame;
    private int updatesWithoutTas;
    private ulong lastFrameCounter;
    private readonly Dictionary<string, object?> vanilla = new();
    private readonly Dictionary<string, object?> everest = new();
    private long? startFileTime;
    private long fileTime;

    public override bool ParseArg(string arg, Queue<string> args)
    {
        if (arg != "--trace")
        {
            return false;
        }
        traceFile = args.TryDequeue(out string? file) ? Path.GetFullPath(file) : "";
        return true;
    }

    public override void Load() => AutoSplitter.OnUpdateInfo += Record;

    public override void Initialize()
    {
        typeof(CelesteTas).ModInterop();
        if (traceFile == null)
        {
            return;
        }
        int tas = Everest.Args.IndexOf("--tas");
        if (traceFile == "")
        {
            Fail("--trace needs a file");
        }
        else if (tas < 0 || tas + 1 >= Everest.Args.Count)
        {
            Fail("--trace needs the TAS to play, given with --tas");
        }
        else
        {
            tasFile = Path.GetFullPath(Everest.Args[tas + 1]);
        }
    }

    public override void Unload()
    {
        AutoSplitter.OnUpdateInfo -= Record;
        trace?.Dispose();
    }

    private void Record(ref AutoSplitter.CoreAutoSplitterInfo info, Func<string, nint> stringWriter)
    {
        if (traceFile == null)
        {
            return;
        }
        if (CelesteTas.IsTasRunning?.Invoke() != true)
        {
            if (trace != null)
            {
                Finish();
            }
            else if (Engine.Scene is Overworld && ++updatesWithoutTas > 60)
            {
                Fail("the TAS didn't start: CelesteTAS logs why");
            }
            return;
        }
        if (trace == null)
        {
            Start();
        }
        else if (Engine.FrameCounter != lastFrameCounter + 1)
        {
            Fail($"{Engine.FrameCounter - lastFrameCounter - 1} updates before frame {frame} weren't recorded: is the TAS fast-forwarding?");
            return;
        }
        lastFrameCounter = Engine.FrameCounter;

        AutoSplitterInfo game = Celeste.Celeste.Instance.AutoSplitterInfo;
        if (game.ChapterStarted)
        {
            startFileTime ??= game.FileTime;
        }
        fileTime = game.FileTime;

        var line = new Dictionary<string, object> { ["frame"] = frame++ };
        Dictionary<string, object?> vanillaChanges = Changes(vanilla, InfoFields.ToDictionary(field => "AutoSplitterInfo." + field.Name, field => field.GetValue(game)));
        if (vanillaChanges.Count > 0)
        {
            line["roots"] = new Dictionary<string, object> { [Root] = vanillaChanges };
        }
        Dictionary<string, object?> everestChanges = Changes(everest, EverestValues(info));
        if (everestChanges.Count > 0)
        {
            line["everest"] = everestChanges;
        }
        trace!.WriteLine(JsonSerializer.Serialize(line));
    }

    private void Start()
    {
        trace = new StreamWriter(traceFile!, append: false) { NewLine = "\n" };
        var header = new Dictionary<string, string>
        {
            ["celeste"] = Celeste.Celeste.Instance.Version.ToString(),
            ["everest"] = Everest.VersionString,
            ["celesteTas"] = Everest.Modules.Single(module => module.Metadata.Name == "CelesteTAS").Metadata.VersionString,
        };
        trace.WriteLine(JsonSerializer.Serialize(header));
        Logger.Info(Tag, "recording to " + traceFile);
    }

    private void Finish()
    {
        string? line = File.ReadLines(tasFile!).FirstOrDefault(text => text.StartsWith("FileTime:"));
        string? timed = line?["FileTime:".Length..].Split('(')[0].Trim();
        if (CelesteTas.IsTasActive?.Invoke() == true)
        {
            Fail($"the TAS paused after frame {frame - 1}, at a breakpoint or the end of a draft");
        }
        else if (startFileTime is not long start)
        {
            Fail("the TAS never began a level, so CelesteTAS can't time it");
        }
        else if (timed == null)
        {
            Fail("the TAS has no FileTime: line for CelesteTAS to fill in");
        }
        else if (!TimeSpan.TryParseExact(timed, TimeFormats, CultureInfo.InvariantCulture, out TimeSpan time)
                 || time.Ticks != fileTime - start)
        {
            var traced = TimeSpan.FromTicks(fileTime - start);
            Fail($"the TAS didn't reach its end: its FileTime is \"{timed}\", but the trace's is {traced.ToString(TimeFormats[traced.TotalHours < 1 ? 0 : 1])}");
        }
        else
        {
            Logger.Info(Tag, $"recorded {frame} updates to {traceFile}");
            Exit(0);
        }
    }

    private void Fail(string problem)
    {
        Logger.Error(Tag, problem);
        Exit(1);
    }

    private void Exit(int code)
    {
        trace?.Dispose();
        trace = null;
        traceFile = null;
        Everest.Events.Celeste.OnShutdown += () => Environment.Exit(code);
        Engine.Instance.Exit();
    }

    private static Dictionary<string, object?> Changes(Dictionary<string, object?> last, Dictionary<string, object?> values)
    {
        var changes = new Dictionary<string, object?>();
        foreach ((string name, object? value) in values)
        {
            if (!last.TryGetValue(name, out object? lastValue) || !Equals(lastValue, value))
            {
                changes[name] = value;
                last[name] = value;
            }
        }
        return changes;
    }

    private static Dictionary<string, object?> EverestValues(in AutoSplitter.CoreAutoSplitterInfo info) => new()
    {
        [nameof(info.LevelSetStrPtr)] = Marshal.PtrToStringUTF8(info.LevelSetStrPtr),
        [nameof(info.ChapterSIDStrPtr)] = Marshal.PtrToStringUTF8(info.ChapterSIDStrPtr),
        [nameof(info.ChapterID)] = info.ChapterID,
        [nameof(info.ChapterMode)] = info.ChapterMode,
        [nameof(info.RoomNameStrPtr)] = Marshal.PtrToStringUTF8(info.RoomNameStrPtr),
        [nameof(info.ChapterTime)] = info.ChapterTime,
        [nameof(info.ChapterStrawberries)] = info.ChapterStrawberries,
        [nameof(info.ChapterFlags)] = (uint) info.ChapterFlags,
        [nameof(info.FileTime)] = info.FileTime,
        [nameof(info.FileStrawberries)] = info.FileStrawberries,
        [nameof(info.FileGoldenStrawberries)] = info.FileGoldenStrawberries,
        [nameof(info.FileCassettes)] = info.FileCassettes,
        [nameof(info.FileHearts)] = info.FileHearts,
        [nameof(info.FileFlags)] = (uint) info.FileFlags,
    };
}

[ModImportName("CelesteTAS")]
internal static class CelesteTas
{
    public static Func<bool>? IsTasActive = null;

    public static Func<bool>? IsTasRunning = null;
}
