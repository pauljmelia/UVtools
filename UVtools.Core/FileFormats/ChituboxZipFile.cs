﻿/*
 *                     GNU AFFERO GENERAL PUBLIC LICENSE
 *                       Version 3, 19 November 2007
 *  Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
 *  Everyone is permitted to copy and distribute verbatim copies
 *  of this license document, but changing it is not allowed.
 */

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using UVtools.Core.Extensions;
using UVtools.Core.GCode;
using UVtools.Core.Layers;
using UVtools.Core.Operations;
using ZLinq;

namespace UVtools.Core.FileFormats;

public sealed class ChituboxZipFile : FileFormat
{
    #region Constants

    public const string GCodeFilename = "run.gcode";

    public static readonly string[] ThumbnailsEntryNames = ["preview.png", "preview_cropping.png"];
    #endregion

    #region Sub Classes

    public class Header
    {
        // ;(****Build and Slicing Parameters****)
        [DisplayName("fileName")] public string Filename { get; set; } = string.Empty;
        [DisplayName("machineType")] public string MachineType { get; set; } = "Default";
        [DisplayName("estimatedPrintTime")] public float EstimatedPrintTime { get; set; }
        [DisplayName("volume")] public float VolumeMl { get; set; }
        [DisplayName("resin")] public string? Resin { get; set; } = "Normal";
        [DisplayName("weight")] public float WeightG { get; set; }
        [DisplayName("price")] public float Price { get; set; }
        [DisplayName("layerHeight")] public float LayerHeight { get; set; }
        [DisplayName("resolutionX")] public uint ResolutionX { get; set; }
        [DisplayName("resolutionY")] public uint ResolutionY { get; set; }
        [DisplayName("machineX")] public float MachineX { get; set; }
        [DisplayName("machineY")] public float MachineY { get; set; }
        [DisplayName("machineZ")] public float MachineZ { get; set; }
        [DisplayName("projectType")] public string ProjectType { get; set; } = "Normal";
        [DisplayName("normalExposureTime")] public float ExposureTime { get; set; } = 7; // 35s
        [DisplayName("bottomLayExposureTime")] public float BottomLayExposureTime { get; set; } = 35; // 35s
        [DisplayName("bottomLayerExposureTime")] public float BottomExposureTime { get; set; } = 35; // 35s
        [DisplayName("normalDropSpeed")] public float RetractSpeed { get; set; } = 150; // 150 mm/m
        [DisplayName("normalLayerLiftSpeed")] public float LiftSpeed { get; set; } = 60; // 60 mm/m
        [DisplayName("normalLayerLiftHeight")] public float LiftHeight { get; set; } = 5; // 5 mm
        [DisplayName("zSlowUpDistance")] public float ZSlowUpDistance { get; set; }
        [DisplayName("bottomLayCount")] public ushort BottomLayCount { get; set; } = 4;
        [DisplayName("bottomLayerCount")] public ushort BottomLayerCount { get; set; } = 4;
        [DisplayName("mirror")] public byte Mirror { get; set; } // 0/1
        [DisplayName("totalLayer")] public uint LayerCount { get; set; }
        [DisplayName("bottomLayerLiftHeight")] public float BottomLiftHeight { get; set; } = 5;
        [DisplayName("bottomLayerLiftSpeed")] public float BottomLiftSpeed { get; set; } = 60;
        [DisplayName("bottomLightOffTime")] public float BottomLightOffDelay { get; set; }
        [DisplayName("lightOffTime")] public float LightOffDelay { get; set; }
        [DisplayName("bottomPWMLight")] public byte BottomLightPWM { get; set; } = 255;
        [DisplayName("PWMLight")] public byte LightPWM { get; set; } = 255;
        [DisplayName("antiAliasLevel")] public byte AntiAliasing { get; set; } = 1;
    }

    #endregion

    #region Properties
    public Header HeaderSettings { get; } = new();

    public override FileFormatType FileType => FileFormatType.Archive;

    public override string ConvertMenuGroup => "Chitubox";

    public override FileExtension[] FileExtensions { get; } =
    [
        new(typeof(ChituboxZipFile), "zip", "Chitubox Zip")
    ];

