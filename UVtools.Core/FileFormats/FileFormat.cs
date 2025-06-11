﻿/*
 *                     GNU AFFERO GENERAL PUBLIC LICENSE
 *                       Version 3, 19 November 2007
 *  Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
 *  Everyone is permitted to copy and distribute verbatim copies
 *  of this license document, but changing it is not allowed.
 */

using CommunityToolkit.Diagnostics;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UVtools.Core.Exceptions;
using UVtools.Core.Extensions;
using UVtools.Core.GCode;
using UVtools.Core.Layers;
using UVtools.Core.Managers;
using UVtools.Core.Objects;
using UVtools.Core.Operations;
using UVtools.Core.PixelEditor;
using ZLinq;
using Timer = System.Timers.Timer;

namespace UVtools.Core.FileFormats;

/// <summary>
/// Slicer <see cref="FileFormat"/> representation
/// </summary>
public abstract class FileFormat : BindableBase, IDisposable, IEquatable<FileFormat>, IList<Layer>
{
    #region Constants

    /// <summary>
    /// Gets the decimal precision for display properties
    /// </summary>
    public const byte DisplayFloatPrecision = 3;

    public const SpeedUnit CoreSpeedUnit = SpeedUnit.MillimetersPerMinute;
    public const string TemporaryFileAppend = ".tmp";
    public const ushort ExtraPrintTime = 300;

    private const string ExtractConfigFileName = "Configuration";
    private const string ExtractConfigFileExtension = "ini";


    public const float DefaultLayerHeight = 0.05f;
    public const ushort DefaultBottomLayerCount = 4;
    public const ushort DefaultTransitionLayerCount = 0;

    public const float DefaultBottomExposureTime = 30;
    public const float DefaultExposureTime = 3;

    public const float DefaultBottomLiftHeight = 6;
    public const float DefaultLiftHeight = 6;
    public const float DefaultBottomLiftSpeed = 100;
    public const float DefaultLiftSpeed = 100;

    public const float DefaultBottomLiftHeight2 = 0;
    public const float DefaultLiftHeight2 = 0;
    public const float DefaultBottomLiftSpeed2 = 300;
    public const float DefaultLiftSpeed2 = 300;


    public const float DefaultBottomRetractSpeed = 100;
    public const float DefaultRetractSpeed = 100;
    public const float DefaultBottomRetractHeight2 = 0;
    public const float DefaultRetractHeight2 = 0;
    public const float DefaultBottomRetractSpeed2 = 80;
    public const float DefaultRetractSpeed2 = 80;

    public const byte DefaultBottomLightPWM = 255;
    public const byte DefaultLightPWM = 255;

    public const string DefaultMachineName = "Unknown";
    public const string DefaultResinName = "Unknown";

    public const byte MaximumAntiAliasing = 16;

    private const ushort QueueTimerPrintTime = 250; // ms

    public const string DATATYPE_PNG = "PNG";
    public const string DATATYPE_JPG = "JPG";
    public const string DATATYPE_JPEG = "JPEG";
    public const string DATATYPE_JP2 = "JP2";
    public const string DATATYPE_BMP = "BMP";
    public const string DATATYPE_TIF = "TIF";
    public const string DATATYPE_TIFF = "TIFF";
    public const string DATATYPE_PPM = "PPM";
    public const string DATATYPE_PMG = "PMG";
    public const string DATATYPE_SR = "SR";
    public const string DATATYPE_RAS = "RAS";

    public const string DATATYPE_RGB555 = "RGB555";
    public const string DATATYPE_RGB565 = "RGB565";
    public const string DATATYPE_RGB555_BE = "RGB555-BE";
    public const string DATATYPE_RGB565_BE = "RGB565-BE";
    public const string DATATYPE_RGB888 = "RGB888";


    public const string DATATYPE_BGR555 = "BGR555";
    public const string DATATYPE_BGR565 = "BGR565";
    public const string DATATYPE_BGR555_BE = "BGR555-BE";
    public const string DATATYPE_BGR565_BE = "BGR565-BE";
    public const string DATATYPE_BGR888 = "BGR888";

    /// <summary>
    /// Gets the default batch count to process layers in parallel
    /// </summary>
    public static int DefaultParallelBatchCount => (CoreSettings.MaxDegreeOfParallelism > 0 ? CoreSettings.MaxDegreeOfParallelism : Environment.ProcessorCount) * 10;

    #endregion

    #region Enums

    /// <summary>
    /// Enumeration of file format types
    /// </summary>
    public enum FileFormatType : byte
    {
        Archive,
        Binary,
        Text
    }

    /// <summary>
    /// Enumeration of file thumbnail size types
    /// </summary>
    public enum FileThumbnailSize : byte
    {
        Small = 0,
        Large
    }

    public enum TransitionLayerTypes : byte
    {
        /// <summary>
        /// Firmware transition layers are handled by printer firmware
        /// </summary>
        Firmware,

        /// <summary>
        /// Software transition layers are handled by software and written on layer data
        /// </summary>
        Software
    }

    /// <summary>
    /// File decode type
    /// </summary>
    public enum FileDecodeType : byte
    {
        /// <summary>
        /// Decodes all the file information and caches layer images
        /// </summary>
        Full,

        /// <summary>
        /// Decodes only the information in the file and thumbnails, no layer image is read nor cached, fast
        /// </summary>
        Partial,
    }

    /// <summary>
    /// Image data type
    /// </summary>
    public enum ImageFormat : byte
    {
        Custom,
        Rle,
        GCode,

        Png8,
        Png24,
        Png32,

        /// <summary>
        /// eg: Nova Bene4, Elfin Mono SE, Whale 1/2
        /// </summary>
        Png24BgrAA,

        /// <summary>
        /// eg: Uniformation GKone, Athena 12K
        /// </summary>
        Png24RgbAA,

        Svg,
    }

    #endregion

    #region Sub Classes

    /// <summary>
    /// Available Print Parameters to modify
    /// </summary>
    public class PrintParameterModifier
    {

        #region Instances

        public static PrintParameterModifier PositionZ { get; } = new("Position Z", "Layer absolute Z position", "mm",
            0, 100000, 0.01m, Layer.HeightPrecision);

        public static PrintParameterModifier BottomLayerCount { get; } = new("Bottom layers",
            "Number of bottom/burn-in layers", "Ξ", 0, ushort.MaxValue, 1, 0);

        public static PrintParameterModifier TransitionLayerCount { get; } = new("Transition layers",
            "Number of fade/transition layers", "Ξ", 0, ushort.MaxValue, 1, 0);

        public static PrintParameterModifier BottomLightOffDelay { get; } = new("Bottom light-off delay",
            "Total motor movement time + rest time to wait before cure a new bottom layer", "s");

        public static PrintParameterModifier LightOffDelay { get; } = new("Light-off delay",
            "Total motor movement time + rest time to wait before cure a new layer", "s");

        public static PrintParameterModifier BottomWaitTimeBeforeCure { get; } = new("Bottom wait before cure",
            "Time to wait/rest before cure a new bottom layer\nChitubox: Rest after retract\nLychee: Wait before print",
            "s");

        public static PrintParameterModifier WaitTimeBeforeCure { get; } = new("Wait before cure",
            "Time to wait/rest before cure a new layer\nChitubox: Rest after retract\nLychee: Wait before print", "s");

        public static PrintParameterModifier BottomExposureTime { get; } =
            new("Bottom exposure time", "Bottom layers exposure time", "s", 0.1M);

        public static PrintParameterModifier ExposureTime { get; } =
            new("Exposure time", "Normal layers exposure time", "s", 0.1M);

        public static PrintParameterModifier BottomWaitTimeAfterCure { get; } = new("Bottom wait after cure",
            "Time to wait/rest after cure a new bottom layer\nChitubox: Rest before lift\nLychee: Wait after print",
            "s");

        public static PrintParameterModifier WaitTimeAfterCure { get; } = new("Wait after cure",
            "Time to wait/rest after cure a new layer\nChitubox: Rest before lift\nLychee: Wait after print", "s");

        public static PrintParameterModifier BottomLiftHeight { get; } =
            new("Bottom lift height", "Lift/peel height between bottom layers", "mm");

        public static PrintParameterModifier LiftHeight { get; } =
            new("Lift height", @"Lift/peel height between layers", "mm");

        public static PrintParameterModifier BottomLiftSpeed { get; } =
            new("Bottom lift speed", "Lift speed of bottom layers", "mm/min", 0, 5000, 5);

        public static PrintParameterModifier BottomLiftAcceleration { get; } =
            new("Bottom lift acceleration", "Lift acceleration of bottom layers", "mm/s²", 0, 10000, 5);

        public static PrintParameterModifier LiftSpeed { get; } =
            new("Lift speed", "Lift speed of normal layers", "mm/min", 0, 5000, 5);

        public static PrintParameterModifier LiftAcceleration { get; } =
            new("Lift acceleration", "Lift acceleration of normal layers", "mm/s²", 0, 10000, 5);

        public static PrintParameterModifier BottomLiftHeight2 { get; } =
            new("2) Bottom lift height", "Second lift/peel height between bottom layers", "mm");

        public static PrintParameterModifier LiftHeight2 { get; } =
            new("2) Lift height", @"Second lift/peel height between layers", "mm");

        public static PrintParameterModifier BottomLiftSpeed2 { get; } =
            new("2) Bottom lift speed", "Lift speed of bottom layers for the second lift sequence (TSMC)", "mm/min", 0, 5000, 5);

        public static PrintParameterModifier BottomLiftAcceleration2 { get; } =
            new("2) Bottom lift acceleration", "Lift acceleration of bottom layers for the second lift sequence (TSMC)", "mm/s²", 0, 10000, 5);

        public static PrintParameterModifier LiftSpeed2 { get; } =
            new("2) Lift speed", "Lift speed of normal layers for the second lift sequence (TSMC)", "mm/min", 0, 5000, 5);

        public static PrintParameterModifier LiftAcceleration2 { get; } =
            new("2) Lift acceleration", "Lift acceleration of normal layers for the second lift sequence (TSMC)", "mm/s²", 0, 10000, 5);

        public static PrintParameterModifier BottomWaitTimeAfterLift { get; } = new("Bottom wait after lift",
            "Time to wait/rest after a lift/peel sequence on bottom layers\nChitubox: Rest after lift\nLychee: Wait after lift",
            "s");

        public static PrintParameterModifier WaitTimeAfterLift { get; } = new("Wait after lift",
            "Time to wait/rest after a lift/peel sequence on layers\nChitubox: Rest after lift\nLychee: Wait after lift",
            "s");

        public static PrintParameterModifier BottomRetractSpeed { get; } = new("Bottom retract speed",
            "Down speed from lift height to next bottom layer cure position", "mm/min", 0, 5000, 5);

        public static PrintParameterModifier BottomRetractAcceleration { get; } = new("Bottom retract acceleration",
            "Down acceleration from lift height to next bottom layer cure position", "mm/s²", 0, 10000, 5);

        public static PrintParameterModifier RetractSpeed { get; } = new("Retract speed",
            "Down speed from lift height to next layer cure position", "mm/min", 0, 5000, 5);

        public static PrintParameterModifier RetractAcceleration { get; } = new("Retract acceleration",
            "Down acceleration from lift height to next layer cure position", "mm/s²", 0, 10000, 5);

        public static PrintParameterModifier BottomRetractHeight2 { get; } = new("2) Bottom retract height",
            "Slow retract height of bottom layers (TSMC)", "mm");

        public static PrintParameterModifier RetractHeight2 { get; } = new("2) Retract height",
            "Slow retract height of normal layers (TSMC)", "mm");

        public static PrintParameterModifier BottomRetractSpeed2 { get; } = new("2) Bottom retract speed",
            "Slow retract speed of bottom layers (TSMC)", "mm/min", 0, 5000, 5);

        public static PrintParameterModifier BottomRetractAcceleration2 { get; } = new("2) Bottom retract acceleration",
            "Slow retract acceleration of bottom layers (TSMC)", "mm/s²", 0, 10000, 5);

        public static PrintParameterModifier RetractSpeed2 { get; } = new("2) Retract speed",
            "Slow retract speed of normal layers (TSMC)", "mm/min", 0, 5000, 5);

        public static PrintParameterModifier RetractAcceleration2 { get; } = new("2) Retract acceleration",
            "Slow retract acceleration of normal layers (TSMC)", "mm/s²", 0, 10000, 5);

        public static PrintParameterModifier BottomLightPWM { get; } = new("Bottom light PWM",
            "UV LED power for bottom layers", "☀", 1, byte.MaxValue, 5, 0);

        public static PrintParameterModifier LightPWM { get; } =
            new("Light PWM", "UV LED power for layers", "☀", 1, byte.MaxValue, 5, 0);

        public static PrintParameterModifier Pause { get; } =
            new("Pause", "Pauses the layer before printing", "⏸️", 0, 1, 1, 0);

        public static PrintParameterModifier ChangeResin { get; } =
            new("Change resin", "Pauses the layer to change the resin", "↹", 0, 1, 1, 0);

        /*public static PrintParameterModifier[] Parameters = {
            BottomLayerCount,

            BottomWaitTimeBeforeCure,
            WaitTimeBeforeCure,
            BottomExposureTime,
            ExposureTime,
            BottomWaitTimeAfterCure,
            WaitTimeAfterCure,

            BottomLightOffDelay,
            LightOffDelay,
            BottomLiftHeight,
            BottomLiftSpeed,
            LiftHeight,
            LiftSpeed,
            BottomWaitTimeAfterLift,
            WaitTimeAfterLift,
            RetractSpeed,

            BottomLightPWM,
            LightPWM
        };*/

        #endregion

        #region Properties

        /// <summary>
        /// Gets the name
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the description
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Gets the value unit
        /// </summary>
        public string ValueUnit { get; }

        /// <summary>
        /// Gets the minimum value
        /// </summary>
        public decimal Minimum { get; }

        /// <summary>
        /// Gets the maximum value
        /// </summary>
        public decimal Maximum { get; }

        /// <summary>
        /// Gets the incrementing value for the dropdown
        /// </summary>
        public decimal Increment { get; set; } = 1;

        /// <summary>
        /// Gets the number of decimal plates
        /// </summary>
        public byte DecimalPlates { get; }

        /// <summary>
        /// Gets or sets the current / old value
        /// </summary>
        public decimal OldValue { get; set; }

        /// <summary>
        /// Gets or sets the new value
        /// </summary>
        public decimal NewValue { get; set; }

        public decimal Value
        {
            get => NewValue;
            set => OldValue = NewValue = value;
        }

        /// <summary>
        /// Gets if the value has changed
        /// </summary>
        public bool HasChanged => OldValue != NewValue;

        #endregion

        #region Constructor

        public PrintParameterModifier(string name, string? description = null, string? valueUnit = null,
            decimal minimum = 0, decimal maximum = 1000, decimal increment = 0.5m, byte decimalPlates = 2)
        {
            Name = name;
            Description = description ?? $"Modify '{name}'";
            ValueUnit = valueUnit ?? string.Empty;
            Minimum = minimum;
            Maximum = maximum;
            Increment = decimalPlates == 0 ? Math.Max(1, increment) : increment;
            DecimalPlates = decimalPlates;
        }

        #endregion

        #region Overrides

        protected bool Equals(PrintParameterModifier other)
        {
            return Name == other.Name;
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((PrintParameterModifier)obj);
        }

        public override int GetHashCode()
        {
            return (Name.GetHashCode());
        }

        public override string ToString()
        {
            return
                $"{nameof(Name)}: {Name}, {nameof(Description)}: {Description}, {nameof(ValueUnit)}: {ValueUnit}, {nameof(Minimum)}: {Minimum}, {nameof(Maximum)}: {Maximum}, {nameof(DecimalPlates)}: {DecimalPlates}, {nameof(OldValue)}: {OldValue}, {nameof(NewValue)}: {NewValue}, {nameof(HasChanged)}: {HasChanged}";
        }

        public PrintParameterModifier Clone()
        {
            return (PrintParameterModifier)MemberwiseClone();
        }

        #endregion
    }

    #endregion

    #region Static Methods

    /// <summary>
    /// Gets the available formats to process
    /// </summary>
    public static FileFormat[] AvailableFormats { get; } =
    [
        new SL1File(), // Prusa SL1
        new ChituboxZipFile(), // Zip
        new ChituboxFile(), // cbddlp, cbt, photon
        new CTBEncryptedFile(), // encrypted ctb
        new AnycubicPhotonSFile(), // photons
        new PHZFile(), // phz
        new AnycubicFile(), // PSW, PW0
        new AnycubicZipFile(), // PWSZ
        new CWSFile(), // CWS
        new AnetFile(), // Anet N4, N7
        new LGSFile(), // LGS, LGS30
        new VDAFile(), // VDA
        new VDTFile(), // VDT
        //new CXDLPv1File(),   // Creality Box v1
        new CrealityCXDLPFile(), // Creality Box
        new CrealityCXDLPv4File(), // Creality Box
        new NanoDLPFile(), // NanoDLP
        new KlipperFile(), // Klipper

        new FDGFile(), // fdg
        new GooFile(), // goo
        new ZCodeFile(), // zcode
        new JXSFile(), // jxs
        new ZCodexFile(), // zcodex
        new MDLPFile(), // MKS v1
        new GR1File(), // GR1 Workshop
        new FlashForgeSVGXFile(), // SVGX
        new QDTFile(), // QDT
        new OSLAFile(), // OSLA
        new OSFFile(), // OSF
        new UVJFile(), // UVJ
        new GenericZIPFile(), // Generic zip files
        new ImageFile() // images
    ];

    public static string AllSlicerFiles => AvailableFormats.AsValueEnumerable().Aggregate("All slicer files|",
        (current, fileFormat) => current.EndsWith('|')
            ? $"{current}{fileFormat.FileFilterExtensionsOnly}"
            : $"{current}; {fileFormat.FileFilterExtensionsOnly}");

    /// <summary>
    /// Gets all filters for open and save file dialogs
    /// </summary>
    public static string AllFileFilters =>
        AllSlicerFiles
        +
        AvailableFormats.AsValueEnumerable().Aggregate(string.Empty,
            (current, fileFormat) => $"{current}|" + fileFormat.FileFilter);

    public static List<KeyValuePair<string, List<string>>> AllFileFiltersAvalonia
    {
        get
        {
            List<KeyValuePair<string, List<string>>> result = [new("All slicer files", [])];

            foreach (var format in AvailableFormats)
            {
                foreach (var fileExtension in format.FileExtensions)
                {
                    if (!fileExtension.IsVisibleOnFileFilters) continue;
                    result[0].Value.Add(fileExtension.Extension);
                    result.Add(new KeyValuePair<string, List<string>>(fileExtension.Description,
                        [fileExtension.Extension]));
                }
            }

            return result;
        }

    }

    public static List<FileExtension> AllFileExtensions
    {
        get
        {
            List<FileExtension> extensions = [];
            foreach (var slicerFile in AvailableFormats)
            {
                extensions.AddRange(slicerFile.FileExtensions);
            }

            return extensions;
        }
    }

    public static IEnumerable<string> AllFileExtensionsString => AvailableFormats.SelectMany(
        slicerFile => slicerFile.FileExtensions, (slicerFile, extension) => extension.Extension);


    /// <summary>
    /// Gets the count of available file extensions
    /// </summary>
    public static byte FileExtensionsCount => (byte)AvailableFormats.AsValueEnumerable().Sum(format => format.FileExtensions.Length);
    //AvailableFormats.AsValueEnumerable().Aggregate<FileFormat, byte>(0,
    //(current, fileFormat) => (byte)(current + fileFormat.FileExtensions.Length));

    /// <summary>
    /// Find <see cref="FileFormat"/> by an extension
    /// </summary>
    /// <param name="extensionOrFilePath"> name to find</param>
    /// <param name="createNewInstance">True to create a new instance of found file format, otherwise will return a pre created one which should be used for read-only purpose</param>
    /// <returns><see cref="FileFormat"/> object or null if not found</returns>
    public static FileFormat? FindByExtensionOrFilePath(string extensionOrFilePath, bool createNewInstance = false) =>
        FindByExtensionOrFilePath(extensionOrFilePath, out _, createNewInstance);

    /// <summary>
    /// Find <see cref="FileFormat"/> by an extension
    /// </summary>
    /// <param name="extensionOrFilePath"> name to find</param>
    /// <param name="fileFormatsSharingExt">Number of file formats sharing the input extension</param>
    /// <param name="createNewInstance">True to create a new instance of found file format, otherwise will return a pre created one which should be used for read-only purpose</param>
    /// <returns><see cref="FileFormat"/> object or null if not found</returns>
    public static FileFormat? FindByExtensionOrFilePath(string extensionOrFilePath, out byte fileFormatsSharingExt,
        bool createNewInstance = false)
    {
        fileFormatsSharingExt = 0;
        if (string.IsNullOrWhiteSpace(extensionOrFilePath)) return null;

        bool isFilePath = false;
        // Test for ext first
        var fileFormats = AvailableFormats.AsValueEnumerable().Where(fileFormat => fileFormat.IsExtensionValid(extensionOrFilePath))
            .ToArray();
        fileFormatsSharingExt = (byte)fileFormats.Length;

        if (fileFormats.Length == 0) // Extension not found, can be filepath, try to find it
        {
            GetFileNameStripExtensions(extensionOrFilePath, out var extension);
            if (string.IsNullOrWhiteSpace(extension)) return null;

            fileFormats = AvailableFormats.AsValueEnumerable().Where(fileFormat => fileFormat.IsExtensionValid(extension)).ToArray();
            if (fileFormats.Length == 0) return null;
            isFilePath = true; // Was a file path
        }

        if (fileFormats.Length == 1 || !isFilePath)
        {
            return createNewInstance
                ? Activator.CreateInstance(fileFormats[0].GetType()) as FileFormat
                : fileFormats[0];
        }

        // Multiple instances using Check for valid candidate
        foreach (var fileFormat in fileFormats)
        {
            if (fileFormat.CanProcess(extensionOrFilePath))
            {
                return createNewInstance
                    ? Activator.CreateInstance(fileFormat.GetType()) as FileFormat
                    : fileFormat;
            }
        }

        return null;
        // Try this in a far and not probable attempt
        //return createNewInstance
        //    ? Activator.CreateInstance(fileFormats[0].GetType()) as FileFormat
        //    : fileFormats[0];
    }

    /// <summary>
    /// Find <see cref="FileFormat"/> by a type name
    /// </summary>
    /// <param name="type">Type name to find</param>
    /// <param name="createNewInstance">True to create a new instance of found file format, otherwise will return a pre created one which should be used for read-only purpose</param>
    /// <returns><see cref="FileFormat"/> object or null if not found</returns>
    public static FileFormat? FindByType(string type, bool createNewInstance = false)
    {
        if (!type.EndsWith("File"))
        {
            type += "File";
        }

        var fileFormat = AvailableFormats.AsValueEnumerable().FirstOrDefault(format =>
            string.Equals(format.GetType().Name, type, StringComparison.OrdinalIgnoreCase));
        if (fileFormat is null) return null;
        return createNewInstance
            ? Activator.CreateInstance(fileFormat.GetType()) as FileFormat
            : fileFormat;
        //return (from t in AvailableFormats where type == t.GetType() select createNewInstance ? (FileFormat) Activator.CreateInstance(type) : t).FirstOrDefault();
    }

    /// <summary>
    /// Find <see cref="FileFormat"/> by any means (type name, extension, filepath)
    /// </summary>
    /// <param name="name">Name to find</param>
    /// <param name="createNewInstance">True to create a new instance of found file format, otherwise will return a pre created one which should be used for read-only purpose</param>
    /// <returns><see cref="FileFormat"/> object or null if not found</returns>
    public static FileFormat? FindByAnyMeans(string name, bool createNewInstance = false)
    {
        return FindByType(name, true)
               ?? FindByExtensionOrFilePath(name, true);
    }

    /// <summary>
    /// Find <see cref="FileFormat"/> by an type
    /// </summary>
    /// <param name="type">Type to find</param>
    /// <param name="createNewInstance">True to create a new instance of found file format, otherwise will return a pre created one which should be used for read-only purpose</param>
    /// <returns><see cref="FileFormat"/> object or null if not found</returns>
    public static FileFormat? FindByType(Type type, bool createNewInstance = false)
    {
        var fileFormat = AvailableFormats.AsValueEnumerable().FirstOrDefault(format => format.GetType() == type);
        if (fileFormat is null) return null;
        return createNewInstance
            ? Activator.CreateInstance(type) as FileFormat
            : fileFormat;
        //return (from t in AvailableFormats where type == t.GetType() select createNewInstance ? (FileFormat) Activator.CreateInstance(type) : t).FirstOrDefault();
    }

    public static FileExtension? FindExtension(string extension)
    {
        return AvailableFormats.AsValueEnumerable().SelectMany(format => format.FileExtensions)
            .FirstOrDefault(ext => ext.Equals(extension));
    }

    public static IEnumerable<FileExtension> FindExtensions(string extension)
    {
        return AvailableFormats.SelectMany(format => format.FileExtensions).Where(ext => ext.Equals(extension));
    }

    public static string? GetFileNameStripExtensions(string? filepath)
    {
        if (filepath is null) return null;
        //if (file.EndsWith(TemporaryFileAppend)) file = Path.GetFileNameWithoutExtension(file);
        return PathExtensions.GetFileNameStripExtensions(filepath,
            AllFileExtensionsString.OrderByDescending(s => s.Length), out _);
    }

    public static string GetFileNameStripExtensions(string filepath, out string strippedExtension)
    {
        //if (file.EndsWith(TemporaryFileAppend)) file = Path.GetFileNameWithoutExtension(file);
        return PathExtensions.GetFileNameStripExtensions(filepath,
            AllFileExtensionsString.OrderByDescending(s => s.Length), out strippedExtension);
    }

    public static FileFormat? Open(string fileFullPath, FileDecodeType decodeType, OperationProgress? progress = null)
    {
        var slicerFile = FindByExtensionOrFilePath(fileFullPath, true);
        if (slicerFile is null) return null;
        slicerFile.Decode(fileFullPath, decodeType, progress);
        return slicerFile;
    }

    public static FileFormat? Open(string fileFullPath, OperationProgress? progress = null) =>
        Open(fileFullPath, FileDecodeType.Full, progress);

    public static Task<FileFormat?> OpenAsync(string fileFullPath, FileDecodeType decodeType,
        OperationProgress? progress = null)
        => Task.Run(() => Open(fileFullPath, decodeType, progress), progress?.Token ?? default);

    public static Task<FileFormat?> OpenAsync(string fileFullPath, OperationProgress? progress = null) =>
        OpenAsync(fileFullPath, FileDecodeType.Full, progress);

    public static float RoundDisplaySize(float value) => MathF.Round(value, DisplayFloatPrecision);
    public static double RoundDisplaySize(double value) => Math.Round(value, DisplayFloatPrecision);
    public static decimal RoundDisplaySize(decimal value) => Math.Round(value, DisplayFloatPrecision);

    /// <summary>
    /// Copy parameters from one file to another
    /// </summary>
    /// <param name="from">From source file</param>
    /// <param name="to">To target file</param>
    /// <returns>Number of affected parameters</returns>
    public static uint CopyParameters(FileFormat from, FileFormat to)
    {
        if (ReferenceEquals(from, to)) return 0;
        if (!from.SupportGlobalPrintParameters || !to.SupportGlobalPrintParameters) return 0;

        uint count = 0;

        to.RefreshPrintParametersModifiersValues();
        var targetPrintModifier = to.PrintParameterModifiers.AsValueEnumerable().ToArray();
        from.RefreshPrintParametersModifiersValues();
        foreach (var sourceModifier in from.PrintParameterModifiers)
        {
            if (!targetPrintModifier.AsValueEnumerable().Contains(sourceModifier)) continue;

            var fromValueObj = from.GetValueFromPrintParameterModifier(sourceModifier);
            var toValueObj = to.GetValueFromPrintParameterModifier(sourceModifier);

            if (fromValueObj is null || toValueObj is null) continue;
            var fromValue = System.Convert.ToDecimal(fromValueObj);
            var toValue = System.Convert.ToDecimal(toValueObj);
            if (fromValue != toValue)
            {
                to.SetValueFromPrintParameterModifier(sourceModifier, fromValue);
                count++;
            }
        }

        return count;
    }

    public static byte[] EncodeImage(string dataType, Mat mat)
    {
        dataType = dataType.ToUpperInvariant();
        if (dataType
            is DATATYPE_PNG
            or DATATYPE_JPG
            or DATATYPE_JPEG
            or DATATYPE_JP2
            or DATATYPE_BMP
            or DATATYPE_TIF
            or DATATYPE_TIFF
            or DATATYPE_PPM
            or DATATYPE_PMG
            or DATATYPE_SR
            or DATATYPE_RAS
           )
        {
            return CvInvoke.Imencode($".{dataType.ToLowerInvariant()}", mat);
        }

        if (dataType
            is DATATYPE_RGB555
            or DATATYPE_RGB565
            or DATATYPE_RGB555_BE
            or DATATYPE_RGB565_BE
            or DATATYPE_RGB888

            or DATATYPE_BGR555
            or DATATYPE_BGR565
            or DATATYPE_BGR555_BE
            or DATATYPE_BGR565_BE
            or DATATYPE_BGR888
           )
        {
            var bytesPerPixel = dataType is "RGB888" or "BGR888" ? 3 : 2;
            var bytes = new byte[mat.Width * mat.Height * bytesPerPixel];
            int index = 0;
            var span = mat.GetDataByteReadOnlySpan();
            for (int i = 0; i < span.Length;)
            {
                byte b = span[i++];
                byte g;
                byte r;

                if (mat.NumberOfChannels == 1) // 8 bit safe-guard
                {
                    r = g = b;
                }
                else
                {
                    g = span[i++];
                    r = span[i++];
                }

                if (mat.NumberOfChannels == 4) i++; // skip alpha

                switch (dataType)
                {
                    case DATATYPE_RGB555:
                        var rgb555 = (ushort)(((r & 0b11111000) << 7) | ((g & 0b11111000) << 2) | (b >> 3));
                        BitExtensions.ToBytesLittleEndian(rgb555, bytes, index);
                        index += 2;
                        break;
                    case DATATYPE_RGB565:
                        var rgb565 = (ushort)(((r & 0b11111000) << 8) | ((g & 0b11111100) << 3) | (b >> 3));
                        BitExtensions.ToBytesLittleEndian(rgb565, bytes, index);
                        index += 2;
                        break;
                    case DATATYPE_RGB555_BE:
                        var rgb555Be = (ushort)(((r & 0b11111000) << 7) | ((g & 0b11111000) << 2) | (b >> 3));
                        BitExtensions.ToBytesBigEndian(rgb555Be, bytes, index);
                        index += 2;
                        break;
                    case DATATYPE_RGB565_BE:
                        var rgb565Be = (ushort)(((r & 0b11111000) << 8) | ((g & 0b11111100) << 3) | (b >> 3));
                        BitExtensions.ToBytesBigEndian(rgb565Be, bytes, index);
                        index += 2;
                        break;
                    case DATATYPE_RGB888:
                        bytes[index++] = r;
                        bytes[index++] = g;
                        bytes[index++] = b;
                        break;
                    case DATATYPE_BGR555:
                        var bgr555 = (ushort)(((b & 0b11111000) << 7) | ((g & 0b11111000) << 2) | (r >> 3));
                        BitExtensions.ToBytesLittleEndian(bgr555, bytes, index);
                        index += 2;
                        break;
                    case DATATYPE_BGR565:
                        var bgr565 = (ushort)(((b & 0b11111000) << 8) | ((g & 0b11111100) << 3) | (r >> 3));
                        BitExtensions.ToBytesLittleEndian(bgr565, bytes, index);
                        index += 2;
                        break;
                    case DATATYPE_BGR555_BE:
                        var bgr555Be = (ushort)(((b & 0b11111000) << 7) | ((g & 0b11111000) << 2) | (r >> 3));
                        BitExtensions.ToBytesBigEndian(bgr555Be, bytes, index);
                        index += 2;
                        break;
                    case DATATYPE_BGR565_BE:
                        var bgr565Be = (ushort)(((b & 0b11111000) << 8) | ((g & 0b11111100) << 3) | (r >> 3));
                        BitExtensions.ToBytesBigEndian(bgr565Be, bytes, index);
                        index += 2;
                        break;
                    case DATATYPE_BGR888:
                        bytes[index++] = b;
                        bytes[index++] = g;
                        bytes[index++] = r;
                        break;
                }
            }

            return bytes;
        }

        throw new NotSupportedException($"The encode type: {dataType} is not supported.");
    }

    public static Mat DecodeImage(string dataType, byte[] bytes, Size resolution)
    {
        if (dataType
            is DATATYPE_PNG
            or DATATYPE_JPG
            or DATATYPE_JPEG
            or DATATYPE_JP2
            or DATATYPE_BMP
            or DATATYPE_TIF
            or DATATYPE_TIFF
            or DATATYPE_PPM
            or DATATYPE_PMG
            or DATATYPE_SR
            or DATATYPE_RAS
           )
        {
            var mat = new Mat();
            CvInvoke.Imdecode(bytes, ImreadModes.Unchanged, mat);
            return mat;
        }

        if (dataType
            is DATATYPE_RGB555
            or DATATYPE_RGB565
            or DATATYPE_RGB555_BE
            or DATATYPE_RGB565_BE
            or DATATYPE_RGB888

            or DATATYPE_BGR555
            or DATATYPE_BGR565
            or DATATYPE_BGR555_BE
            or DATATYPE_BGR565_BE
            or DATATYPE_BGR888
           )
        {
            var mat = new Mat(resolution, DepthType.Cv8U, 3);
            var span = mat.GetDataByteSpan();
            var pixel = 0;
            int i = 0;
            while (i < bytes.Length && pixel < span.Length)
            {
                switch (dataType)
                {
                    case DATATYPE_RGB555:
                        ushort rgb555 = BitExtensions.ToUShortLittleEndian(bytes, i);
                        // 0b0rrrrrgggggbbbbb
                        span[pixel++] = (byte)((rgb555 & 0b00000000_00011111) << 3); // b
                        span[pixel++] = (byte)((rgb555 & 0b00000011_11100000) >> 2); // g
                        span[pixel++] = (byte)((rgb555 & 0b01111100_00000000) >> 7); // r
                        /*span[pixel++] = (byte)((rgb555 << 3) & 0b11111000); // b
                        span[pixel++] = (byte)((rgb555 >> 2) & 0b11111000); // g
                        span[pixel++] = (byte)((rgb555 >> 7) & 0b11111000); // r*/
                        i += 2;
                        break;
                    case DATATYPE_RGB565:
                        // 0brrrrrggggggbbbbb
                        ushort rgb565 = BitExtensions.ToUShortLittleEndian(bytes, i);
                        span[pixel++] = (byte)((rgb565 & 0b00000000_00011111) << 3); // b
                        span[pixel++] = (byte)((rgb565 & 0b00000111_11100000) >> 3); // g
                        span[pixel++] = (byte)((rgb565 & 0b11111000_00000000) >> 8); // r
                        i += 2;
                        break;
                    case DATATYPE_RGB555_BE:
                        ushort rgb555Be = BitExtensions.ToUShortBigEndian(bytes, i);
                        span[pixel++] = (byte)((rgb555Be & 0b00000000_00011111) << 3); // b
                        span[pixel++] = (byte)((rgb555Be & 0b00000011_11100000) >> 2); // g
                        span[pixel++] = (byte)((rgb555Be & 0b01111100_00000000) >> 7); // r
                        i += 2;
                        break;
                    case DATATYPE_RGB565_BE:
                        ushort rgb565Be = BitExtensions.ToUShortBigEndian(bytes, i);
                        span[pixel++] = (byte)((rgb565Be & 0b00000000_00011111) << 3); // b
                        span[pixel++] = (byte)((rgb565Be & 0b00000111_11100000) >> 3); // g
                        span[pixel++] = (byte)((rgb565Be & 0b11111000_00000000) >> 8); // r
                        i += 2;
                        break;
                    case DATATYPE_RGB888:
                        span[pixel++] = bytes[i + 2]; // b
                        span[pixel++] = bytes[i + 1]; // g
                        span[pixel++] = bytes[i]; // r
                        i += 3;
                        break;
                    case DATATYPE_BGR555:
                        ushort bgr555 = BitExtensions.ToUShortLittleEndian(bytes, i);
                        span[pixel++] = (byte)((bgr555 & 0b01111100_00000000) >> 7); // b
                        span[pixel++] = (byte)((bgr555 & 0b00000011_11100000) >> 2); // g
                        span[pixel++] = (byte)((bgr555 & 0b00000000_00011111) << 3); // r
                        i += 2;
                        break;
                    case DATATYPE_BGR565:
                        ushort bgr565 = BitExtensions.ToUShortLittleEndian(bytes, i);
                        span[pixel++] = (byte)((bgr565 & 0b11111000_00000000) >> 8); // b
                        span[pixel++] = (byte)((bgr565 & 0b00000111_11100000) >> 3); // g
                        span[pixel++] = (byte)((bgr565 & 0b00000000_00011111) << 3); // r
                        i += 2;
                        break;
                    case DATATYPE_BGR555_BE:
                        ushort bgr555Be = BitExtensions.ToUShortBigEndian(bytes, i);
                        span[pixel++] = (byte)((bgr555Be & 0b01111100_00000000) >> 7); // b
                        span[pixel++] = (byte)((bgr555Be & 0b00000011_11100000) >> 2); // g
                        span[pixel++] = (byte)((bgr555Be & 0b00000000_00011111) << 3); // r
                        i += 2;
                        break;
                    case DATATYPE_BGR565_BE:
                        ushort bgr565Be = BitExtensions.ToUShortBigEndian(bytes, i);
                        span[pixel++] = (byte)((bgr565Be & 0b11111000_00000000) >> 8); // b
                        span[pixel++] = (byte)((bgr565Be & 0b00000111_11100000) >> 3); // g
                        span[pixel++] = (byte)((bgr565Be & 0b00000000_00011111) << 3); // r
                        i += 2;
                        break;
                    case DATATYPE_BGR888:
                        span[pixel++] = bytes[i]; // b
                        span[pixel++] = bytes[i + 1]; // g
                        span[pixel++] = bytes[i + 2]; // r
                        i += 3;
                        break;
                }
            }

            var diff = span.Length - pixel;
            if (diff > 0) // Fill leftovers
            {
                mat.GetDataByteSpan(diff, pixel).Clear();
            }

            return mat;
        }

        throw new NotSupportedException($"The decode type: {dataType} is not supported.");
    }

