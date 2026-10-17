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

// Records what an auto splitter reads while CelesteTAS plays a TAS, started with --tas FILE.
// Given --trace FILE, it writes the trace to FILE, in the format traces/README.md describes, and
// exits the game once the TAS has ended: with 0 if the trace is complete, or 1, logging why.
public class TraceRecorderModule : EverestModule
{
    private const string Tag = "TraceRecorder";

    // How CelesteTAS writes times: under an hour, and from an hour.
    private static readonly string[] TimeFormats = [@"m\:ss\.fff", @"h\:mm\:ss\.fff"];

    // The probe's names for the roots: the one the game's AutoSplitterInfo is read from, and the
    // save's.
    private const string Root = "Celeste.Celeste::Instance";
    private const string SaveRoot = "Celeste.SaveData::Instance";

    // Every field of the game's AutoSplitterInfo, in the order it declares them.
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
    // The values last recorded. The save's are cleared when it becomes null, so that a save that
    // appears is recorded whole, as the new object it is.
    private readonly Dictionary<string, object?> vanilla = new();
    private readonly Dictionary<string, object?> save = new();
    private readonly Dictionary<string, object?> everest = new();
    // Whether the save was last recorded as null, or null before the first line.
    private bool? saveNull;
    // The changes of the last update, until the next begins: by root, and Everest's.
    private Dictionary<string, object?>? pendingRoots;
    private Dictionary<string, object?>? pendingEverest;
    // The file's time when the TAS's first level began, and the last recorded.
    private long? startFileTime;
    private long fileTime;

    public override bool ParseArg(string arg, Queue<string> args)
    {
        if (arg != "--trace")
        {
            return false;
        }
        // Without a file, Initialize fails the recording, once the game can exit.
        traceFile = args.TryDequeue(out string? file) ? Path.GetFullPath(file) : "";
        return true;
    }

    public override void Load() => AutoSplitter.OnUpdateInfo += Record;

    // Every mod has loaded by now, so CelesteTAS has registered its API.
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

    // Everest raises this at the start of each update of the game: right after the game updates
    // its AutoSplitterInfo, and Everest its own values, from the state the previous update left.
    // They hold until the next update, so they're this update's line. The save changes during the
    // update, so the line is written at the start of the next, with the save as this update left
    // it.
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
                WritePending();
                Finish();
            }
            // CelesteTAS starts the TAS at the end of the title screen's first update, so if it
            // hasn't a second later, it couldn't.
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
        // The game counts its updates. CelesteTAS doesn't update the auto splitter's values while
        // it fast-forwards, so a TAS that does would leave updates out.
        else if (Engine.FrameCounter != lastFrameCounter + 1)
        {
            Fail($"{Engine.FrameCounter - lastFrameCounter - 1} updates before frame {frame} weren't recorded: is the TAS fast-forwarding?");
            return;
        }
        WritePending();
        lastFrameCounter = Engine.FrameCounter;

        AutoSplitterInfo game = Celeste.Celeste.Instance.AutoSplitterInfo;
        if (game.ChapterStarted)
        {
            startFileTime ??= game.FileTime;
        }
        fileTime = game.FileTime;

        pendingRoots = new Dictionary<string, object?>();
        Dictionary<string, object?> vanillaChanges = Changes(vanilla, InfoFields.ToDictionary(field => "AutoSplitterInfo." + field.Name, field => field.GetValue(game)));
        if (vanillaChanges.Count > 0)
        {
            pendingRoots[Root] = vanillaChanges;
        }
        pendingEverest = Changes(everest, EverestValues(info));
    }

    // Writes the last update's line, with the save as that update left it, which is how an auto
    // splitter reads it until this update. Nothing the game does before AutoSplitterInfo.Update
    // changes what's recorded of the save. A new file's save appears in the update that starts the
    // file, one update before AutoSplitterInfo or Everest show it.
    private void WritePending()
    {
        if (pendingRoots == null)
        {
            return;
        }
        SaveData? data = SaveData.Instance;
        if (data == null)
        {
            if (saveNull != true)
            {
                pendingRoots[SaveRoot] = null;
                save.Clear();
            }
            saveNull = true;
        }
        else
        {
            saveNull = false;
            Dictionary<string, object?> saveChanges = Changes(save, new Dictionary<string, object?>
            {
                [nameof(data.Time)] = data.Time,
                [nameof(data.DebugMode)] = data.DebugMode,
            });
            if (saveChanges.Count > 0)
            {
                pendingRoots[SaveRoot] = saveChanges;
            }
        }

        var line = new Dictionary<string, object> { ["frame"] = frame++ };
        if (pendingRoots.Count > 0)
        {
            line["roots"] = pendingRoots;
        }
        if (pendingEverest!.Count > 0)
        {
            line["everest"] = pendingEverest;
        }
        trace!.WriteLine(JsonSerializer.Serialize(line));
        pendingRoots = null;
        pendingEverest = null;
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

    // The TAS has stopped playing, and has to have reached its end. Paused, at a breakpoint or at
    // the end of a draft, it hasn't. Aborted, say by a failed Assert, it has stopped as if it had,
    // but CelesteTAS only fills in the TAS file's FileTime at the end: with the time the file has
    // gained since the TAS's first level began. So that has to be the trace's.
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

    // Exits the game the way it exits itself, after this update: exiting the process from within an
    // update can hang it. Once Everest has shut the game down, it exits the process with 0, so the
    // recorder exits it with the code given just before.
    private void Exit(int code)
    {
        trace?.Dispose();
        trace = null;
        traceFile = null;
        pendingRoots = null;
        Everest.Events.Celeste.OnShutdown += () => Environment.Exit(code);
        Engine.Instance.Exit();
    }

    // The values that differ from those last recorded, which they replace: all of them, the first
    // time.
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

    // The struct's header isn't recorded: it's constant, except InfoVersion, which reads 0xff while
    // Everest updates the struct, as it does here. Its strings are UTF-8, NUL-terminated, with 0 for
    // none.
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

// The part of CelesteTAS's stable API the recorder uses, which Initialize imports.
[ModImportName("CelesteTAS")]
internal static class CelesteTas
{
    // Whether a TAS is playing, or paused.
    public static Func<bool>? IsTasActive = null;

    // Whether a TAS is playing, and not paused.
    public static Func<bool>? IsTasRunning = null;
}