    public override PrintParameterModifier[] PrintParameterModifiers { get; } =
    [
        PrintParameterModifier.BottomLayerCount,
        PrintParameterModifier.TransitionLayerCount,

        PrintParameterModifier.BottomWaitTimeBeforeCure,
        PrintParameterModifier.WaitTimeBeforeCure,

        PrintParameterModifier.BottomExposureTime,
        PrintParameterModifier.ExposureTime,

        PrintParameterModifier.BottomWaitTimeAfterCure,
        PrintParameterModifier.WaitTimeAfterCure,

        PrintParameterModifier.BottomLiftHeight,
        PrintParameterModifier.BottomLiftSpeed,
        PrintParameterModifier.LiftHeight,
        PrintParameterModifier.LiftSpeed,

        PrintParameterModifier.BottomLiftHeight2,
        PrintParameterModifier.BottomLiftSpeed2,
        PrintParameterModifier.LiftHeight2,
        PrintParameterModifier.LiftSpeed2,

        PrintParameterModifier.BottomWaitTimeAfterLift,
        PrintParameterModifier.WaitTimeAfterLift,

        PrintParameterModifier.BottomRetractSpeed,
        PrintParameterModifier.RetractSpeed,

        PrintParameterModifier.BottomRetractHeight2,
        PrintParameterModifier.BottomRetractSpeed2,
        PrintParameterModifier.RetractHeight2,
        PrintParameterModifier.RetractSpeed2,

        PrintParameterModifier.BottomLightPWM,
        PrintParameterModifier.LightPWM
    ];

    public override PrintParameterModifier[] PrintParameterPerLayerModifiers { get; } =
    [
        PrintParameterModifier.PositionZ,
        PrintParameterModifier.WaitTimeBeforeCure,
        PrintParameterModifier.ExposureTime,
        PrintParameterModifier.WaitTimeAfterCure,
        PrintParameterModifier.LiftHeight,
        PrintParameterModifier.LiftSpeed,
        PrintParameterModifier.LiftHeight2,
        PrintParameterModifier.LiftSpeed2,
        PrintParameterModifier.WaitTimeAfterLift,
        PrintParameterModifier.RetractSpeed,
        PrintParameterModifier.RetractHeight2,
        PrintParameterModifier.RetractSpeed2,
        PrintParameterModifier.LightPWM
    ];

    public override Size[] ThumbnailsOriginalSize { get; } =
    [
        new(954, 850),
        new(168, 150)
    ];

    public override uint ResolutionX
    {
        get => HeaderSettings.ResolutionX;
        set => base.ResolutionX = HeaderSettings.ResolutionX = value;
    }

    public override uint ResolutionY
    {
        get => HeaderSettings.ResolutionY;
        set => base.ResolutionY = HeaderSettings.ResolutionY = value;
    }

    public override float DisplayWidth
    {
        get => HeaderSettings.MachineX;
        set => base.DisplayWidth = HeaderSettings.MachineX = RoundDisplaySize(value);
    }

    public override float DisplayHeight
    {
        get => HeaderSettings.MachineY;
        set => base.DisplayHeight = HeaderSettings.MachineY = RoundDisplaySize(value);
    }

    public override float MachineZ
    {
        get => HeaderSettings.MachineZ > 0 ? HeaderSettings.MachineZ : base.MachineZ;
        set => base.MachineZ = HeaderSettings.MachineZ = MathF.Round(value, 2);
    }

    public override FlipDirection DisplayMirror
    {
        get => HeaderSettings.Mirror == 0 ? FlipDirection.None : FlipDirection.Horizontally;
        set
        {
            HeaderSettings.ProjectType = value == FlipDirection.None ? "Normal" : "LCD_mirror";
            HeaderSettings.Mirror = (byte)(value == FlipDirection.None ? 0 : 1);
            RaisePropertyChanged();
        }
    }

    public override byte AntiAliasing
    {
        get => HeaderSettings.AntiAliasing;
        set => base.AntiAliasing = HeaderSettings.AntiAliasing = Math.Clamp(value, (byte)1, (byte)16);
    }

    public override float LayerHeight
    {
        get => HeaderSettings.LayerHeight;
        set => base.LayerHeight = HeaderSettings.LayerHeight = Layer.RoundHeight(value);
    }

    public override uint LayerCount
    {
        get => base.LayerCount;
        set => base.LayerCount = HeaderSettings.LayerCount = base.LayerCount;
    }

    public override ushort BottomLayerCount
    {
        get => HeaderSettings.BottomLayerCount;
        set => base.BottomLayerCount = HeaderSettings.BottomLayerCount = HeaderSettings.BottomLayCount = value;
    }

    public override float BottomLightOffDelay
    {
        get => BottomWaitTimeBeforeCure;
        set => BottomWaitTimeBeforeCure = value;
    }