    public static Mat DecodeImage(string dataType, byte[] bytes, uint resolutionX, uint resolutionY)
        => DecodeImage(dataType, bytes, new Size((int)resolutionX, (int)resolutionY));

    public static byte[] EncodeChituImageRGB15Rle(Mat image)
    {
        const ushort REPEATRGB15MASK = 0x20;
        const ushort RLE16EncodingLimit = 0xFFF;

        var rle = new List<byte>();
        var span = image.GetDataByteReadOnlySpan();

        ushort color15 = 0;
        uint rep = 0;

        void RleRGB15()
        {
            switch (rep)
            {
                case 0:
                    return;
                case 1:
                    rle.Add((byte)(color15 & ~REPEATRGB15MASK));
                    rle.Add((byte)((color15 & ~REPEATRGB15MASK) >> 8));
                    return;
                case 2:
                    for (int i = 0; i < 2; i++)
                    {
                        rle.Add((byte)(color15 & ~REPEATRGB15MASK));
                        rle.Add((byte)((color15 & ~REPEATRGB15MASK) >> 8));
                    }

                    return;
                default:
                    rle.Add((byte)(color15 | REPEATRGB15MASK));
                    rle.Add((byte)((color15 | REPEATRGB15MASK) >> 8));
                    rle.Add((byte)((rep - 1) | 0x3000));
                    rle.Add((byte)(((rep - 1) | 0x3000) >> 8));
                    return;
            }
        }

        int pixel = 0;
        while (pixel < span.Length)
        {
            byte b = span[pixel++];
            byte g;
            byte r;

            if (image.NumberOfChannels == 1) // 8 bit safe-guard
            {
                r = g = b;
            }
            else
            {
                g = span[pixel++];
                r = span[pixel++];
            }

            if (image.NumberOfChannels == 4) pixel++; // skip alpha

            ushort ncolor15 = (ushort)((b >> 3) | ((g >> 2) << 5) | ((r >> 3) << 11));

            if (ncolor15 == color15)
            {
                rep++;
                if (rep == RLE16EncodingLimit)
                {
                    RleRGB15();
                    rep = 0;
                }
            }
            else
            {
                RleRGB15();
                color15 = ncolor15;
                rep = 1;
            }
        }

        RleRGB15();

        return rle.ToArray();
    }

    public static Mat DecodeChituImageRGB15Rle(byte[] rle, Size resolution)
    {
        const ushort REPEATRGB15MASK = 0x20;

        var mat = new Mat(resolution, DepthType.Cv8U, 3);
        var span = mat.GetDataByteSpan();

        int pixel = 0;
        for (uint i = 0; i < rle.Length; i++)
        {

            ushort dot = BitExtensions.ToUShortLittleEndian(rle[i], rle[++i]);
            byte red = (byte)(((dot >> 11) & 0x1F) << 3);
            byte green = (byte)(((dot >> 6) & 0x1F) << 3);
            byte blue = (byte)((dot & 0x1F) << 3);
            int repeat = 1;
            if ((dot & REPEATRGB15MASK) == REPEATRGB15MASK)
            {
                repeat += rle[++i] & 0xFF | ((rle[++i] & 0x0F) << 8);
            }


            for (int n = 0; n < repeat; n++)
            {
                span[pixel++] = blue;
                span[pixel++] = green;
                span[pixel++] = red;
            }
        }

        var diff = span.Length - pixel;
        if (diff > 0) // Fill leftovers
        {
            mat.GetDataByteSpan(diff, pixel).Clear();
        }

        return mat;
    }

    public static Mat DecodeChituImageRGB15Rle(byte[] rle, uint resolutionX, uint resolutionY)
        => DecodeChituImageRGB15Rle(rle, new Size((int)resolutionX, (int)resolutionY));

    public static void MutateGetVarsIterationChamfer(uint startLayerIndex, uint endLayerIndex, int iterationsStart,
        int iterationsEnd, ref bool isFade, out float iterationSteps, out int maxIteration)
    {
        iterationSteps = 0;
        maxIteration = 0;
        isFade = isFade && startLayerIndex != endLayerIndex && iterationsStart != iterationsEnd;
        if (!isFade) return;
        iterationSteps = Math.Abs((iterationsStart - (float)iterationsEnd) / ((float)endLayerIndex - startLayerIndex));
        maxIteration = Math.Max(iterationsStart, iterationsEnd);
    }

    public static int MutateGetIterationVar(bool isFade, int iterationsStart, int iterationsEnd, float iterationSteps,
        int maxIteration, uint startLayerIndex, uint layerIndex)
    {
        if (!isFade) return iterationsStart;
        // calculate iterations based on range
        int iterations = (int)(iterationsStart < iterationsEnd
            ? iterationsStart + (layerIndex - startLayerIndex) * iterationSteps
            : iterationsStart - (layerIndex - startLayerIndex) * iterationSteps);

        // constrain
        return Math.Min(Math.Max(0, iterations), maxIteration);
    }

    public static int MutateGetIterationChamfer(uint layerIndex, uint startLayerIndex, uint endLayerIndex,
        int iterationsStart,
        int iterationsEnd, bool isFade)
    {
        MutateGetVarsIterationChamfer(startLayerIndex, endLayerIndex, iterationsStart, iterationsEnd, ref isFade,
            out float iterationSteps, out int maxIteration);
        return MutateGetIterationVar(isFade, iterationsStart, iterationsEnd, iterationSteps, maxIteration,
            startLayerIndex, layerIndex);
    }

    /// <summary>
    /// Compares file a with file b
    /// </summary>
    /// <param name="left">Left file</param>
    /// <param name="right">Right file</param>
    /// <param name="compareLayers">True if you also want to compare layers</param>
    /// <param name="onlyProperties">A list of strict properties to compare</param>
    /// <returns></returns>
    public static FileFormatComparison Compare(FileFormat left, FileFormat right, bool compareLayers = true,
        params string[] onlyProperties)
    {
        FileFormatComparison comparison = new();

        void CheckAddProperties(object a, object b, uint? layerIndex = null)
        {
            var properties = ReflectionExtensions.GetProperties(a);
            foreach (var aProperty in properties)
            {
                if (aProperty.GetMethod is null || !aProperty.PropertyType.IsPrimitive()) continue;

                if (onlyProperties.Length > 0 && !onlyProperties.AsValueEnumerable().Contains(aProperty.Name)) continue;
                if (aProperty.Name.Contains("File", StringComparison.OrdinalIgnoreCase)) continue;


                var bProperty = b.GetType().GetProperty(aProperty.Name);
                if (bProperty is null) continue;

                var aValue = aProperty.GetValue(a);
                var bValue = bProperty.GetValue(b);

                if (Equals(aValue, bValue)) continue;

                if (layerIndex is null)
                {
                    comparison.Global.Add(new ComparisonItem(aProperty.Name, aValue, bValue));
                }
                else
                {
                    if (!comparison.Layers.TryGetValue(layerIndex.Value, out var list))
                    {
                        list = [];
                        comparison.Layers.Add(layerIndex.Value, list);
                    }

                    list.Add(new ComparisonItem(aProperty.Name, aValue, bValue));
                }
            }
        }

        CheckAddProperties(left, right);


        if (compareLayers)
        {
            var commonLayers = Math.Min(left.LayerCount, right.LayerCount);
            //var diffLayers = Math.Abs(a.Count - b.Count);
            for (uint layerIndex = 0; layerIndex < commonLayers; layerIndex++)
            {
                CheckAddProperties(left[layerIndex], right[layerIndex], layerIndex);
            }
        }

        return comparison;
    }

