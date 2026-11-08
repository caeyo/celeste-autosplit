using LiveSplit.Options;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace CelesteAutosplit;

internal sealed class GamePriority : IDisposable
{
    private const string ProcessName = "Celeste";
    private const int ScanIntervalMs = 2000;

    private readonly object gate = new();
    private readonly HashSet<int> handled = [];
    private readonly HashSet<int> raised = [];
    private int lastScan = Environment.TickCount;
    private bool scanning;
    private bool disposed;

    public volatile bool Enabled;

    public void Update()
    {
        int now = Environment.TickCount;
        lock (gate)
        {
            if (disposed || scanning || unchecked(now - lastScan) < ScanIntervalMs)
            {
                return;
            }

            scanning = true;
            lastScan = now;
        }

        Task.Run(Scan);
    }

    private void Scan()
    {
        try
        {
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }

                var current = new HashSet<int>();
                Process[] processes = Process.GetProcessesByName(ProcessName);
                foreach (Process process in processes)
                {
                    try
                    {
                        current.Add(process.Id);
                        if (Enabled)
                        {
                            Raise(process);
                        }
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }

                if (!Enabled)
                {
                    LowerRaised();
                    handled.Clear();
                }

                handled.IntersectWith(current);
                raised.IntersectWith(current);
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[{CelesteAutosplitFactory.Name}] Could not check Celeste's priority: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            lock (gate)
            {
                scanning = false;
            }
        }
    }

    private void Raise(Process process)
    {
        if (!handled.Add(process.Id))
        {
            return;
        }

        try
        {
            ProcessPriorityClass priority = process.PriorityClass;
            if (priority != ProcessPriorityClass.High && priority != ProcessPriorityClass.RealTime)
            {
                process.PriorityClass = ProcessPriorityClass.High;
                raised.Add(process.Id);
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            Log.Warning($"[{CelesteAutosplitFactory.Name}] Could not raise Celeste's priority: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void LowerRaised()
    {
        foreach (int id in raised)
        {
            try
            {
                using Process process = Process.GetProcessById(id);
                if (process.PriorityClass == ProcessPriorityClass.High)
                {
                    process.PriorityClass = ProcessPriorityClass.Normal;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or Win32Exception or InvalidOperationException or NotSupportedException)
            {
                Log.Warning($"[{CelesteAutosplitFactory.Name}] Could not restore Celeste's priority: {ex.GetType().Name}: {ex.Message}");
            }
        }

        raised.Clear();
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            LowerRaised();
        }
    }
}
