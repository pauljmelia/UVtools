﻿/*
 *                     GNU AFFERO GENERAL PUBLIC LICENSE
 *                       Version 3, 19 November 2007
 *  Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
 *  Everyone is permitted to copy and distribute verbatim copies
 *  of this license document, but changing it is not allowed.
 */

using Emgu.CV;
using Emgu.CV.CvEnum;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Xml.Serialization;
using UVtools.Core.Extensions;
using UVtools.Core.FileFormats;
using UVtools.Core.Layers;
using UVtools.Core.Managers;
using UVtools.Core.Objects;

namespace UVtools.Core.Operations;


#pragma warning disable CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
public sealed class OperationDynamicLayerHeight : Operation
#pragma warning restore CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
{
    #region Sub Classes
    public sealed class Report
    {
        public uint OldLayerCount { get; set; }

        public uint NewLayerCount { get; set; }

        public uint StackedLayers { get; set; }

        public uint ReusedLayers => OldLayerCount - StackedLayers;

        public float MaximumLayerHeight { get; set; }

        public float OldPrintTime { get; set; }

        public float NewPrintTime { get; set; }

        public double SparedPrintTime => Math.Round(OldPrintTime - NewPrintTime, 2);

        public double CompressionRatio => Math.Round((double)OldLayerCount / NewLayerCount * 100.0, 2);

        public override string ToString()
        {
            var oldTime = TimeSpan.FromSeconds(OldPrintTime);
            var newTime = TimeSpan.FromSeconds(NewPrintTime);
            var sparedTime = TimeSpan.FromSeconds(SparedPrintTime);
            return
                $"From {OldLayerCount} layers, {ReusedLayers} got reused, {StackedLayers} got stacked and optimized with dynamic layer height's\n" +
                $"Resultant layers: {NewLayerCount}\n" +
                $"Compression ratio: {CompressionRatio}%\n" +
                $"Maximum layer height reached: {MaximumLayerHeight}mm\n" +
                $"Print time: {oldTime.Hours}h{oldTime.Minutes}m{oldTime.Seconds}s -> {newTime.Hours}h{newTime.Minutes}m{newTime.Seconds}s (- {sparedTime.Hours}h{sparedTime.Minutes}m{sparedTime.Seconds}s)";
        }
    }

    #endregion

    #region Constants
    public const byte ObjectsPerCache = 2;
    #endregion

    #region Members

    private decimal _cacheRamSize = 1.5m;
    private decimal _minimumLayerHeight = 0.03m;
    private decimal _maximumLayerHeight = 0.10m;
    private bool _stripAntiAliasing;
    private bool _reconstructAntiAliasing;
    private byte _maximumErodes = 10;

    private ExposureSetTypes _exposureSetType = ExposureSetTypes.Linear;
    private bool _iterateBottomExposureTime;
    private decimal _bottomExposureTime;
    private decimal _exposureTime;
    private decimal _bottomExposureStep = 0.5m;
    private decimal _exposureStep = 0.2m;
    private RangeObservableCollection<ExposureItem> _automaticExposureTable = [];
    private RangeObservableCollection<ExposureItem> _manualExposureTable = [];

    #endregion

    #region Overrides

    public override bool CanROI => false;
    public override string IconClass => "mdi-format-line-style";
    public override string Title => "Dynamic layer height";

    public override string Description =>
        "Analyze and optimize the model with dynamic layer heights, larger angles will slice at lower layer height" +
        " while more straight angles will slice larger layer height.\n" +
        "Note: The model should be sliced at the lowest layer height possible (0.01mm).\n" +
        "After this, do not apply any modification which reconstruct the z positions of the layers. " +
        "Only few printers support this, make sure your is supported or else it will print a malformed model.";

    public override string ConfirmationText =>
        $"dynamic layers from layers {LayerIndexStart} through {LayerIndexEnd}?";

    public override string ProgressTitle =>
        $"Analyzing and optimizing layers height from layers {LayerIndexStart} through {LayerIndexEnd}";

    public override string ProgressAction => "Processed layers";

    public override string? ValidateSpawn()
    {
        if (!SlicerFile.CanUseLayerPositionZ || !SlicerFile.CanUseLayerExposureTime)
        {
            return NotSupportedMessage;
        }

        if (SlicerFile.LayerHeight * 2 > Layer.MaximumHeightFloat)
        {
            return $"This file already uses the maximum layer height possible ({SlicerFile.LayerHeight}mm).\n" +
                   "Layers can not be stacked, please re-slice your file with the lowest layer height of 0.01mm.";
        }

        for (uint layerIndex = 1; layerIndex < SlicerFile.LayerCount; layerIndex++)
        {
            if ((decimal)Layer.RoundHeight(SlicerFile[layerIndex].PositionZ - SlicerFile[layerIndex - 1].PositionZ) ==
                (decimal)SlicerFile.LayerHeight) continue;
            return $"This file contain layer(s) with modified positions, starting at layer {layerIndex}.\n" +
                   $"This tool requires sequential layers with equal height.\n" +
                   $"If you ran this tool before, you can't run again.";
        }

        return null;
    }

    public override string? ValidateInternally()
    {
        var sb = new StringBuilder();

        /*if (XYResolutionUm <= 0)
        {
            sb.AppendLine($"Display width and height must be a positive value.");
        }*/

        decimal layerHeight = (decimal) SlicerFile.LayerHeight;
        if (_minimumLayerHeight < layerHeight)
        {
            sb.AppendLine(
                $"Minimum layer height ({_minimumLayerHeight}mm) must be equal or higher than file layer height ({layerHeight}mm)");
        }
        if (_minimumLayerHeight > _maximumLayerHeight)
        {
            sb.AppendLine(
                $"Minimum layer height ({_minimumLayerHeight}mm) can't be higher than maximum layer height ({_maximumLayerHeight}mm)");
        }
        if (layerHeight >= _maximumLayerHeight)
        {
            sb.AppendLine(
                $"Maximum layer height ({_maximumLayerHeight}mm) can't be the same or less than current file layer height ({SlicerFile.LayerHeight}mm)");
        }

        var exposureTable = ExposureTableDictionary;

        for (layerHeight = (decimal) SlicerFile.LayerHeight;
             layerHeight <= _maximumLayerHeight;
             layerHeight += (decimal) SlicerFile.LayerHeight)
        {
            layerHeight = Layer.RoundHeight(layerHeight);
            if (exposureTable.TryGetValue(layerHeight, out var exposure))
            {
                if (exposure.BottomExposure <= 0 || exposure.Exposure <= 0)
                {
                    sb.AppendLine($"Layer height {layerHeight}mm exposures must be a positive value, current: {exposure.BottomExposure}s/{exposure.Exposure}s");
                }
            }
            else
            {
                sb.AppendLine($"Layer height {layerHeight}mm exposures are missing.");
            }
        }

        return sb.ToString();
    }

    public override string ToString()
    {
        var result = $"[RAM: {_cacheRamSize}Gb] " +
                     $"[Layer Height: Min: {_minimumLayerHeight}mm Max: {_maximumLayerHeight}mm] " +
                     $"[Strip AA: {_stripAntiAliasing} Reconstruct AA: {_reconstructAntiAliasing}] " +
                     $"[Difference: {_maximumErodes}px] " +
                     $"[Bottom Exposure: {_bottomExposureTime}s Normal Exposure: {_exposureTime}s] " +
                     $"[Exposure type: {_exposureSetType}, Steps: {_bottomExposureStep}s/{_exposureStep}s]" + LayerRangeString;
        if (!string.IsNullOrEmpty(ProfileName)) result = $"{ProfileName}: {result}";
        return result;
    }


    #endregion

    #region Enums

    public enum ExposureSetTypes: byte
    {
        Linear,
        Multiplier,
        Manual
    }

    public static Array ExposureSetTypeItems => Enum.GetValues(typeof(ExposureSetTypes));

    #endregion

    #region Properties

    public decimal CacheRAMSize
    {
        get => _cacheRamSize;
        set
        {
            if (!RaiseAndSetIfChanged(ref _cacheRamSize, Math.Round(value, 2))) return;
            RaisePropertyChanged(nameof(CacheObjectCount));
        }
    }

    public uint CacheObjectCount => (uint)(_cacheRamSize * 1000000000L / SlicerFile.Resolution.Area() / ObjectsPerCache);

    public decimal MinimumLayerHeight
    {
        get => _minimumLayerHeight;
        set
        {
            if (!RaiseAndSetIfChanged(ref _minimumLayerHeight, Layer.RoundHeight(value))) return;
            //RaisePropertyChanged(nameof(ExposureData));
            //if (!IsExposureSetTypeManual) RebuildAutoExposureTable();
        }
    }

    public decimal MaximumLayerHeight
    {
        get => _maximumLayerHeight;
        set
        {
            if(!RaiseAndSetIfChanged(ref _maximumLayerHeight, Layer.RoundHeight(value))) return;
            //RaisePropertyChanged(nameof(ExposureData));
            if(!IsExposureSetTypeManual) RebuildAutoExposureTable();
        }
    }

    public bool StripAntiAliasing
    {
        get => _stripAntiAliasing;
        set => RaiseAndSetIfChanged(ref _stripAntiAliasing, value);
    }

    public bool ReconstructAntiAliasing
    {
        get => _reconstructAntiAliasing;
        set => RaiseAndSetIfChanged(ref _reconstructAntiAliasing, value);
    }

    public byte MaximumErodes
    {
        get => _maximumErodes;
        set => RaiseAndSetIfChanged(ref _maximumErodes, value);
    }

    public ExposureSetTypes ExposureSetType
    {
        get => _exposureSetType;
        set
        {
            if(!RaiseAndSetIfChanged(ref _exposureSetType, value)) return;
            RaisePropertyChanged(nameof(IsExposureSetTypeManual));
            RaisePropertyChanged(nameof(ExposureTable));
            RaisePropertyChanged(nameof(ExposureTableDictionary));
            //RaisePropertyChanged(nameof(ExposureData));
            if (!IsExposureSetTypeManual) RebuildAutoExposureTable();
        }
    }

    public bool IsExposureSetTypeManual => _exposureSetType == ExposureSetTypes.Manual;

    public bool IterateBottomExposureTime
    {
        get => _iterateBottomExposureTime;
        set
        {
            if(!RaiseAndSetIfChanged(ref _iterateBottomExposureTime, value)) return;
            if (!IsExposureSetTypeManual) RebuildAutoExposureTable();
        }
    }

    public decimal BottomExposureTime
    {
        get => _bottomExposureTime;
        set
        {
            if(!RaiseAndSetIfChanged(ref _bottomExposureTime, value)) return;
            if (!IsExposureSetTypeManual) RebuildAutoExposureTable();
        }
    }

    public decimal ExposureTime
    {
        get => _exposureTime;
        set
        {
            if(!RaiseAndSetIfChanged(ref _exposureTime, value)) return;
            if (!IsExposureSetTypeManual) RebuildAutoExposureTable();
        }
            
    }

    public decimal BottomExposureStep
    {
        get => _bottomExposureStep;
        set
        {
            if(!RaiseAndSetIfChanged(ref _bottomExposureStep, value)) return;
            //RaisePropertyChanged(nameof(ExposureData));
            if (!IsExposureSetTypeManual) RebuildAutoExposureTable();
        }
    }

    public decimal ExposureStep
    {
        get => _exposureStep;
        set
        {
            if(!RaiseAndSetIfChanged(ref _exposureStep, value)) return;
            //RaisePropertyChanged(nameof(ExposureData));
            if (!IsExposureSetTypeManual) RebuildAutoExposureTable();
        }
    }

    [XmlIgnore]
    public RangeObservableCollection<ExposureItem> AutomaticExposureTable
    {
        get
        {
            if(_automaticExposureTable.Count == 0) RebuildAutoExposureTable();
            return _automaticExposureTable;
        }
        set => RaiseAndSetIfChanged(ref _automaticExposureTable, value);
    }

    public RangeObservableCollection<ExposureItem> ManualExposureTable
    {
        get => _manualExposureTable;
        set => RaiseAndSetIfChanged(ref _manualExposureTable, value);
    }

    [XmlIgnore]
    public RangeObservableCollection<ExposureItem> ExposureTable => IsExposureSetTypeManual ? _manualExposureTable : AutomaticExposureTable;

    /// <summary>
    /// Gets the exposure table into a dictionary where key is the layer height
    /// </summary>
    [XmlIgnore]
    public Dictionary<decimal, ExposureItem> ExposureTableDictionary
    {
        get
        {
            Dictionary<decimal, ExposureItem> dictionary = new();
            foreach (var exposure in ExposureTable)
            {
                dictionary.TryAdd(exposure.LayerHeight, exposure);
            }

            return dictionary;
        }
    }

    public string ExposureData
    {
        get
        {
            StringBuilder sb = new();
            byte count = 0;
            for (decimal layerHeight = (decimal) SlicerFile.LayerHeight; layerHeight <= _maximumLayerHeight; layerHeight+= (decimal)SlicerFile.LayerHeight)
            {
                decimal bottomExposure = 0;
                decimal exposure = 0;
                switch (_exposureSetType)
                {
                    case ExposureSetTypes.Linear:
                        bottomExposure = _iterateBottomExposureTime ? _bottomExposureTime + count * _bottomExposureStep : _bottomExposureTime;
                        exposure = _exposureTime + count * _exposureStep;
                        break;
                    case ExposureSetTypes.Multiplier:
                        bottomExposure = _iterateBottomExposureTime ? _bottomExposureTime + _bottomExposureTime * count * layerHeight * _bottomExposureStep : _bottomExposureTime;
                        exposure = _exposureTime + _exposureTime * count * layerHeight * _exposureStep;
                        break;
                    case ExposureSetTypes.Manual:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                sb.AppendLine($"{layerHeight:F2}mm: {bottomExposure:F2}s / {exposure:F2}s");
                count++;
            }
            return sb.ToString();
        }
    }

    #endregion

    #region Constructor

    public OperationDynamicLayerHeight()
    {
        //InitManualTable();
    }

    public OperationDynamicLayerHeight(FileFormat slicerFile) : base(slicerFile)
    {
        InitManualTable();
    }

    public override void InitWithSlicerFile()
    {
        base.InitWithSlicerFile();

        var layerHeight = (decimal)SlicerFile.LayerHeight;
        if (_minimumLayerHeight < layerHeight)
        {
            _minimumLayerHeight = layerHeight;
        }
        if (layerHeight * 2 > _maximumLayerHeight)
        {
            _maximumLayerHeight = Math.Min(Layer.MaximumHeight, _maximumLayerHeight*2);
        }
        if (_bottomExposureTime <= 0)
            _bottomExposureTime = (decimal)SlicerFile.BottomExposureTime;
        if (_exposureTime <= 0)
            _exposureTime = (decimal)SlicerFile.ExposureTime;

    }

    public void InitManualTable()
    {
        for (decimal layerHeight = Layer.MinimumHeight;
             layerHeight <= Layer.MaximumHeight;
             layerHeight += Layer.MinimumHeight)
        {
            var item = new ExposureItem(layerHeight, _bottomExposureTime, _exposureTime);
            //item.BottomExposure = _bottomExposureTime;
            //item.Exposure = _exposureTime;
            /*if (layerHeight == (decimal) SlicerFile.LayerHeight)
            {
                
            }*/
            _manualExposureTable.Add(item);
        }
    }

    #endregion

    #region Equality

    private bool Equals(OperationDynamicLayerHeight other)
    {
        return _cacheRamSize == other._cacheRamSize && _minimumLayerHeight == other._minimumLayerHeight && _maximumLayerHeight == other._maximumLayerHeight && _stripAntiAliasing == other._stripAntiAliasing && _reconstructAntiAliasing == other._reconstructAntiAliasing && _maximumErodes == other._maximumErodes && _exposureSetType == other._exposureSetType && _iterateBottomExposureTime == other._iterateBottomExposureTime && _bottomExposureTime == other._bottomExposureTime && _exposureTime == other._exposureTime && _bottomExposureStep == other._bottomExposureStep && _exposureStep == other._exposureStep && Equals(_manualExposureTable, other._manualExposureTable);
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || obj is OperationDynamicLayerHeight other && Equals(other);
    }


    #endregion

    #region Methods

    public void RebuildAutoExposureTable()
    {
        if (SlicerFile is null) return;
        _automaticExposureTable.Clear();
        byte count = 0;
        for (decimal layerHeight = (decimal)SlicerFile.LayerHeight; layerHeight <= _maximumLayerHeight; layerHeight += (decimal)SlicerFile.LayerHeight)
        {
            decimal bottomExposure = 0;
            decimal exposure = 0;
            switch (_exposureSetType)
            {
                case ExposureSetTypes.Linear:
                    bottomExposure = _iterateBottomExposureTime ? _bottomExposureTime + count * _bottomExposureStep : _bottomExposureTime;
                    exposure = _exposureTime + count * _exposureStep;
                    break;
                case ExposureSetTypes.Multiplier:
                    bottomExposure = _iterateBottomExposureTime ? _bottomExposureTime + _bottomExposureTime * count * layerHeight * _bottomExposureStep : _bottomExposureTime;
                    exposure = _exposureTime + _exposureTime * count * layerHeight * _exposureStep;
                    break;
                case ExposureSetTypes.Manual:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            _automaticExposureTable.Add(new ExposureItem(layerHeight, Math.Round(bottomExposure, 2), Math.Round(exposure, 2)));
            count++;
        }
    }

    public void CopyAutomaticTableToManual()
    {
        ManualExposureTable.Clear();
        ManualExposureTable.AddRange(_automaticExposureTable);
        ExposureSetType = ExposureSetTypes.Manual;
    }

    protected override bool ExecuteInternally(OperationProgress progress)
    {
        Report report = new()
        {
            OldLayerCount = SlicerFile.LayerCount,
            OldPrintTime = SlicerFile.PrintTime
        };

        var kernel = EmguExtensions.Kernel3x3Rectangle;

        var matCache = new MatCacheManager(this, (ushort)CacheObjectCount, ObjectsPerCache)
        {
            AutoDispose = true,
            AutoDisposeKeepLast = 1,
            AfterCacheAction = mats =>
            {
                mats[1] = new Mat();
                // Clean AA
                CvInvoke.Threshold(mats[0], mats[1], 127, 255, ThresholdType.Binary);

                if (_stripAntiAliasing)
                {
                    mats[0].Dispose();
                    mats[0] = mats[1];
                }
            }
        };

        List<Layer> layers = [];

        using Mat matXor = new();
        Mat? matXorSum = null;
        Mat? matSum = null;
            
        //float xyResolutionUm = SlicerFile.PixelSizeMicronsMax;
        //if (xyResolutionUm == 0) xyResolutionUm = 35;
        //const double xyRes = 35;
        //var stepAngle = Math.Atan(SlicerFile.LayerHeight*1000 / xyRes) * (180 / Math.PI);
        //byte maximumErodes = (byte) (_maximumLayerHeight * 100 - (decimal) (SlicerFile.LayerHeight * 100f));

        float GetLastPositionZ(float layerHeight) => layers.Count > 0 ? Layer.RoundHeight(layers[^1].PositionZ + layerHeight) : layerHeight;

        void AddNewLayer(Mat mat, float layerHeight)
        {
            if (_stripAntiAliasing && _reconstructAntiAliasing)
            {
                CvInvoke.GaussianBlur(mat, mat, new Size(3, 3), 0);
            }

            report.MaximumLayerHeight = Math.Max(report.MaximumLayerHeight, layerHeight);
            var positionZ = GetLastPositionZ(layerHeight);
            var layer = new Layer((uint) layers.Count, mat, SlicerFile)
            {
                IsModified = true,
                PositionZ = positionZ

            };
            layers.Add(layer);
        }

        void ReUseLayer(uint layerIndex)
        {
            var layer = SlicerFile[layerIndex];
            layer.PositionZ = GetLastPositionZ(SlicerFile.LayerHeight);
            layer.Index = (uint) layers.Count;
            layer.IsModified = true;
            if (_stripAntiAliasing)
            {
                var matThreshold = matCache.Get(layerIndex, 1);
                if (_reconstructAntiAliasing)
                {
                    var blurMat = new Mat();
                    CvInvoke.GaussianBlur(matThreshold, blurMat, new Size(3, 3), 0);
                    layer.LayerMat = blurMat;
                }
                else
                {
                    layer.LayerMat = matThreshold;
                }
            }
            layers.Add(layer);
        }

        for (uint layerIndex = 0; layerIndex < LayerIndexStart; layerIndex++) // Skip layers and re-use layers
        {
            ReUseLayer(layerIndex);
        }

        for (uint layerIndex = LayerIndexStart; layerIndex <= LayerIndexEnd; )
        {
            Debug.WriteLine($"Head Layer: {layerIndex} ({SlicerFile.LayerHeight}mm)");

            if (layerIndex == LayerIndexEnd)
            {
                ReUseLayer(layerIndex);
                break;
            }

            var currentLayerHeight = SlicerFile.LayerHeight;
            byte layerSum = 1;
            byte erodeCount = 0;
            //byte maxErodeCount = 0;
            //double maxSlop = 90;
            matSum?.Dispose();
            matSum = null;
            matXorSum?.Dispose();
            matXorSum = null;

            while (true) // In a stack
            {
                progress.PauseOrCancelIfRequested();
                progress.ProcessedItems = layerIndex - LayerIndexStart;

                if (currentLayerHeight >= (float)_maximumLayerHeight || layerIndex == LayerIndexEnd)
                {
                    // Cant perform any additional stack. Maximum layer height reached!
                    // Break this cycle and restart from same layer
                    matSum ??= matCache.Get(layerIndex, 1).Clone(); // This only happen when layer height is already the maximum supported layer height
                    layerIndex++;
                    break;
                }

                var previousLayerHeight = currentLayerHeight;
                currentLayerHeight = Layer.RoundHeight(currentLayerHeight + SlicerFile.LayerHeight);
                //var currentLayerHeightUm = currentLayerHeight * 1000;

                var (mat1, mat1Threshold) = matCache.Get2(layerIndex);
                var (mat2, mat2Threshold) = matCache.Get2(++layerIndex);

                Debug.Write($"  Stacking layer: {layerIndex} ({currentLayerHeight}mm)");

                matSum ??= mat1.Clone();

                CvInvoke.BitwiseXor(mat1Threshold, mat2Threshold, matXor);
                if (matXorSum is null)
                {
                    matXorSum = matXor.Clone();
                }
                else
                {
                    CvInvoke.Max(matXorSum, matXor, matXorSum);
                }

                //var currentLayerHeigthUm = currentLayerHeight * 1000.0;
                //CvInvoke.Imshow("test", matXorSum);
                //CvInvoke.WaitKey();
                if (CvInvoke.HasNonZero(matXorSum)) // Layers are different
                    //if (!matXorSum.IsZeroed(0, startPos, endPos + 1)) // Layers are different
                {
                    //byte innerErodeCount = 0;
                    bool meetRequirement = false;
                    //using var erodeMatXor = matXorSum.Clone();
                    //Debug.WriteLine($"\n\n{layerIndex} - 0");
                    //CvInvoke.Imshow("Render", erodeMatXor.Roi(SlicerFile.BoundingRectangle));
                    //CvInvoke.WaitKey();
                    while (erodeCount < _maximumErodes)
                    {
                        //innerErodeCount++;
                        erodeCount++;
                        //maxErodeCount = Math.Max(maxErodeCount, erodeCount);

                        /*var slope = Math.Atan(currentLayerHeightUm / (double) (xyResolutionUm * erodeCount)) * (180 / Math.PI);
                        var stepover = Math.Round(currentLayerHeightUm / Math.Tan(slope * (Math.PI / 180)));
                        Debug.Write($" [Slope: {slope:F2} Stepover: {stepover} <= {xyResolutionUm} = {stepover <= xyResolutionUm}]");

                        if (stepover > xyResolutionUm)
                        {
                            break;
                        }*/
                        //Debug.WriteLine($"{layerIndex} - {erodeCount}");
                        CvInvoke.Erode(matXorSum, matXor, kernel, EmguExtensions.AnchorCenter, 1, BorderType.Reflect101, default);
                        //CvInvoke.Imshow("Render", erodeMatXor.Roi(SlicerFile.BoundingRectangle));
                        //CvInvoke.WaitKey();
                        if (!CvInvoke.HasNonZero(matXor))
                            //if (erodeMatXor.IsZeroed(0, startPos, endPos+1)) // Image pixels exhausted and got empty image, can pack and go next
                        {
                            meetRequirement = true;
                            break;
                        } 
                    }

                    //if ((!meetRequirement || erodeCount >= _maximumErodes) && _minimumLayerHeight < (decimal) currentLayerHeight
                    // To many pixels, image still not blank, pack the previous group and start again from current height
                    if (!meetRequirement && _minimumLayerHeight < (decimal) currentLayerHeight)
                    {
                        currentLayerHeight = previousLayerHeight;
                        Debug.WriteLine(string.Empty);
                        break;
                    }

                    if (erodeCount > 0) // Sum only if layers are different from the stack
                    {
                        CvInvoke.Max(matSum, mat2, matSum);
                    }
                }
                else
                {
                    //erodeCount++; // Safe check
                    Debug.Write(" [Equal layer]");
                }

                layerSum++;

                Debug.WriteLine(string.Empty);
            }

            if (layerSum > 1) report.StackedLayers += layerSum;
            Debug.WriteLine($" Packing {layerSum} layers with {currentLayerHeight}mm");
            // Add the result


            var positionZ = GetLastPositionZ(currentLayerHeight);
            if ((decimal)positionZ != (decimal)SlicerFile[layerIndex-1].PositionZ)
            {
                Debug.WriteLine($"{layerIndex}: ({positionZ}mm != {SlicerFile[layerIndex-1].PositionZ}mm) Height mismatch!!");
                throw new InvalidOperationException($"Model height integrity has been violated at layer {layerIndex}/{layers.Count} ({positionZ}mm != {SlicerFile[layerIndex - 1].PositionZ}mm), this operation will not proceed.");
            }
            AddNewLayer(matSum, currentLayerHeight);
        }

        for (uint layerIndex = LayerIndexEnd+1; layerIndex < SlicerFile.LayerCount; layerIndex++) // Add left-overs
        {
            ReUseLayer(layerIndex);
        }

        SlicerFile.SuppressRebuildPropertiesWork(() =>
        {
            SlicerFile.BottomExposureTime = (float)_bottomExposureTime;
            SlicerFile.ExposureTime = (float)_exposureTime;
            SlicerFile.Layers = layers.ToArray();
        }, true, false);

        // Set exposures times per layer
        var exposureDictionary = ExposureTableDictionary;
        for (uint layerIndex = 0; layerIndex < SlicerFile.LayerCount; layerIndex++)
        {
            var layer = SlicerFile[layerIndex];
            var bottomExposure = _bottomExposureTime;
            var exposure = _exposureTime;
            if(exposureDictionary.TryGetValue((decimal)layer.LayerHeight, out var item))
            {
                bottomExposure = item.BottomExposure;
                exposure = item.Exposure;
            }

            layer.ExposureTime = (float)SlicerFile.GetBottomOrNormalValue(layer, bottomExposure, exposure);
        }
        //var layer = slicerFile.LayerManager.Layers[^1];

        /*Debug.WriteLine(layer.ExposureTime);
        Debug.WriteLine(layer.Index);
        Debug.WriteLine(layer.Filename);
        Debug.WriteLine(layer.IsNormalLayer);
        Debug.WriteLine(layer.IsBottomLayer);
        Debug.WriteLine(layer.LiftHeight);
        Debug.WriteLine(layer.LiftSpeed);
        Debug.WriteLine(layer.LightOffDelay);
        Debug.WriteLine(layer.LightPWM);
        Debug.WriteLine(layer.BoundingRectangle);*/
        //Debug.WriteLine(layer.LayerHeight);
        //Debug.WriteLine(layer);
        /*Debug.WriteLine(slicerFile.LayerManager);
        foreach (var layer in slicerFile)
        {
            Debug.WriteLine(layer.Index);
        }*/

        report.NewLayerCount = SlicerFile.LayerCount;
        report.NewPrintTime = SlicerFile.PrintTime;
        AfterCompleteReport = report.ToString();

        return true;
    }

    #endregion
}