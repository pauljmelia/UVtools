﻿/*
 *                     GNU AFFERO GENERAL PUBLIC LICENSE
 *                       Version 3, 19 November 2007
 *  Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
 *  Everyone is permitted to copy and distribute verbatim copies
 *  of this license document, but changing it is not allowed.
 */
using System;
using System.Diagnostics;
using System.Threading;
using UVtools.Core.Extensions;
using UVtools.Core.Objects;

namespace UVtools.Core.Operations;

public sealed class OperationProgress : BindableBase, IDisposable
{
    public const string StatusDecodePreviews = "Decoded Previews";
    public const string StatusGatherLayers = "Gathered Layers";
    public const string StatusDecodeLayers = "Decoded Layers";
    public const string StatusEncodePreviews = "Encoded Previews";
    public const string StatusEncodeLayers = "Encoded Layers";
    public const string StatusWritingFile = "Writing File";
    public const string StatusDecodeGcode = "Decoding GCode";
    public const string StatusEncodeGcode = "Encoding GCode";

    public const string StatusOptimizingBounds = "Gathering Bounds";
    public const string StatusCalculatingBounds = "Calculating Bounds";

    public const string StatusExtracting = "Extracting";

    public const string StatusIslands = "Layers processed (Islands/Overhangs/Resin traps)";
    public const string StatusResinTrapsOptimized = "Layers optimized (Resin traps)";
    public const string StatusResinTraps = "Layers processed (Resin traps)";
    public const string StatusRepairLayers = "Repaired Layers";

    public readonly Lock Mutex = new();

    public CancellationTokenSource TokenSource { get; private set; } = null!;
    public CancellationToken Token => TokenSource.Token;
    public void ThrowIfCancellationRequested() => TokenSource.Token.ThrowIfCancellationRequested();

    public ManualResetEvent ManualReset { get; } = new (true);

    /// <summary>
    /// Blocks the current thread until the current WaitHandle receives a signal.
    /// </summary>
    /// <returns>true if the current instance receives a signal. If the current instance is never signaled, WaitOne() never returns.</returns>
    public bool PauseIfRequested() => ManualReset.WaitOne();

    /// <summary>
    /// Blocks or cancels the current thread until the current WaitHandle receives a signal.
    /// </summary>
    public void PauseOrCancelIfRequested()
    {
        ManualReset.WaitOne();
        TokenSource.Token.ThrowIfCancellationRequested();
    }

    private bool _canCancel = true;
    private bool _isPaused;
    private string _title = "Operation";
    private string _itemName = "Initializing";
    private uint _processedItems;
    private uint _itemCount;
    private string _log = string.Empty;


    public OperationProgress()
    {
        Init();
    }

    public OperationProgress(string name, uint value = 0) : this()
    {
        Reset(name, value);
    }

    public OperationProgress(bool canCancel) : this()
    {
        _canCancel = canCancel;
    }

    public Stopwatch StopWatch { get; } = new ();

