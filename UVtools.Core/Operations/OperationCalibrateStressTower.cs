﻿/*
 *                     GNU AFFERO GENERAL PUBLIC LICENSE
 *                       Version 3, 19 November 2007
 *  Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
 *  Everyone is permitted to copy and distribute verbatim copies
 *  of this license document, but changing it is not allowed.
 */

using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using UVtools.Core.Extensions;
using UVtools.Core.FileFormats;
using UVtools.Core.Layers;

namespace UVtools.Core.Operations;


#pragma warning disable CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
public sealed class OperationCalibrateStressTower : Operation
#pragma warning restore CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
{
    #region Members
    private decimal _displayWidth;
    private decimal _displayHeight;
    private decimal _layerHeight;
    private ushort _bottomLayers;
    private decimal _bottomExposure;
    private decimal _normalExposure;
    private decimal _baseDiameter = 30;
    private decimal _baseHeight = 3;
    private decimal _bodyHeight = 50;
    private decimal _ceilHeight = 3;
    private byte _chamferLayers = 6;
    private bool _enableAntiAliasing = true;
    private bool _mirrorOutput;
    private byte _spirals = 2;
    private decimal _spiralDiameter = 2;
    private SpiralDirections _spiralDirection = SpiralDirections.Both;
    private decimal _spiralAngleStepPerLayer = 1;

    #endregion

    #region Overrides

    public override bool CanROI => false;

    public override LayerRangeSelection StartLayerRangeSelection => LayerRangeSelection.None;
    public override string IconClass => "fa-solid fa-chess-rook";
    public override string Title => "Stress tower";
    public override string Description =>
        "Generates a stress tower to test the printer capabilities.\n" +
        "Note: The current opened file will be overwritten with this test, use a dummy or a not needed file.";

    public override string ConfirmationText =>
        $"generate the stress tower?";

    public override string ProgressTitle =>
        $"Generating the stress tower";

    public override string ProgressAction => "Generated";

    public override string? ValidateInternally()
    {
        var sb = new StringBuilder();

        if (_displayWidth <= 0)
        {
            sb.AppendLine("Display width must be a positive value.");
        }

        if (_displayHeight <= 0)
        {
            sb.AppendLine("Display height must be a positive value.");
        }
           
        return sb.ToString();
    }

    public override string ToString()
    {
        var result = $"[Layer Height: {_layerHeight}] " +
                     $"[Bottom layers: {_bottomLayers}] " +
                     $"[Exposure: {_bottomExposure}/{_normalExposure}] " +
                     $"[Base: H:{_baseHeight} D:{_baseDiameter}] " +
                     $"[Ceil: {_ceilHeight}] [Body: {_bodyHeight}] " +
                     $"[Chamfer: {_chamferLayers}] " +
                     $"[Spirals: {_spirals} Dir: {_spiralDirection} D:{_spiralDiameter} Angle: {_spiralAngleStepPerLayer}º]" +
                     $"[AA: {_enableAntiAliasing}] [Mirror: {_mirrorOutput}]";
        if (!string.IsNullOrEmpty(ProfileName)) result = $"{ProfileName}: {result}";
        return result;
    }

    #endregion

    #region Constructor

    public OperationCalibrateStressTower() { }

    public OperationCalibrateStressTower(FileFormat slicerFile) : base(slicerFile)
    { }

    public override void InitWithSlicerFile()
    {
        base.InitWithSlicerFile();
        if(_layerHeight <= 0) _layerHeight = (decimal)SlicerFile.LayerHeight;
        if(_bottomLayers <= 0) _bottomLayers = SlicerFile.BottomLayerCount;
        if(_bottomExposure <= 0) _bottomExposure = (decimal)SlicerFile.BottomExposureTime;
        if(_normalExposure <= 0) _normalExposure = (decimal)SlicerFile.ExposureTime;
        _mirrorOutput = SlicerFile.DisplayMirror != FlipDirection.None;

        if (SlicerFile.DisplayWidth > 0)
            DisplayWidth = (decimal)SlicerFile.DisplayWidth;
        if (SlicerFile.DisplayHeight > 0)
            DisplayHeight = (decimal)SlicerFile.DisplayHeight;
    }

    #endregion

    #region Properties

    public decimal DisplayWidth
    {
        get => _displayWidth;
        set
        {
            if(!RaiseAndSetIfChanged(ref _displayWidth, FileFormat.RoundDisplaySize(value))) return;
        }
    }

    public decimal DisplayHeight
    {
        get => _displayHeight;
        set
        {
            if(!RaiseAndSetIfChanged(ref _displayHeight, FileFormat.RoundDisplaySize(value))) return;
        }
    }

    public decimal LayerHeight
    {
        get => _layerHeight;
        set
        {
            if(!RaiseAndSetIfChanged(ref _layerHeight, Layer.RoundHeight(value))) return;
            RaisePropertyChanged(nameof(BottomLayersMM));
            RaisePropertyChanged(nameof(LayerCount));
        }
    }

    public ushort Microns => (ushort)(LayerHeight * 1000);

    public ushort BottomLayers
    {
        get => _bottomLayers;
        set
        {
            if(!RaiseAndSetIfChanged(ref _bottomLayers, value)) return;
            RaisePropertyChanged(nameof(BottomLayersMM));
        }
    }

    public decimal BottomLayersMM => Layer.RoundHeight(LayerHeight * BottomLayers);

    public decimal BottomExposure
    {
        get => _bottomExposure;
        set => RaiseAndSetIfChanged(ref _bottomExposure, Math.Round(value, 2));
    }

    public decimal NormalExposure
    {
        get => _normalExposure;
        set => RaiseAndSetIfChanged(ref _normalExposure, Math.Round(value, 2));
    }

    public uint LayerCount => (uint)((_baseHeight + _bodyHeight + _ceilHeight) / LayerHeight);

    public decimal TotalHeight => _baseHeight + _bodyHeight + _ceilHeight;

    public decimal BaseDiameter
    {
        get => _baseDiameter;
        set => RaiseAndSetIfChanged(ref _baseDiameter, value);
    }

    public decimal BaseHeight
    {
        get => _baseHeight;
        set
        {
            if(!RaiseAndSetIfChanged(ref _baseHeight, value)) return;
            RaisePropertyChanged(nameof(TotalHeight));
        }
    }

    public decimal BodyHeight
    {
        get => _bodyHeight;
        set
        {
            if (!RaiseAndSetIfChanged(ref _bodyHeight, value)) return;
            RaisePropertyChanged(nameof(TotalHeight));
        }
    }

    public decimal CeilHeight
    {
        get => _ceilHeight;
        set
        {
            if(!RaiseAndSetIfChanged(ref _ceilHeight, value)) return;
            RaisePropertyChanged(nameof(TotalHeight));
        }
    }

    public byte ChamferLayers
    {
        get => _chamferLayers;
        set => RaiseAndSetIfChanged(ref _chamferLayers, value);
    }

    public bool EnableAntiAliasing
    {
        get => _enableAntiAliasing;
        set => RaiseAndSetIfChanged(ref _enableAntiAliasing, value);
    }

    public bool MirrorOutput
    {
        get => _mirrorOutput;
        set => RaiseAndSetIfChanged(ref _mirrorOutput, value);
    }

    public byte Spirals
    {
        get => _spirals;
        set => RaiseAndSetIfChanged(ref _spirals, value);
    }

    public decimal SpiralDiameter
    {
        get => _spiralDiameter;
        set => RaiseAndSetIfChanged(ref _spiralDiameter, value);
    }

    public SpiralDirections SpiralDirection
    {
        get => _spiralDirection;
        set => RaiseAndSetIfChanged(ref _spiralDirection, value);
    }

    public decimal SpiralAngleStepPerLayer
    {
        get => _spiralAngleStepPerLayer;
        set => RaiseAndSetIfChanged(ref _spiralAngleStepPerLayer, value);
    }

    #endregion

    #region Enums

    public enum SpiralDirections : byte
    {
        Clockwise,
        Alternate,
        Both
    }

    public static Array SpiralDirectionsItems => Enum.GetValues(typeof(SpiralDirections));
    #endregion

    #region Equality

    private bool Equals(OperationCalibrateStressTower other)
    {
        return _layerHeight == other._layerHeight && _bottomLayers == other._bottomLayers && _bottomExposure == other._bottomExposure && _normalExposure == other._normalExposure && _baseDiameter == other._baseDiameter && _baseHeight == other._baseHeight && _bodyHeight == other._bodyHeight && _ceilHeight == other._ceilHeight && _chamferLayers == other._chamferLayers && _enableAntiAliasing == other._enableAntiAliasing && _mirrorOutput == other._mirrorOutput && _spirals == other._spirals && _spiralDiameter == other._spiralDiameter && _spiralDirection == other._spiralDirection && _spiralAngleStepPerLayer == other._spiralAngleStepPerLayer;
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || obj is OperationCalibrateStressTower other && Equals(other);
    }

    #endregion

    #region Methods
    public Mat GetThumbnail()
    {
        Mat thumbnail = EmguExtensions.InitMat(new Size(400, 200), 3);
        var fontFace = FontFace.HersheyDuplex;
        var fontScale = 1;
        var fontThickness = 2;
        const byte xSpacing = 45;
        const byte ySpacing = 45;
        CvInvoke.PutText(thumbnail, "UVtools", new Point(140, 35), fontFace, fontScale, new MCvScalar(255, 27, 245), fontThickness + 1);
        CvInvoke.Line(thumbnail, new Point(xSpacing, 0), new Point(xSpacing, ySpacing + 5), new MCvScalar(255, 27, 245), 3);
        CvInvoke.Line(thumbnail, new Point(xSpacing, ySpacing + 5), new Point(thumbnail.Width - xSpacing, ySpacing + 5), new MCvScalar(255, 27, 245), 3);
        CvInvoke.Line(thumbnail, new Point(thumbnail.Width - xSpacing, 0), new Point(thumbnail.Width - xSpacing, ySpacing + 5), new MCvScalar(255, 27, 245), 3);
        CvInvoke.PutText(thumbnail, "Stress Tower", new Point(xSpacing, ySpacing * 2), fontFace, fontScale, new MCvScalar(0, 255, 255), fontThickness);
        CvInvoke.PutText(thumbnail, $"{Microns}um @ {BottomExposure}s/{NormalExposure}s", new Point(xSpacing, ySpacing * 3), fontFace, fontScale, EmguExtensions.WhiteColor, fontThickness);
        CvInvoke.PutText(thumbnail, $"{_spirals} Spirals @ {_spiralAngleStepPerLayer}deg", new Point(xSpacing, ySpacing * 4), fontFace, fontScale, EmguExtensions.WhiteColor, fontThickness);
        return thumbnail;
    }

    protected override bool ExecuteInternally(OperationProgress progress)
    {
        progress.ItemCount = LayerCount;

        Slicer.Slicer slicer = new(SlicerFile.Resolution, new SizeF((float)DisplayWidth, (float)DisplayHeight));
        Point center = new(SlicerFile.Resolution.Width / 2, SlicerFile.Resolution.Height / 2);
        uint baseRadius = slicer.PixelsFromMillimeters(_baseDiameter) / 2;
        uint baseLayers = (ushort)(_baseHeight / _layerHeight);
        uint bodyLayers = (ushort)(_bodyHeight / _layerHeight);
        uint spiralLayers = (uint)(_spiralDiameter / _layerHeight);
        uint ceilLayers = (ushort)(_ceilHeight / _layerHeight);

        uint basePlusBodyLayers = baseLayers + bodyLayers;

        decimal spiralOffsetAngle = 360m / _spirals;
        uint spiralRadius = slicer.PixelsFromMillimeters(_spiralDiameter) / 2;

        var flip = SlicerFile.DisplayMirror;
        if (flip == FlipDirection.None) flip = FlipDirection.Horizontally;

        /*const FontFace fontFace = FontFace.HersheyDuplex;
        const double fontScale = 1;
        const byte fontThickness = 2;
        LineType lineType = _enableAntiAliasing ? LineType.AntiAlias : LineType.EightConnected;

        var kernel = CvInvoke.GetStructuringElement(ElementShape.Rectangle, new Size(3, 3), EmguExtensions.AnchorCenter);*/
        SlicerFile.Init(LayerCount);

        Parallel.For(0, LayerCount, CoreSettings.GetParallelOptions(progress), layerIndex =>
        {
            progress.PauseIfRequested();
            using var mat = SlicerFile.CreateMat();

            if (layerIndex < baseLayers)
            {
                int chamferOffset = (int)Math.Max(0, _chamferLayers - layerIndex);
                CvInvoke.Circle(mat, center, (int)baseRadius - chamferOffset, EmguExtensions.WhiteColor, -1, _enableAntiAliasing ? LineType.AntiAlias : LineType.EightConnected);
            }
            else if (layerIndex < basePlusBodyLayers)
            {
                decimal angle = (layerIndex * _spiralAngleStepPerLayer) % 360m;

                for (byte spiral = 0; spiral < _spirals; spiral++)
                {
                    decimal spiralAngle = (spiralOffsetAngle * spiral + angle) % 360;
                    if (_spiralDirection == SpiralDirections.Alternate && spiral % 2 == 0)
                    {
                        spiralAngle = -spiralAngle;
                    }
                    Point location = center with { X = (int)(center.X - baseRadius + spiralRadius) };
                    var locationCW = location.Rotate((double)spiralAngle, center);
                    var locationCCW = location.Rotate((double)-spiralAngle, center);

                    uint maxLayer = (uint)Math.Min(layerIndex + spiralLayers, baseLayers + bodyLayers);

                    //for (uint spiralLayerIndex = (uint)layerIndex; spiralLayerIndex < maxLayer; spiralLayerIndex++)
                    //{

                    CvInvoke.Circle(mat, locationCW, (int)spiralRadius, EmguExtensions.WhiteColor, -1, _enableAntiAliasing ? LineType.AntiAlias : LineType.EightConnected);
                    if (_spiralDirection == SpiralDirections.Both)
                    {
                        spiralAngle = -spiralAngle;
                        CvInvoke.Circle(mat, locationCCW, (int)spiralRadius, EmguExtensions.WhiteColor, -1, _enableAntiAliasing ? LineType.AntiAlias : LineType.EightConnected);
                    }
                    //}
                }
            }
            else
            {
                CvInvoke.Circle(mat, center, (int)baseRadius, EmguExtensions.WhiteColor, -1, _enableAntiAliasing ? LineType.AntiAlias : LineType.EightConnected);
            }

            if (_mirrorOutput) CvInvoke.Flip(mat, mat, (FlipType)flip);

            SlicerFile[layerIndex] = new Layer((uint)layerIndex, mat, SlicerFile);
            progress.LockAndIncrement();
        });

        if (SlicerFile.ThumbnailsCount > 0)
        {
            using var thumbnail = GetThumbnail();
            SlicerFile.SetThumbnails(thumbnail);
        }

        SlicerFile.SuppressRebuildPropertiesWork(() =>
        {
            SlicerFile.LayerHeight = (float)LayerHeight;
            SlicerFile.BottomExposureTime = (float)BottomExposure;
            SlicerFile.ExposureTime = (float)NormalExposure;
            SlicerFile.BottomLayerCount = BottomLayers;
        }, true);
            
        return !progress.Token.IsCancellationRequested;
    }

    #endregion
}