    /// <summary>
    /// Checks if a filename is valid or not
    /// </summary>
    /// <param name="filename">The file name only, without path</param>
    /// <param name="errorMessage">The error message to return</param>
    /// <param name="onlyAsciiCharacters">If true, the <paramref name="filename"/> must contain only ASCII characters.</param>
    /// <returns>True if filename is valid, otherwise false.</returns>
    public static bool IsFileNameValid(string filename, out string errorMessage, bool onlyAsciiCharacters = false)
    {
        errorMessage = string.Empty;

        var invalidFileNameChars = Path.GetInvalidFileNameChars();
        var invalidChars = filename.AsValueEnumerable().Where(c => invalidFileNameChars.AsValueEnumerable().Contains(c)).Distinct();

        if (invalidChars.Any())
        {
            errorMessage =
                $"The file \"{filename}\" have invalid characters.\nThe following in-name characters are forbidden: {invalidChars.JoinToString(", ")}.";
            return false;
        }

        if (onlyAsciiCharacters)
        {
            var nonAscii = filename.AsValueEnumerable().Where(c => !char.IsAscii(c)).Distinct();
            if (nonAscii.Any())
            {
                errorMessage =
                    $"The file \"{filename}\" have non-ASCII characters.\nThe following in-name characters are not allowed: {nonAscii.JoinToString(", ")}.";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if a filename is valid or not
    /// </summary>
    /// <param name="filename">The file name only, without path</param>
    /// <param name="onlyAsciiCharacters">If true, the <paramref name="filename"/> must contain only ASCII characters.</param>
    /// <returns></returns>
    public static bool IsFileNameValid(string filename, bool onlyAsciiCharacters = false)
    {
        return IsFileNameValid(filename, out _, onlyAsciiCharacters);
    }

    #endregion

    #region Members

    public Lock Mutex = new();

    private string? _fileFullPath;

    protected ImageFormat _layerImageFormat = ImageFormat.Custom;

    protected Layer[] _layers = [];

    private bool _haveModifiedLayers;
    private uint _version;

    private uint _resolutionX;
    private uint _resolutionY;
    private float _displayWidth;
    private float _displayHeight;

    private byte _antiAliasing = 1;

    private float _layerHeight = DefaultLayerHeight;

    private ushort _bottomLayerCount = DefaultBottomLayerCount;
    private ushort _transitionLayerCount = DefaultTransitionLayerCount;


    private float _bottomLightOffDelay;
    private float _lightOffDelay;

    private float _bottomWaitTimeBeforeCure;
    private float _waitTimeBeforeCure;

    private float _bottomExposureTime = DefaultBottomExposureTime;
    private float _exposureTime = DefaultExposureTime;

    private float _bottomWaitTimeAfterCure;
    private float _waitTimeAfterCure;

    private float _bottomLiftHeight = DefaultBottomLiftHeight;
    private float _liftHeight = DefaultLiftHeight;
    private float _bottomLiftSpeed = DefaultBottomLiftSpeed;
    private float _bottomLiftAcceleration;
    private float _liftSpeed = DefaultLiftSpeed;
    private float _liftAcceleration;

    private float _bottomLiftHeight2 = DefaultBottomLiftHeight2;
    private float _liftHeight2 = DefaultLiftHeight2;
    private float _bottomLiftSpeed2 = DefaultBottomLiftSpeed2;
    private float _bottomLiftAcceleration2;
    private float _liftSpeed2 = DefaultLiftSpeed2;
    private float _liftAcceleration2;

    private float _bottomWaitTimeAfterLift;
    private float _waitTimeAfterLift;

    private float _bottomRetractHeight2 = DefaultBottomRetractHeight2;
    private float _retractHeight2 = DefaultRetractHeight2;
    private float _bottomRetractSpeed2 = DefaultBottomRetractSpeed2;
    private float _bottomRetractAcceleration2;
    private float _retractSpeed2 = DefaultRetractSpeed2;
    private float _retractAcceleration2;
    private float _bottomRetractSpeed = DefaultBottomRetractSpeed;
    private float _bottomRetractAcceleration;
    private float _retractSpeed = DefaultRetractSpeed;
    private float _retractAcceleration;


    private byte _bottomLightPwm = DefaultBottomLightPWM;
    private byte _lightPwm = DefaultLightPWM;

    private float _printTime;
    private float _materialMilliliters;
    private float _machineZ;
    private string _machineName = "Unknown";
    private string? _materialName;
    private float _materialGrams;
    private float _materialCost;
    private bool _suppressRebuildGCode;

    private Rectangle _boundingRectangle = Rectangle.Empty;

    private readonly Timer _queueTimerPrintTime = new(QueueTimerPrintTime) { AutoReset = false };

    #endregion

    #region Properties

    public FileDecodeType DecodeType { get; private set; } = FileDecodeType.Full;

    /// <summary>
    /// Gets the file format type
    /// </summary>
    public abstract FileFormatType FileType { get; }

    /// <summary>
    /// Gets the manufacturing process this file and printer uses
    /// </summary>
    public virtual PrinterManufacturingProcess ManufacturingProcess =>
        MachineName.Contains(" DLP", StringComparison.OrdinalIgnoreCase)
            ? PrinterManufacturingProcess.DLP
            : PrinterManufacturingProcess.mSLA;

    /// <summary>
    /// Gets the layer image format type used by this file format
    /// </summary>
    public ImageFormat LayerImageFormat
    {
        get => _layerImageFormat;
        set => RaiseAndSetIfChanged(ref _layerImageFormat, value);
    }

    /// <summary>
    /// Gets the group name under convert menu to group all extensions, set to null to not group items
    /// </summary>
    public virtual string? ConvertMenuGroup => null;

    /// <summary>
    /// Gets the valid file extensions for this <see cref="FileFormat"/>
    /// </summary>
    public abstract FileExtension[] FileExtensions { get; }

    /// <summary>
    /// The speed unit used by this file format in his internal data
    /// </summary>
    public virtual SpeedUnit FormatSpeedUnit => CoreSpeedUnit;

    /// <summary>
    /// Gets the available <see cref="PrintParameterModifier"/>
    /// </summary>
    public virtual PrintParameterModifier[] PrintParameterModifiers => [];

    /// <summary>
    /// Gets the available <see cref="PrintParameterModifier"/> per layer
    /// </summary>
    public virtual PrintParameterModifier[] PrintParameterPerLayerModifiers => [];

    /// <summary>
    /// Checks if a <see cref="PrintParameterModifier"/> exists on print parameters
    /// </summary>
    /// <param name="modifier"></param>
    /// <returns>True if exists, otherwise false</returns>
    public bool HavePrintParameterModifier(PrintParameterModifier modifier) =>
        PrintParameterModifiers.AsValueEnumerable().Contains(modifier);

    /// <summary>
    /// Checks if a <see cref="PrintParameterModifier"/> exists on layer parameters
    /// </summary>
    /// <param name="modifier"></param>
    /// <returns>True if exists, otherwise false</returns>
    public bool HaveLayerParameterModifier(PrintParameterModifier modifier) =>
        SupportPerLayerSettings && PrintParameterPerLayerModifiers.AsValueEnumerable().Contains(modifier);

    /// <summary>
    /// Gets the file filter for open and save dialogs
    /// </summary>
    public string FileFilter
    {
        get
        {
            var result = string.Empty;

            foreach (var fileExt in FileExtensions)
            {
                if (!ReferenceEquals(result, string.Empty))
                {
                    result += '|';
                }

                result += fileExt.Filter;
            }

            return result;
        }
    }

    /// <summary>
    /// Gets all valid file extensions for Avalonia file dialog
    /// </summary>
    public IEnumerable<KeyValuePair<string, List<string>>> FileFilterAvalonia
        => FileExtensions.Select(fileExt =>
            new KeyValuePair<string, List<string>>(fileExt.Description, [fileExt.Extension]));

    /// <summary>
    /// Gets all valid file extensions in "*.extension1;*.extension2" format
    /// </summary>
    public string FileFilterExtensionsOnly
    {
        get
        {
            var result = string.Empty;

            foreach (var fileExt in FileExtensions)
            {
                if (!ReferenceEquals(result, string.Empty))
                {
                    result += "; ";
                }

                result += $"*.{fileExt.Extension}";
            }

            return result;
        }
    }

    /// <summary>
    /// Gets or sets if change a global property should rebuild every layer data based on them
    /// </summary>
    public bool SuppressRebuildProperties { get; set; }

    /// <summary>
    /// Gets the temporary output file path to use on save and encode
    /// </summary>
    public string TemporaryOutputFileFullPath => $"{FileFullPath}{TemporaryFileAppend}";

    /// <summary>
    /// Gets an instance of <see cref="FileInfo"/> with the current loaded file
    /// </summary>
    public FileInfo? FileInfo => _fileFullPath is not null ? new FileInfo(_fileFullPath) : null;

    /// <summary>
    /// Gets the input file path loaded into this <see cref="FileFormat"/>
    /// </summary>
    public string? FileFullPath
    {
        get => _fileFullPath;
        set
        {
            if (!RaiseAndSetIfChanged(ref _fileFullPath, value)) return;
            RaisePropertyChanged(DirectoryPath);
            RaisePropertyChanged(Filename);
            RaisePropertyChanged(FilenameNoExt);
            RaisePropertyChanged(FilenameStripExtensions);
            RaisePropertyChanged(FileExtension);
            RaisePropertyChanged(FileAbsoluteExtension);
        }
    }

    public string? FileFullPathNoExt => Path.Combine(DirectoryPath!, FilenameNoExt!);

    public string? DirectoryPath => Path.GetDirectoryName(FileFullPath);
    public string? Filename => Path.GetFileName(FileFullPath);

    /// <summary>
    /// Returns the file name without the extension
    /// </summary>
    public string? FilenameNoExt => GetFileNameStripExtensions(FileFullPath);

    /// <summary>
    /// Returns the file name without the extension(s)
    /// </summary>
    public string? FilenameStripExtensions =>
        FileFullPath is null ? null : GetFileNameStripExtensions(FileFullPath, out _);

    /// <summary>
    /// Returns the file extension. The returned value includes the period (".") character of the
    /// extension except when you have a terminal period when you get string.Empty, such as ".exe" or ".cpp".
    /// The returned value is null if the given path is null or empty if the given path does not include an
    /// extension.
    /// </summary>
    public string? FileExtension => Path.GetExtension(FileFullPath);

    /// <summary>
    /// Returns the file extension as safe method where it can include more than one extension. The returned value includes the period (".") character of the
    /// extension except when you have a terminal period when you get string.Empty, such as ".exe" or ".cpp".
    /// The returned value is null if the given path is null or empty if the given path does not include an
    /// extension.
    /// </summary>
    public string? FileAbsoluteExtension
    {
        get
        {
            if (FileFullPath is null) return null;
            GetFileNameStripExtensions(FileFullPath, out var ext);
            return ext;
        }
    }

    /// <summary>
    /// Gets the available versions to set in this file format
    /// </summary>
    public virtual uint[] AvailableVersions => [];

    /// <summary>
    /// Gets the amount of available versions in this file format
    /// </summary>
    public virtual byte AvailableVersionsCount => (byte)AvailableVersions.Length;

    /// <summary>
    /// Gets the available versions to set in this file format given it own extension
    /// </summary>
    /// <returns></returns>
    public uint[] GetAvailableVersionsForExtension() => GetAvailableVersionsForExtension(FileExtension);

    /// <summary>
    /// Gets the available versions to set in this file format for the given extension
    /// </summary>
    /// <param name="extension">Extension name, with or without dot (.)</param>
    /// <returns></returns>
    public virtual uint[] GetAvailableVersionsForExtension(string? extension) => AvailableVersions;

    /// <summary>
    /// Gets the available versions to set in this file format for the given file name
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns></returns>
    public uint[] GetAvailableVersionsForFileName(string? fileName) =>
        GetAvailableVersionsForExtension(Path.GetExtension(fileName));

    /// <summary>
    /// Gets the default version to use in this file when not setting the version
    /// </summary>
    public virtual uint DefaultVersion => 0;

    /// <summary>
    /// Gets or sets the version of this file format
    /// </summary>
    public virtual uint Version
    {
        get => _version;
        set
        {
            if (AvailableVersions.Length > 0 && !AvailableVersions.AsValueEnumerable().Contains(value))
            {
                throw new VersionNotFoundException($"Version {value} not known for this file format");
            }

            RequireFullEncode = true;
            RaiseAndSetIfChanged(ref _version, value);
        }
    }

    /// <summary>
    /// Gets the original thumbnail sizes
    /// </summary>
    public virtual Size[] ThumbnailsOriginalSize => [];

    /// <summary>
    /// Gets the number of thumbnails the file should have
    /// </summary>
    public int ThumbnailCountFileShouldHave => ThumbnailsOriginalSize.Length;

    /// <summary>
    /// Gets the number of thumbnails possible to encode on this file based on current available <see cref="Thumbnails"/> and <see cref="ThumbnailsOriginalSize"/>
    /// </summary>
    public int ThumbnailEncodeCount => Math.Min(ThumbnailsOriginalSize.Length, ThumbnailsCount);

    /// <summary>
    /// Gets the thumbnails count present in this file format
    /// </summary>
    public int ThumbnailsCount => Thumbnails.Count;

    /// <summary>
    /// Gets if this file have any valid thumbnail
    /// </summary>
    public bool HaveThumbnails => ThumbnailsCount > 0;

    /// <summary>
    /// Gets the thumbnails for this <see cref="FileFormat"/>
    /// </summary>
    public List<Mat> Thumbnails { get; } = [];

    public IssueManager IssueManager { get; }

    /// <summary>
    /// Layers List
    /// </summary>
    public Layer[] Layers
    {
        get => _layers;
        set
        {
            Guard.IsNotNull(value, nameof(Layers));

            //if (ReferenceEquals(_layers, value)) return;

            var rebuildProperties = false;
            var oldLayerCount = LayerCount;
            var oldLayers = _layers;
            _layers = value;
            BoundingRectangle = Rectangle.Empty;

            if (LayerCount != oldLayerCount)
            {
                LayerCount = (uint)_layers.Length;
            }

            RequireFullEncode = true;

            if (HaveLayers)
            {
                //SetAllIsModified(true);

                for (uint layerIndex = 0; layerIndex < LayerCount; layerIndex++) // Forced sanitize
                {
                    // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                    if (_layers[layerIndex] is null) // Make sure no layer is null
                    {
                        _layers[layerIndex] = new Layer(layerIndex, this)
                        {
                            IsModified = true
                        };
                    }
                    else
                    {
                        _layers[layerIndex].Index = layerIndex;
                        _layers[layerIndex].SlicerFile = this;

                        if (layerIndex >= oldLayerCount || layerIndex < oldLayerCount &&
                            !_layers[layerIndex].Equals(oldLayers[layerIndex]))
                        {
                            // Marks as modified only if layer image changed on this index
                            _layers[layerIndex].IsModified = true;
                        }
                    }
                }

                if (LayerCount != oldLayerCount && !SuppressRebuildProperties && LastLayer is not null)
                {
                    RebuildLayersProperties();
                    rebuildProperties = true;
                }
            }

#pragma warning disable CA2245
            PrintHeight = PrintHeight;
#pragma warning restore CA2245
            UpdatePrintTime();

            if (!rebuildProperties)
            {
                MaterialMilliliters = -1;
                RebuildGCode();
            }

            RaisePropertyChanged();
            RaisePropertyChanged(nameof(HaveLayers));
        }
    }

    /// <summary>
    /// Gets the layers cache/memory occupation size in bytes
    /// </summary>
    public long LayersCacheSize {
        get
        {
            long size = 0;
            for (int i = 0; i < LayerCount; i++)
            {
                if (this[i] is null) continue;
                size += this[i].CompressedMat.Length;
            }
            return size;
        }
    }

    /// <summary>
    /// Gets the layers cache/memory occupation size in readable string format
    /// </summary>
    public string LayersCacheSizeString => SizeExtensions.SizeSuffix(LayersCacheSize);

    /// <summary>
    /// First layer index, this is always 0
    /// </summary>
    public const uint FirstLayerIndex = 0;

    /// <summary>
    /// Gets the last layer index
    /// </summary>
    public uint LastLayerIndex => HaveLayers ? LayerCount - 1 : 0;

    /// <summary>
    /// Gets the first layer
    /// </summary>
    public Layer? FirstLayer => HaveLayers ? this[0] : null;

    /// <summary>
    /// Gets the last bottom layer
    /// </summary>
    public Layer? LastBottomLayer => this.AsValueEnumerable().LastOrDefault(layer => layer.IsBottomLayer);

    /// <summary>
    /// Gets the first transition layer
    /// </summary>
    public Layer? FirstTransitionLayer => TransitionLayerCount == 0 ? null : this[BottomLayerCount];

    /// <summary>
    /// Gets the last transition layer
    /// </summary>
    public Layer? LastTransitionLayer => TransitionLayerCount == 0 ? null : this[BottomLayerCount + TransitionLayerCount - 1];

    /// <summary>
    /// Gets the first normal layer
    /// </summary>
    public Layer? FirstNormalLayer => this.AsValueEnumerable().FirstOrDefault(layer => layer.IsNormalLayer);

    /// <summary>
    /// Gets the last layer
    /// </summary>
    public Layer? LastLayer => HaveLayers ? this[^1] : null;

    /// <summary>
    /// Gets the smallest bottom layer using the pixel count
    /// </summary>
    public Layer? SmallestBottomLayer => this.AsValueEnumerable().Where(layer => layer is {IsBottomLayer: true, IsEmpty: false}).MinBy(layer => layer.NonZeroPixelCount);

    /// <summary>
    /// Gets the largest bottom layer using the pixel count
    /// </summary>
    public Layer? LargestBottomLayer => this.AsValueEnumerable().Where(layer => layer is {IsBottomLayer: true, IsEmpty: false}).MaxBy(layer => layer.NonZeroPixelCount);

    /// <summary>
    /// Gets the smallest normal layer using the pixel count
    /// </summary>
    public Layer? SmallestNormalLayer => this.AsValueEnumerable().Where(layer => layer is {IsNormalLayer: true, IsEmpty: false}).MinBy(layer => layer.NonZeroPixelCount);

    /// <summary>
    /// Gets the largest layer using the pixel count
    /// </summary>
    public Layer? LargestNormalLayer => this.AsValueEnumerable().Where(layer => layer is {IsNormalLayer: true, IsEmpty: false}).MaxBy(layer => layer.NonZeroPixelCount);

    /// <summary>
    /// Gets the smallest normal layer using the pixel count
    /// </summary>
    public Layer? SmallestLayer => this.AsValueEnumerable().Where(layer => !layer.IsEmpty).MinBy(layer => layer.NonZeroPixelCount);

    /// <summary>
    /// Gets the largest layer using the pixel count
    /// </summary>
    public Layer? LargestLayer => this.AsValueEnumerable().MaxBy(layer => layer.NonZeroPixelCount);

    public Layer? GetSmallestLayerBetween(uint layerStartIndex, uint layerEndIndex)
    {
        return this.AsValueEnumerable().Where((layer, index) => !layer.IsEmpty && index >= layerStartIndex && index <= layerEndIndex).MinBy(layer => layer.NonZeroPixelCount);
    }

    public Layer? GetLargestLayerBetween(uint layerStartIndex, uint layerEndIndex)
    {
        return this.AsValueEnumerable().Where((layer, index) => !layer.IsEmpty && index >= layerStartIndex && index <= layerEndIndex).MaxBy(layer => layer.NonZeroPixelCount);
    }

    /// <summary>
    /// Gets all bottom layers
    /// </summary>
    /// <returns></returns>
    public IEnumerable<Layer> BottomLayers => this.Where(layer => layer.IsBottomLayer);

    /// <summary>
    /// Gets all normal layers
    /// </summary>
    /// <returns></returns>
    public IEnumerable<Layer> NormalLayers => this.Where(layer => layer.IsNormalLayer);

    /// <summary>
    /// Gets all transition layers
    /// </summary>
    /// <returns></returns>
    public IEnumerable<Layer> TransitionLayers => this.Where(layer => layer.IsTransitionLayer);

    /// <summary>
    /// Gets all layers that use TSMC values
    /// </summary>
    /// <returns></returns>
    public IEnumerable<Layer> TsmcLayers => this.Where(layer => layer.IsUsingTSMC);

    /// <summary>
    /// Gets all layers on same position but exclude the first layer on that position
    /// </summary>
    public IEnumerable<Layer> SamePositionedLayers
    {
        get
        {
            for (int layerIndex = 1; layerIndex < LayerCount; layerIndex++)
            {
                var layer = this[layerIndex];
                if (this[layerIndex - 1].PositionZ != layer.PositionZ) continue;
                yield return layer;
            }
        }
    }

    public IEnumerable<Layer> GetDistinctLayersByPositionZ(uint layerIndexStart = 0) =>
        GetDistinctLayersByPositionZ(layerIndexStart, LastLayerIndex);

    public IEnumerable<Layer> GetDistinctLayersByPositionZ(uint layerIndexStart, uint layerIndexEnd)
    {
        return layerIndexEnd - layerIndexStart >= LastLayerIndex
            ? this.DistinctBy(layer => layer.PositionZ)
            : this.Where((_, layerIndex) => layerIndex >= layerIndexStart && layerIndex <= layerIndexEnd).DistinctBy(layer => layer.PositionZ);
    }

    public IEnumerable<Layer> GetLayersFromHeightRange(float startPositionZ, float endPositionZ)
    {
        return this.Where(layer => layer.PositionZ >= startPositionZ && layer.PositionZ <= endPositionZ);
    }

    public IEnumerable<Layer> GetLayersFromHeightRange(float endPositionZ)
    {
        return this.Where(layer => layer.PositionZ <= endPositionZ);
    }

    /// <summary>
    /// True if all layers are using same value parameters as global settings, otherwise false
    /// </summary>
    public bool AllLayersAreUsingGlobalParameters => this.AsValueEnumerable().All(layer => layer.IsUsingGlobalParameters);

    /// <summary>
    /// True if there are one or more layer(s) using different settings than the global settings, otherwise false
    /// <remarks>Same as <see cref="AllLayersAreUsingGlobalParameters"/> negated</remarks>
    /// </summary>
    public bool UsingPerLayerSettings => !AllLayersAreUsingGlobalParameters;

    /// <summary>
    /// True if any layer is using TSMC, otherwise false when none of layers is using TSMC
    /// </summary>
    public bool AnyLayerIsUsingTSMC => this.AsValueEnumerable().Any(layer => layer.IsUsingTSMC);

    /// <summary>
    /// True if the file global property is using TSMC, otherwise false when not using
    /// </summary>
    public bool IsUsingTSMC => (CanUseAnyLiftHeight2 || CanUseAnyRetractHeight2) && (BottomLiftHeight2 > 0 || BottomRetractHeight2 > 0 || LiftHeight2 > 0 || RetractHeight2 > 0);


    /// <summary>
    /// Gets if any layer got modified, otherwise false
    /// Sets all layers `IsModified` flag
    /// </summary>
    public bool IsModified
    {
        get
        {
            for (uint i = 0; i < LayerCount; i++)
            {
                if (this[i].IsModified) return true;
            }
            return false;
        }
        set
        {
            for (uint i = 0; i < LayerCount; i++)
            {
                this[i].IsModified = value;
            }
        }
    }

    /// <summary>
    /// Gets the bounding rectangle of the model
    /// </summary>
    public Rectangle BoundingRectangle
    {
        get => GetBoundingRectangle();
        set
        {
            if(!RaiseAndSetIfChanged(ref _boundingRectangle, value)) return;
            RaisePropertyChanged(nameof(BoundingRectangleMillimeters));
        }
    }

    /// <summary>
    /// Gets the bounding rectangle of the object in millimeters
    /// </summary>
    public RectangleF BoundingRectangleMillimeters
    {
        get
        {
            var pixelSize = PixelSize;
            var boundingRectangle = BoundingRectangle;
            return new RectangleF(
                MathF.Round(boundingRectangle.X * pixelSize.Width, 2),
                MathF.Round(boundingRectangle.Y * pixelSize.Height, 2),
                MathF.Round(boundingRectangle.Width * pixelSize.Width, 2),
                MathF.Round(boundingRectangle.Height * pixelSize.Height, 2));
        }
    }

    /// <summary>
    /// Gets or sets if modifications require a full encode to save
    /// </summary>
    public bool RequireFullEncode
    {
        get => _haveModifiedLayers || IsModified;
        set => RaiseAndSetIfChanged(ref _haveModifiedLayers, value);
    }

    /// <summary>
    /// Gets the image width and height resolution
    /// </summary>
    public Size Resolution
    {
        get => new((int)ResolutionX, (int)ResolutionY);
        set
        {
            ResolutionX = (uint) value.Width;
            ResolutionY = (uint) value.Height;
            RaisePropertyChanged();
        }
    }

    /// <summary>
    /// Gets the image width resolution
    /// </summary>
    public virtual uint ResolutionX
    {
        get => _resolutionX;
        set
        {
            if(!RaiseAndSetIfChanged(ref _resolutionX, value)) return;
            RaisePropertyChanged(nameof(Resolution));
            RaisePropertyChanged(nameof(ResolutionRectangle));
            RaisePropertyChanged(nameof(DisplayPixelCount));
            RaisePropertyChanged(nameof(DisplayAspectRatio));
            RaisePropertyChanged(nameof(DisplayAspectRatioStr));
            RaisePropertyChanged(nameof(IsDisplayPortrait));
            RaisePropertyChanged(nameof(IsDisplayLandscape));

            RaisePropertyChanged(nameof(Xppmm));
            RaisePropertyChanged(nameof(PixelWidth));
            RaisePropertyChanged(nameof(PixelWidthMicrons));

            NotifyAspectChange();
        }
    }

    /// <summary>
    /// Gets the image height resolution
    /// </summary>
    public virtual uint ResolutionY
    {
        get => _resolutionY;
        set
        {
            if(!RaiseAndSetIfChanged(ref _resolutionY, value)) return;
            RaisePropertyChanged(nameof(Resolution));
            RaisePropertyChanged(nameof(ResolutionRectangle));
            RaisePropertyChanged(nameof(DisplayPixelCount));
            RaisePropertyChanged(nameof(DisplayAspectRatio));
            RaisePropertyChanged(nameof(DisplayAspectRatioStr));
            RaisePropertyChanged(nameof(IsDisplayPortrait));
            RaisePropertyChanged(nameof(IsDisplayLandscape));

            RaisePropertyChanged(nameof(Yppmm));
            RaisePropertyChanged(nameof(PixelHeight));
            RaisePropertyChanged(nameof(PixelHeightMicrons));

            NotifyAspectChange();
        }
    }

    /// <summary>
    /// Gets an rectangle that starts at 0,0 and goes up to <see cref="Resolution"/>
    /// </summary>
    public Rectangle ResolutionRectangle => new(Point.Empty, Resolution);

    /// <summary>
    /// Gets the display total number of pixels (<see cref="ResolutionX"/> * <see cref="ResolutionY"/>)
    /// </summary>
    public uint DisplayPixelCount => ResolutionX * ResolutionY;

    /// <summary>
    /// Gets the size of display in millimeters
    /// </summary>
    public SizeF Display
    {
        get => new(DisplayWidth, DisplayHeight);
        set
        {
            DisplayWidth = RoundDisplaySize(value.Width);
            DisplayHeight = RoundDisplaySize(value.Height);
            RaisePropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets the display width in millimeters
    /// </summary>
    public virtual float DisplayWidth
    {
        get => _displayWidth;
        set
        {
            if (!RaiseAndSetIfChanged(ref _displayWidth, RoundDisplaySize(value))) return;
            RaisePropertyChanged(nameof(Display));
            RaisePropertyChanged(nameof(DisplayDiagonal));
            RaisePropertyChanged(nameof(DisplayDiagonalInches));
            RaisePropertyChanged(nameof(Xppmm));
            RaisePropertyChanged(nameof(PixelWidth));
            RaisePropertyChanged(nameof(PixelWidthMicrons));
            NotifyAspectChange();
        }
    }

    /// <summary>
    /// Gets or sets the display height in millimeters
    /// </summary>
    public virtual float DisplayHeight
    {
        get => _displayHeight;
        set
        {
            if(!RaiseAndSetIfChanged(ref _displayHeight, RoundDisplaySize(value))) return;
            RaisePropertyChanged(nameof(Display));
            RaisePropertyChanged(nameof(DisplayDiagonal));
            RaisePropertyChanged(nameof(DisplayDiagonalInches));
            RaisePropertyChanged(nameof(Yppmm));
            RaisePropertyChanged(nameof(PixelHeight));
            RaisePropertyChanged(nameof(PixelHeightMicrons));
            NotifyAspectChange();
        }
    }

    /// <summary>
    /// Gets the display diagonal in millimeters
    /// </summary>
    public float DisplayDiagonal => MathF.Round(MathF.Sqrt(MathF.Pow(DisplayWidth, 2) + MathF.Pow(DisplayHeight, 2)), 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Gets the display diagonal in inch's
    /// </summary>
    public float DisplayDiagonalInches => MathF.Round(MathF.Sqrt(MathF.Pow(DisplayWidth, 2) + MathF.Pow(DisplayHeight, 2)) * (float)UnitExtensions.MillimeterToInch, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Gets the display ratio
    /// </summary>
    public Size DisplayAspectRatio
    {
        get
        {
            var gcd = MathExtensions.GCD(ResolutionX, ResolutionY);
            return new((int)(ResolutionX / gcd), (int)(ResolutionY / gcd));
        }
    }

    public string DisplayAspectRatioStr
    {
        get
        {
            var aspect = DisplayAspectRatio;
            return $"{aspect.Width}:{aspect.Height}";
        }
    }

    /// <summary>
    /// Gets or sets if images need to be mirrored on lcd to print on the correct orientation
    /// </summary>
    public virtual FlipDirection DisplayMirror { get; set; } = FlipDirection.None;

    /// <summary>
    /// Gets if the display is in portrait mode
    /// </summary>
    public bool IsDisplayPortrait => ResolutionY > ResolutionX;

    /// <summary>
    /// Gets if the display is in landscape mode
    /// </summary>
    public bool IsDisplayLandscape => !IsDisplayPortrait;

    /// <summary>
    /// Gets or sets the maximum printer build Z volume
    /// </summary>
    public virtual float MachineZ
    {
        get => _machineZ > 0 ? _machineZ : PrintHeight;
        set => RaiseAndSetIfChanged(ref _machineZ, value);
    }

    /// <summary>
    /// Gets or sets the pixels per mm on X direction
    /// </summary>
    public float Xppmm => ResolutionX > 0 && DisplayWidth > 0 ? ResolutionX / DisplayWidth : 0;

    /// <summary>
    /// Gets or sets the pixels per mm on Y direction
    /// </summary>
    public float Yppmm => ResolutionY > 0 && DisplayHeight > 0 ? ResolutionY / DisplayHeight : 0;

    /// <summary>
    /// Gets or sets the pixels per mm
    /// </summary>
    public SizeF Ppmm => new(Xppmm, Yppmm);

    /// <summary>
    /// Gets the maximum (Width or Height) pixels per mm
    /// </summary>
    public float PpmmMax => Ppmm.Max();

    /// <summary>
    /// Gets the pixel width in millimeters
    /// </summary>
    public float PixelWidth => DisplayWidth > 0 && ResolutionX > 0 ? MathF.Round(DisplayWidth / ResolutionX, 4) : 0;

    /// <summary>
    /// Gets the pixel height in millimeters
    /// </summary>
    public float PixelHeight => DisplayHeight > 0 && ResolutionY > 0 ? MathF.Round(DisplayHeight / ResolutionY, 4) : 0;

    /// <summary>
    /// Gets the pixel size in millimeters
    /// </summary>
    public SizeF PixelSize => new(PixelWidth, PixelHeight);

    /// <summary>
    /// Gets the maximum pixel between width and height in millimeters
    /// </summary>
    public float PixelSizeMax => PixelSize.Max();

    /// <summary>
    /// Gets the pixel area in millimeters
    /// </summary>
    public float PixelArea => PixelSize.Area();

    /// <summary>
    /// Gets the pixel width in microns
    /// </summary>
    public float PixelWidthMicrons => DisplayWidth > 0 && ResolutionX > 0 ? MathF.Round(DisplayWidth / ResolutionX * 1000, 3) : 0;

    /// <summary>
    /// Gets the pixel height in microns
    /// </summary>
    public float PixelHeightMicrons => DisplayHeight > 0 && ResolutionY > 0 ? MathF.Round(DisplayHeight / ResolutionY * 1000, 3) : 0;

    /// <summary>
    /// Gets the pixel size in microns
    /// </summary>
    public SizeF PixelSizeMicrons => new(PixelWidthMicrons, PixelHeightMicrons);

    /// <summary>
    /// Gets the maximum pixel between width and height in microns
    /// </summary>
    public float PixelSizeMicronsMax => PixelSizeMicrons.Max();

    /// <summary>
    /// Gets the pixel area in millimeters
    /// </summary>
    public float PixelAreaMicrons => PixelSizeMicrons.Area();

    /// <summary>
    /// Gets if the pixels are square, otherwise false.
    /// </summary>
    public bool UsingSquarePixels => Math.Abs(PixelWidthMicrons - PixelHeightMicrons) < 0.01;

    /// <summary>
    /// Gets the pixel scale normalized with X relative to Y
    /// </summary>
    public float PixelScaleNormalizeXRelativeToY
    {
        get
        {
            var pixelWidth = PixelWidth;
            var pixelHeigth = PixelHeight;
            return pixelWidth > 0 && pixelHeigth > 0
                ? pixelHeigth / pixelWidth
                : 1;
        }
    }

    /// <summary>
    /// Gets the pixel scale normalized with Y relative to X
    /// </summary>
    public float PixelScaleNormalizeYRelativeToX
    {
        get
        {
            var pixelWidth = PixelWidth;
            var pixelHeigth = PixelHeight;
            return pixelWidth > 0 && pixelHeigth > 0
                ? pixelWidth / pixelHeigth
                : 1;
        }
    }

    /// <summary>
    /// Gets the pixel scale normalized with X relative to Y and Y relative to X
    /// </summary>
    public SizeF PixelScaleNormalized
    {
        get
        {
            var pixelWidth = PixelWidth;
            var pixelHeigth = PixelHeight;
            return pixelWidth > 0 && pixelHeigth > 0
                ? new SizeF(pixelHeigth / pixelWidth, pixelWidth / pixelHeigth)
                : new SizeF(1f, 1f);
        }
    }

    /// <summary>
    /// Translates a pixel size to a X/Y normalized pitch where it compensates the lower side
    /// </summary>
    /// <param name="size"></param>
    /// <returns></returns>
    public SizeF PixelsToNormalizedPitchF(int size)
    {
        var pixelScaleNormalized = PixelScaleNormalized;
        if (pixelScaleNormalized.Width == PixelScaleNormalized.Height)
        {
            return new SizeF(size, size);
        }
        if (pixelScaleNormalized.Width > PixelScaleNormalized.Height)
        {
            return new SizeF(size * pixelScaleNormalized.Width, size);
        }
        else
        {
            return new SizeF(size, size * pixelScaleNormalized.Height);
        }
    }

    /// <summary>
    /// Translates a pixel size to a X/Y normalized pitch where it compensates the lower side
    /// </summary>
    /// <param name="size"></param>
    /// <returns></returns>
    public Size PixelsToNormalizedPitch(int size)
    {
        var pixelScaleNormalized = PixelScaleNormalized;
        if (pixelScaleNormalized.Width == PixelScaleNormalized.Height)
        {
            return new Size(size, size);
        }
        if (pixelScaleNormalized.Width > PixelScaleNormalized.Height)
        {
            return new Size((int)(size * pixelScaleNormalized.Width), size);
        }
        else
        {
            return new Size(size, (int)(size * pixelScaleNormalized.Height));
        }
    }

    /// <summary>
    /// Gets the file volume (XYZ) in mm^3
    /// </summary>
    public float Volume => MathF.Round(this.AsValueEnumerable().Sum(layer => layer.GetVolume()), 3);

    /// <summary>
    /// Gets if the printer have a tilting vat
    /// </summary>
    public virtual bool HaveTiltingVat => false;

    /// <summary>
    /// Gets if the file supports antialiasing usage (grey pixels)
    /// </summary>
    public virtual bool SupportAntiAliasing => true;

    /// <summary>
    /// Gets if the AntiAliasing is emulated/fake with fractions of the time or if is real grey levels
    /// </summary>
    public virtual bool IsAntiAliasingEmulated => false;

    /// <summary>
    /// Checks if this file is using AntiAliasing per it data information
    /// </summary>
    public bool IsUsingAntiAliasing => AntiAliasing > 1;

    /// <summary>
    /// Gets or sets the AntiAliasing level
    /// </summary>
    public virtual byte AntiAliasing
    {
        get => _antiAliasing;
        set
        {
            if (!SupportAntiAliasing) return;
            RaiseAndSet(ref _antiAliasing, value);
        }
    }

    /// <summary>
    /// Gets Layer Height in mm
    /// </summary>
    public virtual float LayerHeight
    {
        get => _layerHeight;
        set
        {
            RaiseAndSet(ref _layerHeight, Layer.RoundHeight(value));
            RaisePropertyChanged(nameof(LayerHeightUm));
        }
    }

    /// <summary>
    /// Gets Layer Height in um
    /// </summary>
    public ushort LayerHeightUm
    {
        get => (ushort)(LayerHeight * 1000);
        set => LayerHeight = value / 1000f;
    }

    /// <summary>
    /// Gets or sets the print height in mm
    /// </summary>
    public virtual float PrintHeight
    {
        get => HaveLayers ? LastLayer?.PositionZ ?? LayerCount * LayerHeight : 0;
        set => RaisePropertyChanged();
    }

    /// <summary>
    /// Check if this file format supports global print parameters
    /// </summary>
    public bool SupportGlobalPrintParameters => PrintParameterModifiers.Length > 0;

    /// <summary>
    /// Checks if this file format supports per layer settings
    /// </summary>
    public bool SupportPerLayerSettings => PrintParameterPerLayerModifiers.Length > 0;

    public bool IsReadOnly => false;

    /// <summary>
    /// Return true if this file have layers, otherwise false
    /// </summary>
    public bool HaveLayers => _layers.Length > 0;

    /// <summary>
    /// Gets the layer count
    /// </summary>
    public int Count => _layers.Length;

    /// <summary>
    /// Gets or sets the layer count
    /// </summary>
    public virtual uint LayerCount
    {
        get => (uint)Count;
        set {
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(LastLayerIndex));
            RaisePropertyChanged(nameof(NormalLayerCount));
            RaisePropertyChanged(nameof(TransitionLayersRepresentation));
        }
    }

    /// <summary>
    /// Return the number of digits on the layer count number, eg: 123 layers = 3 digits
    /// </summary>
    public byte LayerDigits => (byte)LayerCount.DigitCount();

    /// <summary>
    /// Gets or sets the total height for the bottom layers in millimeters
    /// </summary>
    public float BottomLayersHeight
    {
        get => LastBottomLayer?.PositionZ ?? 0;
        set => BottomLayerCount = (ushort)Math.Ceiling(value / LayerHeight);
    }

    #region Universal Properties

    /// <summary>
    /// Gets or sets the number of initial layer count
    /// </summary>
    public virtual ushort BottomLayerCount
    {
        get => _bottomLayerCount;
        set
        {
            RaiseAndSet(ref _bottomLayerCount, value);
            RaisePropertyChanged(nameof(NormalLayerCount));
            RaisePropertyChanged(nameof(TransitionLayersRepresentation));
        }
    }

    /// <summary>
    /// Gets the transition layer type
    /// </summary>
    public virtual TransitionLayerTypes TransitionLayerType => TransitionLayerTypes.Software;

    /// <summary>
    /// Gets or sets the number of transition layers
    /// </summary>
    public virtual ushort TransitionLayerCount
    {
        get => _transitionLayerCount;
        set
        {
            RaiseAndSet(ref _transitionLayerCount, (ushort)Math.Min(value, MaximumPossibleTransitionLayerCount));
            RaisePropertyChanged(nameof(HaveTransitionLayers));
            RaisePropertyChanged(nameof(TransitionLayersRepresentation));
        }
    }

    /// <summary>
    /// Gets if have transition layers
    /// </summary>
    public bool HaveTransitionLayers => _transitionLayerCount > 0;

    /// <summary>
    /// Gets the maximum transition layers this layer collection supports
    /// </summary>
    public uint MaximumPossibleTransitionLayerCount
    {
        get
        {
            if (BottomLayerCount == 0) return 0;
            int layerCount = (int)LayerCount - BottomLayerCount - 1;
            if (layerCount <= 0) return 0;
            return (uint)layerCount;
        }
    }

    /// <summary>
    /// Gets the number of normal layer count
    /// </summary>
    public uint NormalLayerCount => LayerCount - BottomLayerCount;

    /// <summary>
    /// Gets or sets the bottom layer off time in seconds
    /// </summary>
    public virtual float BottomLightOffDelay
    {
        get => _bottomLightOffDelay;
        set
        {
            RaiseAndSet(ref _bottomLightOffDelay, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(LightOffDelayRepresentation));
        }
    }

    /// <summary>
    /// Gets or sets the layer off time in seconds
    /// </summary>
    public virtual float LightOffDelay
    {
        get => _lightOffDelay;
        set
        {
            RaiseAndSet(ref _lightOffDelay, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(LightOffDelayRepresentation));
        }
    }

    /// <summary>
    /// Gets or sets the bottom time in seconds to wait before cure the layer
    /// </summary>
    public virtual float BottomWaitTimeBeforeCure
    {
        get => _bottomWaitTimeBeforeCure;
        set
        {
            RaiseAndSet(ref _bottomWaitTimeBeforeCure, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(WaitTimeRepresentation));
        }
    }


    /// <summary>
    /// Gets or sets the time in seconds to wait after cure the layer
    /// </summary>
    public virtual float WaitTimeBeforeCure
    {
        get => _waitTimeBeforeCure;
        set
        {
            RaiseAndSet(ref _waitTimeBeforeCure, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(WaitTimeRepresentation));
        }
    }

    /// <summary>
    /// Gets or sets the initial exposure time for <see cref="BottomLayerCount"/> in seconds
    /// </summary>
    public virtual float BottomExposureTime
    {
        get => _bottomExposureTime;
        set
        {
            RaiseAndSet(ref _bottomExposureTime, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(ExposureRepresentation));
            RaisePropertyChanged(nameof(TransitionLayersRepresentation));
        }
    }

    /// <summary>
    /// Gets or sets the normal layer exposure time in seconds
    /// </summary>
    public virtual float ExposureTime
    {
        get => _exposureTime;
        set
        {
            RaiseAndSet(ref _exposureTime, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(ExposureRepresentation));
            RaisePropertyChanged(nameof(TransitionLayersRepresentation));
        }
    }

    /// <summary>
    /// Gets or sets the bottom time in seconds to wait after cure the layer
    /// </summary>
    public virtual float BottomWaitTimeAfterCure
    {
        get => _bottomWaitTimeAfterCure;
        set
        {
            RaiseAndSet(ref _bottomWaitTimeAfterCure, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(WaitTimeRepresentation));
        }
    }

    /// <summary>
    /// Gets or sets the time in seconds to wait after cure the layer
    /// </summary>
    public virtual float WaitTimeAfterCure
    {
        get => _waitTimeAfterCure;
        set
        {
            RaiseAndSet(ref _waitTimeAfterCure, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(WaitTimeRepresentation));
        }
    }

    /// <summary>
    /// Gets: Total bottom lift height (lift1 + lift2)
    /// Sets: Bottom lift1 with value and lift2 with 0
    /// </summary>
    public float BottomLiftHeightTotal
    {
        get => MathF.Round(BottomLiftHeight + BottomLiftHeight2, 2);
        set
        {
            BottomLiftHeight = MathF.Round(Math.Max(0, value), 2);
            BottomLiftHeight2 = 0;
        }
    }

    /// <summary>
    /// Gets: Total lift height (lift1 + lift2)
    /// Sets: Lift1 with value and lift2 with 0
    /// </summary>
    public float LiftHeightTotal
    {
        get => MathF.Round(LiftHeight + LiftHeight2, 2);
        set
        {
            LiftHeight = MathF.Round(Math.Max(0, value), 2);
            LiftHeight2 = 0;
        }
    }

    /// <summary>
    /// Gets or sets the bottom lift height in mm
    /// </summary>
    public virtual float BottomLiftHeight
    {
        get => _bottomLiftHeight;
        set
        {
            RaiseAndSet(ref _bottomLiftHeight, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(BottomLiftHeightTotal));
            RaisePropertyChanged(nameof(LiftRepresentation));
            BottomRetractHeight2 = BottomRetractHeight2; // Sanitize
        }
    }

    /// <summary>
    /// Gets or sets the bottom lift speed in mm/min
    /// </summary>
    public virtual float BottomLiftSpeed
    {
        get => _bottomLiftSpeed;
        set
        {
            RaiseAndSet(ref _bottomLiftSpeed, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(LiftRepresentation));
        }
    }

    /// <summary>
    /// Gets or sets the bottom lift acceleration in mm/s²
    /// </summary>
    public virtual float BottomLiftAcceleration
    {
        get => _bottomLiftAcceleration;
        set
        {
            RaiseAndSet(ref _bottomLiftAcceleration, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(LiftRepresentation));
        }
    }

    /// <summary>
    /// Gets or sets the lift height in mm
    /// </summary>
    public virtual float LiftHeight
    {
        get => _liftHeight;
        set
        {
            RaiseAndSet(ref _liftHeight, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(LiftHeightTotal));
            RaisePropertyChanged(nameof(LiftRepresentation));
            RetractHeight2 = RetractHeight2; // Sanitize
        }
    }

    /// <summary>
    /// Gets or sets the speed in mm/min
    /// </summary>
    public virtual float LiftSpeed
    {
        get => _liftSpeed;
        set
        {
            RaiseAndSet(ref _liftSpeed, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(LiftRepresentation));
        }
    }

    /// <summary>
    /// Gets or sets the lift acceleration in mm/s²
    /// </summary>
    public virtual float LiftAcceleration
    {
        get => _liftAcceleration;
        set
        {
            RaiseAndSet(ref _liftAcceleration, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(LiftRepresentation));
        }
    }

    /// <summary>
    /// Gets or sets the second bottom lift height in mm
    /// </summary>
    public virtual float BottomLiftHeight2
    {
        get => _bottomLiftHeight2;
        set
        {
            RaiseAndSet(ref _bottomLiftHeight2, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(BottomLiftHeightTotal));
            RaisePropertyChanged(nameof(LiftRepresentation));
            BottomRetractHeight2 = BottomRetractHeight2; // Sanitize
        }
    }

    /// <summary>
    /// Gets or sets the second bottom lift speed in mm/min
    /// </summary>
    public virtual float BottomLiftSpeed2
    {
        get => _bottomLiftSpeed2;
        set
        {
            RaiseAndSet(ref _bottomLiftSpeed2, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(LiftRepresentation));
        }
    }

    /// <summary>
    /// Gets or sets the second bottom lift acceleration in mm/s²
    /// </summary>
    public virtual float BottomLiftAcceleration2
    {
        get => _bottomLiftAcceleration2;
        set
        {
            RaiseAndSet(ref _bottomLiftAcceleration2, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(LiftRepresentation));
        }
    }

    /// <summary>
    /// Gets or sets the second lift height in mm (This is the closer to fep retract)
    /// </summary>
    public virtual float LiftHeight2
    {
        get => _liftHeight2;
        set
        {
            RaiseAndSet(ref _liftHeight2, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(LiftHeightTotal));
            RaisePropertyChanged(nameof(LiftRepresentation));
            RetractHeight2 = RetractHeight2; // Sanitize
        }
    }


    /// <summary>
    /// Gets or sets the second speed in mm/min (This is the closer to fep retract)
    /// </summary>
    public virtual float LiftSpeed2
    {
        get => _liftSpeed2;
        set
        {
            RaiseAndSet(ref _liftSpeed2, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(LiftRepresentation));
        }
    }

    /// <summary>
    /// Gets or sets the second lift acceleration in mm/s²
    /// </summary>
    public virtual float LiftAcceleration2
    {
        get => _liftAcceleration2;
        set
        {
            RaiseAndSet(ref _liftAcceleration2, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(LiftRepresentation));
        }
    }

    /// <summary>
    /// Gets or sets the bottom time in seconds to wait after lift / before retract
    /// </summary>
    public virtual float BottomWaitTimeAfterLift
    {
        get => _bottomWaitTimeAfterLift;
        set
        {
            RaiseAndSet(ref _bottomWaitTimeAfterLift, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(WaitTimeRepresentation));
        }
    }

    /// <summary>
    /// Gets or sets the time in seconds to wait after lift / before retract
    /// </summary>
    public virtual float WaitTimeAfterLift
    {
        get => _waitTimeAfterLift;
        set
        {
            RaiseAndSet(ref _waitTimeAfterLift, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(WaitTimeRepresentation));
        }
    }

    /// <summary>
    /// Gets: Total bottom retract height (retract1 + retract2)  alias of <see cref="BottomLiftHeightTotal"/>
    /// </summary>
    public float BottomRetractHeightTotal => BottomLiftHeightTotal;

    /// <summary>
    /// Gets: Total retract height (retract1 + retract2) alias of <see cref="LiftHeightTotal"/>
    /// </summary>
    public float RetractHeightTotal => LiftHeightTotal;

    /// <summary>
    /// Gets the bottom retract height in mm
    /// </summary>
    public float BottomRetractHeight => MathF.Round(BottomLiftHeightTotal - BottomRetractHeight2, 2);

    /// <summary>
    /// Gets or sets the speed in mm/min for the bottom retracts
    /// </summary>
    public virtual float BottomRetractSpeed
    {
        get => _bottomRetractSpeed;
        set
        {
            RaiseAndSet(ref _bottomRetractSpeed, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(RetractRepresentation));
        }
    }

    /// <summary>
    /// Gets or sets the acceleration in mm/s² for the bottom retracts
    /// </summary>
    public virtual float BottomRetractAcceleration
    {
        get => _bottomRetractAcceleration;
        set
        {
            RaiseAndSet(ref _bottomRetractAcceleration, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(RetractRepresentation));
        }
    }

    /// <summary>
    /// Gets the retract height in mm
    /// </summary>
    public float RetractHeight => MathF.Round(LiftHeightTotal - RetractHeight2, 2);

    /// <summary>
    /// Gets the speed in mm/min for the retracts
    /// </summary>
    public virtual float RetractSpeed
    {
        get => _retractSpeed;
        set
        {
            RaiseAndSet(ref _retractSpeed, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(RetractRepresentation));
        }
    }

    /// <summary>
    /// Gets or sets the acceleration in mm/s² for the retracts
    /// </summary>
    public virtual float RetractAcceleration
    {
        get => _retractAcceleration;
        set
        {
            RaiseAndSet(ref _retractAcceleration, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(RetractRepresentation));
        }
    }

    /// <summary>
    /// Gets or sets the second bottom retract height in mm
    /// </summary>
    public virtual float BottomRetractHeight2
    {
        get => _bottomRetractHeight2;
        set
        {
            value = Math.Clamp(MathF.Round(value, 2), 0, BottomRetractHeightTotal);
            RaiseAndSet(ref _bottomRetractHeight2, value);
            RaisePropertyChanged(nameof(BottomRetractHeight));
            RaisePropertyChanged(nameof(BottomRetractHeightTotal));
            RaisePropertyChanged(nameof(RetractRepresentation));
        }
    }

    /// <summary>
    /// Gets the speed in mm/min for the retracts
    /// </summary>
    public virtual float BottomRetractSpeed2
    {
        get => _bottomRetractSpeed2;
        set
        {
            RaiseAndSet(ref _bottomRetractSpeed2, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(RetractRepresentation));
        }
    }

    /// <summary>
    /// Gets or sets the second acceleration in mm/s² for the bottom retracts
    /// </summary>
    public virtual float BottomRetractAcceleration2
    {
        get => _bottomRetractAcceleration2;
        set
        {
            RaiseAndSet(ref _bottomRetractAcceleration2, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(RetractRepresentation));
        }
    }

    /// <summary>
    /// Gets or sets the second retract height in mm
    /// </summary>
    public virtual float RetractHeight2
    {
        get => _retractHeight2;
        set
        {
            value = Math.Clamp(MathF.Round(value, 2), 0, RetractHeightTotal);
            RaiseAndSet(ref _retractHeight2, value);
            RaisePropertyChanged(nameof(RetractHeight));
            RaisePropertyChanged(nameof(RetractHeightTotal));
            RaisePropertyChanged(nameof(RetractRepresentation));
        }
    }

    /// <summary>
    /// Gets the speed in mm/min for the retracts
    /// </summary>
    public virtual float RetractSpeed2
    {
        get => _retractSpeed2;
        set
        {
            RaiseAndSet(ref _retractSpeed2, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(RetractRepresentation));
        }
    }

    /// <summary>
    /// Gets or sets the second acceleration in mm/s² for the retracts
    /// </summary>
    public virtual float RetractAcceleration2
    {
        get => _retractAcceleration2;
        set
        {
            RaiseAndSet(ref _retractAcceleration2, MathF.Round(Math.Max(0, value), 2));
            RaisePropertyChanged(nameof(RetractRepresentation));
        }
    }

    /// <summary>
    /// Gets or sets the bottom pwm value from 0 to 255
    /// </summary>
    public virtual byte BottomLightPWM
    {
        get => _bottomLightPwm;
        set => RaiseAndSet(ref _bottomLightPwm, value);
    }

    /// <summary>
    /// Gets or sets the pwm value from 0 to 255
    /// </summary>
    public virtual byte LightPWM
    {
        get => _lightPwm;
        set => RaiseAndSet(ref _lightPwm, value);
    }

    /// <summary>
    /// Gets the minimum used speed for bottom layers in mm/min
    /// </summary>
    public float MinimumBottomSpeed
    {
        get
        {
            float speed = float.MaxValue;
            if (BottomLiftSpeed > 0) speed = Math.Min(speed, BottomLiftSpeed);
            if (CanUseBottomLiftSpeed2 && BottomLiftSpeed2 > 0) speed = Math.Min(speed, BottomLiftSpeed2);
            if (CanUseBottomRetractSpeed && BottomRetractSpeed > 0) speed = Math.Min(speed, BottomRetractSpeed);
            if (CanUseBottomRetractSpeed2 && BottomRetractSpeed2 > 0) speed = Math.Min(speed, BottomRetractSpeed2);
            if (Math.Abs(speed - float.MaxValue) < 0.01) return 0;

            return speed;
        }
    }

    /// <summary>
    /// Gets the minimum used speed for normal bottom layers in mm/min
    /// </summary>
    public float MinimumNormalSpeed
    {
        get
        {
            float speed = float.MaxValue;
            if (LiftSpeed > 0) speed = Math.Min(speed, LiftSpeed);
            if (CanUseLiftSpeed2 && LiftSpeed2 > 0) speed = Math.Min(speed, LiftSpeed2);
            if (CanUseRetractSpeed && RetractSpeed > 0) speed = Math.Min(speed, RetractSpeed);
            if (CanUseRetractSpeed2 && RetractSpeed2 > 0) speed = Math.Min(speed, RetractSpeed2);
            if (Math.Abs(speed - float.MaxValue) < 0.01) return 0;

            return speed;
        }
    }

    /// <summary>
    /// Gets the minimum used speed in mm/min
    /// </summary>
    public float MinimumSpeed
    {
        get
        {
            var bottomSpeed = MinimumBottomSpeed;
            var normalSpeed = MinimumNormalSpeed;
            if (bottomSpeed <= 0) return normalSpeed;
            if (normalSpeed <= 0) return bottomSpeed;

            return Math.Min(bottomSpeed, normalSpeed);
        }
    }

    /// <summary>
    /// Gets the maximum used speed for bottom layers in mm/min
    /// </summary>
    public float MaximumBottomSpeed
    {
        get
        {
            float speed = BottomLiftSpeed;
            if (CanUseBottomLiftSpeed2) speed = Math.Max(speed, BottomLiftSpeed2);
            if (CanUseBottomRetractSpeed) speed = Math.Max(speed, BottomRetractSpeed);
            if (CanUseBottomRetractSpeed2) speed = Math.Max(speed, BottomRetractSpeed2);

            return speed;
        }
    }

    /// <summary>
    /// Gets the maximum used speed for normal bottom layers in mm/min
    /// </summary>
    public float MaximumNormalSpeed
    {
        get
        {
            float speed = LiftSpeed;
            if (CanUseLiftSpeed2) speed = Math.Max(speed, LiftSpeed2);
            if (CanUseRetractSpeed) speed = Math.Max(speed, RetractSpeed);
            if (CanUseRetractSpeed2) speed = Math.Max(speed, RetractSpeed2);

            return speed;
        }
    }

    /// <summary>
    /// Gets the maximum used speed in mm/min
    /// </summary>
    public float MaximumSpeed => Math.Max(MaximumBottomSpeed, MaximumNormalSpeed);

    public bool CanUseBottomLayerCount => HavePrintParameterModifier(PrintParameterModifier.BottomLayerCount);
    public bool CanUseTransitionLayerCount => HavePrintParameterModifier(PrintParameterModifier.TransitionLayerCount);

    public bool CanUseBottomLightOffDelay => HavePrintParameterModifier(PrintParameterModifier.BottomLightOffDelay);
    public bool CanUseLightOffDelay => HavePrintParameterModifier(PrintParameterModifier.LightOffDelay);
    public bool CanUseAnyLightOffDelay => CanUseBottomLightOffDelay || CanUseLightOffDelay;

    public bool CanUseBottomWaitTimeBeforeCure => HavePrintParameterModifier(PrintParameterModifier.BottomWaitTimeBeforeCure);
    public bool CanUseWaitTimeBeforeCure => HavePrintParameterModifier(PrintParameterModifier.WaitTimeBeforeCure);
    public bool CanUseAnyWaitTimeBeforeCure => CanUseBottomWaitTimeBeforeCure || CanUseWaitTimeBeforeCure;

    public bool CanUseBottomExposureTime => HavePrintParameterModifier(PrintParameterModifier.BottomExposureTime);
    public bool CanUseExposureTime => HavePrintParameterModifier(PrintParameterModifier.ExposureTime);
    public bool CanUseAnyExposureTime => CanUseBottomExposureTime || CanUseExposureTime;

    public bool CanUseBottomWaitTimeAfterCure => HavePrintParameterModifier(PrintParameterModifier.BottomWaitTimeAfterCure);
    public bool CanUseWaitTimeAfterCure => HavePrintParameterModifier(PrintParameterModifier.WaitTimeAfterCure);
    public bool CanUseAnyWaitTimeAfterCure => CanUseBottomWaitTimeAfterCure || CanUseWaitTimeAfterCure;

    public bool CanUseBottomLiftHeight => HavePrintParameterModifier(PrintParameterModifier.BottomLiftHeight);
    public bool CanUseLiftHeight => HavePrintParameterModifier(PrintParameterModifier.LiftHeight);
    public bool CanUseAnyLiftHeight => CanUseBottomLiftHeight || CanUseLiftHeight;

    public bool CanUseBottomLiftSpeed => HavePrintParameterModifier(PrintParameterModifier.BottomLiftSpeed);
    public bool CanUseLiftSpeed => HavePrintParameterModifier(PrintParameterModifier.LiftSpeed);
    public bool CanUseAnyLiftSpeed => CanUseBottomLiftSpeed || CanUseLiftSpeed;

    public bool CanUseBottomLiftAcceleration => HavePrintParameterModifier(PrintParameterModifier.BottomLiftAcceleration);
    public bool CanUseLiftAcceleration => HavePrintParameterModifier(PrintParameterModifier.LiftAcceleration);
    public bool CanUseAnyLiftAcceleration => CanUseBottomLiftAcceleration || CanUseLiftAcceleration;

    public bool CanUseBottomLiftHeight2 => HavePrintParameterModifier(PrintParameterModifier.BottomLiftHeight2);
    public bool CanUseLiftHeight2 => HavePrintParameterModifier(PrintParameterModifier.LiftHeight2);
    public bool CanUseAnyLiftHeight2 => CanUseBottomLiftHeight2 || CanUseLiftHeight2;

    public bool CanUseBottomLiftSpeed2 => HavePrintParameterModifier(PrintParameterModifier.BottomLiftSpeed2);
    public bool CanUseLiftSpeed2 => HavePrintParameterModifier(PrintParameterModifier.LiftSpeed2);
    public bool CanUseAnyLiftSpeed2 => CanUseBottomLiftSpeed2 || CanUseLiftSpeed2;

    public bool CanUseBottomLiftAcceleration2 => HavePrintParameterModifier(PrintParameterModifier.BottomLiftAcceleration2);
    public bool CanUseLiftAcceleration2 => HavePrintParameterModifier(PrintParameterModifier.LiftAcceleration2);
    public bool CanUseAnyLiftAcceleration2 => CanUseBottomLiftAcceleration2 || CanUseLiftAcceleration2;

    public bool CanUseBottomWaitTimeAfterLift => HavePrintParameterModifier(PrintParameterModifier.BottomWaitTimeAfterLift);
    public bool CanUseWaitTimeAfterLift => HavePrintParameterModifier(PrintParameterModifier.WaitTimeAfterLift);
    public bool CanUseAnyWaitTimeAfterLift => CanUseBottomWaitTimeAfterLift || CanUseWaitTimeAfterLift;

    public bool CanUseBottomRetractSpeed => HavePrintParameterModifier(PrintParameterModifier.BottomRetractSpeed);
    public bool CanUseRetractSpeed => HavePrintParameterModifier(PrintParameterModifier.RetractSpeed);
    public bool CanUseAnyRetractSpeed => CanUseBottomRetractSpeed || CanUseRetractSpeed;

    public bool CanUseBottomRetractAcceleration => HavePrintParameterModifier(PrintParameterModifier.BottomRetractAcceleration);
    public bool CanUseRetractAcceleration => HavePrintParameterModifier(PrintParameterModifier.RetractAcceleration);
    public bool CanUseAnyRetractAcceleration => CanUseBottomRetractAcceleration || CanUseRetractAcceleration;

    public bool CanUseBottomRetractHeight2 => HavePrintParameterModifier(PrintParameterModifier.BottomRetractHeight2);
    public bool CanUseRetractHeight2 => HavePrintParameterModifier(PrintParameterModifier.RetractHeight2);
    public bool CanUseAnyRetractHeight2 => CanUseBottomRetractHeight2 || CanUseRetractHeight2;
    public bool CanUseBottomRetractSpeed2 => HavePrintParameterModifier(PrintParameterModifier.BottomRetractSpeed2);
    public bool CanUseRetractSpeed2 => HavePrintParameterModifier(PrintParameterModifier.RetractSpeed2);
    public bool CanUseAnyRetractSpeed2 => CanUseBottomRetractSpeed2 || CanUseRetractSpeed2;

    public bool CanUseBottomRetractAcceleration2 => HavePrintParameterModifier(PrintParameterModifier.BottomRetractAcceleration2);
    public bool CanUseRetractAcceleration2 => HavePrintParameterModifier(PrintParameterModifier.RetractAcceleration2);
    public bool CanUseAnyRetractAcceleration2 => CanUseBottomRetractAcceleration2 || CanUseRetractAcceleration2;

    public bool CanUseAnyWaitTime => CanUseBottomWaitTimeBeforeCure || CanUseBottomWaitTimeAfterCure || CanUseBottomWaitTimeAfterLift ||
                                     CanUseWaitTimeBeforeCure || CanUseWaitTimeAfterCure || CanUseWaitTimeAfterLift;

    public bool CanUseBottomLightPWM => HavePrintParameterModifier(PrintParameterModifier.BottomLightPWM);
    public bool CanUseLightPWM => HavePrintParameterModifier(PrintParameterModifier.LightPWM);
    public bool CanUseAnyLightPWM => CanUseBottomLightPWM || CanUseLightPWM;

    public virtual bool CanUseSameLayerPositionZ => CanUseLayerPositionZ;
    public bool CanUseLayerPositionZ => HaveLayerParameterModifier(PrintParameterModifier.PositionZ);
    public bool CanUseLayerWaitTimeBeforeCure => HaveLayerParameterModifier(PrintParameterModifier.WaitTimeBeforeCure);
    public bool CanUseLayerExposureTime => HaveLayerParameterModifier(PrintParameterModifier.ExposureTime);
    public bool CanUseLayerWaitTimeAfterCure => HaveLayerParameterModifier(PrintParameterModifier.WaitTimeAfterCure);
    public bool CanUseLayerLiftHeight => HaveLayerParameterModifier(PrintParameterModifier.LiftHeight);
    public bool CanUseLayerLiftSpeed => HaveLayerParameterModifier(PrintParameterModifier.LiftSpeed);
    public bool CanUseLayerLiftAcceleration => HaveLayerParameterModifier(PrintParameterModifier.LiftAcceleration);
    public bool CanUseLayerLiftHeight2 => HaveLayerParameterModifier(PrintParameterModifier.LiftHeight2);
    public bool CanUseLayerLiftSpeed2 => HaveLayerParameterModifier(PrintParameterModifier.LiftSpeed2);
    public bool CanUseLayerLiftAcceleration2 => HaveLayerParameterModifier(PrintParameterModifier.LiftAcceleration2);
    public bool CanUseLayerWaitTimeAfterLift => HaveLayerParameterModifier(PrintParameterModifier.WaitTimeAfterLift);
    public bool CanUseLayerRetractSpeed => HaveLayerParameterModifier(PrintParameterModifier.RetractSpeed);
    public bool CanUseLayerRetractAcceleration => HaveLayerParameterModifier(PrintParameterModifier.RetractAcceleration);
    public bool CanUseLayerRetractHeight2 => HaveLayerParameterModifier(PrintParameterModifier.RetractHeight2);
    public bool CanUseLayerRetractSpeed2 => HaveLayerParameterModifier(PrintParameterModifier.RetractSpeed2);
    public bool CanUseLayerRetractAcceleration2 => HaveLayerParameterModifier(PrintParameterModifier.RetractAcceleration2);
    public bool CanUseLayerLightOffDelay => HaveLayerParameterModifier(PrintParameterModifier.LightOffDelay);
    public bool CanUseLayerAnyWaitTimeBeforeCure => CanUseLayerWaitTimeBeforeCure || CanUseLayerLightOffDelay;
    public bool CanUseLayerLightPWM => HaveLayerParameterModifier(PrintParameterModifier.LightPWM);
    public bool CanUseLayerPause => HaveLayerParameterModifier(PrintParameterModifier.Pause);
    public bool CanUseLayerChangeResin => HaveLayerParameterModifier(PrintParameterModifier.ChangeResin);

    public string TransitionLayersRepresentation
    {
        get
        {
            var str = TransitionLayerCount.ToString(CultureInfo.InvariantCulture);

            if (!CanUseTransitionLayerCount)
            {
                return str;
            }

            if (TransitionLayerCount > 0)
            {
                var decrement = ParseTransitionStepTimeFromLayers();
                if (decrement != 0)
                {
                    str += $"/{-decrement}s";
                }
            }

            return str;
        }
    }

    public string ExposureRepresentation
    {
        get
        {
            var str = string.Empty;

            if (CanUseBottomExposureTime)
            {
                str += BottomExposureTime.ToString(CultureInfo.InvariantCulture);
            }
            if (CanUseExposureTime)
            {
                if (!string.IsNullOrEmpty(str)) str += '/';
                str += ExposureTime.ToString(CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrEmpty(str)) str += 's';

            return str;
        }
    }

    public string LiftRepresentation
    {
        get
        {
            var str = string.Empty;

            var haveBottomLiftHeight = CanUseBottomLiftHeight;
            var haveLiftHeight = CanUseLiftHeight;
            var haveBottomLiftHeight2 = CanUseBottomLiftHeight2;
            var haveLiftHeight2 = CanUseLiftHeight2;

            var haveBottomLiftSpeed2 = CanUseBottomLiftSpeed2;
            var haveLiftSpeed2 = CanUseLiftSpeed2;

            if (!haveBottomLiftHeight && !haveLiftHeight && !haveBottomLiftHeight2 && !haveLiftHeight2) return str;

            // Sequence 1
            if (haveBottomLiftHeight)
            {
                str += BottomLiftHeight.ToString(CultureInfo.InvariantCulture);
                if (haveBottomLiftHeight2 && BottomLiftHeight2 > 0)
                {
                    str += $"+{BottomLiftHeight2.ToString(CultureInfo.InvariantCulture)}";
                }
            }

            if (haveLiftHeight)
            {
                if (!string.IsNullOrEmpty(str)) str += '/';
                str += LiftHeight.ToString(CultureInfo.InvariantCulture);

                if (haveLiftHeight2 && LiftHeight2 > 0)
                {
                    str += $"+{LiftHeight2.ToString(CultureInfo.InvariantCulture)}";
                }
            }

            if (string.IsNullOrEmpty(str)) return str;

            str += "mm";

            var haveBottomLiftSpeed = CanUseBottomLiftSpeed;
            var haveLiftSpeed = CanUseLiftSpeed;

            if (!haveBottomLiftSpeed && !haveLiftSpeed) return str;

            str += " @ ";

            if (haveBottomLiftSpeed)
            {
                str += BottomLiftSpeed.ToString(CultureInfo.InvariantCulture);
                if (haveBottomLiftSpeed2 && haveBottomLiftHeight2 && BottomLiftHeight2 > 0)
                {
                    str += $"+{BottomLiftSpeed2.ToString(CultureInfo.InvariantCulture)}";
                }
            }

            if (haveLiftSpeed)
            {
                if (haveBottomLiftSpeed) str += '/';
                str += LiftSpeed.ToString(CultureInfo.InvariantCulture);
                if (haveLiftSpeed2 && haveLiftHeight2 && LiftHeight2 > 0)
                {
                    str += $"+{LiftSpeed2.ToString(CultureInfo.InvariantCulture)}";
                }
            }

            str += "mm/min";

            /*// Sequence 2
            if (haveBottomLiftHeight2)
            {
                str += $"\n2th: {BottomLiftHeight2.ToString(CultureInfo.InvariantCulture)}";
            }
            if (haveLiftHeight2)
            {
                str += str.EndsWith("mm/min") ? "\n2th: " : '/';
                str += LiftHeight2.ToString(CultureInfo.InvariantCulture);
            }

            if (str.EndsWith("mm/min")) return str;

            str += "mm @ ";

            var haveBottomLiftSpeed2 = CanUseBottomLiftSpeed2;
            var haveLiftSpeed2 = CanUseLiftSpeed2;
            if (haveBottomLiftSpeed2)
            {
                str += BottomLiftSpeed2.ToString(CultureInfo.InvariantCulture);
            }
            if (haveLiftSpeed2)
            {
                if (haveBottomLiftSpeed2) str += '/';
                str += LiftSpeed2.ToString(CultureInfo.InvariantCulture);
            }

            str += "mm/min";*/

            return str;
        }
    }

    public string RetractRepresentation
    {
        get
        {
            var str = string.Empty;

            var haveBottomRetractHeight = CanUseBottomLiftHeight;
            var haveRetractHeight = CanUseLiftHeight;
            var haveBottomRetractSpeed = CanUseBottomRetractSpeed;
            var haveRetractSpeed = CanUseRetractSpeed;
            var haveBottomRetractHeight2 = CanUseBottomRetractHeight2;
            var haveRetractHeight2 = CanUseRetractHeight2;
            var haveBottomRetractSpeed2 = CanUseBottomRetractSpeed2;
            var haveRetractSpeed2 = CanUseRetractSpeed2;

            if (!haveBottomRetractSpeed && !haveRetractSpeed && !haveBottomRetractHeight2 && !haveRetractHeight2) return str;

            // Sequence 1
            if (haveBottomRetractHeight)
            {
                str += BottomRetractHeight.ToString(CultureInfo.InvariantCulture);
                if (haveBottomRetractHeight2 && BottomRetractHeight2 > 0)
                {
                    str += $"+{BottomRetractHeight2.ToString(CultureInfo.InvariantCulture)}";
                }
            }
            if (haveRetractHeight)
            {
                if (!string.IsNullOrEmpty(str)) str += '/';
                str += RetractHeight.ToString(CultureInfo.InvariantCulture);
                if (haveRetractHeight2 && RetractHeight2 > 0)
                {
                    str += $"+{RetractHeight2.ToString(CultureInfo.InvariantCulture)}";
                }
            }

            if (string.IsNullOrEmpty(str)) return str;

            str += "mm @ ";


            if (haveBottomRetractSpeed)
            {
                str += BottomRetractSpeed.ToString(CultureInfo.InvariantCulture);
                if (haveBottomRetractSpeed2 && haveBottomRetractHeight2 && BottomRetractHeight2 > 0)
                {
                    str += $"+{BottomRetractSpeed2.ToString(CultureInfo.InvariantCulture)}";
                }
            }
            if (haveRetractSpeed)
            {
                if (haveBottomRetractSpeed) str += '/';
                str += RetractSpeed.ToString(CultureInfo.InvariantCulture);
                if (haveRetractSpeed2 && haveRetractHeight2 && RetractHeight2 > 0)
                {
                    str += $"+{RetractSpeed2.ToString(CultureInfo.InvariantCulture)}";
                }
            }

            str += "mm/min";

            return str;
        }
    }

    public string LightOffDelayRepresentation
    {
        get
        {
            var str = string.Empty;

            if (CanUseBottomLightOffDelay)
            {
                str += BottomLightOffDelay.ToString(CultureInfo.InvariantCulture);
            }
            if (CanUseLightOffDelay)
            {
                if (!string.IsNullOrEmpty(str)) str += '/';
                str += LightOffDelay.ToString(CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrEmpty(str)) str += 's';

            return str;
        }
    }

    public string WaitTimeRepresentation
    {
        get
        {
            var str = string.Empty;

            if (CanUseBottomWaitTimeBeforeCure || CanUseBottomWaitTimeAfterCure || CanUseBottomWaitTimeAfterLift)
            {
                str += $"{BottomWaitTimeBeforeCure}/{BottomWaitTimeAfterCure}/{BottomWaitTimeAfterLift}s";
            }
            if (!string.IsNullOrEmpty(str)) str += "|";
            if (CanUseWaitTimeBeforeCure || CanUseWaitTimeAfterCure || CanUseWaitTimeAfterLift)
            {
                str += $"{WaitTimeBeforeCure}/{WaitTimeAfterCure}/{WaitTimeAfterLift}s";
            }

            return str;
        }
    }

    public IEnumerable<IEnumerable<int>> BatchLayersIndexes(int batchSize = 0)
    {
        if (batchSize <= 0) batchSize = DefaultParallelBatchCount;
        return Enumerable.Range(0, (int) LayerCount).Chunk(batchSize);
    }

    public IEnumerable<IEnumerable<Layer>> BatchLayers(int batchSize = 0)
    {
        if (batchSize <= 0) batchSize = DefaultParallelBatchCount;
        return this.Chunk(batchSize);
    }

    #endregion

    /// <summary>
    /// Gets the estimate print time in seconds
    /// </summary>
    public virtual float PrintTime
    {
        get
        {
            if (_printTime <= 0)
            {
                _printTime = PrintTimeComputed;
            }
            return _printTime;
        }
        set
        {
            if (value <= 0)
            {
                value = PrintTimeComputed;
            }
            if(!RaiseAndSetIfChanged(ref _printTime, value)) return;
            RaisePropertyChanged(nameof(PrintTimeHours));
            RaisePropertyChanged(nameof(PrintTimeString));
            RaisePropertyChanged(nameof(DisplayTotalOnTime));
            RaisePropertyChanged(nameof(DisplayTotalOnTimeString));
            RaisePropertyChanged(nameof(DisplayTotalOffTime));
            RaisePropertyChanged(nameof(DisplayTotalOffTimeString));
        }
    }

    /// <summary>
    /// Gets the calculated estimate print time in seconds
    /// </summary>
    public float PrintTimeComputed
    {
        get
        {
            if (!HaveLayers) return 0;
            float time = ExtraPrintTime;
            bool computeGeneral = false;
            if (!computeGeneral)
            {
                foreach (var layer in this)
                {
                    if (layer is null)
                    {
                        computeGeneral = true;
                        break;
                    }

                    time += layer.CalculatePrintTime();
                }
            }

            if (computeGeneral)
            {
                var bottomMotorTime = CalculateMotorMovementTime(LayerGroup.Bottom);
                var motorTime = CalculateMotorMovementTime(LayerGroup.Normal);
                if (HaveTiltingVat)
                {
                    bottomMotorTime = 4;
                    motorTime = 4;
                }

                time = ExtraPrintTime +
                       BottomLightOffDelay * BottomLayerCount +
                       LightOffDelay * NormalLayerCount +
                       BottomWaitTimeBeforeCure * BottomLayerCount +
                       WaitTimeBeforeCure * NormalLayerCount +
                       BottomExposureTime * BottomLayerCount +
                       ExposureTime * NormalLayerCount +
                       BottomWaitTimeAfterCure * BottomLayerCount +
                       WaitTimeAfterCure * NormalLayerCount +
                       BottomWaitTimeAfterLift * BottomLayerCount +
                       WaitTimeAfterLift * NormalLayerCount;

                if (SupportGCode)
                {
                    time += bottomMotorTime * BottomLayerCount + motorTime * NormalLayerCount;

                    if (BottomWaitTimeBeforeCure <= 0)
                    {
                        time += BottomLightOffDelay * BottomLayerCount;
                    }
                    if (WaitTimeBeforeCure <= 0)
                    {
                        time += LightOffDelay * NormalLayerCount;
                    }
                }
                else
                {
                    time += motorTime > BottomLightOffDelay ? bottomMotorTime * BottomLayerCount : BottomLightOffDelay * BottomLayerCount;
                    time += motorTime > LightOffDelay ? motorTime * NormalLayerCount : LightOffDelay * NormalLayerCount;
                }
            }

            return MathF.Round(time, 2);
        }
    }

    /// <summary>
    /// Gets the estimate print time in hours
    /// </summary>
    public float PrintTimeHours => MathF.Round(PrintTime / 3600, 2);

    /// <summary>
    /// Gets the estimate print time in hours and minutes formatted
    /// </summary>
    public string PrintTimeString
    {
        get
        {
            var printTime = PrintTime;
            return TimeSpan.FromSeconds(float.IsPositiveInfinity(printTime) || float.IsNaN(printTime) ? 0 : printTime).ToTimeString(false);
        }
    }

    /// <summary>
    /// Gets the total time in seconds the display will remain on exposing the layers during the print
    /// </summary>
    public float DisplayTotalOnTime => MathF.Round(this.AsValueEnumerable().Where(layer => layer is not null).Sum(layer => layer.ExposureTime), 2);

    /// <summary>
    /// Gets the total time formatted in hours, minutes and seconds the display will remain on exposing the layers during the print
    /// </summary>
    public string DisplayTotalOnTimeString => TimeSpan.FromSeconds(DisplayTotalOnTime).ToTimeString();

    /// <summary>
    /// Gets the total time in seconds the display will remain off during the print.
    /// This is the difference between <see cref="PrintTime"/> and <see cref="DisplayTotalOnTime"/>
    /// </summary>
    public float DisplayTotalOffTime
    {
        get
        {
            var printTime = PrintTime;
            if (float.IsPositiveInfinity(printTime) || float.IsNaN(printTime)) return float.NaN;
            var value = MathF.Round(PrintTime - DisplayTotalOnTime, 2);
            return value <= 0 ? float.NaN : value;
        }
    }

    /// <summary>
    /// Gets the total time formatted in hours, minutes and seconds the display will remain off during the print.
    /// This is the difference between <see cref="PrintTime"/> and <see cref="DisplayTotalOnTime"/>
    /// </summary>
    public string DisplayTotalOffTimeString
    {
        get
        {
            var time = DisplayTotalOffTime;
            return TimeSpan.FromSeconds(float.IsPositiveInfinity(time) || float.IsNaN(time) ? 0 : time).ToTimeString();
        }
    }

    /// <summary>
    /// Gets the starting material milliliters when the file was loaded
    /// </summary>
    public float StartingMaterialMilliliters { get; private set; }

    /// <summary>
    /// Gets the estimate used material in ml
    /// </summary>
    public virtual float MaterialMilliliters {
        get => _materialMilliliters;
        set
        {
            if (value <= 0) // Recalculate
            {
                value = MathF.Round(this.AsValueEnumerable().Where(layer => layer is not null).Sum(layer => layer.MaterialMilliliters), 3);
            }
            else // Set from value
            {
                value = MathF.Round(value, 3);
            }

            if(!RaiseAndSetIfChanged(ref _materialMilliliters, value)) return;
            RaisePropertyChanged(nameof(MaterialMillilitersInteger));

            if (StartingMaterialMilliliters > 0 && StartingMaterialCost > 0)
            {
                MaterialCost = GetMaterialCostPer(_materialMilliliters);
            }
            //RaisePropertyChanged(nameof(MaterialCost));
        }
    }

    /// <summary>
    /// Gets the estimate used material in ml and rounded to next integer
    /// </summary>
    public uint MaterialMillilitersInteger => (uint)Math.Ceiling(MaterialMilliliters);

    //public float MaterialMillilitersComputed =>


        /// <summary>
        /// Gets the estimate material in grams
        /// </summary>
    public virtual float MaterialGrams
    {
        get => _materialGrams;
        set => RaiseAndSetIfChanged(ref _materialGrams, MathF.Round(value, 3));
    }

    /// <summary>
    /// Gets the starting material cost when the file was loaded
    /// </summary>
    public float StartingMaterialCost { get; private set; }

    /// <summary>
    /// Gets the estimate material cost
    /// </summary>
    public virtual float MaterialCost
    {
        get => _materialCost;
        set => RaiseAndSetIfChanged(ref _materialCost, MathF.Round(value, 3));
    }

    /// <summary>
    /// Gets the material cost per one milliliter
    /// </summary>
    public float MaterialMilliliterCost => StartingMaterialMilliliters > 0 ? StartingMaterialCost / StartingMaterialMilliliters : 0;

    public float GetMaterialCostPer(float milliliters, byte roundDigits = 3) => MathF.Round(MaterialMilliliterCost * milliliters, roundDigits);

    /// <summary>
    /// Gets the material name
    /// </summary>
    public virtual string? MaterialName
    {
        get => _materialName;
        set
        {
            if (!RaiseAndSetIfChanged(ref _materialName, value)) return;
            if (FileType == FileFormatType.Binary) RequireFullEncode = true;
        }
    }

    /// <summary>
    /// Gets the machine name
    /// </summary>
    public virtual string MachineName
    {
        get => _machineName;
        set
        {
            if(!RaiseAndSetIfChanged(ref _machineName, value)) return;
            if(FileType == FileFormatType.Binary) RequireFullEncode = true;
        }

    }

    /// <summary>
    /// Gets the GCode, returns null if not supported
    /// </summary>
    public GCodeBuilder? GCode { get; set; }

    /// <summary>
    /// Gets the GCode, returns null if not supported
    /// </summary>
    public string? GCodeStr
    {
        get => GCode?.ToString();
        set
        {
            if (GCode is null) return;
            GCode.Clear();
            if (!string.IsNullOrWhiteSpace(value))
            {
                GCode.Append(value);
            }
            RaisePropertyChanged();

        }
    }

    /// <summary>
    /// Gets if this file format supports gcode
    /// </summary>
    public virtual bool SupportGCode => GCode is not null;

    /// <summary>
    /// Gets if this file have available gcode to read
    /// </summary>
    public bool HaveGCode => SupportGCode && !GCode!.IsEmpty;

    /// <summary>
    /// Disable or enable the gcode auto rebuild when needed, set this to false to manually write your own gcode
    /// </summary>
    public bool SuppressRebuildGCode
    {
        get => _suppressRebuildGCode;
        set => RaiseAndSetIfChanged(ref _suppressRebuildGCode, value);
    }

    /// <summary>
    /// Get all configuration objects with properties and values
    /// </summary>
    public virtual object[] Configs => [];

    /// <summary>
    /// Gets if this file is valid to decode
    /// </summary>
    public bool CanDecode => !string.IsNullOrEmpty(FileFullPath) && File.Exists(FileFullPath);

    /// <summary>
    /// Gets if this file is valid to encode
    /// </summary>
    public bool CanEncode => !string.IsNullOrEmpty(FileFullPath);

    #endregion

    #region Constructor
    protected FileFormat()
    {
        IssueManager = new(this);
        _queueTimerPrintTime.Elapsed += (sender, e) => UpdatePrintTime();

        _layerImageFormat = FileType switch
        {
            FileFormatType.Archive => ImageFormat.Png8,
            FileFormatType.Binary => ImageFormat.Rle,
            FileFormatType.Text => ImageFormat.Custom,
            _ => throw new ArgumentOutOfRangeException(nameof(LayerImageFormat), _layerImageFormat, null)
        };
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (SuppressRebuildProperties) return;
        if (e.PropertyName
            is nameof(BottomLayerCount)
            or nameof(BottomLightOffDelay)
            or nameof(LightOffDelay)
            or nameof(BottomWaitTimeBeforeCure)
            or nameof(WaitTimeBeforeCure)
            or nameof(BottomExposureTime)
            or nameof(ExposureTime)
            or nameof(BottomWaitTimeAfterCure)
            or nameof(WaitTimeAfterCure)
            or nameof(BottomLiftHeight)
            or nameof(BottomLiftSpeed)
            or nameof(BottomLiftAcceleration)
            or nameof(LiftHeight)
            or nameof(LiftSpeed)
            or nameof(LiftAcceleration)
            or nameof(BottomLiftHeight2)
            or nameof(BottomLiftSpeed2)
            or nameof(BottomLiftAcceleration2)
            or nameof(LiftHeight2)
            or nameof(LiftSpeed2)
            or nameof(LiftAcceleration2)
            or nameof(BottomWaitTimeAfterLift)
            or nameof(WaitTimeAfterLift)
            or nameof(BottomRetractSpeed)
            or nameof(BottomRetractAcceleration)
            or nameof(RetractSpeed)
            or nameof(RetractAcceleration)
            or nameof(BottomRetractHeight2)
            or nameof(BottomRetractSpeed2)
            or nameof(BottomRetractAcceleration2)
            or nameof(RetractHeight2)
            or nameof(RetractSpeed2)
            or nameof(RetractAcceleration2)
            or nameof(BottomLightPWM)
            or nameof(LightPWM)
        )
        {
            RebuildLayersProperties(false, e.PropertyName);
            if (e.PropertyName
                   is nameof(BottomLayerCount)
                   or nameof(BottomExposureTime)
                   or nameof(ExposureTime)
               && TransitionLayerType == TransitionLayerTypes.Software
              ) ResetCurrentTransitionLayers(false);

            if (e.PropertyName
               is not nameof(BottomLightPWM)
               and not nameof(LightPWM)
              ) UpdatePrintTimeQueued();

            return;
        }

        // Fix transition layers times in software mode
        if (e.PropertyName is nameof(TransitionLayerCount) && TransitionLayerType == TransitionLayerTypes.Software)
        {
            ResetCurrentTransitionLayers();
            return;
        }
    }

    #endregion

    #region Indexers
    public Layer this[uint index]
    {
        get => _layers[index];
        set => SetLayer(index, value);
    }

    public Layer this[int index]
    {
        get => _layers[index];
        set => SetLayer((uint)index, value);
    }

    public Layer this[long index]
    {
        get => _layers[index];
        set => SetLayer((uint)index, value);
    }

    public Layer[] this[System.Range range] => _layers[range];

    /// <summary>
    /// Sets a layer
    /// </summary>
    /// <param name="index">Layer index</param>
    /// <param name="layer">Layer to add</param>
    /// <param name="makeClone">True to add a clone of the layer</param>
    public void SetLayer(uint index, Layer layer, bool makeClone = false)
    {
        if (index >= LayerCount) return;
        layer.IsModified = true;
        _layers[index] = makeClone ? layer.Clone() : layer;
        layer.Index = index;
        layer.SlicerFile = this;
    }

    /// <summary>
    /// Add a list of layers
    /// </summary>
    /// <param name="layers">Layers to add</param>
    /// <param name="makeClone">True to add a clone of layers</param>
    public void SetLayers(IEnumerable<Layer> layers, bool makeClone = false)
    {
        foreach (var layer in layers)
        {
            SetLayer(layer.Index, layer, makeClone);
        }
    }

    /// <summary>
    /// Get layer given index
    /// </summary>
    /// <param name="index">Layer index</param>
    /// <returns></returns>
    public Layer GetLayer(uint index)
    {
        return _layers[index];
    }

    #endregion

    #region Numerators
    public IEnumerator<Layer> GetEnumerator()
    {
        return ((IEnumerable<Layer>)Layers).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
    #endregion

    #region Overrides
    public override bool Equals(object? obj)
    {
        return Equals(obj as FileFormat);
    }

    public bool Equals(FileFormat? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return FileFullPath == other.FileFullPath;
    }

    public override int GetHashCode()
    {
        return FileFullPath?.GetHashCode() ?? 0;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _queueTimerPrintTime.Dispose();
        Clear();
    }

    #endregion

    #region Notification

    /// <summary>
    /// Raise notification for aspect changes, like <see cref="Resolution"/>, <see cref="Display"/> and relatives
    /// </summary>
    protected void NotifyAspectChange()
    {
        RaisePropertyChanged(nameof(Ppmm));
        RaisePropertyChanged(nameof(PpmmMax));
        RaisePropertyChanged(nameof(PixelSizeMicrons));
        RaisePropertyChanged(nameof(PixelArea));
        RaisePropertyChanged(nameof(PixelAreaMicrons));
        RaisePropertyChanged(nameof(PixelSizeMicronsMax));
        RaisePropertyChanged(nameof(PixelSize));
        RaisePropertyChanged(nameof(PixelSizeMax));
    }
    #endregion

    #region Methods
    /// <summary>
    /// Clears all definitions and properties, it also dispose valid candidates
    /// </summary>
    public virtual void Clear()
    {
        FileFullPath = null;
        _layers = [];
        GCode?.Clear();
        ClearThumbnails();
    }

    public void ClearThumbnails()
    {
        if (ThumbnailsCount == 0) return;

        foreach (var mat in Thumbnails)
        {
            mat.Dispose();
        }

        Thumbnails.Clear();

        RaisePropertyChanged(nameof(ThumbnailsCount));
        RaisePropertyChanged(nameof(HaveThumbnails));
        RaisePropertyChanged(nameof(ThumbnailEncodeCount));
        RaisePropertyChanged(nameof(Thumbnails));
    }

    /// <summary>
    /// Check if a file is valid and can be processed before read it against the <see cref="FileFormat"/> decode scheme
    /// </summary>
    /// <param name="fileFullPath"></param>
    /// <returns></returns>
    public virtual bool CanProcess(string? fileFullPath)
    {
        if (fileFullPath is null) return false;
        if (!File.Exists(fileFullPath)) return false;
        //if (!IsExtensionValid(fileFullPath, true)) return false;
        return true;
    }


    /// <summary>
    /// Validate if a file is a valid <see cref="FileFormat"/>
    /// </summary>
    /// <param name="fileFullPath">Full file path</param>
    public void FileValidation(string? fileFullPath)
    {
        if (string.IsNullOrWhiteSpace(fileFullPath)) throw new ArgumentNullException(nameof(FileFullPath), "FileFullPath can't be null nor empty.");
        if (!File.Exists(fileFullPath)) throw new FileNotFoundException("The specified file does not exists.", fileFullPath);

        if (!IsExtensionValid(fileFullPath, true)) throw new FileLoadException("The specified file is not valid.", fileFullPath);
    }

    /// <summary>
    /// Checks if a extension is valid under the <see cref="FileFormat"/>
    /// </summary>
    /// <param name="extension">Extension to check without the dot (.)</param>
    /// <param name="isFilePath">True if <paramref name="extension"/> is a full file path, otherwise false for extension only</param>
    /// <returns>True if valid, otherwise false</returns>
    public bool IsExtensionValid(string extension, bool isFilePath = false)
    {
        if (isFilePath)
        {
            GetFileNameStripExtensions(extension, out extension);
        }
        return !string.IsNullOrWhiteSpace(extension) && FileExtensions.AsValueEnumerable().Any(fileExtension => fileExtension.Equals(extension));
    }

    /// <summary>
    /// Gets all valid file extensions in a specified format
    /// </summary>
    public string GetFileExtensions(string prepend = ".", string separator = ", ")
    {
        var result = string.Empty;

        foreach (var fileExt in FileExtensions)
        {
            if (!ReferenceEquals(result, string.Empty))
            {
                result += separator;
            }
            result += $"{prepend}{fileExt.Extension}";
        }

        return result;
    }

    public bool FileEndsWith(string extension)
    {
        if (FileFullPath is null) return false;
        if (extension[0] != '.') extension = $".{extension}";
        return FileFullPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ||
               FileFullPath.EndsWith($"{extension}{TemporaryFileAppend}", StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// Gets if a <see cref="Point"/> coordinate is inside <see cref="Resolution"/> bounds
    /// </summary>
    /// <param name="xy">The coordinate</param>
    /// <returns></returns>
    public bool IsPointInsideBounds(Point xy)
    {
        return IsPixelInsideXBounds(xy.X) && IsPixelInsideYBounds(xy.Y);
    }

    /// <summary>
    /// Gets if a pixel coordinate is inside <see cref="ResolutionX"/> bounds
    /// </summary>
    /// <param name="x">The X coordinate</param>
    /// <returns></returns>
    public bool IsPixelInsideXBounds(int x)
    {
        return x >= 0 && x < ResolutionX;
    }

    /// <summary>
    /// Gets if a pixel coordinate is inside <see cref="ResolutionY"/> bounds
    /// </summary>
    /// <param name="y">The Y coordinate</param>
    /// <returns></returns>
    public bool IsPixelInsideYBounds(int y)
    {
        return y >= 0 && y < ResolutionY;
    }

    /// <summary>
    /// Gets if a pixel coordinate is inside <see cref="ResolutionX"/> bounds
    /// </summary>
    /// <param name="x">The coordinate</param>
    /// <returns></returns>
    public bool IsPixelInsideXBounds(uint x)
    {
        return x < ResolutionX;
    }

    /// <summary>
    /// Gets if a pixel coordinate is inside <see cref="ResolutionY"/> bounds
    /// </summary>
    /// <param name="y">The Y coordinate</param>
    /// <returns></returns>
    public bool IsPixelInsideYBounds(uint y)
    {
        return y < ResolutionY;
    }

    /// <summary>
    /// Renames the current file with a new name in the same directory.
    /// </summary>
    /// <param name="newFileName">New filename without the extension</param>
    /// <param name="overwrite">True to overwrite file if exists, otherwise false</param>
    /// <returns>True if renamed, otherwise false.</returns>
    public bool RenameFile(string newFileName, bool overwrite = false)
    {
        if(string.IsNullOrWhiteSpace(newFileName)) return false;
        if (!File.Exists(FileFullPath)) return false;

        var filename = GetFileNameStripExtensions(FileFullPath, out var ext);

        if (string.Equals(filename, newFileName, StringComparison.Ordinal)) return false;

        var newFileFullPath = Path.Combine(DirectoryPath!, $"{newFileName}.{ext}");

        try
        {
            File.Move(FileFullPath, newFileFullPath, overwrite);
            FileFullPath = newFileFullPath;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            throw;
        }


        return true;
    }

    /// <summary>
    /// Gets a thumbnail by a maximum height or lower
    /// </summary>
    /// <param name="maxHeight">Max height allowed</param>
    /// <returns></returns>
    public Mat? GetThumbnailByHeight(uint maxHeight = 400)
    {
        return Thumbnails.AsValueEnumerable().OrderByDescending(mat => mat.Height).FirstOrDefault(thumbnail => !thumbnail.IsEmpty && thumbnail.Height <= maxHeight);
    }

    /// <summary>
    /// Gets a thumbnail by a maximum width or lower
    /// </summary>
    /// <param name="maxWidth">Max width allowed</param>
    /// <returns></returns>
    public Mat? GetThumbnailByWidth(int maxWidth)
    {
        return Thumbnails.AsValueEnumerable().OrderByDescending(mat => mat.Width).FirstOrDefault(thumbnail => !thumbnail.IsEmpty && thumbnail.Width <= maxWidth);
    }

    /// <summary>
    /// Gets a thumbnail by a maximum height or lower
    /// </summary>
    /// <param name="maxHeight">Max height allowed</param>
    /// <returns></returns>
    public Mat? GetThumbnailByHeight(int maxHeight)
    {
        return Thumbnails.AsValueEnumerable().OrderByDescending(mat => mat.Height).FirstOrDefault(thumbnail => !thumbnail.IsEmpty && thumbnail.Height <= maxHeight);
    }

    /// <summary>
    /// Gets a thumbnail by index in a safe manner (No exceptions)
    /// </summary>
    /// <param name="index">Thumbnail index</param>
    /// <returns></returns>
    public Mat? GetThumbnail(int index)
    {
        return index < 0 || ThumbnailsCount <= index ? null : Thumbnails[index];
    }

    /// <summary>
    /// Gets the thumbnail by it smallest or largest size
    /// </summary>
    /// <returns></returns>
    public Mat? GetThumbnail(FileThumbnailSize size)
    {
        return size switch
        {
            FileThumbnailSize.Small => GetSmallestThumbnail(),
            FileThumbnailSize.Large => GetLargestThumbnail(),
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, null)
        };
    }

    /// <summary>
    /// Gets the largest thumbnail in the collection
    /// </summary>
    /// <returns></returns>
    public Mat? GetSmallestThumbnail()
    {
        if (ThumbnailsCount == 0) return null;
        return Thumbnails.AsValueEnumerable().Where(mat => !mat.IsEmpty).MinBy(mat => mat.Size.Area());
    }

    /// <summary>
    /// Gets the largest thumbnail in the collection
    /// </summary>
    /// <returns></returns>
    public Mat? GetLargestThumbnail()
    {
        if (ThumbnailsCount == 0) return null;
        return Thumbnails.AsValueEnumerable().Where(mat => !mat.IsEmpty).MaxBy(mat => mat.Size.Area());
    }

    /// <summary>
    /// Sanitizes the thumbnails to respect the file specification:<br/>
    /// - Remove empty thumbnails<br/>
    /// - Remove the excess thumbnails up to spec, if requested<br/>
    /// - Check if the thumbnails have the correct number of channels<br/>
    /// - Force BGR color space and strip alpha channel if required by the format<br/>
    /// - Resize thumbnails to the spec<br/>
    /// - Creates missing thumbnails required by the file spec<br/>
    /// </summary>
    /// <param name="trimToFileSpec">True to trim the excess thumbnails, otherwise false to not trim</param>
    /// <returns>True if anything changed, otherwise false</returns>
    public bool SanitizeThumbnails(bool trimToFileSpec = false)
    {
        bool changed = false;
        // Remove empty thumbnails, however this should never happen
        for (int i = ThumbnailsCount-1; i >= 0; i--)
        {
            if (!Thumbnails[i].IsEmpty) continue;
            Thumbnails.RemoveAt(i);
            changed = true;
        }

        // Remove the excess thumbnails up to spec, if requested
        if (trimToFileSpec && ThumbnailsCount > ThumbnailsOriginalSize.Length)
        {
            var difference = ThumbnailsCount - ThumbnailsOriginalSize.Length;
            Thumbnails.RemoveRange(ThumbnailsCount - difference, difference);
            changed = true;
        }

        // Check if the thumbnails have the correct number of channels
        for (var i = 0; i < ThumbnailsCount; i++)
        {
            var numberOfChannels = Thumbnails[i].NumberOfChannels;
            var validNumberOfChannels = new[] { 1, 3, 4 };


            if (!validNumberOfChannels.AsValueEnumerable().Contains(numberOfChannels))
            {
                throw new InvalidDataException($"The thumbnail {i} holds an invalid number of channels ({numberOfChannels}). To be valid should have: <{string.Join(", ", validNumberOfChannels)}>.");
            }
        }

        // Force BGR color space and strip alpha channel if required by the format
        if (FileType != FileFormatType.Archive)
        {
            for (var i = 0; i < ThumbnailsCount; i++)
            {
                switch (Thumbnails[i].NumberOfChannels)
                {
                    case 1:
                        CvInvoke.CvtColor(Thumbnails[i], Thumbnails[i], ColorConversion.Gray2Bgr);
                        changed = true;
                        break;
                    case 4:
                        CvInvoke.CvtColor(Thumbnails[i], Thumbnails[i], ColorConversion.Bgra2Bgr);
                        changed = true;
                        break;
                }
            }
        }

        // Resize thumbnails to the spec
        if (ThumbnailsCount > 0 && ThumbnailsOriginalSize.Length > 0)
        {
            int originalThumbnailSize = 0;
            for (var i = 0; i < ThumbnailsCount; i++)
            {
                if (Thumbnails[i].Size != ThumbnailsOriginalSize[originalThumbnailSize])
                {
                    CvInvoke.Resize(Thumbnails[i], Thumbnails[i], ThumbnailsOriginalSize[originalThumbnailSize]);
                    changed = true;
                }

                originalThumbnailSize++;
                if (originalThumbnailSize >= ThumbnailsOriginalSize.Length)
                {
                    originalThumbnailSize = 0;
                }
            }
        }

        // Creates missing thumbnails required by the file spec
        while (ThumbnailsCount < ThumbnailsOriginalSize.Length)
        {
            changed = true;
            var requestedSize = ThumbnailsOriginalSize[ThumbnailsCount];
            var bestThumbnail = GetThumbnailByHeight(requestedSize.Height)?.Clone();
            if (bestThumbnail is null)
            {
                using var matRoi = DecodeType == FileDecodeType.Partial ? null : FirstLayer?.LayerMatModelBoundingRectangle;
                if (matRoi is null || matRoi.RoiMat.IsEmpty)
                {
                    var genMat = EmguExtensions.InitMat(new Size(200, 100), 3);
                    CvInvoke.PutText(genMat, About.Software, new Point(40, 60), FontFace.HersheyDuplex, 1, EmguExtensions.WhiteColor, 2);
                    if (genMat.Size != requestedSize) CvInvoke.Resize(genMat, genMat, requestedSize);
                    Thumbnails.Add(genMat);
                }
                else
                {
                    var genMat = new Mat();
                    CvInvoke.CvtColor(matRoi.RoiMat, genMat, ColorConversion.Gray2Bgr);
                    if (genMat.Size != requestedSize) CvInvoke.Resize(genMat, genMat, requestedSize);
                    CvInvoke.Resize(genMat, genMat, requestedSize);
                    Thumbnails.Add(genMat);
                }
            }
            else
            {
                if (bestThumbnail.Size != requestedSize)
                {
                    CvInvoke.Resize(bestThumbnail, bestThumbnail, requestedSize);
                }
                Thumbnails.Add(bestThumbnail);
            }
        }

        if (changed)
        {
            RaisePropertyChanged(nameof(ThumbnailsCount));
            RaisePropertyChanged(nameof(HaveThumbnails));
            RaisePropertyChanged(nameof(ThumbnailEncodeCount));
            RaisePropertyChanged(nameof(Thumbnails));
            RequireFullEncode = true;
        }

        return changed;
    }


    /// <summary>
    /// Replaces thumbnails from a list of thumbnails and clone them
    /// </summary>
    /// <param name="images"></param>
    /// <param name="generateImagesIfEmpty">If true and if <paramref name="images"/> is empty, it will fill the thumbnails with generated images</param>
    public bool ReplaceThumbnails(IEnumerable<Mat> images, bool generateImagesIfEmpty = false)
    {
        foreach (var thumbnail in Thumbnails)
        {
            thumbnail.Dispose();
        }

        Thumbnails.Clear();
        var haveImages = images.AsValueEnumerable().Any();
        if (!haveImages && !generateImagesIfEmpty) return false;

        if (haveImages)
        {
            Thumbnails.AddRange(images.Where(mat => !mat.IsEmpty).Select(mat => mat.Clone()));
        }

        if (!SanitizeThumbnails())
        {
            RaisePropertyChanged(nameof(ThumbnailsCount));
            RaisePropertyChanged(nameof(HaveThumbnails));
            RaisePropertyChanged(nameof(ThumbnailEncodeCount));
            RaisePropertyChanged(nameof(Thumbnails));
            RequireFullEncode = true;
        }

        return true;
    }

    /// <summary>
    /// Sets the current thumbnails from a list of thumbnails and clone them
    /// </summary>
    /// <param name="images"></param>
    /// <param name="generateImagesIfEmpty">If true and if <paramref name="images"/> is empty, it will fill the thumbnails with generated images</param>
    public bool SetThumbnails(IEnumerable<Mat> images, bool generateImagesIfEmpty = false)
    {
        if (ThumbnailsCount == 0) return false;

        var imageList = images.AsValueEnumerable().Where(mat => !mat.IsEmpty).ToList();
        var haveImages = imageList.Count > 0;
        if (!haveImages && !generateImagesIfEmpty) return false;

        if (haveImages)
        {
            var imageIndex = 0;
            for (int i = 0; i < ThumbnailsCount; i++)
            {
                var image = imageList[Math.Min(imageIndex++, imageList.Count - 1)];
                SetThumbnail(i, image);
            }
        }

        if (!SanitizeThumbnails())
        {
            RaisePropertyChanged(nameof(ThumbnailsCount));
            RaisePropertyChanged(nameof(HaveThumbnails));
            RaisePropertyChanged(nameof(ThumbnailEncodeCount));
            RaisePropertyChanged(nameof(Thumbnails));
            RequireFullEncode = true;
        }

        return true;

    }

    /// <summary>
    /// Sets all thumbnails the same image
    /// </summary>
    /// <param name="image">Image to set</param>
    public bool SetThumbnails(Mat image)
    {
        Guard.IsNotNull(image);
        return SetThumbnails([image]);
    }

    /// <summary>
    /// Sets all thumbnails from a disk file
    /// </summary>
    /// <param name="filePath"></param>
    public bool SetThumbnails(string filePath)
    {
        if (ThumbnailsCount == 0) return false;
        if (!File.Exists(filePath)) return false;
        using var image = CvInvoke.Imread(filePath, ImreadModes.Color);
        return SetThumbnails(image);
    }

    /// <summary>
    /// Sets a thumbnail from mat
    /// </summary>
    /// <param name="index">Thumbnail index</param>
    /// <param name="image"></param>
    public bool SetThumbnail(int index, Mat image)
    {
        Guard.IsNotNull(image);
        if (index >= ThumbnailsCount) return false;
        if (ReferenceEquals(Thumbnails[index], image)) return false;
        Thumbnails[index].Dispose();
        Thumbnails[index] = image.Clone();
        if (ThumbnailsOriginalSize.Length-1 >= index && Thumbnails[index].Size != ThumbnailsOriginalSize[index])
        {
            CvInvoke.Resize(Thumbnails[index], Thumbnails[index], ThumbnailsOriginalSize[index]);
        }
        RaisePropertyChanged(nameof(Thumbnails));
        RequireFullEncode = true;
        return true;
    }


    /// <summary>
    /// Sets a thumbnail from a disk file
    /// </summary>
    /// <param name="index">Thumbnail index</param>
    /// <param name="filePath"></param>
    public bool SetThumbnail(int index, string filePath)
    {
        if (index >= ThumbnailsCount) return false;
        if (!File.Exists(filePath)) return false;
        return SetThumbnail(index, CvInvoke.Imread(filePath, ImreadModes.Color));
    }

    /// <summary>
    /// Method that are called before a full or partial encode
    /// <param name="isPartialEncode">True if is a partial encode, otherwise false</param>
    /// </summary>
    private void BeforeEncode(bool isPartialEncode)
    {
        // Convert wait time to light-off delay if first not supported but the second is
        var bottomWaitTime = BottomWaitTimeAfterCure;
        var normalWaitTime = WaitTimeAfterCure;

        if (bottomWaitTime > 0 && !CanUseBottomWaitTimeBeforeCure && CanUseBottomLightOffDelay)
        {
            SetBottomLightOffDelay(normalWaitTime);
        }

        if (normalWaitTime > 0 && !CanUseWaitTimeBeforeCure && CanUseLightOffDelay)
        {
            SetNormalLightOffDelay(normalWaitTime);
        }
    }

    /// <summary>
    /// Triggers before attempt to save/encode the file
    /// </summary>
    protected virtual void OnBeforeEncode(bool isPartialEncode)
    { }

    /// <summary>
    /// Triggers after save/encode the file
    /// </summary>
    protected virtual void OnAfterEncode(bool isPartialEncode) { }

    /// <summary>
    /// Encode to an output file
    /// </summary>
    /// <param name="progress"></param>
    protected abstract void EncodeInternally(OperationProgress progress);

    /// <summary>
    /// Encode to an output file
    /// </summary>
    /// <param name="fileFullPath">Output file</param>
    /// <param name="progress"></param>
    public void Encode(string? fileFullPath, OperationProgress? progress = null)
    {
        fileFullPath ??= FileFullPath ?? throw new ArgumentNullException(nameof(fileFullPath));
        if (DecodeType == FileDecodeType.Partial) throw new InvalidOperationException("File was partial decoded, a full encode is not possible.");

        progress ??= new OperationProgress();
        progress.Reset(OperationProgress.StatusEncodeLayers, LayerCount);

        Sanitize();

        // Backup old file name and prepare the temporary file to be written next
        var oldFilePath = FileFullPath;
        FileFullPath = fileFullPath;
        var tempFile = TemporaryOutputFileFullPath;
        if (File.Exists(tempFile)) File.Delete(tempFile);

        // Sanitize Version after file name is set
        SanitizeVersion();

        // Make sure thumbnails are all set, otherwise clone/create them
        SanitizeThumbnails(FileType != FileFormatType.Archive);

        OnBeforeEncode(false);
        BeforeEncode(false);

        ValidateLayerImageFormat();

        bool success;
        try
        {
            EncodeInternally(progress);

            // Move temporary output file in place
            if (File.Exists(tempFile)) File.Move(tempFile, fileFullPath, true);

            IsModified = false;
            RequireFullEncode = false;

            success = true;
        }
        catch (Exception)
        {
            // Restore backup file path and delete the temporary
            FileFullPath = oldFilePath;
            if (File.Exists(tempFile)) File.Delete(tempFile);
            throw;
        }

        if (success) OnAfterEncode(false);
    }

    public void Encode(OperationProgress progress) => Encode(null, progress);

    public Task EncodeAsync(string? fileFullPath, OperationProgress? progress = null) =>
        Task.Run(() => Encode(fileFullPath, progress), progress?.Token ?? default);

    public Task EncodeAsync(OperationProgress progress) => EncodeAsync(null, progress);

    /// <summary>
    /// Decode a slicer file
    /// </summary>
    /// <param name="progress"></param>
    protected abstract void DecodeInternally(OperationProgress progress);

    /// <summary>
    /// Decode a slicer file
    /// </summary>
    /// <param name="fileFullPath">file path to load, use null to reload file</param>
    /// <param name="progress"></param>
    public void Decode(string? fileFullPath = null, OperationProgress? progress = null) => Decode(fileFullPath, FileDecodeType.Full, progress);

    /// <summary>
    /// Decode a slicer file
    /// </summary>
    /// <param name="fileFullPath">file path to load, use null to reload file</param>
    /// <param name="fileDecodeType"></param>
    /// <param name="progress"></param>
    public void Decode(string? fileFullPath, FileDecodeType fileDecodeType, OperationProgress? progress = null)
    {
        Clear();
        if(!string.IsNullOrWhiteSpace(fileFullPath)) FileFullPath = fileFullPath;
        FileValidation(FileFullPath);

        DecodeType = fileDecodeType;
        progress ??= new OperationProgress();
        progress.Reset(OperationProgress.StatusGatherLayers, LayerCount);

        DecodeInternally(progress);
        progress.PauseOrCancelIfRequested();

        var layerHeightDigits = LayerHeight.DecimalDigits();
        if (layerHeightDigits > Layer.HeightPrecision)
        {
            throw new FileLoadException($"The layer height ({LayerHeight}mm) have more decimal digits than the supported ({Layer.HeightPrecision}) digits.\n" +
                                        "Lower and fix your layer height on slicer to avoid precision errors.", fileFullPath);
        }

        IsModified = false;
        StartingMaterialMilliliters = MaterialMilliliters;
        StartingMaterialCost = MaterialCost;
        if (StartingMaterialCost <= 0)
        {
            StartingMaterialCost = StartingMaterialMilliliters * CoreSettings.AverageResin1000MlBottleCost / 1000f;
            MaterialCost = StartingMaterialCost;
        }

        if (CanUseTransitionLayerCount && TransitionLayerType == TransitionLayerTypes.Software)
        {
            SuppressRebuildPropertiesWork(() =>
            {
                var transitionLayers = ParseTransitionLayerCountFromLayers();
                if (transitionLayers > 0)
                {
                    TransitionLayerCount = transitionLayers;
                }
            });
        }

        bool reSaveFile = Sanitize();
        if (reSaveFile)
        {
            Save(progress);
        }

        var thumbnailsSanitized = SanitizeThumbnails();

        GetBoundingRectangle(progress);
    }

    public Task DecodeAsync(string? fileFullPath, FileDecodeType fileDecodeType, OperationProgress? progress = null) =>
        Task.Run(() => Decode(fileFullPath, fileDecodeType, progress), progress?.Token ?? default);

    public Task DecodeAsync(string? fileFullPath = null, OperationProgress? progress = null)
        => DecodeAsync(fileFullPath, FileDecodeType.Full, progress);


    /// <summary>
    /// Reloads the file
    /// </summary>
    /// <param name="fileDecodeType"></param>
    /// <param name="progress"></param>
    public void Reload(FileDecodeType fileDecodeType, OperationProgress? progress = null) => Decode(null, fileDecodeType, progress);

    /// <summary>
    /// Reloads the file
    /// </summary>
    /// <param name="progress"></param>
    public void Reload(OperationProgress? progress = null) => Reload(FileDecodeType.Full, progress);

    /// <summary>
    /// Reloads the file
    /// </summary>
    /// <param name="fileDecodeType"></param>
    /// <param name="progress"></param>
    public Task ReloadAsync(FileDecodeType fileDecodeType, OperationProgress? progress = null) => DecodeAsync(null, fileDecodeType, progress);

    /// <summary>
    /// Reloads the file
    /// </summary>
    /// <param name="progress"></param>
    public Task ReloadAsync(OperationProgress? progress = null) => ReloadAsync(FileDecodeType.Full, progress);

    /// <summary>
    /// Calculate  and store layers hash
    /// </summary>
    public void CalculateLayersHash()
    {
        foreach (var layer in this)
        {
            var hash = layer.Hash;
        }
    }

    /// <summary>
    /// Detect image format from <see cref="Mat"/>
    /// </summary>
    /// <param name="mat"></param>
    /// <param name="png24Variant"></param>
    /// <returns></returns>
    /// <exception cref="MessageException"></exception>
    public ImageFormat FetchImageFormat(Mat mat, ImageFormat png24Variant = ImageFormat.Png24BgrAA)
    {
        if (mat.Depth != DepthType.Cv8U) throw new MessageException($"Unable to auto detect and/or use image format from {mat.Depth} depth. Was expecting {DepthType.Cv8U}.");
        return mat.NumberOfChannels switch
        {
            1 => ImageFormat.Png8,
            3 => ResolutionX > 0 && ResolutionX == mat.Width * 3 ? png24Variant : ImageFormat.Png24,
            4 => ImageFormat.Png32,
            _ => throw new MessageException($"Unable to auto detect and/or use image format from {mat.NumberOfChannels} channels. Was expecting 1, 3 or 4.")
        };
    }

    /// <summary>
    /// Detect image format from image byte array
    /// </summary>
    /// <param name="matBytes"></param>
    /// <param name="png24Variant"></param>
    /// <returns></returns>
    public ImageFormat FetchImageFormat(byte[] matBytes, ImageFormat png24Variant = ImageFormat.Png24BgrAA)
    {
        using var mat = new Mat();
        CvInvoke.Imdecode(matBytes, ImreadModes.Unchanged, mat);
        return FetchImageFormat(mat, png24Variant);
    }

    /// <summary>
    /// Detect image format from <see cref="ZipArchive"/>
    /// </summary>
    /// <param name="archive"></param>
    /// <param name="png24Variant"></param>
    /// <param name="mathRegex"></param>
    /// <returns></returns>
    /// <exception cref="MessageException"></exception>
    public ImageFormat FetchImageFormat(ZipArchive archive, ImageFormat png24Variant = ImageFormat.Png24BgrAA, string mathRegex = "([0-9]+)[.]png$")
    {
        foreach (var pngEntry in archive.Entries)
        {
            if (!pngEntry.Name.EndsWith(".png")) continue;
            var match = Regex.Match(pngEntry.Name, mathRegex);
            if (!match.Success || match.Groups.Count < 2) continue;
            if (!uint.TryParse(match.Groups[1].Value, out _)) continue;

            using var stream = pngEntry.Open();
            return FetchImageFormat(stream.ToArray(), png24Variant);
        }

        throw new MessageException("Unable to detect layer image format from the archive, no valid candidates found.");
    }

    /// <summary>
    /// Encodes all <see cref="Thumbnails"/> into <paramref name="zipArchive"/> given a path format
    /// </summary>
    /// <param name="zipArchive"></param>
    /// <param name="pathFormat">
    /// {0} = Index<br/>
    /// {1} = Width<br/>
    /// {2} = Height<br/>
    /// <para>Example: thumbnail/thumbnail{1}x{2}.png</para>
    /// </param>
    /// <param name="progress"></param>
    public void EncodeAllThumbnailsInZip(ZipArchive zipArchive, string pathFormat = "preview{0}.png", OperationProgress? progress = null)
    {
        progress ??= new OperationProgress();
        progress.Reset(OperationProgress.StatusEncodePreviews, (uint)ThumbnailsCount);
        for (var i = 0; i < ThumbnailsCount; i++)
        {
            var thumbnail = Thumbnails[i];
            zipArchive.CreateEntryFromContent(string.Format(pathFormat, i, thumbnail.Width, thumbnail.Height), thumbnail.GetPngByes(), ZipArchiveMode.Create);
            progress++;
        }
    }

    /// <summary>
    /// Encodes <see cref="Thumbnails"/> into <paramref name="zipArchive"/> given specific entry path's
    /// </summary>
    /// <param name="zipArchive"></param>
    /// <param name="progress"></param>
    /// <param name="entryPaths"></param>
    public void EncodeThumbnailsInZip(ZipArchive zipArchive, OperationProgress? progress = null, params string[] entryPaths)
    {
        progress ??= new OperationProgress();
        progress.Reset(OperationProgress.StatusEncodePreviews, (uint)entryPaths.Length);
        for (var i = 0; i < ThumbnailsCount && i < entryPaths.Length; i++)
        {
            var thumbnail = Thumbnails[i];
            zipArchive.CreateEntryFromContent(entryPaths[i], thumbnail.GetPngByes(), ZipArchiveMode.Create);
            progress++;
        }
    }

    public void DecodeThumbnailsFromZip(ZipArchive zipArchive, OperationProgress? progress = null, params string[] entryPaths)
    {
        progress ??= new OperationProgress();
        progress.Reset(OperationProgress.StatusDecodePreviews, (uint)entryPaths.Length);
        foreach (var entryPath in entryPaths)
        {
            using var stream = zipArchive.GetEntry(entryPath)?.Open();
            if (stream is null) continue;
            var mat = new Mat();
            CvInvoke.Imdecode(stream.ToArray(), ImreadModes.Unchanged, mat);
            Thumbnails.Add(mat);
            progress++;
        }
    }

    public void DecodeAllThumbnailsFromZip(ZipArchive zipArchive, OperationProgress? progress = null, string entryStartsWith = "preview", string entryEndsWith = ".png")
    {
        progress ??= new OperationProgress();
        progress.Reset(OperationProgress.StatusDecodePreviews, (uint)ThumbnailCountFileShouldHave);
        foreach (var entity in zipArchive.Entries)
        {
            if (!string.IsNullOrWhiteSpace(entryEndsWith) && !entity.Name.EndsWith(entryEndsWith)) continue;
            if (!entity.Name.StartsWith(entryStartsWith)) continue;
            using var stream = entity.Open();
            Mat mat = new();
            CvInvoke.Imdecode(stream.ToArray(), ImreadModes.Unchanged, mat);
            Thumbnails.Add(mat);
            progress++;
        }
    }

    public void EncodeLayersInZip(ZipArchive zipArchive, string prepend, byte padDigits, IndexStartNumber layerIndexStartNumber = default,
        OperationProgress? progress = null, string path = "", bool useCache = false, Func<uint, Mat, Mat>? matGenFunc = null)
    {
        if (DecodeType != FileDecodeType.Full || !HaveLayers) return;
        progress ??= new OperationProgress();
        progress.Reset(OperationProgress.StatusEncodeLayers, LayerCount);
        var batches = BatchLayers();
        var pngLayerBytes = new byte[LayerCount][];

        var layerImageType = LayerImageFormat;

        if (useCache)
        {
            var distinctLayers = this.DistinctBy(layer => layer.Hash);
            batches = distinctLayers.Chunk(DefaultParallelBatchCount);
        }

        foreach (var batch in batches)
        {
            Parallel.ForEach(batch, CoreSettings.GetParallelOptions(progress), layer =>
            {
                progress.PauseIfRequested();
                var layerIndex = layer.Index;
                if (matGenFunc is null)
                {
                    switch (layerImageType)
                    {
                        case ImageFormat.Png24:
                        {
                            using var mat = layer.LayerMat;
                            CvInvoke.CvtColor(mat, mat, ColorConversion.Gray2Bgr);
                            pngLayerBytes[layerIndex] = mat.GetPngByes();

                            break;
                        }
                        case ImageFormat.Png32:
                        {
                            using var mat = layer.LayerMat;
                            CvInvoke.CvtColor(mat, mat, ColorConversion.Gray2Bgra);
                            pngLayerBytes[layerIndex] = mat.GetPngByes();

                            break;
                        }
                        case ImageFormat.Png24BgrAA:
                        {
                            using var mat = layer.LayerMat;
                            using var outputMat = mat.Reshape(3);
                            pngLayerBytes[layerIndex] = outputMat.GetPngByes();

                            break;
                        }
                        case ImageFormat.Png24RgbAA:
                        {
                            using var mat = layer.LayerMat;
                            using var outputMat = mat.Reshape(3);
                            CvInvoke.CvtColor(outputMat, outputMat, ColorConversion.Bgr2Rgb);
                            pngLayerBytes[layerIndex] = outputMat.GetPngByes();

                            break;
                        }
                        default:
                            pngLayerBytes[layerIndex] = layer.CompressedPngBytes;
                            break;
                    }
                }
                else
                {
                    using var mat = layer.LayerMat;
                    using var newMat = matGenFunc.Invoke(layerIndex, mat);
                    pngLayerBytes[layerIndex] = newMat.GetPngByes();
                }

                progress.LockAndIncrement();
            });

            foreach (var layer in batch)
            {
                var layerIndex = layer.Index;
                var entryPath = Path.Combine(path, layer.FormatFileName(prepend, padDigits, layerIndexStartNumber));
                zipArchive.CreateEntryFromContent(entryPath, pngLayerBytes[layerIndex], ZipArchiveMode.Create);
                pngLayerBytes[layerIndex] = null!;
            }
        }
    }

    public void EncodeLayersInZip(ZipArchive zipArchive, byte padDigits, IndexStartNumber layerIndexStartNumber = default, OperationProgress? progress = null, string path = "", bool useCache = false, Func<uint, Mat, Mat>? matGenFunc = null)
        => EncodeLayersInZip(zipArchive, string.Empty, padDigits, layerIndexStartNumber, progress, path, useCache, matGenFunc);

    public void EncodeLayersInZip(ZipArchive zipArchive, string prepend, IndexStartNumber layerIndexStartNumber = default, OperationProgress? progress = null, string path = "", bool useCache = false, Func<uint, Mat, Mat>? matGenFunc = null)
        => EncodeLayersInZip(zipArchive, prepend, 0, layerIndexStartNumber, progress, path, useCache, matGenFunc);

    public void EncodeLayersInZip(ZipArchive zipArchive, IndexStartNumber layerIndexStartNumber, OperationProgress? progress = null, string path = "", bool useCache = false, Func<uint, Mat, Mat>? matGenFunc = null)
        => EncodeLayersInZip(zipArchive, string.Empty, 0, layerIndexStartNumber, progress, path, useCache, matGenFunc);

    public void EncodeLayersInZip(ZipArchive zipArchive, OperationProgress progress, string path = "", bool useCache = false, Func<uint, Mat, Mat>? matGenFunc = null)
        => EncodeLayersInZip(zipArchive, string.Empty, 0, IndexStartNumber.Zero, progress, path, useCache, matGenFunc);


    public void DecodeLayersFromZip(ZipArchiveEntry[] layerEntries, OperationProgress? progress = null, Func<uint, byte[], Mat>? matGenFunc = null)
    {
        if (DecodeType != FileDecodeType.Full || !HaveLayers) return;
        progress ??= new OperationProgress();
        progress.Reset(OperationProgress.StatusDecodeLayers, LayerCount);

        var layerImageFormat = LayerImageFormat;

        Parallel.For(0, LayerCount, CoreSettings.GetParallelOptions(progress), layerIndex =>
        {
            progress.PauseIfRequested();
            byte[] pngBytes;
            lock (Mutex)
            {
                using var stream = layerEntries[layerIndex].Open();
                pngBytes = stream.ToArray();
            }

            if (matGenFunc is null)
            {
                /*if (layerImageFormat == ImageFormat.AutoDetect)
                {
                    using var mat = new Mat();
                    CvInvoke.Imdecode(pngBytes, ImreadModes.Unchanged, mat);
                    if (mat.Depth != DepthType.Cv8U) throw new MessageException($"Unable to auto detect and/or use image format from {mat.Depth} depth. Was expecting {DepthType.Cv8U}.", FileFullPath);
                    if (mat.NumberOfChannels == 1)
                    {
                        LayerImageFormat = layerImageFormat = ImageFormat.Png8;
                    }
                    else if (mat.NumberOfChannels == 3)
                    {
                        LayerImageFormat = layerImageFormat = ImageFormat.Png24;
                    }
                    else if (mat.NumberOfChannels == 4)
                    {
                        LayerImageFormat = layerImageFormat = ImageFormat.Png32;
                    }
                    else
                    {
                        throw new MessageException($"Unable to auto detect and/or use image format from {mat.NumberOfChannels} channels. Was expecting 1, 3 or 4.");
                    }
                }*/

                switch (layerImageFormat)
                {
                    case ImageFormat.Png8:
                    case ImageFormat.Png24:
                    case ImageFormat.Png32:
                        _layers[layerIndex] = new Layer((uint)layerIndex, pngBytes, this);
                        break;
                    case ImageFormat.Png24BgrAA:
                    {
                        using var bgrMat = new Mat();
                        CvInvoke.Imdecode(pngBytes, ImreadModes.Color, bgrMat);
                        using var greyMat = bgrMat.Reshape(1);

                        _layers[layerIndex] = new Layer((uint) layerIndex, greyMat, this);

                        break;
                    }
                    case ImageFormat.Png24RgbAA:
                    {
                        using Mat rgbMat = new();
                        CvInvoke.Imdecode(pngBytes, ImreadModes.Color, rgbMat);
                        CvInvoke.CvtColor(rgbMat, rgbMat, ColorConversion.Bgr2Rgb);
                        using var greyMat = rgbMat.Reshape(1);

                        _layers[layerIndex] = new Layer((uint)layerIndex, greyMat, this);

                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException(nameof(layerImageFormat), layerImageFormat, null);
                }
            }
            else
            {
                using var mat = matGenFunc.Invoke((uint) layerIndex, pngBytes);
                _layers[layerIndex] = new Layer((uint)layerIndex, mat, this);
            }

            progress.LockAndIncrement();
        });
    }

    public void DecodeLayersFromZipRegex(ZipArchive zipArchive, string regex, IndexStartNumber layerIndexStartNumber = IndexStartNumber.Zero, OperationProgress? progress = null, Func<uint, byte[], Mat>? matGenFunc = null)
    {
        var layerEntries = new ZipArchiveEntry?[LayerCount];

        foreach (var entry in zipArchive.Entries)
        {
            var match = Regex.Match(entry.Name, regex);
            if (!match.Success || match.Groups.Count < 2 || match.Groups[1].Value.Length == 0 || !uint.TryParse(match.Groups[1].Value, out var layerIndex)) continue;


            if (layerIndexStartNumber == IndexStartNumber.One && layerIndex > 0) layerIndex--;
            if (layerIndex >= LayerCount) continue;

            layerEntries[layerIndex] = entry;
        }

        for (uint layerIndex = 0; layerIndex < LayerCount; layerIndex++)
        {
            if (layerEntries[layerIndex] is not null) continue;
            Clear();
            throw new FileLoadException($"Layer {layerIndex} not found", FileFullPath);
        }


        DecodeLayersFromZip(layerEntries!, progress, matGenFunc);
    }

    public void DecodeLayersFromZip(ZipArchive zipArchive, byte padDigits, IndexStartNumber layerIndexStartNumber = IndexStartNumber.Zero, OperationProgress? progress = null, Func<uint, byte[], Mat>? matGenFunc = null)
        => DecodeLayersFromZipRegex(zipArchive, @$"([0-9]{{{padDigits}}})[.]png$", layerIndexStartNumber, progress, matGenFunc);

    public void DecodeLayersFromZip(ZipArchive zipArchive, string prepend, IndexStartNumber layerIndexStartNumber = IndexStartNumber.Zero, OperationProgress? progress = null, Func<uint, byte[], Mat>? matGenFunc = null)
        => DecodeLayersFromZipRegex(zipArchive, @$"^{Regex.Escape(prepend)}([0-9]+)[.]png$", layerIndexStartNumber, progress, matGenFunc);

    public void DecodeLayersFromZip(ZipArchive zipArchive, IndexStartNumber layerIndexStartNumber = IndexStartNumber.Zero, OperationProgress? progress = null, Func<uint, byte[], Mat>? matGenFunc = null)
        => DecodeLayersFromZipRegex(zipArchive, @"^([0-9]+)[.]png$", layerIndexStartNumber, progress, matGenFunc);

    public void DecodeLayersFromZip(ZipArchive zipArchive, OperationProgress progress, Func<uint, byte[], Mat>? matGenFunc = null)
        => DecodeLayersFromZipRegex(zipArchive, @"^([0-9]+)[.]png$", IndexStartNumber.Zero, progress, matGenFunc);

    public void DecodeLayersFromZipIgnoreFilename(ZipArchive zipArchive, IndexStartNumber layerIndexStartNumber = IndexStartNumber.Zero, OperationProgress? progress = null, Func<uint, byte[], Mat>? matGenFunc = null)
    {
        DecodeLayersFromZipRegex(zipArchive, @$"([0-9]{{1,{LayerDigits}}})[.]png$", layerIndexStartNumber, progress, matGenFunc);
    }

    public void DecodeLayersFromZipIgnoreFilename(ZipArchive zipArchive, OperationProgress progress, Func<uint, byte[], Mat>? matGenFunc = null)
        => DecodeLayersFromZipIgnoreFilename(zipArchive, IndexStartNumber.Zero, progress, matGenFunc);

    /// <summary>
    /// Extract contents to a folder
    /// </summary>
    /// <param name="path">Path to folder where content will be extracted</param>
    /// <param name="genericConfigExtract"></param>
    /// <param name="genericLayersExtract"></param>
    /// <param name="progress"></param>
    public virtual void Extract(string path, bool genericConfigExtract = true, bool genericLayersExtract = true, OperationProgress? progress = null)
    {
        progress ??= new OperationProgress();
        progress.ItemName = OperationProgress.StatusExtracting;
        /*if (emptyFirst)
        {
            if (Directory.Exists(path))
            {
                DirectoryInfo di = new DirectoryInfo(path);

                foreach (FileInfo file in di.GetFiles())
                {
                    file.Delete();
                }
                foreach (DirectoryInfo dir in di.GetDirectories())
                {
                    dir.Delete(true);
                }
            }
        }*/

        //if (!Directory.Exists(path))
        //{
        Directory.CreateDirectory(path);
        //}

        if (genericConfigExtract)
        {
            if (Configs.Length > 0)
            {
                using TextWriter tw = new StreamWriter(Path.Combine(path, $"{ExtractConfigFileName}.{ExtractConfigFileExtension}"), false);
                foreach (var config in Configs)
                {
                    var type = config.GetType();
                    tw.WriteLine($"[{type.Name}]");
                    foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (property.Name.Equals("Item")) continue;
                        tw.WriteLine($"{property.Name} = {property.GetValue(config)}");
                    }

                    tw.WriteLine();
                }

                tw.Close();
            }
        }

        if (genericLayersExtract)
        {
            if (HaveLayers)
            {
                using TextWriter tw = new StreamWriter(Path.Combine(path, "Layers.ini"));
                for (int layerIndex = 0; layerIndex < LayerCount; layerIndex++)
                {
                    var layer = this[layerIndex];
                    tw.WriteLine($"[{layerIndex}]");
                    tw.WriteLine($"{nameof(layer.NonZeroPixelCount)}: {layer.NonZeroPixelCount}");
                    tw.WriteLine($"{nameof(layer.BoundingRectangle)}: {layer.BoundingRectangle}");
                    tw.WriteLine($"{nameof(layer.IsBottomLayer)}: {layer.IsBottomLayer}");
                    tw.WriteLine($"{nameof(layer.LayerHeight)}: {layer.LayerHeight}");
                    tw.WriteLine($"{nameof(layer.PositionZ)}: {layer.PositionZ}");

                    if (CanUseLayerLightOffDelay)
                        tw.WriteLine($"{nameof(layer.LightOffDelay)}: {layer.LightOffDelay}");
                    if (CanUseLayerWaitTimeBeforeCure)
                        tw.WriteLine($"{nameof(layer.WaitTimeBeforeCure)}: {layer.WaitTimeBeforeCure}");
                    tw.WriteLine($"{nameof(layer.ExposureTime)}: {layer.ExposureTime}");
                    if (CanUseLayerWaitTimeAfterCure)
                        tw.WriteLine($"{nameof(layer.WaitTimeAfterCure)}: {layer.WaitTimeAfterCure}");


                    if (CanUseLayerLiftHeight) tw.WriteLine($"{nameof(layer.LiftHeight)}: {layer.LiftHeight}");
                    if (CanUseLayerLiftSpeed) tw.WriteLine($"{nameof(layer.LiftSpeed)}: {layer.LiftSpeed}");
                    if (CanUseLayerLiftAcceleration) tw.WriteLine($"{nameof(layer.LiftAcceleration)}: {layer.LiftAcceleration}");
                    if (CanUseLayerLiftHeight2) tw.WriteLine($"{nameof(layer.LiftHeight2)}: {layer.LiftHeight2}");
                    if (CanUseLayerLiftSpeed2) tw.WriteLine($"{nameof(layer.LiftSpeed2)}: {layer.LiftSpeed2}");
                    if (CanUseLayerLiftAcceleration2) tw.WriteLine($"{nameof(layer.LiftAcceleration2)}: {layer.LiftAcceleration2}");
                    if (CanUseLayerWaitTimeAfterLift) tw.WriteLine($"{nameof(layer.WaitTimeAfterLift)}: {layer.WaitTimeAfterLift}");
                    if (CanUseLayerRetractSpeed)
                    {
                        tw.WriteLine($"{nameof(layer.RetractHeight)}: {layer.RetractHeight}");
                        tw.WriteLine($"{nameof(layer.RetractSpeed)}: {layer.RetractSpeed}");
                        if (CanUseLayerRetractAcceleration) tw.WriteLine($"{nameof(layer.RetractAcceleration)}: {layer.RetractAcceleration}");
                    }
                    if (CanUseLayerRetractHeight2) tw.WriteLine($"{nameof(layer.RetractHeight2)}: {layer.RetractHeight2}");
                    if (CanUseLayerRetractSpeed2) tw.WriteLine($"{nameof(layer.RetractSpeed2)}: {layer.RetractSpeed2}");
                    if (CanUseLayerRetractAcceleration2) tw.WriteLine($"{nameof(layer.RetractAcceleration2)}: {layer.RetractAcceleration2}");

                    if (CanUseLayerLightPWM) tw.WriteLine($"{nameof(layer.LightPWM)}: {layer.LightPWM}");

                    if (CanUseLayerPause) tw.WriteLine($"{nameof(layer.Pause)}: {layer.Pause}");
                    if (CanUseLayerChangeResin) tw.WriteLine($"{nameof(layer.ChangeResin)}: {layer.ChangeResin}");

                    var materialMillilitersPercent = layer.MaterialMillilitersPercent;
                    if (!float.IsNaN(materialMillilitersPercent))
                    {
                        tw.WriteLine($"{nameof(layer.MaterialMilliliters)}: {layer.MaterialMilliliters}ml ({materialMillilitersPercent:F2}%)");
                    }

                    tw.WriteLine();
                }
                tw.Close();
            }
        }


        if (FileType == FileFormatType.Archive)
        {
            if (FileFullPath is not null)
            {
                progress.CanCancel = false;
                ZipArchiveExtensions.ImprovedExtractToDirectory(FileFullPath, path, ZipArchiveExtensions.Overwrite.Always);
                return;
            }
        }

        progress.ItemCount = LayerCount;

        if (genericLayersExtract)
        {
            uint i = 0;
            foreach (var thumbnail in Thumbnails)
            {
                if (thumbnail.IsEmpty)
                {
                    continue;
                }

                thumbnail.Save(Path.Combine(path, $"Thumbnail{i}.png"));
                i++;
            }

            if (HaveLayers && DecodeType == FileDecodeType.Full)
            {
                Parallel.ForEach(this, CoreSettings.GetParallelOptions(progress), layer =>
                {
                    progress.PauseIfRequested();
                    var byteArr = layer.CompressedPngBytes;
                    if (byteArr.Length == 0) return;
                    using var stream = new FileStream(Path.Combine(path, layer.Filename), FileMode.Create, FileAccess.Write);
                    stream.Write(byteArr, 0, byteArr.Length);
                    progress.LockAndIncrement();
                });
            }
        }
    }

    /// <summary>
    /// Gets the transition layer count calculated from layer exposure time configuration
    /// </summary>
    /// <returns>Transition layer count</returns>
    public ushort ParseTransitionLayerCountFromLayers()
    {
        ushort count = 0;
        for (uint layerIndex = BottomLayerCount; layerIndex < LastLayerIndex; layerIndex++)
        {
            if (this[layerIndex].ExposureTime < this[layerIndex + 1].ExposureTime) break; // Increasing time related to previous layer, we want decreasing time only
            if (Math.Abs(this[layerIndex].ExposureTime - this[layerIndex + 1].ExposureTime) < 0.009f) break; // First equal layer, transition ended
            count++;
        }

        return count > 0 ? count : TransitionLayerCount;
    }

    /// <summary>
    /// Parse the transition step time from layers, value is returned as positive from normal perspective and logic (Longer - shorter)
    /// </summary>
    /// <returns>Seconds</returns>
    public float ParseTransitionStepTimeFromLayers()
    {
        if (LayerCount < 3) return 0;
        var transitionLayerCount = ParseTransitionLayerCountFromLayers();
        return transitionLayerCount == 0
            ? 0
            : MathF.Round(this[BottomLayerCount].ExposureTime - this[BottomLayerCount + 1].ExposureTime, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Gets the transition step time from a long and short exposure time, value is returned as positive from normal perspective and logic (Longer - shorter)
    /// </summary>
    /// <param name="longExposureTime">The long exposure time</param>
    /// <param name="shortExposureTime">The small exposure time</param>
    /// <param name="transitionLayerCount">Number of transition layers</param>
    /// <returns>Seconds</returns>
    public static float GetTransitionStepTime(float longExposureTime, float shortExposureTime, ushort transitionLayerCount)
    {
        return transitionLayerCount == 0 || longExposureTime == shortExposureTime
            ? 0
            : MathF.Round((longExposureTime - shortExposureTime) / (transitionLayerCount + 1), 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Gets the transition step time from <see cref="BottomExposureTime"/> and <see cref="ExposureTime"/>, value is returned as positive from normal perspective and logic (Longer - shorter)
    /// </summary>
    /// <param name="transitionLayerCount">Number of transition layers</param>
    /// <returns>Seconds</returns>
    public float GetTransitionStepTime(ushort transitionLayerCount)
    {
        return GetTransitionStepTime(BottomExposureTime, ExposureTime, transitionLayerCount);
    }

    /// <summary>
    /// Gets the transition step time from <see cref="LastBottomLayer"/> and first normal layer after the last transition layer, value is returned as positive from normal perspective and logic (Longer - shorter)
    /// </summary>
    /// <param name="transitionLayerCount">Number of transition layers</param>
    /// <returns>Seconds</returns>
    public float GetTransitionStepTimeFromLayers(ushort transitionLayerCount)
    {
        if (LayerCount < 3 || BottomLayerCount > LayerCount) return 0;
        var bottomExposureTime = LastBottomLayer?.ExposureTime ?? BottomExposureTime;
        var exposureTime = TransitionLayerCount > 0 && BottomLayerCount + TransitionLayerCount <= LayerCount
            ? this[BottomLayerCount + TransitionLayerCount - 1].ExposureTime
            : this[BottomLayerCount].ExposureTime;

        return GetTransitionStepTime(bottomExposureTime, exposureTime, transitionLayerCount);
    }

    /// <summary>
    /// Gets the transition step time from <see cref="BottomExposureTime"/> and <see cref="ExposureTime"/>, value is returned as positive from normal perspective and logic (Longer - shorter)
    /// </summary>
    /// <returns>Seconds</returns>
    public float GetTransitionStepTime() => GetTransitionStepTime(TransitionLayerCount);

    /// <summary>
    /// Gets the transition layer count based on long and short exposure time
    /// </summary>
    /// <param name="longExposureTime">The long exposure time</param>
    /// <param name="shortExposureTime">The small exposure time</param>
    /// <param name="decrementTime">Decrement time</param>
    /// <param name="rounding">Midpoint rounding method</param>
    /// <returns></returns>
    public static ushort GetTransitionLayerCount(float longExposureTime, float shortExposureTime, float decrementTime, MidpointRounding rounding = MidpointRounding.AwayFromZero)
    {
        return decrementTime == 0 ? (ushort)0 : (ushort)Math.Round((longExposureTime - shortExposureTime) / decrementTime - 1, rounding);
    }

    /// <summary>
    /// Gets the transition layer count based on <see cref="BottomExposureTime"/> and <see cref="ExposureTime"/>
    /// </summary>
    /// <param name="stepDecrementTime">Step decrement time in seconds</param>
    /// <param name="constrainToLayerCount">True if transition layer count can't be higher than supported by the file, otherwise set to false to not look at possible file layers</param>
    /// <param name="rounding">Midpoint rounding method</param>
    /// <returns>Transition layer count</returns>
    public ushort GetTransitionLayerCount(float stepDecrementTime, bool constrainToLayerCount = true, MidpointRounding rounding = MidpointRounding.AwayFromZero)
    {
        var count = GetTransitionLayerCount(BottomExposureTime, ExposureTime, stepDecrementTime, rounding);
        if (constrainToLayerCount) count = (ushort)Math.Min(count, MaximumPossibleTransitionLayerCount);
        return count;
    }

    /// <summary>
    /// Gets the transition layer count based on <see cref="LastBottomLayer"/> and first normal layer after the last transition layer
    /// </summary>
    /// <param name="stepDecrementTime">Step decrement time in seconds</param>
    /// <param name="constrainToLayerCount">True if transition layer count can't be higher than supported by the file, otherwise set false to not look at possible file layers</param>
    /// <param name="rounding">Midpoint rounding method</param>
    /// <returns>Transition layer count</returns>
    public ushort GetTransitionLayerCountFromLayers(float stepDecrementTime, bool constrainToLayerCount = true, MidpointRounding rounding = MidpointRounding.AwayFromZero)
    {
        if (LayerCount < 3 || BottomLayerCount > LayerCount) return 0;
        var bottomExposureTime = LastBottomLayer?.ExposureTime ?? BottomExposureTime;
        var exposureTime = TransitionLayerCount > 0 && BottomLayerCount + TransitionLayerCount <= LayerCount
            ? this[BottomLayerCount + TransitionLayerCount - 1].ExposureTime
            : this[BottomLayerCount].ExposureTime;

        var count = GetTransitionLayerCount(bottomExposureTime, exposureTime, stepDecrementTime, rounding);
        if (constrainToLayerCount) count = (ushort)Math.Min(count, MaximumPossibleTransitionLayerCount);
        return count;
    }


    /// <summary>
    /// Re-set exposure time to the transition layers
    /// </summary>
    /// <param name="resetExposureTimes">True to default all the previous transition layers exposure time, otherwise false</param>
    public void ResetCurrentTransitionLayers(bool resetExposureTimes = true)
    {
        if (TransitionLayerType != TransitionLayerTypes.Software) return;
        SetTransitionLayers(TransitionLayerCount, resetExposureTimes);
    }

    /// <summary>
    /// Set transition layers and exposure times, but do not set that count to file property <see cref="TransitionLayerCount"/>
    /// </summary>
    /// <param name="transitionLayerCount">Number of transition layers to set</param>
    /// <param name="resetExposureTimes">True to default all the previous transition layers exposure time, otherwise false</param>
    public void SetTransitionLayers(ushort transitionLayerCount, bool resetExposureTimes = true)
    {
        var bottomExposureTime = LastBottomLayer?.ExposureTime ?? BottomExposureTime;
        var exposureTime = ExposureTime;
        var layersToReset = new List<Layer>();
        for (uint layerIndex = BottomLayerCount; layerIndex < LastLayerIndex; layerIndex++)
        {
            if (Math.Abs(this[layerIndex].ExposureTime - this[layerIndex + 1].ExposureTime) < 0.009)
            {
                exposureTime = this[layerIndex].ExposureTime;
                break; // First equal layer, transition ended
            }
            layersToReset.Add(this[layerIndex]);
        }

        if (resetExposureTimes)
        {
            foreach (var layer in layersToReset)
            {
                layer.ExposureTime = exposureTime;
            }
        }

        if (transitionLayerCount == 0) return;

        float decrement = GetTransitionStepTime(bottomExposureTime, exposureTime, transitionLayerCount);
        if (decrement <= 0) return;

        uint appliedLayers = 0;
        for (uint layerIndex = BottomLayerCount; appliedLayers < transitionLayerCount && layerIndex < LayerCount; layerIndex++)
        {
            appliedLayers++;
            this[layerIndex].ExposureTime = Math.Clamp(bottomExposureTime - (decrement * appliedLayers), exposureTime, bottomExposureTime);
        }
    }

    /// <summary>
    /// Calculate the PositionZ for an layer index in mm
    /// </summary>
    /// <param name="layerIndex"></param>
    /// <param name="usePreviousLayer">Use the previous layer to calculate the PositionZ if possible, otherwise it will multiply the number by the <see cref="LayerHeight"/></param>
    /// <returns>The height in mm</returns>
    public float CalculatePositionZ(uint layerIndex, bool usePreviousLayer = true)
    {
        if (usePreviousLayer)
        {
            int previousLayerIndex = (int)layerIndex - 1;
            if (ContainsLayerAndValid(previousLayerIndex) && this[previousLayerIndex].PositionZ > 0)
            {
                return Layer.RoundHeight(this[previousLayerIndex].PositionZ + LayerHeight);
            }
        }

        return Layer.RoundHeight((layerIndex+1) * LayerHeight);
    }

    /// <summary>
    /// Gets the global value for bottom or normal layers based on layer index
    /// </summary>
    /// <typeparam name="T">Type of value</typeparam>
    /// <param name="layerIndex">Layer index</param>
    /// <param name="bottomValue">Initial value</param>
    /// <param name="normalValue">Normal value</param>
    /// <returns></returns>
    public T GetBottomOrNormalValue<T>(uint layerIndex, T bottomValue, T normalValue)
    {
        return layerIndex < BottomLayerCount ? bottomValue : normalValue;
    }

    /// <summary>
    /// Gets the global value for bottom or normal layers based on layer
    /// </summary>
    /// <typeparam name="T">Type of value</typeparam>
    /// <param name="layer">Layer</param>
    /// <param name="bottomValue">Initial value</param>
    /// <param name="normalValue">Normal value</param>
    /// <returns></returns>
    public T GetBottomOrNormalValue<T>(Layer layer, T bottomValue, T normalValue)
    {
        return layer.IsBottomLayer ? bottomValue : normalValue;
    }

    /// <summary>
    /// Refresh print parameters globals with this file settings
    /// </summary>
    public void RefreshPrintParametersModifiersValues()
    {
        if (!SupportGlobalPrintParameters) return;

        var enumerable = PrintParameterModifiers.AsValueEnumerable();

        if (enumerable.Contains(PrintParameterModifier.BottomLayerCount))
        {
            PrintParameterModifier.BottomLayerCount.Value = BottomLayerCount;
        }

        if (enumerable.Contains(PrintParameterModifier.TransitionLayerCount))
        {
            PrintParameterModifier.TransitionLayerCount.Value = TransitionLayerCount;
        }

        if (enumerable.Contains(PrintParameterModifier.BottomLightOffDelay))
        {
            PrintParameterModifier.BottomLightOffDelay.Value = (decimal)BottomLightOffDelay;
        }

        if (enumerable.Contains(PrintParameterModifier.LightOffDelay))
        {
            PrintParameterModifier.LightOffDelay.Value = (decimal)LightOffDelay;
        }

        if (enumerable.Contains(PrintParameterModifier.BottomWaitTimeBeforeCure))
        {
            PrintParameterModifier.BottomWaitTimeBeforeCure.Value = (decimal)BottomWaitTimeBeforeCure;
        }

        if (enumerable.Contains(PrintParameterModifier.WaitTimeBeforeCure))
        {
            PrintParameterModifier.WaitTimeBeforeCure.Value = (decimal)WaitTimeBeforeCure;
        }

        if (enumerable.Contains(PrintParameterModifier.BottomExposureTime))
        {
            PrintParameterModifier.BottomExposureTime.Value = (decimal) BottomExposureTime;
        }

        if (enumerable.Contains(PrintParameterModifier.ExposureTime))
        {
            PrintParameterModifier.ExposureTime.Value = (decimal)ExposureTime;
        }

        if (enumerable.Contains(PrintParameterModifier.BottomWaitTimeAfterCure))
        {
            PrintParameterModifier.BottomWaitTimeAfterCure.Value = (decimal)BottomWaitTimeAfterCure;
        }

        if (enumerable.Contains(PrintParameterModifier.WaitTimeAfterCure))
        {
            PrintParameterModifier.WaitTimeAfterCure.Value = (decimal)WaitTimeAfterCure;
        }

        if (enumerable.Contains(PrintParameterModifier.BottomLiftHeight))
        {
            PrintParameterModifier.BottomLiftHeight.Value = (decimal)BottomLiftHeight;
        }

        if (enumerable.Contains(PrintParameterModifier.BottomLiftSpeed))
        {
            PrintParameterModifier.BottomLiftSpeed.Value = (decimal)BottomLiftSpeed;
        }

        if (enumerable.Contains(PrintParameterModifier.BottomLiftAcceleration))
        {
            PrintParameterModifier.BottomLiftAcceleration.Value = (decimal)BottomLiftAcceleration;
        }

        if (enumerable.Contains(PrintParameterModifier.LiftHeight))
        {
            PrintParameterModifier.LiftHeight.Value = (decimal)LiftHeight;
        }

        if (enumerable.Contains(PrintParameterModifier.LiftSpeed))
        {
            PrintParameterModifier.LiftSpeed.Value = (decimal)LiftSpeed;
        }

        if (enumerable.Contains(PrintParameterModifier.LiftAcceleration))
        {
            PrintParameterModifier.LiftAcceleration.Value = (decimal)LiftAcceleration;
        }

        if (enumerable.Contains(PrintParameterModifier.BottomLiftHeight2))
        {
            PrintParameterModifier.BottomLiftHeight2.Value = (decimal)BottomLiftHeight2;
        }

        if (enumerable.Contains(PrintParameterModifier.BottomLiftSpeed2))
        {
            PrintParameterModifier.BottomLiftSpeed2.Value = (decimal)BottomLiftSpeed2;
        }

        if (enumerable.Contains(PrintParameterModifier.BottomLiftAcceleration2))
        {
            PrintParameterModifier.BottomLiftAcceleration2.Value = (decimal)BottomLiftAcceleration2;
        }

        if (enumerable.Contains(PrintParameterModifier.LiftHeight2))
        {
            PrintParameterModifier.LiftHeight2.Value = (decimal)LiftHeight2;
        }

        if (enumerable.Contains(PrintParameterModifier.LiftSpeed2))
        {
            PrintParameterModifier.LiftSpeed2.Value = (decimal)LiftSpeed2;
        }

        if (enumerable.Contains(PrintParameterModifier.LiftAcceleration2))
        {
            PrintParameterModifier.LiftAcceleration2.Value = (decimal)LiftAcceleration2;
        }

        if (enumerable.Contains(PrintParameterModifier.BottomWaitTimeAfterLift))
        {
            PrintParameterModifier.BottomWaitTimeAfterLift.Value = (decimal)BottomWaitTimeAfterLift;
        }

        if (enumerable.Contains(PrintParameterModifier.WaitTimeAfterLift))
        {
            PrintParameterModifier.WaitTimeAfterLift.Value = (decimal)WaitTimeAfterLift;
        }

        if (enumerable.Contains(PrintParameterModifier.BottomRetractSpeed))
        {
            PrintParameterModifier.BottomRetractSpeed.Value = (decimal)BottomRetractSpeed;
        }

        if (enumerable.Contains(PrintParameterModifier.BottomRetractAcceleration))
        {
            PrintParameterModifier.BottomRetractAcceleration.Value = (decimal)BottomRetractAcceleration;
        }

        if (enumerable.Contains(PrintParameterModifier.RetractSpeed))
        {
            PrintParameterModifier.RetractSpeed.Value = (decimal)RetractSpeed;
        }

        if (enumerable.Contains(PrintParameterModifier.RetractAcceleration))
        {
            PrintParameterModifier.RetractAcceleration.Value = (decimal)RetractAcceleration;
        }

        if (enumerable.Contains(PrintParameterModifier.BottomRetractHeight2))
        {
            PrintParameterModifier.BottomRetractHeight2.Value = (decimal)BottomRetractHeight2;
        }

        if (enumerable.Contains(PrintParameterModifier.BottomRetractSpeed2))
        {
            PrintParameterModifier.BottomRetractSpeed2.Value = (decimal)BottomRetractSpeed2;
        }

        if (enumerable.Contains(PrintParameterModifier.BottomRetractAcceleration2))
        {
            PrintParameterModifier.BottomRetractAcceleration2.Value = (decimal)BottomRetractAcceleration2;
        }

        if (enumerable.Contains(PrintParameterModifier.RetractHeight2))
        {
            PrintParameterModifier.RetractHeight2.Value = (decimal)RetractHeight2;
        }

        if (enumerable.Contains(PrintParameterModifier.RetractSpeed2))
        {
            PrintParameterModifier.RetractSpeed2.Value = (decimal)RetractSpeed2;
        }

        if (enumerable.Contains(PrintParameterModifier.RetractAcceleration2))
        {
            PrintParameterModifier.RetractAcceleration2.Value = (decimal)RetractAcceleration2;
        }

        if (enumerable.Contains(PrintParameterModifier.BottomLightPWM))
        {
            PrintParameterModifier.BottomLightPWM.Value = BottomLightPWM;
        }

        if (enumerable.Contains(PrintParameterModifier.LightPWM))
        {
            PrintParameterModifier.LightPWM.Value = LightPWM;
        }
    }

    /// <summary>
    /// Refresh print parameters per layer globals with this file settings
    /// </summary>
    public void RefreshPrintParametersPerLayerModifiersValues(uint layerIndex)
    {
        if (!SupportPerLayerSettings) return;
        var layer = this[layerIndex];

        var enumerable = PrintParameterPerLayerModifiers.AsValueEnumerable();

        if (enumerable.Contains(PrintParameterModifier.PositionZ))
        {
            PrintParameterModifier.PositionZ.Value = (decimal)layer.PositionZ;
        }

        if (enumerable.Contains(PrintParameterModifier.LightOffDelay))
        {
            PrintParameterModifier.LightOffDelay.Value = (decimal)layer.LightOffDelay;
        }

        if (enumerable.Contains(PrintParameterModifier.WaitTimeBeforeCure))
        {
            PrintParameterModifier.WaitTimeBeforeCure.Value = (decimal)layer.WaitTimeBeforeCure;
        }

        if (enumerable.Contains(PrintParameterModifier.ExposureTime))
        {
            PrintParameterModifier.ExposureTime.Value = (decimal)layer.ExposureTime;
        }

        if (enumerable.Contains(PrintParameterModifier.WaitTimeAfterCure))
        {
            PrintParameterModifier.WaitTimeAfterCure.Value = (decimal)layer.WaitTimeAfterCure;
        }

        if (enumerable.Contains(PrintParameterModifier.LiftHeight))
        {
            PrintParameterModifier.LiftHeight.Value = (decimal)layer.LiftHeight;
        }

        if (enumerable.Contains(PrintParameterModifier.LiftSpeed))
        {
            PrintParameterModifier.LiftSpeed.Value = (decimal)layer.LiftSpeed;
        }

        if (enumerable.Contains(PrintParameterModifier.LiftAcceleration))
        {
            PrintParameterModifier.LiftAcceleration.Value = (decimal)layer.LiftAcceleration;
        }

        if (enumerable.Contains(PrintParameterModifier.LiftHeight2))
        {
            PrintParameterModifier.LiftHeight2.Value = (decimal)layer.LiftHeight2;
        }

        if (enumerable.Contains(PrintParameterModifier.LiftSpeed2))
        {
            PrintParameterModifier.LiftSpeed2.Value = (decimal)layer.LiftSpeed2;
        }

        if (enumerable.Contains(PrintParameterModifier.LiftAcceleration2))
        {
            PrintParameterModifier.LiftAcceleration2.Value = (decimal)layer.LiftAcceleration2;
        }

        if (enumerable.Contains(PrintParameterModifier.WaitTimeAfterLift))
        {
            PrintParameterModifier.WaitTimeAfterLift.Value = (decimal)layer.WaitTimeAfterLift;
        }

        if (enumerable.Contains(PrintParameterModifier.RetractSpeed))
        {
            PrintParameterModifier.RetractSpeed.Value = (decimal)layer.RetractSpeed;
        }

        if (enumerable.Contains(PrintParameterModifier.RetractAcceleration))
        {
            PrintParameterModifier.RetractAcceleration.Value = (decimal)layer.RetractAcceleration;
        }

        if (enumerable.Contains(PrintParameterModifier.RetractHeight2))
        {
            PrintParameterModifier.RetractHeight2.Value = (decimal)layer.RetractHeight2;
        }

        if (enumerable.Contains(PrintParameterModifier.RetractSpeed2))
        {
            PrintParameterModifier.RetractSpeed2.Value = (decimal)layer.RetractSpeed2;
        }

        if (enumerable.Contains(PrintParameterModifier.RetractAcceleration2))
        {
            PrintParameterModifier.RetractAcceleration2.Value = (decimal)layer.RetractAcceleration2;
        }

        if (enumerable.Contains(PrintParameterModifier.LightPWM))
        {
            PrintParameterModifier.LightPWM.Value = layer.LightPWM;
        }

        if (enumerable.Contains(PrintParameterModifier.Pause))
        {
            PrintParameterModifier.Pause.Value = System.Convert.ToDecimal(layer.Pause);
        }

        if (enumerable.Contains(PrintParameterModifier.ChangeResin))
        {
            PrintParameterModifier.ChangeResin.Value = System.Convert.ToDecimal(layer.ChangeResin);
        }
    }

    /// <summary>
    /// Gets the value attributed to <see cref="FileFormat.PrintParameterModifier"/>
    /// </summary>
    /// <param name="modifier">Modifier to use</param>
    /// <returns>A value</returns>
    public object? GetValueFromPrintParameterModifier(PrintParameterModifier modifier)
    {
        if (ReferenceEquals(modifier, PrintParameterModifier.BottomLayerCount))
            return BottomLayerCount;

        if (ReferenceEquals(modifier, PrintParameterModifier.TransitionLayerCount))
            return TransitionLayerCount;

        if (ReferenceEquals(modifier, PrintParameterModifier.BottomLightOffDelay))
            return BottomLightOffDelay;
        if (ReferenceEquals(modifier, PrintParameterModifier.LightOffDelay))
            return LightOffDelay;

        if (ReferenceEquals(modifier, PrintParameterModifier.BottomWaitTimeBeforeCure))
            return BottomWaitTimeBeforeCure;
        if (ReferenceEquals(modifier, PrintParameterModifier.WaitTimeBeforeCure))
            return WaitTimeBeforeCure;

        if (ReferenceEquals(modifier, PrintParameterModifier.BottomExposureTime))
            return BottomExposureTime;
        if (ReferenceEquals(modifier, PrintParameterModifier.ExposureTime))
            return ExposureTime;

        if (ReferenceEquals(modifier, PrintParameterModifier.BottomWaitTimeAfterCure))
            return BottomWaitTimeAfterCure;
        if (ReferenceEquals(modifier, PrintParameterModifier.WaitTimeAfterCure))
            return WaitTimeAfterCure;

        if (ReferenceEquals(modifier, PrintParameterModifier.BottomLiftHeight))
            return BottomLiftHeight;
        if (ReferenceEquals(modifier, PrintParameterModifier.BottomLiftSpeed))
            return BottomLiftSpeed;
        if (ReferenceEquals(modifier, PrintParameterModifier.BottomLiftAcceleration))
            return BottomLiftAcceleration;

        if (ReferenceEquals(modifier, PrintParameterModifier.LiftHeight))
            return LiftHeight;
        if (ReferenceEquals(modifier, PrintParameterModifier.LiftSpeed))
            return LiftSpeed;
        if (ReferenceEquals(modifier, PrintParameterModifier.LiftAcceleration))
            return LiftAcceleration;


        if (ReferenceEquals(modifier, PrintParameterModifier.BottomLiftHeight2))
            return BottomLiftHeight2;
        if (ReferenceEquals(modifier, PrintParameterModifier.BottomLiftSpeed2))
            return BottomLiftSpeed2;
        if (ReferenceEquals(modifier, PrintParameterModifier.BottomLiftAcceleration2))
            return BottomLiftAcceleration2;

        if (ReferenceEquals(modifier, PrintParameterModifier.LiftHeight2))
            return LiftHeight2;
        if (ReferenceEquals(modifier, PrintParameterModifier.LiftSpeed2))
            return LiftSpeed2;
        if (ReferenceEquals(modifier, PrintParameterModifier.LiftAcceleration2))
            return LiftAcceleration2;

        if (ReferenceEquals(modifier, PrintParameterModifier.BottomWaitTimeAfterLift))
            return BottomWaitTimeAfterLift;
        if (ReferenceEquals(modifier, PrintParameterModifier.WaitTimeAfterLift))
            return WaitTimeAfterLift;

        if (ReferenceEquals(modifier, PrintParameterModifier.BottomRetractSpeed))
            return BottomRetractSpeed;
        if (ReferenceEquals(modifier, PrintParameterModifier.BottomRetractAcceleration))
            return BottomRetractAcceleration;
        if (ReferenceEquals(modifier, PrintParameterModifier.RetractSpeed))
            return RetractSpeed;
        if (ReferenceEquals(modifier, PrintParameterModifier.RetractAcceleration))
            return RetractAcceleration;

        if (ReferenceEquals(modifier, PrintParameterModifier.BottomRetractHeight2))
            return BottomRetractHeight2;
        if (ReferenceEquals(modifier, PrintParameterModifier.BottomRetractSpeed2))
            return BottomRetractSpeed2;
        if (ReferenceEquals(modifier, PrintParameterModifier.BottomRetractAcceleration2))
            return BottomRetractAcceleration2;

        if (ReferenceEquals(modifier, PrintParameterModifier.RetractHeight2))
            return RetractHeight2;
        if (ReferenceEquals(modifier, PrintParameterModifier.RetractSpeed2))
            return RetractSpeed2;
        if (ReferenceEquals(modifier, PrintParameterModifier.RetractAcceleration2))
            return RetractAcceleration2;


        if (ReferenceEquals(modifier, PrintParameterModifier.BottomLightPWM))
            return BottomLightPWM;
        if (ReferenceEquals(modifier, PrintParameterModifier.LightPWM))
            return LightPWM;

        return null;
    }

    /// <summary>
    /// Sets a property value attributed to <paramref name="modifier"/>
    /// </summary>
    /// <param name="modifier">Modifier to use</param>
    /// <param name="value">Value to set</param>
    /// <returns>True if set, otherwise false <paramref name="modifier"/> not found</returns>
    public bool SetValueFromPrintParameterModifier(PrintParameterModifier modifier, decimal value)
    {
        if (ReferenceEquals(modifier, PrintParameterModifier.BottomLayerCount))
        {
            BottomLayerCount = (ushort)value;
            return true;
        }

        if (ReferenceEquals(modifier, PrintParameterModifier.TransitionLayerCount))
        {
            TransitionLayerCount = (ushort)value;
            return true;
        }

        if (ReferenceEquals(modifier, PrintParameterModifier.BottomLightOffDelay))
        {
            BottomLightOffDelay = (float)value;
            return true;
        }
        if (ReferenceEquals(modifier, PrintParameterModifier.LightOffDelay))
        {
            LightOffDelay = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, PrintParameterModifier.BottomWaitTimeBeforeCure))
        {
            BottomWaitTimeBeforeCure = (float)value;
            return true;
        }
        if (ReferenceEquals(modifier, PrintParameterModifier.WaitTimeBeforeCure))
        {
            WaitTimeBeforeCure = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, PrintParameterModifier.BottomExposureTime))
        {
            BottomExposureTime = (float) value;
            return true;
        }
        if (ReferenceEquals(modifier, PrintParameterModifier.ExposureTime))
        {
            ExposureTime = (float) value;
            return true;
        }

        if (ReferenceEquals(modifier, PrintParameterModifier.BottomWaitTimeAfterCure))
        {
            BottomWaitTimeAfterCure = (float)value;
            return true;
        }
        if (ReferenceEquals(modifier, PrintParameterModifier.WaitTimeAfterCure))
        {
            WaitTimeAfterCure = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, PrintParameterModifier.BottomLiftHeight))
        {
            BottomLiftHeight = (float) value;
            return true;
        }
        if (ReferenceEquals(modifier, PrintParameterModifier.BottomLiftSpeed))
        {
            BottomLiftSpeed = (float)value;
            return true;
        }
        if (ReferenceEquals(modifier, PrintParameterModifier.BottomLiftAcceleration))
        {
            BottomLiftAcceleration = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, PrintParameterModifier.LiftHeight))
        {
            LiftHeight = (float) value;
            return true;
        }
        if (ReferenceEquals(modifier, PrintParameterModifier.LiftSpeed))
        {
            LiftSpeed = (float) value;
            return true;
        }
        if (ReferenceEquals(modifier, PrintParameterModifier.LiftAcceleration))
        {
            LiftAcceleration = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, PrintParameterModifier.BottomLiftHeight2))
        {
            BottomLiftHeight2 = (float)value;
            return true;
        }
        if (ReferenceEquals(modifier, PrintParameterModifier.BottomLiftSpeed2))
        {
            BottomLiftSpeed2 = (float)value;
            return true;
        }
        if (ReferenceEquals(modifier, PrintParameterModifier.BottomLiftAcceleration2))
        {
            BottomLiftAcceleration2 = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, PrintParameterModifier.LiftHeight2))
        {
            LiftHeight2 = (float)value;
            return true;
        }
        if (ReferenceEquals(modifier, PrintParameterModifier.LiftSpeed2))
        {
            LiftSpeed2 = (float)value;
            return true;
        }
        if (ReferenceEquals(modifier, PrintParameterModifier.LiftAcceleration2))
        {
            LiftAcceleration2 = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, PrintParameterModifier.BottomWaitTimeAfterLift))
        {
            BottomWaitTimeAfterLift = (float)value;
            return true;
        }
        if (ReferenceEquals(modifier, PrintParameterModifier.WaitTimeAfterLift))
        {
            WaitTimeAfterLift = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, PrintParameterModifier.BottomRetractSpeed))
        {
            BottomRetractSpeed = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, PrintParameterModifier.BottomRetractAcceleration))
        {
            BottomRetractAcceleration = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, PrintParameterModifier.RetractSpeed))
        {
            RetractSpeed = (float) value;
            return true;
        }

        if (ReferenceEquals(modifier, PrintParameterModifier.RetractAcceleration))
        {
            RetractAcceleration = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, PrintParameterModifier.BottomRetractHeight2))
        {
            BottomRetractHeight2 = (float)value;
            return true;
        }
        if (ReferenceEquals(modifier, PrintParameterModifier.BottomRetractSpeed2))
        {
            BottomRetractSpeed2 = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, PrintParameterModifier.BottomRetractAcceleration2))
        {
            BottomRetractAcceleration2 = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, PrintParameterModifier.RetractHeight2))
        {
            RetractHeight2 = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, PrintParameterModifier.RetractSpeed2))
        {
            RetractSpeed2 = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, PrintParameterModifier.RetractAcceleration2))
        {
            RetractAcceleration2 = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, PrintParameterModifier.BottomLightPWM))
        {
            BottomLightPWM = (byte)value;
            return true;
        }
        if (ReferenceEquals(modifier, PrintParameterModifier.LightPWM))
        {
            LightPWM = (byte)value;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Sets properties from print parameters
    /// </summary>
    /// <returns>Number of affected parameters</returns>
    public byte SetValuesFromPrintParametersModifiers()
    {
        if (!SupportGlobalPrintParameters) return 0;
        byte changed = 0;
        foreach (var modifier in PrintParameterModifiers)
        {
            if(!modifier.HasChanged) continue;
            modifier.OldValue = modifier.NewValue;
            SetValueFromPrintParameterModifier(modifier, modifier.NewValue);
            changed++;
        }

        return changed;
    }

    public void SetNoDelays()
    {
        BottomLightOffDelay = 0;
        LightOffDelay = 0;
        BottomWaitTimeBeforeCure = 0;
        WaitTimeBeforeCure = 0;
        BottomWaitTimeAfterCure = 0;
        WaitTimeAfterCure = 0;
        BottomWaitTimeAfterLift = 0;
        WaitTimeAfterLift = 0;
    }

    public float CalculateBottomLightOffDelay(float extraTime = 0) => CalculateLightOffDelay(LayerGroup.Bottom, extraTime);

    public bool SetBottomLightOffDelay(float extraTime = 0) => SetLightOffDelay(LayerGroup.Bottom, extraTime);

    public float CalculateNormalLightOffDelay(float extraTime = 0) => CalculateLightOffDelay(LayerGroup.Normal, extraTime);

    public bool SetNormalLightOffDelay(float extraTime = 0) => SetLightOffDelay(LayerGroup.Normal, extraTime);

    public float CalculateMotorMovementTime(LayerGroup layerGroup, float extraTime = 0)
    {
        return layerGroup == LayerGroup.Bottom
            ? OperationCalculator.LightOffDelayC.CalculateSeconds(BottomLiftHeight, BottomLiftSpeed, BottomRetractSpeed, extraTime, BottomLiftHeight2, BottomLiftSpeed2, BottomRetractHeight2, BottomRetractSpeed2)
            : OperationCalculator.LightOffDelayC.CalculateSeconds(LiftHeight, LiftSpeed, RetractSpeed, extraTime, LiftHeight2, LiftSpeed2, RetractHeight2, RetractSpeed2);
    }

    public float CalculateLightOffDelay(LayerGroup layerGroup, float extraTime = 0)
    {
        extraTime = MathF.Round(extraTime, 2);
        if (SupportGCode) return extraTime;
        return CalculateMotorMovementTime(layerGroup, extraTime);
    }

    public bool SetLightOffDelay(LayerGroup layerGroup, float extraTime = 0)
    {
        float lightOff = CalculateLightOffDelay(layerGroup, extraTime);
        if (layerGroup == LayerGroup.Bottom)
        {
            if (BottomLightOffDelay != lightOff)
            {
                BottomLightOffDelay = lightOff;
                return true;
            }

            return false;
        }

        if (LightOffDelay != lightOff)
        {
            LightOffDelay = lightOff;
            return true;
        }

        return false;
    }

    public float GetWaitTimeBeforeCure(LayerGroup layerGroup)
    {
        return layerGroup == LayerGroup.Bottom ? GetBottomWaitTimeBeforeCure() : GetNormalWaitTimeBeforeCure();
    }

    /// <summary>
    /// Gets the bottom wait time before cure, if not available calculate it from light off delay
    /// </summary>
    /// <returns></returns>
    public float GetBottomWaitTimeBeforeCure()
    {
        if (CanUseBottomWaitTimeBeforeCure)
        {
            return BottomWaitTimeBeforeCure;
        }

        if (CanUseWaitTimeBeforeCure)
        {
            return WaitTimeBeforeCure;
        }

        if (CanUseBottomLightOffDelay)
        {
            return (float)Math.Max(0, Math.Round(BottomLightOffDelay - CalculateBottomLightOffDelay(), 2));
        }

        if (CanUseLightOffDelay)
        {
            return (float)Math.Max(0, Math.Round(LightOffDelay - CalculateNormalLightOffDelay(), 2));
        }

        return 0;
    }

    /// <summary>
    /// Gets the wait time before cure, if not available calculate it from light off delay
    /// </summary>
    /// <returns></returns>
    public float GetNormalWaitTimeBeforeCure()
    {
        if (CanUseWaitTimeBeforeCure)
        {
            return WaitTimeBeforeCure;
        }

        if (CanUseLightOffDelay)
        {
            return (float)Math.Max(0, Math.Round(LightOffDelay - CalculateNormalLightOffDelay(), 2));
        }

        return 0;
    }

    /// <summary>
    /// Attempt to set wait time before cure if supported, otherwise fall-back to light-off delay
    /// </summary>
    /// <param name="layerGroup">Choose the layer group to set the wait time.</param>
    /// <param name="time">The time to set</param>
    /// <param name="zeroLightOffDelayCalculateBase">When true and time is zero, it will calculate light-off delay without extra time, otherwise false to set light-off delay to 0 when time is 0</param>
    public void SetWaitTimeBeforeCureOrLightOffDelay(LayerGroup layerGroup, float time = 0, bool zeroLightOffDelayCalculateBase = false)
    {
        if (layerGroup == LayerGroup.Bottom)
        {
            SetBottomWaitTimeBeforeCureOrLightOffDelay(time, zeroLightOffDelayCalculateBase);
        }
        else
        {
            SetNormalWaitTimeBeforeCureOrLightOffDelay(time, zeroLightOffDelayCalculateBase);
        }
    }

    public void SetBottomWaitTimeBeforeCureOrLightOffDelay(float time = 0, bool zeroLightOffDelayCalculateBase = false)
    {
        if (CanUseBottomWaitTimeBeforeCure)
        {
            BottomLightOffDelay = 0;
            BottomWaitTimeBeforeCure = time;
        }
        else if (CanUseBottomLightOffDelay)
        {
            if (time == 0 && !zeroLightOffDelayCalculateBase)
            {
                BottomLightOffDelay = 0;
                return;
            }

            SetBottomLightOffDelay(time);
        }
    }

    public void SetNormalWaitTimeBeforeCureOrLightOffDelay(float time = 0, bool zeroLightOffDelayCalculateBase = false)
    {
        if (CanUseWaitTimeBeforeCure)
        {
            LightOffDelay = 0;
            WaitTimeBeforeCure = time;
        }
        else if (CanUseLightOffDelay)
        {
            if (time == 0 && !zeroLightOffDelayCalculateBase)
            {
                LightOffDelay = 0;
                return;
            }

            SetNormalLightOffDelay(time);
        }
    }

    /// <summary>
    /// Rebuilds GCode based on current settings
    /// </summary>
    public virtual void RebuildGCode()
    {
        if (!SupportGCode || _suppressRebuildGCode) return;
        GCode!.RebuildGCode(this);
        RaisePropertyChanged(nameof(GCodeStr));
    }

    /// <summary>
    /// Saves current configuration on input file
    /// </summary>
    /// <param name="progress"></param>
    public void Save(OperationProgress? progress = null)
    {
        SaveAs(null, progress);
    }

    /// <summary>
    /// Saves current configuration on a copy
    /// </summary>
    /// <param name="filePath">File path to save copy as, use null to overwrite active file (Same as <see cref="Save"/>)</param>
    /// <param name="progress"></param>
    /// <exception cref="ArgumentNullException"><see cref="FileFullPath"/></exception>
    public void SaveAs(string? filePath = null, OperationProgress? progress = null)
    {
        if (RequireFullEncode)
        {
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                FileFullPath = filePath;
            }
            Encode(FileFullPath, progress);
            return;
        }

        if (string.IsNullOrWhiteSpace(FileFullPath))
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(FileFullPath), "Not encoded yet and both source and output files are null");
            Encode(filePath, progress);
            return;
        }

        OnBeforeEncode(true);
        BeforeEncode(true);

        // Backup old file name and prepare the temporary file to be written next
        var oldFilePath = FileFullPath!;
        if(!string.IsNullOrWhiteSpace(filePath)) FileFullPath = filePath;
        var tempFile = TemporaryOutputFileFullPath;

        try
        {
            File.Copy(oldFilePath, tempFile, true);

            progress ??= new OperationProgress();
            PartialSaveInternally(progress);

            // Move temporary output file in place
            File.Move(tempFile, FileFullPath, true);
            OnAfterEncode(true);
        }
        catch (Exception)
        {
            // Restore backup file path and delete the temporary
            FileFullPath = oldFilePath;
            if (File.Exists(tempFile)) File.Delete(tempFile);
            throw;
        }

    }

    /// <summary>
    /// Partial save of the file, this is the file information only.
    /// When this function is called it's already ready to save to file
    /// </summary>
    /// <param name="progress"></param>
    protected abstract void PartialSaveInternally(OperationProgress progress);

    /// <summary>
    /// Triggers when a conversion is valid and before start converting values
    /// </summary>
    /// <param name="source">Source file format</param>
    /// <returns>True to continue the conversion, otherwise false to stop</returns>
    protected virtual bool OnBeforeConvertFrom(FileFormat source) => true;

    /// <summary>
    /// Triggers when a conversion is valid and before start converting values
    /// </summary>
    /// <param name="output">Target file format</param>
    /// <returns>True to continue the conversion, otherwise false to stop</returns>
    protected virtual bool OnBeforeConvertTo(FileFormat output) => true;

    /// <summary>
    /// Triggers when the conversion is made but before encoding
    /// </summary>
    /// <param name="source">Source file format</param>
    /// <returns>True to continue the conversion, otherwise false to stop</returns>
    protected virtual bool OnAfterConvertFrom(FileFormat source) => true;

    /// <summary>
    /// Triggers when the conversion is made but before encoding
    /// </summary>
    /// <param name="output">Output file format</param>
    /// <returns>True to continue the conversion, otherwise false to stop</returns>
    protected virtual bool OnAfterConvertTo(FileFormat output) => true;

    /// <summary>
    /// Converts this file type to another file type
    /// </summary>
    /// <param name="to">Target file format</param>
    /// <param name="fileFullPath">Output path file</param>
    /// <param name="version">File version to use</param>
    /// <param name="progress"></param>
    /// <returns>The converted file if successful, otherwise null</returns>
    public virtual FileFormat? Convert(Type to, string fileFullPath, uint version = 0, OperationProgress? progress = null)
    {
        var found = AvailableFormats.AsValueEnumerable().Any(format => to == format.GetType());
        if (!found) return null;

        progress ??= new OperationProgress("Converting");

        if (Activator.CreateInstance(to) is not FileFormat convertSlicerFile) return null;
        convertSlicerFile.FileFullPath = fileFullPath;

        if (!convertSlicerFile.OnBeforeConvertFrom(this)) return null;
        if (!OnBeforeConvertTo(convertSlicerFile)) return null;

        if (version > 0 && version != convertSlicerFile.Version)
        {
            convertSlicerFile.Version = version;
        }

        convertSlicerFile.SanitizeVersion();

        convertSlicerFile.SuppressRebuildPropertiesWork(() =>
        {
            convertSlicerFile.Init(CloneLayers());
            convertSlicerFile.AntiAliasing = ValidateAntiAliasingLevel();
            convertSlicerFile.LayerHeight = LayerHeight;
            convertSlicerFile.LayerCount = LayerCount;
            convertSlicerFile.BottomLayerCount = BottomLayerCount;
            convertSlicerFile.TransitionLayerCount = TransitionLayerCount;
            convertSlicerFile.ResolutionX = ResolutionX;
            convertSlicerFile.ResolutionY = ResolutionY;
            convertSlicerFile.DisplayWidth = DisplayWidth;
            convertSlicerFile.DisplayHeight = DisplayHeight;
            convertSlicerFile.MachineZ = MachineZ;
            convertSlicerFile.DisplayMirror = DisplayMirror;

            // Exposure
            convertSlicerFile.BottomExposureTime = BottomExposureTime;
            convertSlicerFile.ExposureTime = ExposureTime;

            // Lifts
            convertSlicerFile.BottomLiftHeight = BottomLiftHeight;
            convertSlicerFile.BottomLiftSpeed = BottomLiftSpeed;
            convertSlicerFile.BottomLiftAcceleration = BottomLiftAcceleration;

            convertSlicerFile.LiftHeight = LiftHeight;
            convertSlicerFile.LiftSpeed = LiftSpeed;
            convertSlicerFile.LiftAcceleration = LiftAcceleration;

            convertSlicerFile.BottomLiftSpeed2 = BottomLiftSpeed2;
            convertSlicerFile.BottomLiftAcceleration2 = BottomLiftAcceleration2;
            convertSlicerFile.LiftSpeed2 = LiftSpeed2;
            convertSlicerFile.LiftAcceleration2 = LiftAcceleration2;

            convertSlicerFile.BottomRetractSpeed = BottomRetractSpeed;
            convertSlicerFile.BottomRetractAcceleration = BottomRetractAcceleration;
            convertSlicerFile.RetractSpeed = RetractSpeed;
            convertSlicerFile.RetractAcceleration = RetractAcceleration;

            convertSlicerFile.BottomRetractSpeed2 = BottomRetractSpeed2;
            convertSlicerFile.BottomRetractAcceleration2 = BottomRetractAcceleration2;
            convertSlicerFile.RetractSpeed2 = RetractSpeed2;
            convertSlicerFile.RetractAcceleration2 = RetractAcceleration2;


            if (convertSlicerFile.CanUseAnyLiftHeight2 && (CanUseAnyLiftHeight2 || GetType() == typeof(SL1File))) // Both are TSMC compatible
            {
                convertSlicerFile.BottomLiftHeight2 = BottomLiftHeight2;
                convertSlicerFile.LiftHeight2 = LiftHeight2;

                convertSlicerFile.BottomRetractHeight2 = BottomRetractHeight2;
                convertSlicerFile.RetractHeight2 = RetractHeight2;
            }
            /*else if (slicerFile.CanUseAnyLiftHeight2) // Output format is compatible with TSMC, but input isn't
            {
                slicerFile.BottomLiftHeight = BottomLiftHeight;
                slicerFile.LiftHeight = LiftHeight;
            }*/
            else if (CanUseAnyLiftHeight2) // Output format isn't compatible with TSMC, but input is
            {
                convertSlicerFile.BottomLiftHeight = BottomLiftHeightTotal;
                convertSlicerFile.LiftHeight = LiftHeightTotal;

                // Set to the slowest retract speed
                if (BottomRetractSpeed2 > 0 && BottomRetractSpeed > BottomRetractSpeed2)
                {
                    convertSlicerFile.BottomRetractSpeed = BottomRetractSpeed2;
                }

                if (BottomRetractAcceleration > 0 && BottomRetractAcceleration2 > 0 && BottomRetractAcceleration > BottomRetractAcceleration2)
                {
                    convertSlicerFile.BottomRetractAcceleration = BottomRetractAcceleration2;
                }

                // Set to the slowest retract speed
                if (RetractSpeed2 > 0 && RetractSpeed > RetractSpeed2)
                {
                    convertSlicerFile.RetractSpeed = RetractSpeed2;
                }

                if (RetractAcceleration > 0 && RetractAcceleration2 > 0 && RetractAcceleration > RetractAcceleration2)
                {
                    convertSlicerFile.RetractAcceleration = RetractAcceleration2;
                }
            }

            // Wait times
            convertSlicerFile.BottomLightOffDelay = BottomLightOffDelay;
            convertSlicerFile.LightOffDelay = LightOffDelay;

            convertSlicerFile.BottomWaitTimeBeforeCure = BottomWaitTimeBeforeCure;
            convertSlicerFile.WaitTimeBeforeCure = WaitTimeBeforeCure;

            convertSlicerFile.BottomWaitTimeAfterCure = BottomWaitTimeAfterCure;
            convertSlicerFile.WaitTimeAfterCure = WaitTimeAfterCure;

            convertSlicerFile.BottomWaitTimeAfterLift = BottomWaitTimeAfterLift;
            convertSlicerFile.WaitTimeAfterLift = WaitTimeAfterLift;

            convertSlicerFile.BottomLightPWM = BottomLightPWM;
            convertSlicerFile.LightPWM = LightPWM;

            // Others
            convertSlicerFile.MachineName = MachineName;
            convertSlicerFile.MaterialName = MaterialName;
            convertSlicerFile.MaterialMilliliters = MaterialMilliliters;
            convertSlicerFile.MaterialGrams = MaterialGrams;
            convertSlicerFile.MaterialCost = MaterialCost;
            convertSlicerFile.PrintTime = PrintTime;
            convertSlicerFile.PrintHeight = PrintHeight;
            convertSlicerFile.BoundingRectangle = _boundingRectangle;

            convertSlicerFile.ReplaceThumbnails(Thumbnails, true);
        });

        if (!convertSlicerFile.OnAfterConvertFrom(this)) return null;
        if (!OnAfterConvertTo(convertSlicerFile)) return null;

        convertSlicerFile.Encode(fileFullPath, progress);

        return convertSlicerFile;
    }

    /// <summary>
    /// Converts this file type to another file type
    /// </summary>
    /// <param name="to">Target file format</param>
    /// <param name="fileFullPath">Output path file</param>
    /// <param name="version">File version</param>
    /// <param name="progress"></param>
    /// <returns>TThe converted file if successful, otherwise null</returns>
    public FileFormat? Convert(FileFormat to, string fileFullPath, uint version = 0, OperationProgress? progress = null)
        => Convert(to.GetType(), fileFullPath, version, progress);

    /// <summary>
    /// Changes the compression method of all layers to a new method
    /// </summary>
    /// <param name="newCodec">The new method to change to</param>
    public void ChangeLayersCompressionMethod(LayerCompressionCodec newCodec)
    {
        for (int i = 0; i < LayerCount; i++)
        {
            this[i].CompressionCodec = newCodec;
        }
        /*progress ??= new OperationProgress($"Changing layers compression codec to {newCodec}");
        progress.Reset("Layers", LayerCount);

        Parallel.ForEach(this, CoreSettings.GetParallelOptions(progress), layer =>
        {
            progress.PauseIfRequested();
            layer.CompressionCodec = newCodec;
            progress.LockAndIncrement();
        });*/
    }

    /// <summary>
    /// Validate AntiAlias Level
    /// </summary>
    public byte ValidateAntiAliasingLevel()
    {
        if (AntiAliasing <= 1) return 1;
        //if(AntiAliasing % 2 != 0) throw new ArgumentException("AntiAliasing must be multiples of 2, otherwise use 0 or 1 to disable it", nameof(AntiAliasing));
        return AntiAliasing;
    }

    /// <summary>
    /// Validate the layer image format
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    protected void ValidateLayerImageFormat()
    {
        if (LayerImageFormat is ImageFormat.Png24BgrAA or ImageFormat.Png24RgbAA)
        {
            if (ResolutionX % 3 != 0)
            {
                throw new InvalidOperationException($"Resolution width of {ResolutionX}px is invalid. Width must be in multiples of 3.\n" +
                                                    "Fix your printer slicing settings with the correct width that is multiple of 3.");
            }
        }
    }

    /// <summary>
    /// SuppressRebuildProperties = true, call the invoker and reset SuppressRebuildProperties = false
    /// </summary>
    /// <param name="action">Action work</param>
    /// <param name="callRebuildOnEnd">True to force rebuild the layer properties after the work and before reset to false</param>
    /// <param name="recalculateZPos">True to recalculate z position of each layer (requires <paramref name="callRebuildOnEnd"/> = true), otherwise false</param>
    /// <param name="property">Property name to change for each layer, use null to update all properties (requires <paramref name="callRebuildOnEnd"/> = true)</param>
    public void SuppressRebuildPropertiesWork(Action action, bool callRebuildOnEnd = false, bool recalculateZPos = true, string? property = null)
    {
        /*SuppressRebuildProperties = true;
        action.Invoke();
        if(callRebuildOnEnd) LayerManager.RebuildLayersProperties(recalculateZPos, property);
        SuppressRebuildProperties = false;*/
        SuppressRebuildPropertiesWork(() =>
        {
            action.Invoke();
            return true;
        }, callRebuildOnEnd, recalculateZPos, property);
    }

    /// <summary>
    /// SuppressRebuildProperties = true, call the invoker and reset SuppressRebuildProperties = false
    /// </summary>
    /// <param name="action">Action work</param>
    /// <param name="callRebuildOnEnd">True to force rebuild the layer properties after the work and before reset to false</param>
    /// <param name="recalculateZPos">True to recalculate z position of each layer (requires <paramref name="callRebuildOnEnd"/> = true), otherwise false</param>
    /// <param name="property">Property name to change for each layer, use null to update all properties (requires <paramref name="callRebuildOnEnd"/> = true)</param>
    public bool SuppressRebuildPropertiesWork(Func<bool> action, bool callRebuildOnEnd = false, bool recalculateZPos = true, string? property = null)
    {
        bool result;
        try
        {
            SuppressRebuildProperties = true;
            result = action.Invoke();
            if (callRebuildOnEnd && result) RebuildLayersProperties(recalculateZPos, property);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            throw;
        }
        finally
        {
            SuppressRebuildProperties = false;
        }

        return result;
    }

    /// <summary>
    /// Parses and updates global properties from layers.<br/>
    /// Use this method if file lacks of global properties metadata but have layers metadata
    /// </summary>
    public void UpdateGlobalPropertiesFromLayers()
    {
        if (!HaveLayers) return;
        SuppressRebuildPropertiesWork(() =>
        {
            LayerHeight = Layer.RoundHeight(this.AsValueEnumerable().FirstOrDefault(layer => !layer.IsDummy && layer.LayerHeight > 0)?.LayerHeight ?? 0);

            BottomLayerCount = 0;
            float lastExposureTime = 0;
            for (uint layerIndex = 0; layerIndex < LayerCount; layerIndex++)
            {
                if (this[layerIndex].IsDummy) continue;
                if (lastExposureTime <= 0.01) lastExposureTime = this[layerIndex].ExposureTime;
                else if (Math.Abs(lastExposureTime - this[layerIndex].ExposureTime) > 0.01)
                {
                    BottomLayerCount = (ushort)layerIndex;
                    break;
                }
            }

            var enumerator = this.AsValueEnumerable();

            if (CanUseLayerLiftHeight) LiftHeight = enumerator.LastOrDefault(layer => layer.IsNormalLayer && !layer.IsDummy && layer.LiftHeight > 0)?.LiftHeight ?? DefaultLiftHeight;
            if (CanUseLayerLiftSpeed) LiftSpeed = enumerator.LastOrDefault(layer => layer.IsNormalLayer && !layer.IsDummy && layer.LiftSpeed > 0)?.LiftSpeed ?? DefaultLiftSpeed;
            if (CanUseLayerLiftAcceleration) LiftAcceleration = enumerator.LastOrDefault(layer => layer.IsNormalLayer && !layer.IsDummy && layer.LiftAcceleration > 0)?.LiftAcceleration ?? 0;
            if (CanUseLayerLiftHeight2) LiftHeight2 = enumerator.LastOrDefault(layer => layer.IsNormalLayer && !layer.IsDummy && layer.LiftHeight2 > 0)?.LiftHeight2 ?? 0;
            if (CanUseLayerLiftSpeed2) LiftSpeed2 = enumerator.LastOrDefault(layer => layer.IsNormalLayer && !layer.IsDummy && layer.LiftSpeed2 > 0)?.LiftSpeed2 ?? DefaultLiftSpeed2;
            if (CanUseLayerLiftAcceleration2) LiftAcceleration2 = enumerator.LastOrDefault(layer => layer.IsNormalLayer && !layer.IsDummy && layer.LiftAcceleration2 > 0)?.LiftAcceleration2 ?? 0;

            if (CanUseLayerWaitTimeAfterLift) WaitTimeAfterLift = enumerator.LastOrDefault(layer => layer.IsNormalLayer && !layer.IsDummy && layer.WaitTimeAfterLift > 0)?.WaitTimeAfterLift ?? 0;

            if (CanUseLayerRetractSpeed) RetractSpeed = enumerator.LastOrDefault(layer => layer.IsNormalLayer && !layer.IsDummy && layer.RetractSpeed > 0)?.RetractSpeed ?? DefaultRetractSpeed;
            if (CanUseLayerRetractAcceleration) RetractAcceleration = enumerator.LastOrDefault(layer => layer.IsNormalLayer && !layer.IsDummy && layer.RetractAcceleration > 0)?.RetractAcceleration ?? 0;
            if (CanUseLayerRetractHeight2) RetractHeight2 = enumerator.LastOrDefault(layer => layer.IsNormalLayer && !layer.IsDummy && layer.RetractHeight2 > 0)?.RetractHeight2 ?? 0;
            if (CanUseLayerRetractSpeed2) RetractSpeed2 = enumerator.LastOrDefault(layer => layer.IsNormalLayer && !layer.IsDummy && layer.RetractSpeed2 > 0)?.RetractSpeed2 ?? DefaultRetractSpeed2;
            if (CanUseLayerRetractAcceleration2) RetractAcceleration2 = enumerator.LastOrDefault(layer => layer.IsNormalLayer && !layer.IsDummy && layer.RetractAcceleration2 > 0)?.RetractAcceleration2 ?? 0;

            if (CanUseLayerLightOffDelay) LightOffDelay = enumerator.LastOrDefault(layer => layer.IsNormalLayer && !layer.IsDummy && layer.LightOffDelay > 0)?.LightOffDelay ?? 0;
            if (CanUseLayerWaitTimeBeforeCure) WaitTimeBeforeCure = enumerator.LastOrDefault(layer => layer.IsNormalLayer && !layer.IsDummy && layer.WaitTimeBeforeCure > 0)?.WaitTimeBeforeCure ?? 0;
            if (CanUseLayerExposureTime) ExposureTime = enumerator.LastOrDefault(layer => layer.IsNormalLayer && !layer.IsDummy && layer.ExposureTime > 0)?.ExposureTime ?? DefaultExposureTime;
            if (CanUseLayerLightPWM) LightPWM = enumerator.LastOrDefault(layer => layer.IsNormalLayer && !layer.IsDummy && layer.LightPWM > 0)?.LightPWM ?? DefaultLightPWM;
            if (CanUseLayerWaitTimeAfterCure) WaitTimeAfterCure = enumerator.LastOrDefault(layer => layer.IsNormalLayer && !layer.IsDummy && layer.WaitTimeAfterCure > 0)?.WaitTimeAfterCure ?? 0;


            if (BottomLayerCount == 0)
            {
                BottomLiftHeight = LiftHeight;
                BottomLiftSpeed = LiftSpeed;
                BottomLiftAcceleration = LiftAcceleration;
                BottomLiftHeight2 = LiftHeight2;
                BottomLiftSpeed2 = LiftSpeed2;
                BottomLiftAcceleration2 = LiftAcceleration2;

                BottomWaitTimeAfterLift = WaitTimeAfterLift;

                BottomRetractSpeed = RetractSpeed;
                BottomRetractAcceleration = RetractAcceleration;
                BottomRetractHeight2 = RetractHeight2;
                BottomRetractSpeed2 = RetractSpeed2;
                BottomRetractAcceleration2 = RetractAcceleration2;

                BottomLightOffDelay = LightOffDelay;
                BottomWaitTimeBeforeCure = WaitTimeBeforeCure;
                BottomExposureTime = ExposureTime;
                BottomLightPWM = LightPWM;
                BottomWaitTimeAfterCure = WaitTimeAfterCure;
            }
            else
            {
                if (CanUseLayerLiftHeight) BottomLiftHeight = enumerator.FirstOrDefault(layer => !layer.IsDummy && layer.LiftHeight > 0)?.LiftHeight ?? DefaultBottomLiftHeight;
                if (CanUseLayerLiftSpeed) BottomLiftSpeed = enumerator.FirstOrDefault(layer => !layer.IsDummy && layer.LiftSpeed > 0)?.LiftSpeed ?? DefaultBottomLiftSpeed;
                if (CanUseLayerLiftAcceleration) BottomLiftAcceleration = enumerator.FirstOrDefault(layer => !layer.IsDummy && layer.LiftAcceleration > 0)?.LiftAcceleration ?? 0;
                if (CanUseLayerLiftHeight2) BottomLiftHeight2 = enumerator.FirstOrDefault(layer => !layer.IsDummy && layer.LiftHeight2 > 0)?.LiftHeight2 ?? 0;
                if (CanUseLayerLiftSpeed2) BottomLiftSpeed2 = enumerator.FirstOrDefault(layer => !layer.IsDummy && layer.LiftSpeed2 > 0)?.LiftSpeed2 ?? DefaultBottomLiftSpeed2;
                if (CanUseLayerLiftAcceleration2) BottomLiftAcceleration2 = enumerator.FirstOrDefault(layer => !layer.IsDummy && layer.LiftAcceleration2 > 0)?.LiftAcceleration2 ?? 0;

                if (CanUseLayerWaitTimeAfterLift) BottomWaitTimeAfterLift = enumerator.FirstOrDefault(layer => !layer.IsDummy && layer.WaitTimeAfterLift > 0)?.WaitTimeAfterLift ?? 0;

                if (CanUseLayerRetractSpeed) BottomRetractSpeed = enumerator.FirstOrDefault(layer => !layer.IsDummy && layer.RetractSpeed > 0)?.RetractSpeed ?? DefaultBottomRetractSpeed;
                if (CanUseLayerRetractAcceleration) BottomRetractAcceleration = enumerator.FirstOrDefault(layer => !layer.IsDummy && layer.RetractAcceleration > 0)?.RetractAcceleration ?? 0;
                if (CanUseLayerRetractHeight2) BottomRetractHeight2 = enumerator.FirstOrDefault(layer => !layer.IsDummy && layer.RetractHeight2 > 0)?.RetractHeight2 ?? 0;
                if (CanUseLayerRetractSpeed2) BottomRetractSpeed2 = enumerator.FirstOrDefault(layer => !layer.IsDummy && layer.RetractSpeed2 > 0)?.RetractSpeed2 ?? DefaultBottomRetractSpeed2;
                if (CanUseLayerRetractAcceleration2) BottomRetractAcceleration2 = enumerator.FirstOrDefault(layer => !layer.IsDummy && layer.RetractAcceleration2 > 0)?.RetractAcceleration2 ?? 0;

                if (CanUseLayerLightOffDelay) BottomLightOffDelay = enumerator.FirstOrDefault(layer => !layer.IsDummy && layer.LightOffDelay > 0)?.LightOffDelay ?? 0;
                if (CanUseLayerWaitTimeBeforeCure) BottomWaitTimeBeforeCure = enumerator.FirstOrDefault(layer => !layer.IsDummy && layer.WaitTimeBeforeCure > 0)?.WaitTimeBeforeCure ?? 0;
                if (CanUseLayerExposureTime) BottomExposureTime = enumerator.FirstOrDefault(layer => !layer.IsDummy && layer.ExposureTime > 0)?.ExposureTime ?? DefaultBottomExposureTime;
                if (CanUseLayerLightPWM) BottomLightPWM = enumerator.FirstOrDefault(layer => layer.LightPWM > 0)?.LightPWM ?? DefaultBottomLightPWM;
                if (CanUseLayerWaitTimeAfterCure) BottomWaitTimeAfterCure = enumerator.FirstOrDefault(layer => !layer.IsDummy && layer.WaitTimeAfterCure > 0)?.WaitTimeAfterCure ?? 0;
            }
        });


        /* OLD code for gcode (Not the best accuracy due transition layers and dummy layers)
         SuppressRebuildPropertiesWork(() =>
        {
            var bottomLayer = FirstLayer;
            if (bottomLayer is not null)
            {
                if (bottomLayer.LightOffDelay > 0) BottomLightOffDelay = bottomLayer.LightOffDelay;
                if (bottomLayer.WaitTimeBeforeCure > 0) BottomWaitTimeBeforeCure = bottomLayer.WaitTimeBeforeCure;
                if (bottomLayer.ExposureTime > 0) BottomExposureTime = bottomLayer.ExposureTime;
                if (bottomLayer.WaitTimeAfterCure > 0) BottomWaitTimeAfterCure = bottomLayer.WaitTimeAfterCure;
                if (bottomLayer.LiftHeight > 0) BottomLiftHeight = bottomLayer.LiftHeight;
                if (bottomLayer.LiftSpeed > 0) BottomLiftSpeed = bottomLayer.LiftSpeed;
                if (bottomLayer.LiftAcceleration > 0) BottomLiftAcceleration = bottomLayer.LiftAcceleration;
                if (bottomLayer.LiftHeight2 > 0) BottomLiftHeight2 = bottomLayer.LiftHeight2;
                if (bottomLayer.LiftSpeed2 > 0) BottomLiftSpeed2 = bottomLayer.LiftSpeed2;
                if (bottomLayer.LiftAcceleration2 > 0) BottomLiftAcceleration2 = bottomLayer.LiftAcceleration2;
                if (bottomLayer.WaitTimeAfterLift > 0) BottomWaitTimeAfterLift = bottomLayer.WaitTimeAfterLift;
                if (bottomLayer.RetractSpeed > 0) BottomRetractSpeed = bottomLayer.RetractSpeed;
                if (bottomLayer.RetractAcceleration > 0) BottomRetractAcceleration = bottomLayer.RetractAcceleration;
                if (bottomLayer.RetractHeight2 > 0) BottomRetractHeight2 = bottomLayer.RetractHeight2;
                if (bottomLayer.RetractSpeed2 > 0) BottomRetractSpeed2 = bottomLayer.RetractSpeed2;
                if (bottomLayer.RetractAcceleration2 > 0) BottomRetractAcceleration2 = bottomLayer.RetractAcceleration2;
                if (bottomLayer.LightPWM > 0) BottomLightPWM = bottomLayer.LightPWM;
            }

            var normalLayer = LastLayer;
            if (normalLayer is not null)
            {
                if (normalLayer.LightOffDelay > 0) LightOffDelay = normalLayer.LightOffDelay;
                if (normalLayer.WaitTimeBeforeCure > 0) WaitTimeBeforeCure = normalLayer.WaitTimeBeforeCure;
                if (normalLayer.ExposureTime > 0) ExposureTime = normalLayer.ExposureTime;
                if (normalLayer.WaitTimeAfterCure > 0) WaitTimeAfterCure = normalLayer.WaitTimeAfterCure;
                if (normalLayer.LiftHeight > 0) LiftHeight = normalLayer.LiftHeight;
                if (normalLayer.LiftSpeed > 0) LiftSpeed = normalLayer.LiftSpeed;
                if (normalLayer.LiftAcceleration > 0) LiftAcceleration = normalLayer.LiftAcceleration;
                if (normalLayer.LiftHeight2 > 0) LiftHeight2 = normalLayer.LiftHeight2;
                if (normalLayer.LiftSpeed2 > 0) LiftSpeed2 = normalLayer.LiftSpeed2;
                if (normalLayer.LiftAcceleration2 > 0) LiftAcceleration2 = normalLayer.LiftAcceleration2;
                if (normalLayer.WaitTimeAfterLift > 0) WaitTimeAfterLift = normalLayer.WaitTimeAfterLift;
                if (normalLayer.RetractSpeed > 0) RetractSpeed = normalLayer.RetractSpeed;
                if (normalLayer.RetractAcceleration > 0) RetractAcceleration = normalLayer.RetractAcceleration;
                if (normalLayer.RetractHeight2 > 0) RetractHeight2 = normalLayer.RetractHeight2;
                if (normalLayer.RetractSpeed2 > 0) RetractSpeed2 = normalLayer.RetractSpeed2;
                if (normalLayer.RetractAcceleration2 > 0) RetractAcceleration2 = normalLayer.RetractAcceleration2;
                if (normalLayer.LightPWM > 0) LightPWM = normalLayer.LightPWM;
            }
        });*/
    }

    public void UpdatePrintTime()
    {
        PrintTime = PrintTimeComputed;
        //Debug.WriteLine($"Time updated: {_printTime}s");
    }

    public void UpdatePrintTimeQueued()
    {
        lock (Mutex)
        {
            _queueTimerPrintTime.Stop();
            _queueTimerPrintTime.Start();
        }
    }

    /// <summary>
    /// Converts millimeters to pixels given the current resolution and display size
    /// </summary>
    /// <param name="millimeters">Millimeters to convert</param>
    /// <param name="fallbackToPixels">Fallback to this value in pixels if no ratio is available to make the conversion</param>
    /// <returns>Pixels</returns>
    public uint MillimetersXToPixels(float millimeters, uint fallbackToPixels = 0)
    {
        var ppmm = Xppmm;
        if (ppmm <= 0) return fallbackToPixels;
        return (uint)(ppmm * millimeters);
    }

    /// <summary>
    /// Converts millimeters to pixels given the current resolution and display size
    /// </summary>
    /// <param name="millimeters">Millimeters to convert</param>
    /// <param name="fallbackToPixels">Fallback to this value in pixels if no ratio is available to make the conversion</param>
    /// <returns>Pixels</returns>
    public uint MillimetersYToPixels(float millimeters, uint fallbackToPixels = 0)
    {
        var ppmm = Yppmm;
        if (ppmm <= 0) return fallbackToPixels;
        return (uint)(ppmm * millimeters);
    }

    /// <summary>
    /// Converts millimeters to pixels given the current resolution and display size
    /// </summary>
    /// <param name="millimeters">Millimeters to convert</param>
    /// <param name="fallbackToPixels">Fallback to this value in pixels if no ratio is available to make the conversion</param>
    /// <returns>Pixels</returns>
    public uint MillimetersToPixels(float millimeters, uint fallbackToPixels = 0)
    {
        var ppmm = PpmmMax;
        if (ppmm <= 0) return fallbackToPixels;
        return (uint)(ppmm * millimeters);
    }

    /// <summary>
    /// Converts millimeters to pixels given the current resolution and display size
    /// </summary>
    /// <param name="millimeters">Millimeters to convert</param>
    /// <param name="fallbackToPixels">Fallback to this value in pixels if no ratio is available to make the conversion</param>
    /// <returns>Pixels</returns>
    public float MillimetersToPixelsF(float millimeters, uint fallbackToPixels = 0)
    {
        var ppmm = PpmmMax;
        if (ppmm <= 0) return fallbackToPixels;
        return ppmm * millimeters;
    }

    /// <summary>
    /// From a pixel position get the equivalent position on the display
    /// </summary>
    /// <param name="x">X position in pixels</param>
    /// <param name="precision">Decimal precision</param>
    /// <returns>Display position in millimeters</returns>
    public float PixelToDisplayPositionX(int x, byte precision = DisplayFloatPrecision) => MathF.Round(PixelWidth * x, precision);

    /// <summary>
    /// From a pixel position get the equivalent position on the display
    /// </summary>
    /// <param name="y">Y position in pixels</param>
    /// <param name="precision">Decimal precision</param>
    /// <returns>Display position in millimeters</returns>
    public float PixelToDisplayPositionY(int y, byte precision = DisplayFloatPrecision) => MathF.Round(PixelHeight * y, precision);

    /// <summary>
    /// From a pixel position get the equivalent position on the display
    /// </summary>
    /// <param name="x">X position in pixels</param>
    /// <param name="y">Y position in pixels</param>
    /// <param name="precision">Decimal precision</param>
    /// <returns>Resolution position in pixels</returns>
    public PointF PixelToDisplayPosition(int x, int y, byte precision = DisplayFloatPrecision) =>new(PixelToDisplayPositionX(x, precision), PixelToDisplayPositionY(y, precision));
    public PointF PixelToDisplayPosition(Point point, byte precision = DisplayFloatPrecision) => new(PixelToDisplayPositionX(point.X, precision), PixelToDisplayPositionY(point.Y, precision));

    /// <summary>
    /// From a pixel position get the equivalent position on the display
    /// </summary>
    /// <param name="x">X position in millimeters</param>
    /// <returns>Resolution position in pixels</returns>
    public int DisplayToPixelPositionX(float x) => (int)(x * Xppmm);

    /// <summary>
    /// From a pixel position get the equivalent position on the display
    /// </summary>
    /// <param name="y">Y position in millimeters</param>
    /// <returns>Resolution position in pixels</returns>
    public int DisplayToPixelPositionY(float y) => (int)(y * Yppmm);

    /// <summary>
    /// From a display position get the equivalent position on the pixel
    /// </summary>
    /// <param name="x">X position in millimeters</param>
    /// <param name="y">Y position in millimeters</param>
    /// <returns>Resolution position in pixels</returns>
    public Point DisplayToPixelPosition(float x, float y) => new(DisplayToPixelPositionX(x), DisplayToPixelPositionY(y));
    /// <summary>
    /// From a display position get the equivalent position on the pixel
    /// </summary>
    /// <param name="point"></param>
    /// <returns></returns>
    public Point DisplayToPixelPosition(PointF point) => new(DisplayToPixelPositionX(point.X), DisplayToPixelPositionY(point.Y));

    public bool SanitizeBoundingRectangle(ref Rectangle rectangle)
    {
        var oldRectangle = rectangle;
        rectangle = Rectangle.Intersect(rectangle, ResolutionRectangle);
        return oldRectangle != rectangle;
    }

    public Rectangle GetBoundingRectangle(OperationProgress? progress = null)
    {
        var firstLayer = FirstLayer;
        if (!_boundingRectangle.IsEmpty || !HaveLayers || firstLayer is null || !firstLayer.HaveImage) return _boundingRectangle;
        progress ??= new OperationProgress(OperationProgress.StatusOptimizingBounds, LayerCount - 1);
        var boundingRectangle = Rectangle.Empty;
        uint firstValidLayerBounds = 0;

        void FindFirstBoundingRectangle()
        {
            for (uint layerIndex = 0; layerIndex < Count; layerIndex++)
            {
                firstValidLayerBounds = layerIndex;
                var layer = this[layerIndex];
                if (layer is null || layer.NonZeroPixelCount == 0 || layer.BoundingRectangle.IsEmpty) continue;
                boundingRectangle = layer.BoundingRectangle;
                break;
            }
        }

        FindFirstBoundingRectangle();
        //_boundingRectangle = firstLayer.BoundingRectangle;

        if (boundingRectangle.IsEmpty) // Safe checking, all layers haven't a bounding rectangle
        {
            progress.Reset(OperationProgress.StatusOptimizingBounds, LayerCount - 1);
            Parallel.For(0, LayerCount, CoreSettings.GetParallelOptions(progress), layerIndex =>
            {
                progress.PauseIfRequested();
                this[layerIndex].GetBoundingRectangle();
                progress.LockAndIncrement();
            });

            FindFirstBoundingRectangle();
        }

        if (ContainsLayer(firstValidLayerBounds + 1))
        {
            progress.Reset(OperationProgress.StatusCalculatingBounds, LayerCount - firstValidLayerBounds - 1);
            for (var i = firstValidLayerBounds + 1; i < LayerCount; i++)
            {
                var layer = this[i];
                if (layer is null || layer.NonZeroPixelCount == 0 || layer.BoundingRectangle.IsEmpty) continue;
                boundingRectangle = Rectangle.Union(boundingRectangle, layer.BoundingRectangle);
                progress++;
            }
        }

        BoundingRectangle = boundingRectangle;
        return _boundingRectangle;
    }

    public Rectangle GetBoundingRectangle(int marginX, int marginY, OperationProgress? progress = null)
    {
        var rect = GetBoundingRectangle(progress);
        if (marginX == 0 && marginY == 0) return rect;
        rect.Inflate(marginX / 2, marginY / 2);
        SanitizeBoundingRectangle(ref rect);
        return rect;
    }

    public Rectangle GetBoundingRectangle(int margin, OperationProgress? progress = null) => GetBoundingRectangle(margin, margin, progress);
    public Rectangle GetBoundingRectangle(Size margin, OperationProgress? progress = null) => GetBoundingRectangle(margin.Width, margin.Height, progress);


    /// <summary>
    /// Creates an empty mat of file <see cref="Resolution"/> size and create a dummy pixel to prevent an empty layer detection
    /// </summary>
    /// <param name="dummyPixelLocation">Location to set the dummy pixel, use a negative value (-1,-1) to set to the bounding center</param>
    /// <param name="dummyPixelBrightness">Dummy pixel brightness</param>
    /// <returns></returns>
    public Mat CreateMatWithDummyPixel(Point dummyPixelLocation, byte dummyPixelBrightness)
    {
        var newMat = EmguExtensions.InitMat(Resolution);
        if (dummyPixelBrightness > 0)
        {
            if (dummyPixelLocation.IsAnyNegative()) dummyPixelLocation = BoundingRectangle.Center();
            newMat.SetByte(newMat.GetPixelPos(dummyPixelLocation), dummyPixelBrightness);
        }

        return newMat;
    }

    /// <summary>
    /// Creates an empty mat of file <see cref="Resolution"/> size and create a dummy pixel to prevent an empty layer detection
    /// </summary>
    /// <param name="dummyPixelLocation">Location to set the dummy pixel, use a negative value (-1,-1) to set to the bounding center</param>
    /// <returns></returns>
    public Mat CreateMatWithDummyPixel(Point dummyPixelLocation) => CreateMatWithDummyPixel(dummyPixelLocation, SupportGCode ? (byte) 1 : (byte) 128);

    /// <summary>
    /// Creates an empty mat of file <see cref="Resolution"/> size
    /// </summary>
    /// <param name="dummyPixelBrightness">Dummy pixel brightness</param>
    /// <returns></returns>
    public Mat CreateMatWithDummyPixel(byte dummyPixelBrightness) => CreateMatWithDummyPixel(BoundingRectangle.Center(), dummyPixelBrightness);

    /// <summary>
    /// Creates an empty mat of file <see cref="Resolution"/> size
    /// </summary>
    /// <returns></returns>
    public Mat CreateMatWithDummyPixel() => CreateMatWithDummyPixel(SupportGCode ? (byte)1 : (byte)128);

    /// <summary>
    /// Creates an empty mat of file <see cref="Resolution"/> size and create a dummy pixel on optimal position from layer information to prevent an empty layer detection
    /// </summary>
    /// <param name="layerIndex">The layer index to fetch better position of the dummy pixel</param>
    /// <remarks>If the selected layer index does not exist, it will use the global <see cref="BoundingRectangle"/> instead</remarks>
    /// <returns></returns>
    public Mat CreateMatWithDummyPixelFromLayer(uint layerIndex)
    {
        SanitizeLayerIndex(ref layerIndex);
        if (layerIndex >= LayerCount || this[layerIndex].IsEmpty)
        {
            return CreateMatWithDummyPixel();
        }

        var location = this[layerIndex].FirstPixelPosition;
        if (location.X <= 0 || location.Y <= 0) location = new Point(-1, -1);

        return CreateMatWithDummyPixel(location);
    }

    /// <summary>
    /// Creates an empty mat of file <see cref="Resolution"/> size
    /// </summary>
    /// <param name="initMat">True to black out the mat</param>
    /// <returns></returns>
    public Mat CreateMat(bool initMat = true)
    {
        return initMat
            ? EmguExtensions.InitMat(Resolution)
            : new Mat(Resolution, DepthType.Cv8U, 1);
    }

    #endregion

    #region Layer collection methods
    public void Init(Layer[] layers)
    {
        var oldLayerCount = LayerCount;
        _layers = layers;
        if (LayerCount != oldLayerCount)
        {
            LayerCount = LayerCount;
        }

        SanitizeLayers();
    }

    public void Init(uint layerCount, bool initializeLayers = false)
    {
        var oldLayerCount = LayerCount;
        _layers = new Layer[layerCount];
        if (initializeLayers)
        {
            for (uint layerIndex = 0; layerIndex < layerCount; layerIndex++)
            {
                _layers[layerIndex] = new Layer(layerIndex, this);
            }
        }

        if (LayerCount != oldLayerCount)
        {
            LayerCount = LayerCount;
        }
    }

    public void Add(Layer layer)
    {
        Layers = _layers.AsValueEnumerable().Append(layer).ToArray();
    }

    public void Add(IEnumerable<Layer> layers)
    {
        var list = _layers.AsValueEnumerable().ToList();
        list.AddRange(layers);
        Layers = list.ToArray();
    }

    /// <summary>
    /// True if the <paramref name="layerIndex"/> exists in the collection, otherwise false
    /// </summary>
    /// <param name="layerIndex">Layer index to check</param>
    public bool ContainsLayer(int layerIndex)
    {
        return layerIndex >= 0 && layerIndex < Count;
    }

    /// <summary>
    /// True if the <paramref name="layerIndex"/> exists in the collection, otherwise false
    /// </summary>
    /// <param name="layerIndex">Layer index to check</param>
    public bool ContainsLayer(uint layerIndex)
    {
        return layerIndex < LayerCount;
    }

    /// <summary>
    /// True if the <paramref name="layerIndex"/> exists in the collection and if is valid, ie: not null, otherwise false
    /// </summary>
    /// <param name="layerIndex">Layer index to check</param>
    /// <returns></returns>
    public bool ContainsLayerAndValid(int layerIndex)
    {
        return ContainsLayer(layerIndex) && this[layerIndex] is not null;
    }

    /// <summary>
    /// True if the <paramref name="layerIndex"/> exists in the collection and if is valid, ie: not null, otherwise false
    /// </summary>
    /// <param name="layerIndex">Layer index to check</param>
    /// <returns></returns>
    public bool ContainsLayerAndValid(uint layerIndex)
    {
        return ContainsLayer(layerIndex) && this[layerIndex] is not null;
    }

    /// <summary>
    /// True if the <paramref name="layer"/> exists in the collection, otherwise false
    /// </summary>
    /// <param name="layer"></param>
    /// <returns></returns>
    public bool ContainsLayer(Layer layer)
    {
        return _layers.AsValueEnumerable().Contains(layer);
    }

    /// <summary>
    /// True if the <paramref name="layer"/> exists in the collection, otherwise false
    /// </summary>
    /// <param name="layer"></param>
    public bool Contains(Layer layer)
    {
        return _layers.AsValueEnumerable().Contains(layer);
    }

    public void CopyTo(Layer[] array, int arrayIndex)
    {
        _layers.CopyTo(array, arrayIndex);
    }

    public bool Remove(Layer layer)
    {
        var list = _layers.AsValueEnumerable().ToList();
        var result = list.Remove(layer);
        if (result)
        {
            Layers = list.AsValueEnumerable().ToArray();
        }

        return result;
    }

    public int IndexOf(Layer layer)
    {
        for (int layerIndex = 0; layerIndex < Count; layerIndex++)
        {
            if (_layers[layerIndex].Equals(layer)) return layerIndex;
        }

        return -1;
    }

    public void Prepend(Layer layer) => Insert(0, layer);
    public void Prepend(IEnumerable<Layer> layers) => InsertRange(0, layers);
    public void Append(Layer layer) => Add(layer);
    public void AppendRange(IEnumerable<Layer> layers) => Add(layers);

    public void Insert(int index, Layer layer)
    {
        if (index < 0) return;
        if (index > Count)
        {
            Add(layer); // Append
            return;
        }

        var list = _layers.AsValueEnumerable().ToList();
        list.Insert(index, layer);
        Layers = list.AsValueEnumerable().ToArray();
    }

    public void InsertRange(int index, IEnumerable<Layer> layers)
    {
        if (index < 0) return;

        if (index > Count)
        {
            Add(layers);
            return;
        }

        var list = _layers.AsValueEnumerable().ToList();
        list.InsertRange(index, layers);
        Layers = list.AsValueEnumerable().ToArray();
    }

    public void RemoveAt(int index)
    {
        if (index >= LastLayerIndex) return;
        var list = _layers.AsValueEnumerable().ToList();
        list.RemoveAt(index);
        Layers = list.AsValueEnumerable().ToArray();
    }

    public void RemoveRange(int index, int count)
    {
        if (count <= 0 || index >= LastLayerIndex) return;
        var list = _layers.AsValueEnumerable().ToList();
        list.RemoveRange(index, count);
        Layers = list.AsValueEnumerable().ToArray();
    }

    /// <summary>
    /// Clone layers
    /// </summary>
    /// <returns></returns>
    public Layer[] CloneLayers()
    {
        return Layer.CloneLayers(_layers);
    }

    /*
    /// <summary>
    /// Removes all null layers in the collection
    /// </summary>
    public void RemoveNullLayers()
    {
        var oldCount = LayerCount;
        var layers = this.Where(layer => layer is not null).ToArray();
        if (layers.Length == oldCount) return;
        Layers = layers;
    }*/

    /// <summary>
    /// Reallocate with new size
    /// </summary>
    /// <returns></returns>
    public Layer[] ReallocateNew(uint newLayerCount, bool makeClone = false)
    {
        var layers = new Layer[newLayerCount];
        for (uint layerIndex = 0; layerIndex < LayerCount; layerIndex++)
        {
            if (layerIndex >= newLayerCount) break;
            var layer = this[layerIndex];
            layers[layerIndex] = makeClone ? layer.Clone() : layer;
        }

        return layers;
    }

    /// <summary>
    /// Reallocate layer count with a new size
    /// </summary>
    /// <param name="newLayerCount">New layer count</param>
    /// <param name="resetLayerProperties"></param>
    public void Reallocate(uint newLayerCount, bool resetLayerProperties = false)
    {
        int differenceLayerCount = (int)newLayerCount - Count;
        if (differenceLayerCount == 0) return;

        Array.Resize(ref _layers, (int)newLayerCount);

        SuppressRebuildPropertiesWork(() =>
        {
            Layers = _layers;
        }, resetLayerProperties, resetLayerProperties);
    }

    /// <summary>
    /// Reallocate at given index
    /// </summary>
    /// <returns></returns>
    public void ReallocateInsert(uint insertAtLayerIndex, uint layerCount, bool resetLayerProperties = false, bool fixPositionZ = false)
    {
        if (layerCount == 0) return;
        insertAtLayerIndex = Math.Min(insertAtLayerIndex, LayerCount);

        var newLayerCount = LayerCount + layerCount;
        var rightDestinationIndex = insertAtLayerIndex + layerCount;

        var newLayers = new Layer[newLayerCount];

        // Copy from start to insert index
        if (insertAtLayerIndex > 0) Array.Copy(_layers, 0, newLayers, 0, insertAtLayerIndex);

        // Rearrange from last insert to end
        if (insertAtLayerIndex < LayerCount)
            Array.Copy(
                _layers, insertAtLayerIndex,
                newLayers, rightDestinationIndex,
                LayerCount - insertAtLayerIndex);

        SuppressRebuildPropertiesWork(() =>
        {
            Layers = newLayers;
        }, resetLayerProperties, resetLayerProperties);

        if (!resetLayerProperties && fixPositionZ)
        {
            var addedDistance = LayerHeight + this[rightDestinationIndex-1].PositionZ - this[insertAtLayerIndex].PositionZ;
            for (var layerIndex = rightDestinationIndex; layerIndex < newLayerCount; layerIndex++)
            {
                this[layerIndex].PositionZ += addedDistance;
            }

#pragma warning disable CA2245
            PrintHeight = PrintHeight;
#pragma warning restore CA2245
        }

    }

    /// <summary>
    /// Reallocate at a kept range
    /// </summary>
    /// <param name="startLayerIndex"></param>
    /// <param name="endLayerIndex"></param>
    /// <param name="resetLayerProperties"></param>
    public void ReallocateKeepRange(uint startLayerIndex, uint endLayerIndex, bool resetLayerProperties = false)
    {
        if ((int)(endLayerIndex - startLayerIndex) < 0) return;
        var newLayers = new Layer[1 + endLayerIndex - startLayerIndex];

        Array.Copy(_layers, startLayerIndex, newLayers, 0, newLayers.Length);

        SuppressRebuildPropertiesWork(() =>
        {
            Layers = newLayers;
        }, resetLayerProperties, resetLayerProperties);
    }

    /// <summary>
    /// Reallocate at start
    /// </summary>
    /// <returns></returns>
    public void ReallocateStart(uint layerCount, bool resetLayerProperties = false, bool fixPositionZ = false) => ReallocateInsert(0, layerCount, resetLayerProperties, fixPositionZ);

    /// <summary>
    /// Reallocate at end
    /// </summary>
    /// <returns></returns>
    public void ReallocateEnd(uint layerCount, bool resetLayerProperties = false) => ReallocateInsert(LayerCount, layerCount, resetLayerProperties);

    /// <summary>
    /// Allocate layers from a Mat array
    /// </summary>
    /// <param name="mats"></param>
    /// <param name="progress"></param>
    /// <returns>The new Layer array</returns>
    public Layer[] AllocateFromMat(Mat[] mats, OperationProgress? progress = null)
    {
        progress ??= new OperationProgress();
        var layers = new Layer[mats.Length];
        Parallel.For(0, mats.Length, CoreSettings.GetParallelOptions(progress), i =>
        {
            progress.PauseIfRequested();
            layers[i] = new Layer((uint)i, mats[i], this);
        });

        return layers;
    }

    /// <summary>
    /// Allocate layers from a Mat array and set them to the current file
    /// </summary>
    /// <param name="mats"></param>
    /// <param name="progress"></param>
    /// /// <returns>The new Layer array</returns>
    public Layer[] AllocateAndSetFromMat(Mat[] mats, OperationProgress? progress = null)
    {
        var layers = AllocateFromMat(mats, progress);
        Layers = layers;
        return layers;
    }
    #endregion

    #region Layer methods

    /// <summary>
    /// Try to parse starting and ending layer index from a string
    /// </summary>
    /// <param name="value">String value to parse, in start:end format</param>
    /// <param name="layerIndexStart">Parsed starting layer index</param>
    /// <param name="layerIndexEnd">Parsed ending layer index</param>
    /// <returns></returns>
    public bool TryParseLayerIndexRange(string value, out uint layerIndexStart, out uint layerIndexEnd)
    {
        layerIndexStart = 0;
        layerIndexEnd = LastLayerIndex;

        if (string.IsNullOrWhiteSpace(value)) return false;

        var split = value.Split([':', '|', '-'], StringSplitOptions.TrimEntries);

        if (split[0] != string.Empty)
        {
            if(split[0].Equals("FIRST", StringComparison.OrdinalIgnoreCase)) layerIndexStart = 0;
            else if(split[0].Equals("LB", StringComparison.OrdinalIgnoreCase)) layerIndexStart = LastBottomLayer?.Index ?? 0;
            else if(split[0].Equals("FN", StringComparison.OrdinalIgnoreCase)) layerIndexStart = FirstNormalLayer?.Index ?? 0;
            else if(split[0].Equals("LAST", StringComparison.OrdinalIgnoreCase)) layerIndexStart = LastLayerIndex;
            else if(!uint.TryParse(split[0], out layerIndexStart)) return false;
            SanitizeLayerIndex(ref layerIndexStart);
        }

        if (split.Length == 1)
        {
            layerIndexEnd = layerIndexStart;
            return true;
        }

        if (split[1] != string.Empty)
        {
            if (split[1].Equals("FIRST", StringComparison.OrdinalIgnoreCase)) layerIndexEnd = 0;
            else if (split[1].Equals("LB", StringComparison.OrdinalIgnoreCase)) layerIndexEnd = LastBottomLayer?.Index ?? 0;
            else if (split[1].Equals("FN", StringComparison.OrdinalIgnoreCase)) layerIndexEnd = FirstNormalLayer?.Index ?? 0;
            else if (split[1].Equals("LAST", StringComparison.OrdinalIgnoreCase)) layerIndexEnd = LastLayerIndex;
            else if (!uint.TryParse(split[1], out layerIndexEnd)) return false;
            SanitizeLayerIndex(ref layerIndexEnd);
        }

        return layerIndexStart <= layerIndexEnd;
    }

    /// <summary>
    /// Constrains a layer index to be inside the range between 0 and <see cref="LastLayerIndex"/>
    /// </summary>
    /// <param name="layerIndex">Layer index to sanitize</param>
    /// <returns>True if sanitized, otherwise false</returns>
    public bool SanitizeLayerIndex(ref int layerIndex)
    {
        var originalValue = layerIndex;
        layerIndex = Math.Clamp(layerIndex, 0, (int)LastLayerIndex);
        return originalValue != layerIndex;
    }

    /// <summary>
    /// Constrains a layer index to be inside the range between 0 and <see cref="LastLayerIndex"/>
    /// </summary>
    /// <param name="layerIndex">Layer index to sanitize</param>
    /// <returns>True if sanitized, otherwise false</returns>
    public bool SanitizeLayerIndex(ref uint layerIndex)
    {
        var originalValue = layerIndex;
        layerIndex = Math.Min(layerIndex, LastLayerIndex);
        return originalValue != layerIndex;
    }

    /// <summary>
    /// Constrains a layer index to be inside the range between 0 and <see cref="LastLayerIndex"/>
    /// </summary>
    /// <param name="layerIndex">Layer index to sanitize</param>
    public uint SanitizeLayerIndex(int layerIndex)
    {
        return (uint)Math.Clamp(layerIndex, 0, LastLayerIndex);
    }

    /// <summary>
    /// Constrains a layer index to be inside the range between 0 and <see cref="LastLayerIndex"/>
    /// </summary>
    /// <param name="layerIndex">Layer index to sanitize</param>
    public uint SanitizeLayerIndex(uint layerIndex)
    {
        return Math.Min(layerIndex, LastLayerIndex);
    }


    /// <summary>
    /// Re-assign layer indexes and parent <see cref="FileFormat"/>
    /// </summary>
    public void SanitizeLayers()
    {
        for (uint layerIndex = 0; layerIndex < LayerCount; layerIndex++)
        {
            if(this[layerIndex] is null) continue;
            this[layerIndex].Index = layerIndex;
            this[layerIndex].SlicerFile = this;
        }
    }

    /// <summary>
    /// Sanitize file and thrown exception if a severe problem is found
    /// </summary>
    /// <returns>True if one or more corrections has been applied, otherwise false</returns>
    public bool Sanitize()
    {
        bool appliedCorrections = false;

        for (uint layerIndex = 0; layerIndex < LayerCount; layerIndex++)
        {
            // Check for null layers
            if (this[layerIndex] is null) throw new InvalidDataException($"Layer {layerIndex} was defined but doesn't contain a valid image.");
            if (layerIndex <= 0) continue;
            // Check for bigger position z than it successor
            if (this[layerIndex - 1].PositionZ > this[layerIndex].PositionZ && this[layerIndex - 1].NonZeroPixelCount > 1)
                throw new InvalidDataException($"Layer {layerIndex - 1} ({this[layerIndex - 1].PositionZ}mm) have a higher Z position than the successor layer {layerIndex} ({this[layerIndex].PositionZ}mm).\n");
        }

        if ((ResolutionX == 0 || ResolutionY == 0) && DecodeType == FileDecodeType.Full)
        {
            var layer = FirstLayer;
            if (layer is not null)
            {
                using var mat = layer.LayerMat;

                if (mat.Size.HaveZero())
                {
                    throw new FileLoadException($"File resolution ({Resolution}) is invalid and can't be auto fixed due invalid layers with same problem ({mat.Size}).", FileFullPath);
                }

                Resolution = mat.Size;
                appliedCorrections = true;
            }
        }

        // Fix 0mm positions at layer 0
        if (this[0].PositionZ == 0)
        {
            for (uint layerIndex = 0; layerIndex < LayerCount; layerIndex++)
            {
                this[layerIndex].PositionZ = Layer.RoundHeight(this[layerIndex].PositionZ + LayerHeight);
            }

            appliedCorrections = true;
        }

        // Fix LightPWM of 0
        if (LightPWM == 0)
        {
            LightPWM = DefaultLightPWM;
            appliedCorrections = true;
        }
        if (BottomLightPWM == 0)
        {
            BottomLightPWM = DefaultBottomLightPWM;
            appliedCorrections = true;
        }

        return appliedCorrections;
    }

    /// <summary>
    /// Sanitize version and return true if a correction has been applied
    /// </summary>
    /// <returns>True if one or more corrections has been applied, otherwise false</returns>
    public bool SanitizeVersion()
    {
        // Sanitize Version
        if (AvailableVersionsCount > 0)
        {
            var possibleVersions = GetAvailableVersionsForExtension(FileExtension);
            if (possibleVersions.Length > 0)
            {
                if (!possibleVersions.AsValueEnumerable().Contains(Version)) // Version not found on possible versions, set to last
                {
                    Version = possibleVersions[^1];
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Rebuild layer properties based on slice settings
    /// </summary>
    public void RebuildLayersProperties(bool recalculateZPos = true, string? property = null)
    {
        //var layerHeight = SlicerFile.LayerHeight;
        for (uint layerIndex = 0; layerIndex < LayerCount; layerIndex++)
        {
            var layer = this[layerIndex];
            layer.Index = layerIndex;
            layer.SlicerFile = this;

            if (recalculateZPos)
            {
                layer.PositionZ = CalculatePositionZ(layerIndex, false);
            }

            if (property != string.Empty)
            {
                if (property is null or nameof(BottomLayerCount))
                {
                    layer.LightOffDelay = GetBottomOrNormalValue(layer, BottomLightOffDelay, LightOffDelay);
                    layer.WaitTimeBeforeCure = GetBottomOrNormalValue(layer, BottomWaitTimeBeforeCure, WaitTimeBeforeCure);
                    layer.ExposureTime = GetBottomOrNormalValue(layer, BottomExposureTime, ExposureTime);
                    layer.WaitTimeAfterCure = GetBottomOrNormalValue(layer, BottomWaitTimeAfterCure, WaitTimeAfterCure);
                    if ((layer.IsBottomLayer && CanUseBottomLiftHeight2 && !CanUseLayerLiftHeight2) || (layer.IsNormalLayer && CanUseLiftHeight2 && !CanUseLayerLiftHeight2))
                        layer.LiftHeight = GetBottomOrNormalValue(layer, BottomLiftHeightTotal, LiftHeightTotal);
                    else
                        layer.LiftHeight = GetBottomOrNormalValue(layer, BottomLiftHeight, LiftHeight);
                    layer.LiftSpeed = GetBottomOrNormalValue(layer, BottomLiftSpeed, LiftSpeed);
                    layer.LiftAcceleration = GetBottomOrNormalValue(layer, BottomLiftAcceleration, LiftAcceleration);
                    layer.LiftHeight2 = GetBottomOrNormalValue(layer, BottomLiftHeight2, LiftHeight2);
                    layer.LiftSpeed2 = GetBottomOrNormalValue(layer, BottomLiftSpeed2, LiftSpeed2);
                    layer.LiftAcceleration2 = GetBottomOrNormalValue(layer, BottomLiftAcceleration2, LiftAcceleration2);
                    layer.WaitTimeAfterLift = GetBottomOrNormalValue(layer, BottomWaitTimeAfterLift, WaitTimeAfterLift);
                    layer.RetractSpeed = GetBottomOrNormalValue(layer, BottomRetractSpeed, RetractSpeed);
                    layer.RetractAcceleration = GetBottomOrNormalValue(layer, BottomRetractAcceleration, RetractAcceleration);
                    layer.RetractHeight2 = GetBottomOrNormalValue(layer, BottomRetractHeight2, RetractHeight2);
                    layer.RetractSpeed2 = GetBottomOrNormalValue(layer, BottomRetractSpeed2, RetractSpeed2);
                    layer.RetractAcceleration2 = GetBottomOrNormalValue(layer, BottomRetractAcceleration2, RetractAcceleration2);
                    layer.LightPWM = GetBottomOrNormalValue(layer, BottomLightPWM, LightPWM);
                }
                else
                {
                    if (layer.IsBottomLayer)
                    {
                        if (property == nameof(BottomLightOffDelay)) layer.LightOffDelay = BottomLightOffDelay;
                        else if (property == nameof(BottomWaitTimeBeforeCure)) layer.WaitTimeBeforeCure = BottomWaitTimeBeforeCure;
                        else if (property == nameof(BottomExposureTime)) layer.ExposureTime = BottomExposureTime;
                        else if (property == nameof(BottomWaitTimeAfterCure)) layer.WaitTimeAfterCure = BottomWaitTimeAfterCure;
                        else if (property == nameof(BottomLiftHeight))
                        {
                            if (CanUseBottomLiftHeight2 && !CanUseLayerLiftHeight2)
                                layer.LiftHeight = BottomLiftHeightTotal;
                            else
                                layer.LiftHeight = BottomLiftHeight;
                        }
                        else if (property == nameof(BottomLiftSpeed)) layer.LiftSpeed = BottomLiftSpeed;
                        else if (property == nameof(BottomLiftAcceleration)) layer.LiftAcceleration = BottomLiftAcceleration;
                        else if (property == nameof(BottomLiftHeight2)) layer.LiftHeight2 = BottomLiftHeight2;
                        else if (property == nameof(BottomLiftSpeed2)) layer.LiftSpeed2 = BottomLiftSpeed2;
                        else if (property == nameof(BottomLiftAcceleration2)) layer.LiftAcceleration2 = BottomLiftAcceleration2;
                        else if (property == nameof(BottomWaitTimeAfterLift)) layer.WaitTimeAfterLift = BottomWaitTimeAfterLift;
                        else if (property == nameof(BottomRetractSpeed)) layer.RetractSpeed = BottomRetractSpeed;
                        else if (property == nameof(BottomRetractAcceleration)) layer.RetractAcceleration = BottomRetractAcceleration;
                        else if (property == nameof(BottomRetractHeight2)) layer.RetractHeight2 = BottomRetractHeight2;
                        else if (property == nameof(BottomRetractSpeed2)) layer.RetractSpeed2 = BottomRetractSpeed2;
                        else if (property == nameof(BottomRetractAcceleration2)) layer.RetractAcceleration2 = BottomRetractAcceleration2;
                        else if (property == nameof(BottomLightPWM)) layer.LightPWM = BottomLightPWM;

                        // Propagate value to layer when bottom property does not exists
                        else if (property == nameof(LightOffDelay) && !CanUseBottomLightOffDelay) layer.LightOffDelay = LightOffDelay;
                        else if (property == nameof(WaitTimeBeforeCure) && !CanUseBottomWaitTimeBeforeCure) layer.WaitTimeBeforeCure = WaitTimeBeforeCure;
                        else if (property == nameof(ExposureTime) && !CanUseBottomExposureTime) layer.ExposureTime = ExposureTime;
                        else if (property == nameof(WaitTimeAfterCure) && !CanUseBottomWaitTimeAfterCure) layer.WaitTimeAfterCure = WaitTimeAfterCure;
                        else if (property == nameof(LiftHeight) && !CanUseBottomLiftHeight)
                        {
                            if (CanUseLiftHeight2 && !CanUseLayerLiftHeight2)
                                layer.LiftHeight = LiftHeightTotal;
                            else
                                layer.LiftHeight = LiftHeight;
                        }
                        else if (property == nameof(LiftSpeed) && !CanUseBottomLiftSpeed) layer.LiftSpeed = LiftSpeed;
                        else if (property == nameof(LiftAcceleration) && !CanUseBottomLiftAcceleration) layer.LiftAcceleration = LiftAcceleration;
                        else if (property == nameof(LiftHeight2) && !CanUseBottomLiftHeight2) layer.LiftHeight2 = LiftHeight2;
                        else if (property == nameof(LiftSpeed2) && !CanUseBottomLiftSpeed2) layer.LiftSpeed2 = LiftSpeed2;
                        else if (property == nameof(LiftAcceleration2) && !CanUseBottomLiftAcceleration2) layer.LiftAcceleration2 = LiftAcceleration2;
                        else if (property == nameof(WaitTimeAfterLift) && !CanUseBottomWaitTimeAfterLift) layer.WaitTimeAfterLift = WaitTimeAfterLift;
                        else if (property == nameof(RetractSpeed) && !CanUseBottomRetractSpeed) layer.RetractSpeed = RetractSpeed;
                        else if (property == nameof(RetractAcceleration) && !CanUseBottomRetractAcceleration) layer.RetractAcceleration = RetractAcceleration;
                        else if (property == nameof(RetractHeight2) && !CanUseBottomRetractHeight2) layer.RetractHeight2 = RetractHeight2;
                        else if (property == nameof(RetractSpeed2) && !CanUseRetractSpeed2) layer.RetractSpeed2 = RetractSpeed2;
                        else if (property == nameof(RetractAcceleration2) && !CanUseBottomRetractAcceleration2) layer.RetractAcceleration2 = RetractAcceleration2;
                        else if (property == nameof(LightPWM) && !CanUseBottomLightPWM) layer.LightPWM = LightPWM;
                    }
                    else // Normal layers
                    {
                        if (property == nameof(LightOffDelay)) layer.LightOffDelay = LightOffDelay;
                        else if (property == nameof(WaitTimeBeforeCure)) layer.WaitTimeBeforeCure = WaitTimeBeforeCure;
                        else if (property == nameof(ExposureTime)) layer.ExposureTime = ExposureTime;
                        else if (property == nameof(WaitTimeAfterCure)) layer.WaitTimeAfterCure = WaitTimeAfterCure;
                        else if (property == nameof(LiftHeight))
                        {
                            if (CanUseLiftHeight2 && !CanUseLayerLiftHeight2)
                                layer.LiftHeight = LiftHeightTotal;
                            else
                                layer.LiftHeight = LiftHeight;
                        }
                        else if (property == nameof(LiftSpeed)) layer.LiftSpeed = LiftSpeed;
                        else if (property == nameof(LiftAcceleration)) layer.LiftAcceleration = LiftAcceleration;
                        else if (property == nameof(LiftHeight2)) layer.LiftHeight2 = LiftHeight2;
                        else if (property == nameof(LiftSpeed2)) layer.LiftSpeed2 = LiftSpeed2;
                        else if (property == nameof(LiftAcceleration2)) layer.LiftAcceleration2 = LiftAcceleration2;
                        else if (property == nameof(WaitTimeAfterLift)) layer.WaitTimeAfterLift = WaitTimeAfterLift;
                        else if (property == nameof(RetractSpeed)) layer.RetractSpeed = RetractSpeed;
                        else if (property == nameof(RetractAcceleration)) layer.RetractAcceleration = RetractAcceleration;
                        else if (property == nameof(RetractHeight2)) layer.RetractHeight2 = RetractHeight2;
                        else if (property == nameof(RetractSpeed2)) layer.RetractSpeed2 = RetractSpeed2;
                        else if (property == nameof(RetractAcceleration2)) layer.RetractAcceleration2 = RetractAcceleration2;
                        else if (property == nameof(LightPWM)) layer.LightPWM = LightPWM;
                    }
                }
            }

            layer.MaterialMilliliters = -1; // Recalculate this value to be sure
        }

        RebuildGCode();
    }

    /// <summary>
    /// Set LiftHeight to 0 if previous and current have same PositionZ
    /// <param name="zeroLightOffDelay">If true also set light off to 0, otherwise current value will be kept.</param>
    /// </summary>
    public void SetNoLiftForSamePositionedLayers(bool zeroLightOffDelay = false)
        => SetLiftForSamePositionedLayers(0, zeroLightOffDelay);

    public void SetLiftForSamePositionedLayers(float liftHeight = 0, bool zeroLightOffDelay = false)
    {
        for (int layerIndex = 1; layerIndex < LayerCount; layerIndex++)
        {
            var layer = this[layerIndex];
            if (this[layerIndex - 1].PositionZ != layer.PositionZ) continue;
            layer.LiftHeightTotal = liftHeight;
            layer.WaitTimeAfterLift = 0;
            if (zeroLightOffDelay)
            {
                layer.LightOffDelay = 0;
                layer.WaitTimeBeforeCure = 0;
                layer.WaitTimeAfterCure = 0;
            }
        }
        RebuildGCode();
    }

    public Mat GetMergedMatForSequentialPositionedLayers(uint layerIndex, MatCacheManager cacheManager, out uint lastLayerIndex)
    {
        var startLayerPositionZ = this[layerIndex].PositionZ;
        lastLayerIndex = layerIndex;
        var layerMat = cacheManager.Get1(layerIndex).Clone();

        for (var curIndex = layerIndex + 1; curIndex < LayerCount && this[curIndex].PositionZ == startLayerPositionZ; curIndex++)
        {
            CvInvoke.Max(layerMat, cacheManager.Get1(curIndex), layerMat);
            lastLayerIndex = curIndex;
        }

        return layerMat;
    }

    public Mat GetMergedMatForSequentialPositionedLayers(uint layerIndex, MatCacheManager cacheManager)
        => GetMergedMatForSequentialPositionedLayers(layerIndex, cacheManager, out _);

    public Mat GetMergedMatForSequentialPositionedLayers(uint layerIndex, out uint lastLayerIndex)
    {
        var startLayer = this[layerIndex];
        lastLayerIndex = layerIndex;
        var layerMat = startLayer.LayerMat;

        for (var curIndex = layerIndex + 1; curIndex < LayerCount && this[curIndex].PositionZ == startLayer.PositionZ; curIndex++)
        {
            using var nextLayer = this[curIndex].LayerMat;
            CvInvoke.Max(nextLayer, layerMat, layerMat);
            lastLayerIndex = curIndex;
        }

        return layerMat;
    }

    public Mat GetMergedMatForSequentialPositionedLayers(uint layerIndex)
        => GetMergedMatForSequentialPositionedLayers(layerIndex, out _);
    #endregion

    #region Draw Modifications
    public void DrawModifications(IList<PixelOperation> drawings, OperationProgress? progress = null)
    {
        progress ??= new OperationProgress();
        progress.Reset("Drawings", (uint)drawings.Count);

        var group1 = drawings
            .Where(operation => operation.OperationType
                is PixelOperation.PixelOperationType.Drawing
                or PixelOperation.PixelOperationType.Text
                or PixelOperation.PixelOperationType.Fill)
            .GroupBy(operation => operation.LayerIndex);

        Parallel.ForEach(group1, CoreSettings.GetParallelOptions(progress), layerOperationGroup =>
        {
            progress.PauseIfRequested();
            var layer = this[layerOperationGroup.Key];
            using var mat = layer.LayerMat;

            foreach (var operation in layerOperationGroup)
            {
                if (operation.OperationType == PixelOperation.PixelOperationType.Drawing)
                {
                    if (operation is not PixelDrawing operationDrawing) continue;
                    if (operationDrawing.BrushSize == 1)
                    {
                        mat.SetByte(operation.Location.X, operation.Location.Y, operationDrawing.Brightness);
                        continue;
                    }

                    var pixelWidth = PixelWidth;
                    var pixelHeigth = PixelHeight;
                    var diameter = PixelsToNormalizedPitchF(operationDrawing.BrushSize);

                    mat.DrawAlignedPolygon((byte)operationDrawing.BrushShape, diameter,
                        operationDrawing.Location,
                        new MCvScalar(operationDrawing.Brightness), operationDrawing.RotationAngle,
                        operationDrawing.Thickness, operationDrawing.LineType);
                    /*switch (operationDrawing.BrushShape)
                        {
                            case PixelDrawing.BrushShapeType.Square:
                                CvInvoke.Rectangle(mat, operationDrawing.Rectangle, new MCvScalar(operationDrawing.Brightness), operationDrawing.Thickness, operationDrawing.LineType);
                                break;
                            case PixelDrawing.BrushShapeType.Circle:
                                CvInvoke.Circle(mat, operation.Location, operationDrawing.BrushSize / 2,
                                    new MCvScalar(operationDrawing.Brightness), operationDrawing.Thickness, operationDrawing.LineType);
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }*/
                }
                else if (operation.OperationType == PixelOperation.PixelOperationType.Text)
                {
                    if (operation is not PixelText operationText) continue;
                    mat.PutTextRotated(operationText.Text, operationText.Location, operationText.Font,
                        operationText.FontScale, new MCvScalar(operationText.Brightness), operationText.Thickness,
                        operationText.LineType, operationText.Mirror, operationText.LineAlignment,
                        (double)operationText.Angle);
                }
                else if (operation.OperationType == PixelOperation.PixelOperationType.Fill)
                {
                    if (operation is not PixelFill operationFill) continue;
                    var pixel = mat.GetByte(operation.Location);
                    if (!operationFill.IsAdd && pixel == 0) continue;

                    using var vec = layer.Contours.GetContoursInside(operation.Location);

                    if (vec.Size > 0)
                    {
                        CvInvoke.DrawContours(mat, vec, -1, new MCvScalar(operationFill.Brightness), -1);
                    }
                }
            }

            layer.LayerMat = mat;
            progress.LockAndIncrement();
        });

        var group2 = drawings
            .Where(operation => operation.OperationType
                is PixelOperation.PixelOperationType.Supports
                or PixelOperation.PixelOperationType.DrainHole)
            .GroupBy(operation => operation.LayerIndex)
            .OrderByDescending(group => group.Key);

        if (group2.Any())
        {
            using var matCache = new MatCacheManager(this, 0, group2.First().Key)
            {
                AutoDispose = true,
                Direction = false
            };
            foreach (var layerOperationGroup in group2)
            {
                var toProcess = layerOperationGroup.ToList();
                var drawnSupportLayers = 0;
                var drawnDrainHoleLayers = 0;
                for (int operationLayer = (int)layerOperationGroup.Key - 1; operationLayer >= 0 && toProcess.Count > 0; operationLayer--)
                {
                    var layer = this[operationLayer];
                    var mat = matCache.Get1((uint) operationLayer);
                    var isMatModified = false;

                    for (var i = toProcess.Count-1; i >= 0; i--)
                    {
                        progress.PauseOrCancelIfRequested();
                        var operation = toProcess[i];
                        if (operation.OperationType == PixelOperation.PixelOperationType.Supports)
                        {
                            var operationSupport = (PixelSupport) operation;

                            int radius = (operationLayer > 10
                                ? Math.Min(operationSupport.TipDiameter + drawnSupportLayers, operationSupport.PillarDiameter)
                                : operationSupport.BaseDiameter) / 2;
                            uint whitePixels;

                            int yStart = Math.Max(0, operation.Location.Y - operationSupport.TipDiameter / 2);
                            int xStart = Math.Max(0, operation.Location.X - operationSupport.TipDiameter / 2);

                            var tipDiameter = PixelsToNormalizedPitch(operationSupport.TipDiameter);
                            var tipRadius = PixelsToNormalizedPitch(operationSupport.TipDiameter / 2);
                            var pillarDiameter = PixelsToNormalizedPitch(operationSupport.PillarDiameter);

                            using (var matCircleRoi = new Mat(mat, new Rectangle(xStart, yStart, tipDiameter.Width, tipDiameter.Height)))
                            {
                                using var matCircleMask = matCircleRoi.NewZeros();
                                matCircleMask.DrawCircle(tipRadius.ToPoint(), tipRadius, new MCvScalar(operation.PixelBrightness), -1);
                                CvInvoke.BitwiseAnd(matCircleRoi, matCircleMask, matCircleMask);
                                whitePixels = (uint) CvInvoke.CountNonZero(matCircleMask);
                            }

                            if (whitePixels >= Math.Pow(operationSupport.TipDiameter, 2) / 3)
                            {
                                //CvInvoke.Circle(mat, operation.Location, radius, new MCvScalar(255), -1);
                                if (drawnSupportLayers == 0) continue; // Supports nonexistent, keep digging
                                toProcess.RemoveAt(i);
                                continue; // White area end supporting
                            }

                            mat.DrawCircle(operation.Location, PixelsToNormalizedPitch(radius), new MCvScalar(operation.PixelBrightness), -1, operationSupport.LineType);
                            isMatModified = true;
                            drawnSupportLayers++;
                        }
                        else if (operation.OperationType == PixelOperation.PixelOperationType.DrainHole)
                        {
                            var operationDrainHole = (PixelDrainHole) operation;

                            var diameterPitched = PixelsToNormalizedPitch(operationDrainHole.Diameter);
                            var radius = PixelsToNormalizedPitch(operationDrainHole.Diameter / 2);
                            uint blackPixels;

                            int xStart = Math.Max(0, operation.Location.X - radius.Width);
                            int yStart = Math.Max(0, operation.Location.Y - radius.Height);

                            using (var matCircleRoi = new Mat(mat, new Rectangle(xStart, yStart, diameterPitched.Width, diameterPitched.Height)))
                            {
                                using var matCircleRoiInv = new Mat();
                                CvInvoke.Threshold(matCircleRoi, matCircleRoiInv, 100, 255, ThresholdType.BinaryInv);
                                using var matCircleMask = matCircleRoi.NewZeros();
                                matCircleMask.DrawCircle(radius.ToPoint(), radius, EmguExtensions.WhiteColor, -1);
                                CvInvoke.BitwiseAnd(matCircleRoiInv, matCircleMask, matCircleMask);
                                blackPixels = (uint) CvInvoke.CountNonZero(matCircleMask);
                            }

                            if (blackPixels >= Math.Pow(operationDrainHole.Diameter, 2) / 3) // Enough area to drain?
                            {
                                if (drawnDrainHoleLayers == 0) continue; // Drill not found a target yet, keep digging
                                toProcess.RemoveAt(i);
                                continue; // Stop drill drain found!
                            }

                            mat.DrawCircle(operation.Location, radius, EmguExtensions.BlackColor, -1, operationDrainHole.LineType);
                            isMatModified = true;
                            drawnDrainHoleLayers++;
                        }
                    }

                    if (isMatModified)
                    {
                        layer.LayerMat = mat;
                    }
                }

                progress += (uint)layerOperationGroup.Count();
            }
        }
    }
    #endregion

    #region Generators methods

    /// <summary>
    /// Generates a heatmap based on a stack of layers
    /// </summary>
    /// <param name="layerIndexStart">Layer index to start from</param>
    /// <param name="layerIndexEnd">Layer index to end on</param>
    /// <param name="roi">Region of interest</param>
    /// <param name="progress"></param>
    /// <returns>Heatmap grayscale Mat</returns>
    public Mat GenerateHeatmap(uint layerIndexStart = 0, uint layerIndexEnd = uint.MaxValue, Rectangle roi = default, OperationProgress? progress = null)
    {
        SanitizeLayerIndex(ref layerIndexEnd);

        progress ??= new OperationProgress();
        progress.Title = $"Generating a heatmap from layers {layerIndexStart} through {layerIndexEnd}";
        progress.ItemName = "layers";

        if (roi.IsEmpty) roi = ResolutionRectangle;

        var resultMat = EmguExtensions.InitMat(roi.Size, 1, DepthType.Cv32S);
        var layerRange = GetDistinctLayersByPositionZ(layerIndexStart, layerIndexEnd).AsValueEnumerable().ToArray();

        progress.ItemCount = (uint)layerRange.Length;

        Parallel.ForEach(layerRange, CoreSettings.GetParallelOptions(progress), layer =>
        {
            progress.PauseIfRequested();
            using var mat = GetMergedMatForSequentialPositionedLayers(layer.Index);
            using var mat32Roi = mat.Roi(roi);

            mat32Roi.ConvertTo(mat32Roi, DepthType.Cv32S);

            lock (progress.Mutex)
            {
                CvInvoke.Add(resultMat, mat32Roi, resultMat);
                progress++;
            }
        });


        resultMat.ConvertTo(resultMat, DepthType.Cv8U, 1.0 / layerRange.Length);

        return resultMat;
    }

    /// <summary>
    /// Generates a heatmap based on a stack of layers
    /// </summary>
    /// <param name="roi">Region of interest</param>
    /// <param name="progress"></param>
    /// <returns>Heatmap grayscale Mat</returns>
    public Mat GenerateHeatmap(Rectangle roi, OperationProgress? progress = null) => GenerateHeatmap(0, uint.MaxValue, roi, progress);

    public Task<Mat> GenerateHeatmapAsync(uint layerIndexStart = 0, uint layerIndexEnd = uint.MaxValue, Rectangle roi = default, OperationProgress? progress = null)
        => Task.Run(() => GenerateHeatmap(layerIndexStart, layerIndexEnd, roi, progress), progress?.Token ?? default);

    public Task<Mat> GenerateHeatmapAsync(Rectangle roi, OperationProgress? progress = null)
        => Task.Run(() => GenerateHeatmap(0, uint.MaxValue, roi, progress), progress?.Token ?? default);

    #endregion
}