    public override float LightOffDelay
    {
        get => WaitTimeBeforeCure;
        set => WaitTimeBeforeCure = value;
    }

    public override float BottomWaitTimeBeforeCure
    {
        get => HeaderSettings.BottomLightOffDelay;
        set => base.BottomWaitTimeBeforeCure = HeaderSettings.BottomLightOffDelay = MathF.Round(value, 2);
    }

    public override float WaitTimeBeforeCure
    {
        get => HeaderSettings.LightOffDelay;
        set => base.WaitTimeBeforeCure = HeaderSettings.LightOffDelay = MathF.Round(value, 2);
    }

    public override float BottomExposureTime
    {
        get => HeaderSettings.BottomExposureTime;
        set => base.BottomExposureTime = HeaderSettings.BottomExposureTime = HeaderSettings.BottomLayExposureTime = MathF.Round(value, 2);
    }

    public override float ExposureTime
    {
        get => HeaderSettings.ExposureTime;
        set => base.ExposureTime = HeaderSettings.ExposureTime = MathF.Round(value, 2);
    }

    public override float BottomLiftHeight
    {
        get => HeaderSettings.BottomLiftHeight;
        set => base.BottomLiftHeight = HeaderSettings.BottomLiftHeight = MathF.Round(value, 2);
    }

    public override float LiftHeight
    {
        get => HeaderSettings.LiftHeight;
        set => base.LiftHeight = HeaderSettings.LiftHeight = MathF.Round(value, 2);
    }

    public override float BottomLiftSpeed
    {
        get => HeaderSettings.BottomLiftSpeed;
        set => base.BottomLiftSpeed = HeaderSettings.BottomLiftSpeed = MathF.Round(value, 2);
    }

    public override float LiftSpeed
    {
        get => HeaderSettings.LiftSpeed;
        set => base.LiftSpeed = HeaderSettings.LiftSpeed = MathF.Round(value, 2);
    }

    public override float RetractSpeed
    {
        get => HeaderSettings.RetractSpeed;
        set => base.RetractSpeed = HeaderSettings.RetractSpeed = MathF.Round(value, 2);
    }

    public override byte BottomLightPWM
    {
        get => HeaderSettings.BottomLightPWM;
        set => base.BottomLightPWM = HeaderSettings.BottomLightPWM = value;
    }

    public override byte LightPWM
    {
        get => HeaderSettings.LightPWM;
        set => base.LightPWM = HeaderSettings.LightPWM = value;
    }

    public override float PrintTime
    {
        get => base.PrintTime;
        set
        {
            base.PrintTime = value;
            HeaderSettings.EstimatedPrintTime = base.PrintTime;
        }
    }

    public override float MaterialMilliliters
    {
        get => base.MaterialMilliliters;
        set
        {
            base.MaterialMilliliters = value;
            HeaderSettings.VolumeMl = base.MaterialMilliliters;
        }
    }

    public override float MaterialGrams
    {
        get => MathF.Round(HeaderSettings.WeightG, 3);
        set
        {
            HeaderSettings.WeightG = MathF.Round(value, 3);
            RaisePropertyChanged();
        }
    }

    public override float MaterialCost
    {
        get => MathF.Round(HeaderSettings.Price, 3);
        set
        {
            HeaderSettings.Price = MathF.Round(value, 3);
            RaisePropertyChanged();
        }
    }

    public override string? MaterialName
    {
        get => HeaderSettings.Resin;
        set
        {
            HeaderSettings.Resin = value;
            RaisePropertyChanged();
        }
    }

    public override string MachineName
    {
        get => HeaderSettings.MachineType;
        set => base.MachineName = HeaderSettings.MachineType = value;
    }

    public override object[] Configs => [HeaderSettings];

    public override bool SupportGCode => base.SupportGCode && !IsPHZZip;

    public bool IsPHZZip;
    #endregion

    #region Constructor
    public ChituboxZipFile()
    {
        GCode = new GCodeBuilder
        {
            UseComments = true,
            GCodePositioningType = GCodeBuilder.GCodePositioningTypes.Absolute,
            GCodeSpeedUnit = GCodeBuilder.GCodeSpeedUnits.MillimetersPerMinute,
            GCodeTimeUnit = GCodeBuilder.GCodeTimeUnits.Milliseconds,
            GCodeShowImageType = GCodeBuilder.GCodeShowImageTypes.FilenamePng1Started,
            LayerMoveCommand = GCodeBuilder.GCodeMoveCommands.G0,
            EndGCodeMoveCommand = GCodeBuilder.GCodeMoveCommands.G1
        };
    }
    #endregion

