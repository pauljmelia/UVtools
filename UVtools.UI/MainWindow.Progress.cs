﻿/*
 *                     GNU AFFERO GENERAL PUBLIC LICENSE
 *                       Version 3, 19 November 2007
 *  Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
 *  Everyone is permitted to copy and distribute verbatim copies
 *  of this license document, but changing it is not allowed.
 */

using Avalonia.Threading;
using System;
using System.ComponentModel.DataAnnotations;
using System.Timers;
using UVtools.Core.Operations;
using UVtools.UI.Structures;

namespace UVtools.UI;

public partial class MainWindow
{
    #region Members
    public OperationProgress Progress { get; } = new();
    private readonly Timer _progressTimer = new(200) { AutoReset = true };
    private long _progressLastTotalSeconds;
    private LogItem _progressLogItem = null!;
    private bool _isProgressVisible;

    #endregion

    #region Properties

    public bool IsProgressVisible
    {
        get => _isProgressVisible;
        set
        {
            if (!RaiseAndSetIfChanged(ref _isProgressVisible, value)) return;
            _ramUsageTimer.Enabled = value && Settings.General.AvailableRamLimit > 0;
        }
    }

    #endregion

    public void InitProgress()
    {
        _progressTimer.Elapsed += (sender, args) =>
        {
            var elapsedSeconds = Progress.StopWatch.ElapsedMilliseconds / 1000;
            if (_progressLastTotalSeconds == elapsedSeconds) return;
            /*Debug.WriteLine(StopWatch.ElapsedMilliseconds);
            Debug.WriteLine(elapsedSeconds);
            Debug.WriteLine(_lastTotalSeconds);*/
            _progressLastTotalSeconds = elapsedSeconds;

            Dispatcher.UIThread.InvokeAsync(() => Progress.TriggerRefresh(), DispatcherPriority.Render);
        };
    }

    public void ProgressOnClickPauseResume()
    {
        if (!Progress.CanCancel) return;
        Progress.IsPaused = !Progress.IsPaused;
    }

    public void ProgressOnClickCancel()
    {
        if (!Progress.CanCancel) return;
        DialogResult = DialogResults.Cancel;
        Progress.CanCancel = false;
        Progress.TokenSource.Cancel();
        Progress.IsPaused = false;
    }

    public void ProgressShow(string title, bool canCancel = true)
    {
        IsGUIEnabled = false;
        Progress.Init(canCancel);
        Progress.Title = title;
        _progressLogItem = new(title);

        Progress.StopWatch.Restart();
        _progressLastTotalSeconds = 0;

        if (!_progressTimer.Enabled)
        {
            _progressTimer.Start();
        }

        Progress.TriggerRefresh();

        IsProgressVisible = true;

        InvalidateVisual();
    }

    public void ProgressFinish()
    {
        _progressTimer.Stop();
        Progress.StopWatch.Stop();
        _progressLogItem.ElapsedTime = Math.Round(Progress.StopWatch.Elapsed.TotalSeconds, 2);
        App.MainWindow.AddLog(_progressLogItem);
        IsProgressVisible = false;
        InvalidateVisual();
    }
}