    /// <summary>
    /// Gets or sets if operation can be cancelled
    /// </summary>
    public bool CanCancel
    {
        get
        {
            if (!_canCancel) return _canCancel;
            return Token is {IsCancellationRequested: false, CanBeCanceled: true} && _canCancel;
        }
        set => RaiseAndSetIfChanged(ref _canCancel, value);
    }

    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if(!RaiseAndSetIfChanged(ref _isPaused, value)) return;
            if (value)
            {
                ManualReset.Reset(); // pause
                StopWatch.Stop();
            }
            else
            {
                ManualReset.Set(); // resume
                StopWatch.Start();
            }
        }
    }

    /// <summary>
    /// Gets or sets the item name for the operation
    /// </summary>
    public string Title
    {
        get => _title;
        set => RaiseAndSetIfChanged(ref _title, value);
    }

    public string ElapsedTimeString => $"{StopWatch.Elapsed.Minutes}m {StopWatch.Elapsed.Seconds}s";
    //{StopWatch.Elapsed.Milliseconds} ms

    /// <summary>
    /// Gets or sets the item name for the operation
    /// </summary>
    public string ItemName
    {
        get => _itemName;
        set
        {
            if(!RaiseAndSetIfChanged(ref _itemName, value)) return;
            RaisePropertyChanged(nameof(Description));
        }
    }

    /// <summary>
    /// Gets or sets the number of processed items
    /// </summary>
    public uint ProcessedItems
    {
        get => _processedItems;
        set
        {
            //_processedItems = value;
            if(!RaiseAndSetIfChanged(ref _processedItems, value)) return;
            RaisePropertyChanged(nameof(ProgressPercent));
            RaisePropertyChanged(nameof(Description));
        }
    }

    /// <summary>
    /// Gets or sets the total of item count on this operation
    /// </summary>
    public uint ItemCount
    {
        get => _itemCount;
        set
        {
            RaiseAndSetIfChanged(ref _itemCount, value);
            RaisePropertyChanged(nameof(IsIndeterminate));
            RaisePropertyChanged(nameof(ProgressPercent));
            RaisePropertyChanged(nameof(Description));
        }
    }

    /// <summary>
    /// Detailed log to show below the progress bar
    /// </summary>
    public string Log
    {
        get => _log;
        set => RaiseAndSetIfChanged(ref _log, value);
    }

    /// <summary>
    /// Gets or sets an tag
    /// </summary>
    public object? Tag { get; set; }

    /// <summary>
    /// Gets the remaining items to be processed
    /// </summary>
    public uint RemainingItems => _itemCount - _processedItems;

    public int ProgressStep => (int)ProgressPercent;

    public string Description => ToString();

    public bool IsIndeterminate => _itemCount == 0;

    /// <summary>
    /// Gets the progress from 0 to 100%
    /// </summary>
    public double ProgressPercent => _itemCount == 0 ? 0 : Math.Clamp(Math.Round(_processedItems * 100.0 / _itemCount, 2), 0, 100);

    public static OperationProgress operator +(OperationProgress progress, uint value)
    {
        progress.ProcessedItems += value;
        return progress;
    }

    public static OperationProgress operator ++(OperationProgress progress)
    {
        progress.ProcessedItems++;
        return progress;
    }

    public static OperationProgress operator --(OperationProgress progress)
    {
        progress.ProcessedItems--;
        return progress;
    }

    public void Init(bool canCancel = true)
    {
        CanCancel = canCancel;
        IsPaused = false;
        Title = "Operation";
        ItemName = "Initializing";
        ItemCount = 0;
        ProcessedItems = 0;
        Log = string.Empty;

        TokenSource = new CancellationTokenSource();
        RaisePropertyChanged(nameof(CanCancel));
    }

    public void ResetAll(string title, string name = "", uint itemCount = 0, uint items = 0)
    {
        Title = title;
        Reset(name, itemCount, items);
    }

    public void Reset(string name = "", uint itemCount = 0, uint items = 0)
    {
        ItemName = name;
        ItemCount = itemCount;
        ProcessedItems = items;
    }

    public void ResetNameAndProcessed(string name = "", uint items = 0)
    {
        ItemName = name;
        ProcessedItems = items;
    }


    public override string ToString()
    {
        if (_itemCount == 0 && _processedItems == 0)
        {
            return $"{_itemName}";
        }

        if (_itemCount == 0 && _processedItems > 0)
        {
            return $"{_processedItems} {_itemName}";
        }

        return string.Format($"{{0:D{_itemCount.DigitCount()}}}/{{1}} {{2}} | {{3:F2}}%",
            _processedItems, _itemCount, _itemName,
            ProgressPercent);
    }

    public void TriggerRefresh()
    {
        RaisePropertyChanged(nameof(ElapsedTimeString));
        RaisePropertyChanged(nameof(CanCancel));
        //OnPropertyChanged(nameof(ProgressPercent));
        //OnPropertyChanged(nameof(Description));
    }

    public void LockAndIncrement()
    {
        /*lock (Mutex)
        {
            ProcessedItems++;
        }*/
        Interlocked.Increment(ref _processedItems);
        RaisePropertyChanged(nameof(ProcessedItems));
        RaisePropertyChanged(nameof(ProgressPercent));
        RaisePropertyChanged(nameof(Description));
    }

    public void Dispose()
    {
        TokenSource.Dispose();
        ManualReset.Dispose();
    }
}