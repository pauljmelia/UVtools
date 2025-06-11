﻿using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System.Timers;
using UVtools.Core.Operations;
using UVtools.UI.Controls.Tools;
using UVtools.UI.Extensions;
using UVtools.UI.Windows;

namespace UVtools.UI.Controls.Calibrators;

public partial class CalibrateToleranceControl : ToolControl
{
    public OperationCalibrateTolerance Operation => (BaseOperation as OperationCalibrateTolerance)!;

    private readonly Timer _timer = null!;

    /*public string ProfileName
    {
        get => _profileName;
        set => RaiseAndSetIfChanged(ref _profileName, value);
    }*/

    //public bool IsProfileAddEnabled => Operation.ScaleXFactor != 100 || Operation.ScaleYFactor != 100;

    private Bitmap? _previewImage;
    /*private string _profileName;
    private bool _isProfileNameEnabled;*/

    public Bitmap? PreviewImage
    {
        get => _previewImage;
        set => RaiseAndSetIfChanged(ref _previewImage, value);
    }

    public bool IsDisplaySizeVisible => SlicerFile!.DisplayWidth <= 0 && SlicerFile.DisplayHeight <= 0;

    public CalibrateToleranceControl()
    {
        BaseOperation = new OperationCalibrateTolerance(SlicerFile!);
        if (!ValidateSpawn()) return;
        InitializeComponent();
            

        _timer = new Timer(100)
        {
            AutoReset = false
        };
        _timer.Elapsed += (sender, e) => Dispatcher.UIThread.InvokeAsync(UpdatePreview);
    }

    public override void Callback(ToolWindow.Callbacks callback)
    {
        if (App.SlicerFile is null) return;
        switch (callback)
        {
            case ToolWindow.Callbacks.Init:
            case ToolWindow.Callbacks.AfterLoadProfile:
                Operation.PropertyChanged += (sender, e) =>
                {
                    _timer.Stop();
                    _timer.Start();
                    /*if (e.PropertyName == nameof(Operation.ScaleXFactor) || e.PropertyName == nameof(Operation.ScaleYFactor))
                    {
                        RaisePropertyChanged(nameof(IsProfileAddEnabled));
                        return;
                    }*/
                };
                _timer.Stop();
                _timer.Start();
                break;
        }
    }

    public void UpdatePreview()
    {
        var layers = Operation.GetLayers();
        _previewImage?.Dispose();
        PreviewImage = layers[^1].ToBitmap();
        foreach (var layer in layers)
        {
            layer.Dispose();
        }
    }

    /*public async Task AddProfile()
    {
        OperationResize resize = new OperationResize
        {
            ProfileName = ProfileName,
            X = Operation.ScaleXFactor,
            Y = Operation.ScaleYFactor
        };
        var find = OperationProfiles.FindByName(resize, ProfileName);
        if (find is not null)
        {
            if (await ParentWindow.MessageBoxQuestion(
                $"A profile with same name and/or values already exists, do you want to overwrite:\n{find}\nwith:\n{resize}\n?") != MessageButtonResult.Yes) return;

            OperationProfiles.RemoveProfile(resize, false);
        }

        OperationProfiles.AddProfile(resize);
        await ParentWindow.MessageBoxInfo($"The resize profile has been added.\nGo to Tools - Resize and select the saved profile to load it in.\n{resize}");
    }

    public void AutoNameProfile()
    {
        var printerName = string.IsNullOrEmpty(App.SlicerFile.MachineName)
            ? "MyPrinterX"
            : App.SlicerFile.MachineName;
        ProfileName = $"{printerName}, MyResinX, {Operation.Microns}µm, {Operation.BottomExposure}s/{Operation.NormalExposure}s";
    }
    */
}