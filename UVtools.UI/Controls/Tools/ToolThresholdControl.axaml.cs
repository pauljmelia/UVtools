﻿using Emgu.CV.CvEnum;
using UVtools.Core.Operations;

namespace UVtools.UI.Controls.Tools;

public partial class ToolThresholdControl : ToolControl
{
    private int _selectedPresetIndex;
    private bool _isThresholdEnabled = true;
    private bool _isMaximumEnabled = true;
    private bool _isTypeEnabled = true;
    public OperationThreshold Operation => (BaseOperation as OperationThreshold)!;

    public string[] Presets =>
    [
        "Free use",
        "Strip AntiAliasing",
        "Set pixel brightness"
    ];

    public bool IsThresholdEnabled
    {
        get => _isThresholdEnabled;
        set => RaiseAndSetIfChanged(ref _isThresholdEnabled, value);
    }

    public bool IsMaximumEnabled
    {
        get => _isMaximumEnabled;
        set => RaiseAndSetIfChanged(ref _isMaximumEnabled, value);
    }

    public bool IsTypeEnabled
    {
        get => _isTypeEnabled;
        set => RaiseAndSetIfChanged(ref _isTypeEnabled, value);
    }

    public int SelectedPresetIndex
    {
        get => _selectedPresetIndex;
        set
        {
            if (!RaiseAndSetIfChanged(ref _selectedPresetIndex, value)) return;

            switch (_selectedPresetIndex)
            {
                case 0:
                    IsThresholdEnabled = true;
                    IsMaximumEnabled = true;
                    IsTypeEnabled = true;
                    break;
                case 1:
                    IsThresholdEnabled = true;
                    IsMaximumEnabled = false;
                    IsTypeEnabled = false;
                    Operation.Threshold = 127;
                    Operation.Maximum = 255;
                    Operation.Type = ThresholdType.Binary;
                    break;
                case 2:
                    IsThresholdEnabled = false;
                    IsMaximumEnabled = true;
                    IsTypeEnabled = false;
                    Operation.Threshold = 254;
                    //Operation.Maximum = 254;
                    Operation.Type = ThresholdType.Binary;
                    break;
            }
        }
    }

    public ToolThresholdControl()
    {
        BaseOperation = new OperationThreshold(SlicerFile!);
        if (!ValidateSpawn()) return; 
        InitializeComponent();
    }
}