    #region Methods

    public override bool CanProcess(string? fileFullPath)
    {
        if (!base.CanProcess(fileFullPath)) return false;

        try
        {
            using var zip = ZipFile.Open(fileFullPath!, ZipArchiveMode.Read);

            var gcodeFound = false;
            foreach (var entry in zip.Entries)
            {
                if (entry.Name.EndsWith(KlipperFile.KlipperFileIdentifier)) return false;
                if (entry.Name.EndsWith(".gcode")) gcodeFound = true;
            }

            return gcodeFound;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }


        return false;
    }

    protected override void EncodeInternally(OperationProgress progress)
    {
        using var outputFile = ZipFile.Open(TemporaryOutputFileFullPath, ZipArchiveMode.Create);

        if (!IsPHZZip)
        {
            RebuildGCode();
            outputFile.CreateEntryFromContent(GCodeFilename, GCodeStr, ZipArchiveMode.Create);
        }

        EncodeThumbnailsInZip(outputFile, progress, ThumbnailsEntryNames);
        EncodeLayersInZip(outputFile, IndexStartNumber.One, progress);
    }

    protected override void DecodeInternally(OperationProgress progress)
    {
        using var inputFile = ZipFile.Open(FileFullPath!, ZipArchiveMode.Read);
        var entry = inputFile.GetEntry(GCodeFilename);
        if (entry is not null)
        {
            //Clear();
            //throw new FileLoadException("run.gcode not found", fileFullPath);
            using TextReader tr = new StreamReader(entry.Open());
            GCode!.Clear();
            while (tr.ReadLine() is { } line)
            {
                GCode.AppendLine(line);
                if (string.IsNullOrEmpty(line)) continue;

                if (line[0] != ';')
                {
                    continue;
                }

                var splitLine = line.Split(':');
                if (splitLine.Length < 2) continue;

                foreach (var propertyInfo in HeaderSettings.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    var displayNameAttribute = propertyInfo.GetCustomAttributes(false).AsValueEnumerable().OfType<DisplayNameAttribute>().FirstOrDefault();
                    if (displayNameAttribute is null) continue;
                    if (!splitLine[0].Trim(' ', ';').Equals(displayNameAttribute.DisplayName)) continue;
                    propertyInfo.SetValueFromString(HeaderSettings, splitLine[1].Trim());
                }
            }
            tr.Close();
        }
        else
        {
            IsPHZZip = true;
        }

        if (HeaderSettings.LayerCount == 0)
        {
            foreach (var zipEntry in inputFile.Entries)
            {
                if(!zipEntry.Name.EndsWith(".png")) continue;
                var filename = Path.GetFileNameWithoutExtension(zipEntry.Name);
                if (!filename.AsValueEnumerable().All(char.IsDigit)) continue;
                if (!uint.TryParse(filename, out var layerIndex)) continue;
                HeaderSettings.LayerCount = Math.Max(HeaderSettings.LayerCount, layerIndex);
            }
        }

        DecodeThumbnailsFromZip(inputFile, progress, ThumbnailsEntryNames);

        Init(HeaderSettings.LayerCount, DecodeType == FileDecodeType.Partial);
        DecodeLayersFromZip(inputFile, IndexStartNumber.One, progress);

        if (IsPHZZip) // PHZ file
        {
            RebuildLayersProperties();
        }
        else
        {
            GCode?.ParseLayersFromGCode(this);
        }
    }

    public override void RebuildGCode()
    {
        if (!SupportGCode || SuppressRebuildGCode) return;
        GCode?.RebuildGCode(this, [HeaderSettings]);
        RaisePropertyChanged(nameof(GCodeStr));
    }

    protected override void PartialSaveInternally(OperationProgress progress)
    {
        using var outputFile = ZipFile.Open(TemporaryOutputFileFullPath, ZipArchiveMode.Update);
        var entriesToRemove = outputFile.Entries.AsValueEnumerable().Where(zipEntry => zipEntry.Name.EndsWith(".gcode")).ToArray();
        foreach (var zipEntry in entriesToRemove)
        {
            zipEntry.Delete();
        }

        if (!IsPHZZip)
        {
            outputFile.CreateEntryFromContent(GCodeFilename, GCodeStr, ZipArchiveMode.Update);
        }

        //Decode(FileFullPath, progress);
    }
    #endregion
}