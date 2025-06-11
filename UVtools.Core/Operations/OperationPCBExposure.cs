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
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;
using UVtools.Core.Excellon;
using UVtools.Core.Extensions;
using UVtools.Core.FileFormats;
using UVtools.Core.Gerber;
using UVtools.Core.Layers;
using UVtools.Core.Objects;
using ZLinq;

namespace UVtools.Core.Operations;


#pragma warning disable CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
public class OperationPCBExposure : Operation
#pragma warning restore CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
{
    #region Sub Classes

    public sealed class PCBExposureFile : GenericFileRepresentation
    {
        private bool _invertPolarity;

        private double _sizeScale = 1;

        /// <summary>
        /// Gets or sets to invert the polarity when drawing
        /// </summary>
        public bool InvertPolarity
        {
            get => _invertPolarity;
            set => RaiseAndSetIfChanged(ref _invertPolarity, value);
        }

        /// <summary>
        /// Gets or sets the scale to apply to each shape drawing size.
        /// Positions and vectors aren't affected by this.
        /// </summary>
        public double SizeScale
        {
            get => _sizeScale;
            set => RaiseAndSetIfChanged(ref _sizeScale, Math.Max(0.001, Math.Round(value, 4)));
        }

        public PCBExposureFile() { }

        public PCBExposureFile(string filePath, bool invertPolarity = false) : base(filePath)
        {
            _invertPolarity = invertPolarity;
        }
    }
    #endregion

    #region Static

    public static string[] ValidExtensions =>
    [
        "gbr", // Gerber
        "gko", // Board outline layer
        "gtl", // Top layer
        "gto", // Top silkscreen layer
        "gts", // Top solder mask layer
        "gbl", // Bottom layer
        "gbo", // Bottom silkscreen layer
        "gbs", // Bottom solder mask layer
        "gml", // Mechanical layer
        "drl", // Drill holes
        "xln"  // Eagle drill holes
    ];
    #endregion

    #region Members
    private RangeObservableCollection<PCBExposureFile> _files = [];

    private bool _mergeFiles;
    private decimal _layerHeight;
    private decimal _exposureTime;
    private MidpointRoundingType _sizeMidpointRounding = MidpointRoundingType.AwayFromZero;
    private decimal _offsetX;
    private decimal _offsetY;
    private bool _mirror;
    private bool _invertColor;
    private bool _enableAntiAliasing;


    #endregion

    #region Overrides

    public override LayerRangeSelection StartLayerRangeSelection => LayerRangeSelection.None;
    public override string IconClass => "fa-solid fa-microchip";
    public override string Title => "PCB exposure";
    public override string Description =>
        "Converts a gerber file to a pixel perfect image given your printer LCD/resolution to exposure the copper traces.\n" +
        "Note: The current opened file will be overwritten with this gerber image, use a dummy or a not needed file.";

    public override string ConfirmationText =>
        "generate the PCB traces?";

    public override string ProgressTitle =>
        "Generating PCB traces";

    public override string ProgressAction => "Tracing";

    public override string? ValidateSpawn()
    {
        if(SlicerFile.DisplayWidth <= 0 || SlicerFile.DisplayHeight <= 0)
        {
            return $"{NotSupportedMessage}\nReason: No display size information is available to calculate the correct pixel pitch, and so, it's unable to produce a pixel perfect image.";
        }

        return null;
    }

    public override string? ValidateInternally()
    {
        var sb = new StringBuilder();
        if (_files.Count == 0)
        {
            sb.AppendLine("Select at least one gerber file");
        }
        else
        {
            foreach (var file in _files)
            {
                if(!file.Exists) sb.AppendLine($"The file {file} does not exists");
            }
        }

        return sb.ToString();
    }

    public override string ToString()
    {
        var result = $"{string.Join(" / ", _files)} [Exposure: {_exposureTime}s] [Rounding: {_sizeMidpointRounding}] [Mirror: {_mirror}] [Invert: {_invertColor}]";
        if (!string.IsNullOrEmpty(ProfileName)) result = $"{ProfileName}: {result}";
        return result;
    }

    public int Count => _files.Count;

    public PCBExposureFile this[int index] => _files[index];

    public override Operation Clone()
    {
        var clone = (OperationPCBExposure)base.Clone();
        clone._files = _files.CloneByXmlSerialization();
        return clone;
    }

    #endregion

    #region Constructor

    public OperationPCBExposure() { }

    public OperationPCBExposure(FileFormat slicerFile) : base(slicerFile)
    {
        if (_layerHeight <= 0) _layerHeight = (decimal)SlicerFile.LayerHeight;
        if (_exposureTime <= 0) _exposureTime = (decimal)SlicerFile.BottomExposureTime;
        //_mirror = SlicerFile.DisplayMirror != FlipDirection.None;
    }

    #endregion

    #region Properties
    public RangeObservableCollection<PCBExposureFile> Files
    {
        get => _files;
        set => RaiseAndSetIfChanged(ref _files, value);
    }

    public uint FileCount => (uint)_files.Count;

    public bool MergeFiles
    {
        get => _mergeFiles;
        set => RaiseAndSetIfChanged(ref _mergeFiles, value);
    }

    public decimal LayerHeight
    {
        get => _layerHeight;
        set => RaiseAndSetIfChanged(ref _layerHeight, Layer.RoundHeight(value));
    }

    public decimal ExposureTime
    {
        get => _exposureTime;
        set => RaiseAndSetIfChanged(ref _exposureTime, Math.Round(Math.Max(0, value), 2));
    }

    public MidpointRoundingType SizeMidpointRounding
    {
        get => _sizeMidpointRounding;
        set => RaiseAndSetIfChanged(ref _sizeMidpointRounding, value);
    }

    public decimal OffsetX
    {
        get => _offsetX;
        set => RaiseAndSetIfChanged(ref _offsetX, value);
    }

    public decimal OffsetY
    {
        get => _offsetY;
        set => RaiseAndSetIfChanged(ref _offsetY, value);
    }

    public bool Mirror
    {
        get => _mirror;
        set => RaiseAndSetIfChanged(ref _mirror, value);
    }

    public bool InvertColor
    {
        get => _invertColor;
        set => RaiseAndSetIfChanged(ref _invertColor, value);
    }

    public bool EnableAntiAliasing
    {
        get => _enableAntiAliasing;
        set => RaiseAndSetIfChanged(ref _enableAntiAliasing, value);
    }

    #endregion

    #region Equality

    protected bool Equals(OperationPCBExposure other)
    {
        return _files.Equals(other._files) && _mergeFiles == other._mergeFiles && _layerHeight == other._layerHeight && _exposureTime == other._exposureTime && _sizeMidpointRounding == other._sizeMidpointRounding && _offsetX == other._offsetX && _offsetY == other._offsetY && _mirror == other._mirror && _invertColor == other._invertColor && _enableAntiAliasing == other._enableAntiAliasing;
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((OperationPCBExposure) obj);
    }

    #endregion

    #region Methods

    public void AddFilesFromZip(string zipFile)
    {
        if (!File.Exists(zipFile) || !zipFile.EndsWith(".zip")) return;
        using var zip = ZipFile.Open(zipFile, ZipArchiveMode.Read);

        var tmpPath = PathExtensions.GetTemporaryDirectory($"{About.Software}.");
        foreach (var entry in zip.Entries)
        {
            if(!ValidExtensions.AsValueEnumerable().Any(extension => entry.Name.EndsWith($".{extension}", StringComparison.OrdinalIgnoreCase))) continue;

            var filePath = entry.ImprovedExtractToFile(tmpPath, false);
            if (!string.IsNullOrEmpty(filePath))
            {
                AddFile(filePath);
            }
        }

    }

    public void AddFile(string filePath, bool handleZipFiles = true)
    {
        if (!File.Exists(filePath)) return;
        if (filePath.EndsWith(".zip"))
        {
            if(handleZipFiles) AddFilesFromZip(filePath);
            return;
        }
        if(!ValidExtensions.AsValueEnumerable().Any(extension => filePath.EndsWith($".{extension}", StringComparison.OrdinalIgnoreCase))) return;
        var file = new PCBExposureFile(filePath);
        if (_files.Contains(file)) return;
        _files.Add(file);
    }

    public void AddFiles(string[] files, bool handleZipFiles = true)
    {
        foreach (var file in files)
        {
            AddFile(file);
        }
    }

    public void Sort() => _files.Sort();

    public Mat GetMat(PCBExposureFile file, bool canMirror = true)
    {
        var mat = SlicerFile.CreateMat();
        DrawMat(file, mat, canMirror);
        return mat;
    }

    public void DrawMat(PCBExposureFile file, Mat mat, bool canMirror = true)
    {
        if (!file.Exists) return;


        if (ExcellonDrillFormat.Extensions.AsValueEnumerable().Any(file.IsExtension))
        {
            ExcellonDrillFormat.ParseAndDraw(file, mat, SlicerFile.Ppmm, _sizeMidpointRounding, new SizeF((float)OffsetX, (float)OffsetY), _enableAntiAliasing);
        }
        else
        {
            GerberFormat.ParseAndDraw(file, mat, SlicerFile.Ppmm, _sizeMidpointRounding, new SizeF((float)OffsetX, (float)OffsetY), _enableAntiAliasing);
        }

        //var boundingRectangle = CvInvoke.BoundingRectangle(mat);
        //var cropped = mat.Roi(new Size(boundingRectangle.Right, boundingRectangle.Bottom));
        var cropped = mat.CropByBounds();

        if (_invertColor) CvInvoke.BitwiseNot(cropped, cropped);
        if (_mirror && canMirror)
        {
            var flip = SlicerFile.DisplayMirror;
            if (flip == FlipDirection.None) flip = FlipDirection.Horizontally;
            CvInvoke.Flip(mat, mat, (FlipType)flip);
        }

        return;
    }

    protected override bool ExecuteInternally(OperationProgress progress)
    {
        if (_files.Count == 0) return false;
        var layers = new List<Layer>();
        using var mergeMat = SlicerFile.CreateMat();
        progress.ItemCount = FileCount;

        //var orderFiles = _files.OrderBy(file => file.IsExtension(".drl") || file.IsExtension(".xln")).ToArray();
        var orderFiles = _files.AsValueEnumerable().OrderBy(file => ExcellonDrillFormat.Extensions.AsValueEnumerable().Any(file.IsExtension)).ToArray();

        for (var i = 0; i < orderFiles.Length; i++)
        {
            DrawMat(orderFiles[i], mergeMat, false);
            if (!_mergeFiles)
            {
                using var mat = GetMat(orderFiles[i]);
                if (CvInvoke.HasNonZero(mat))
                {
                    layers.Add(new Layer(mat, SlicerFile));
                }
            }

            progress++;
        }

        if (_mergeFiles)
        {
            if (CvInvoke.HasNonZero(mergeMat))
            {
                if (_mirror)
                {
                    var flip = SlicerFile.DisplayMirror;
                    if (flip == FlipDirection.None) flip = FlipDirection.Horizontally;
                    CvInvoke.Flip(mergeMat, mergeMat, (FlipType)flip);
                }
                layers.Add(new Layer(mergeMat, SlicerFile));
            }
        }

        SlicerFile.SuppressRebuildPropertiesWork(() =>
        {
            SlicerFile.LayerHeight = (float) _layerHeight;
            SlicerFile.TransitionLayerCount = 0;
            SlicerFile.BottomLayerCount = 1;
            SlicerFile.BottomExposureTime = (float) _exposureTime;
            SlicerFile.ExposureTime = (float)_exposureTime;
            SlicerFile.BottomLiftHeightTotal = 0;
            SlicerFile.LiftHeightTotal = 0;
            /*SlicerFile.BottomLiftSpeed = 300;
            SlicerFile.BottomLiftSpeed2 = 300;
            SlicerFile.LiftSpeed = 300;
            SlicerFile.LiftSpeed2 = 300;*/
            SlicerFile.SetNoDelays();

            SlicerFile.Layers = layers.ToArray();
        }, true);

        if (_mirror) // Reposition layers
        {
            using var op = new OperationMove(SlicerFile, Anchor.TopLeft)
            {
                MarginLeft = SlicerFile.BoundingRectangle.X,
                MarginTop = SlicerFile.BoundingRectangle.Y,
            };

            var flip = SlicerFile.DisplayMirror;
            if (flip == FlipDirection.None) flip = FlipDirection.Horizontally;
            switch (flip)
            {
                case FlipDirection.Horizontally:
                    op.MarginLeft = (int)SlicerFile.ResolutionX - SlicerFile.BoundingRectangle.Right;
                    break;
                case FlipDirection.Vertically:
                    op.MarginTop = (int)SlicerFile.ResolutionY - SlicerFile.BoundingRectangle.Bottom;
                    break;
                case FlipDirection.Both:
                    op.MarginLeft = (int)SlicerFile.ResolutionX - SlicerFile.BoundingRectangle.Right;
                    op.MarginTop = (int)SlicerFile.ResolutionY - SlicerFile.BoundingRectangle.Bottom;
                    break;
            }

            op.Execute(progress);
        }

        using var croppedMat = mergeMat.CropByBounds(20);
        using var bgrMat = new Mat();
        CvInvoke.CvtColor(croppedMat, bgrMat, ColorConversion.Gray2Bgr);
        SlicerFile.SetThumbnails(bgrMat);

        return !progress.Token.IsCancellationRequested;
    }


    #endregion
}