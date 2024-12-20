﻿/*
 *                     GNU AFFERO GENERAL PUBLIC LICENSE
 *                       Version 3, 19 November 2007
 *  Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
 *  Everyone is permitted to copy and distribute verbatim copies
 *  of this license document, but changing it is not allowed.
 */

using BinarySerialization;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UVtools.Core.Converters;
using UVtools.Core.EmguCV;
using UVtools.Core.Extensions;
using UVtools.Core.Layers;
using UVtools.Core.Objects;
using UVtools.Core.Operations;
using UVtools.Core.Printer;

namespace UVtools.Core.FileFormats;

public sealed class CrealityCXDLPFile : FileFormat
{
    #region Constants

    private const byte MAGIC_SIZE = 9; // CXSW3DV2
    private const string MAGIC_VALUE = "CXSW3DV2";
    private const string HEADER_VALUE_GENERIC = "CXSW3D";
    private const byte DEFAULT_VERSION = 3;

    #endregion

    #region Sub Classes

    #region Header
    public sealed class Header
    {
        /// <summary>
        /// Gets the size of the magic
        /// </summary>
        [FieldOrder(0)] 
        [FieldEndianness(Endianness.Big)] 
        public uint MagicSize { get; set; } = MAGIC_SIZE;

        /// <summary>
        /// Gets the magic name
        /// </summary>
        [FieldOrder(1)] 
        [FieldLength(MAGIC_SIZE)] 
        [SerializeAs(SerializedType.TerminatedString)] 
        public string Magic { get; set; } = MAGIC_VALUE;

        [FieldOrder(2)]
        [FieldEndianness(Endianness.Big)]
        public ushort Version { get; set; } = DEFAULT_VERSION;

        /// <summary>
        /// Gets the printer model
        /// </summary>
        [FieldOrder(3)]
        public NullTerminatedUintStringBigEndian PrinterModel { get; set; } = string.Empty;

        /// <summary>
        /// Gets the number of records in the layer table
        /// </summary>
        [FieldOrder(4)] 
        [FieldEndianness(Endianness.Big)] 
        public ushort LayerCount { get; set; }

        /// <summary>
        /// Gets the printer resolution along X axis, in pixels. This information is critical to correctly decoding layer images.
        /// </summary>
        [FieldOrder(5)]
        [FieldEndianness(Endianness.Big)] 
        public ushort ResolutionX { get; set; }

        /// <summary>
        /// Gets the printer resolution along Y axis, in pixels. This information is critical to correctly decoding layer images.
        /// </summary>
        [FieldOrder(6)]
        [FieldEndianness(Endianness.Big)] 
        public ushort ResolutionY { get; set; }
            
        [FieldOrder(7)] [FieldLength(64)]
        public byte[] Offset { get; set; } = new byte[64];

        public void Validate()
        {
            /*if (HeaderSize != HEADER_SIZE || HeaderValue != HEADER_VALUE)
            {
                throw new FileLoadException("Not a valid CXDLP file!");
            }*/

            if (MagicSize - 1 != Magic.Length || !Magic.StartsWith(HEADER_VALUE_GENERIC))
            {
                throw new FileLoadException("Invalid header data for CXDLP file.");
            }
        }

        public override string ToString()
        {
            return $"{nameof(MagicSize)}: {MagicSize}, {nameof(Magic)}: {Magic}, {nameof(Version)}: {Version}, {nameof(PrinterModel)}: {PrinterModel}, {nameof(LayerCount)}: {LayerCount}, {nameof(ResolutionX)}: {ResolutionX}, {nameof(ResolutionY)}: {ResolutionY}, {nameof(Offset)}: {Offset}";
        }
    }

    #endregion

    #region SlicerInfo
    // Address: 363407
    public sealed class SlicerInfo
    {
        [FieldOrder(0)]
        [FieldEndianness(Endianness.Big)]
        public uint DisplayWidthLength { get; set; }

        [FieldOrder(1)]
        [FieldEncoding("UTF-16BE")]
        [FieldLength(nameof(DisplayWidthLength))]
        public string DisplayWidth { get; set; } = string.Empty;

        [FieldOrder(2)]
        [FieldEndianness(Endianness.Big)]
        public uint DisplayHeightLength { get; set; }

        [FieldOrder(3)]
        [FieldEncoding("UTF-16BE")]
        [FieldLength(nameof(DisplayHeightLength))]
        public string DisplayHeight { get; set; } = string.Empty;

        [FieldOrder(4)]
        [FieldEndianness(Endianness.Big)]
        public uint LayerHeightLength { get; set; } = 8;

        [FieldOrder(5)]
        [FieldEncoding("UTF-16BE")]
        [FieldLength(nameof(LayerHeightLength))]
        public string LayerHeight { get; set; } = DefaultLayerHeight.ToString(CultureInfo.InvariantCulture);

        [FieldOrder(6)]
        [FieldEndianness(Endianness.Big)]
        public ushort ExposureTime { get; set; }

        [FieldOrder(7)]
        [FieldEndianness(Endianness.Big)]
        public ushort WaitTimeBeforeCure { get; set; } = 1; // 1 as minimum or it wont print!

        [FieldOrder(8)]
        [FieldEndianness(Endianness.Big)]
        public ushort BottomExposureTime { get; set; }

        [FieldOrder(9)]
        [FieldEndianness(Endianness.Big)]
        public ushort BottomLayersCount { get; set; }

        [FieldOrder(10)]
        [FieldEndianness(Endianness.Big)]
        public ushort BottomLiftHeight { get; set; }

        [FieldOrder(11)]
        [FieldEndianness(Endianness.Big)]
        public ushort BottomLiftSpeed { get; set; }

        [FieldOrder(12)]
        [FieldEndianness(Endianness.Big)]
        public ushort LiftHeight { get; set; }

        [FieldOrder(13)]
        [FieldEndianness(Endianness.Big)]
        public ushort LiftSpeed { get; set; }

        [FieldOrder(14)]
        [FieldEndianness(Endianness.Big)]
        public ushort RetractSpeed { get; set; }

        [FieldOrder(15)]
        [FieldEndianness(Endianness.Big)]
        public ushort BottomLightPWM { get; set; } = 255;

        [FieldOrder(16)]
        [FieldEndianness(Endianness.Big)]
        public ushort LightPWM { get; set; } = 255;

        public override string ToString()
        {
            return $"{nameof(DisplayWidthLength)}: {DisplayWidthLength}, {nameof(DisplayWidth)}: {DisplayWidth}, {nameof(DisplayHeightLength)}: {DisplayHeightLength}, {nameof(DisplayHeight)}: {DisplayHeight}, {nameof(LayerHeightLength)}: {LayerHeightLength}, {nameof(LayerHeight)}: {LayerHeight}, {nameof(ExposureTime)}: {ExposureTime}, {nameof(WaitTimeBeforeCure)}: {WaitTimeBeforeCure}, {nameof(BottomExposureTime)}: {BottomExposureTime}, {nameof(BottomLayersCount)}: {BottomLayersCount}, {nameof(BottomLiftHeight)}: {BottomLiftHeight}, {nameof(BottomLiftSpeed)}: {BottomLiftSpeed}, {nameof(LiftHeight)}: {LiftHeight}, {nameof(LiftSpeed)}: {LiftSpeed}, {nameof(RetractSpeed)}: {RetractSpeed}, {nameof(BottomLightPWM)}: {BottomLightPWM}, {nameof(LightPWM)}: {LightPWM}";
        }
    }
    #endregion

    #region PreLayer

    public sealed class PreLayer
    {
        [FieldOrder(0)]
        [FieldEndianness(Endianness.Big)]
        public uint LayerArea { get; set; }

        public PreLayer()
        {
        }

        public PreLayer(uint layerArea)
        {
            LayerArea = layerArea;
        }
    }

    #endregion

    #region SlicerInfoV3

    public sealed class SlicerInfoV3
    {
        //[FieldOrder(0)] [FieldEndianness(Endianness.Big)] public uint SoftwareNameSize { get; set; } = (uint)About.SoftwareWithVersion.Length + 1;

        [FieldOrder(0)] public NullTerminatedUintStringBigEndian SoftwareName { get; set; } = new(About.SoftwareWithVersion);

        //[FieldOrder(2)] [FieldEndianness(Endianness.Big)] public uint MaterialNameSize { get; set; }

        [FieldOrder(1)] public NullTerminatedUintStringBigEndian MaterialName { get; set; } = new();

        [FieldOrder(2)] public byte DistortionCompensationEnabled { get; set; }
        [FieldOrder(3)] public uint DistortionCompensationThickness { get; set; } = 600;
        [FieldOrder(4)] public uint DistortionCompensationFocalLength { get; set; } = 300000;
        [FieldOrder(5)] public byte XYAxisProfileCompensationEnabled { get; set; } = 1;
        [FieldOrder(6)] public ushort XYAxisProfileCompensationValue { get; set; }
        [FieldOrder(7)] public byte ZPenetrationCompensationEnabled { get; set; }
        [FieldOrder(8)] public ushort ZPenetrationCompensationLevel { get; set; } = 1000;
        [FieldOrder(9)] public byte AntiAliasEnabled { get; set; } = 1;
        [FieldOrder(10)] public byte AntiAliasGreyMinValue { get; set; } = 1;
        [FieldOrder(11)] public byte AntiAliasGreyMaxValue { get; set; } = byte.MaxValue;
        [FieldOrder(12)] public byte ImageBlurEnabled { get; set; } = 0;
        [FieldOrder(13)] public byte ImageBlurLevel { get; set; } = 2;
        [FieldOrder(14)] public PageBreak PageBreak { get; set; } = new();


        public override string ToString()
        {
            return $"{nameof(SoftwareName)}: {SoftwareName}, {nameof(MaterialName)}: {MaterialName}, {nameof(DistortionCompensationEnabled)}: {DistortionCompensationEnabled}, {nameof(DistortionCompensationThickness)}: {DistortionCompensationThickness}, {nameof(DistortionCompensationFocalLength)}: {DistortionCompensationFocalLength}, {nameof(XYAxisProfileCompensationEnabled)}: {XYAxisProfileCompensationEnabled}, {nameof(XYAxisProfileCompensationValue)}: {XYAxisProfileCompensationValue}, {nameof(ZPenetrationCompensationEnabled)}: {ZPenetrationCompensationEnabled}, {nameof(ZPenetrationCompensationLevel)}: {ZPenetrationCompensationLevel}, {nameof(AntiAliasEnabled)}: {AntiAliasEnabled}, {nameof(AntiAliasGreyMinValue)}: {AntiAliasGreyMinValue}, {nameof(AntiAliasGreyMaxValue)}: {AntiAliasGreyMaxValue}, {nameof(ImageBlurEnabled)}: {ImageBlurEnabled}, {nameof(ImageBlurLevel)}: {ImageBlurLevel}, {nameof(PageBreak)}: {PageBreak}";
        }
    }

    #endregion

    #region Layer Def

    public sealed class LayerDef
    {
        public static byte[] GetHeaderBytes(uint layerArea, uint lineCount)
        {
            var bytes = new byte[8];
            BitExtensions.ToBytesBigEndian(layerArea, bytes);
            BitExtensions.ToBytesBigEndian(lineCount, bytes, 4);
            return bytes;
        }

        [FieldOrder(0)] [FieldEndianness(Endianness.Big)] public uint LayerArea { get; set; }
        [FieldOrder(1)] [FieldEndianness(Endianness.Big)] public uint LineCount { get; set; }
        [FieldOrder(2)] [FieldCount(nameof(LineCount))] public LayerLine[] Lines { get; set; } = Array.Empty<LayerLine>();
        [FieldOrder(3)] public PageBreak PageBreak { get; set; } = new();

        public LayerDef() { }

        public LayerDef(uint layerArea, uint lineCount, LayerLine[] lines)
        {
            LayerArea = layerArea;
            LineCount = lineCount;
            Lines = lines;
        }
    }

    public sealed class LayerLine
    {
        public const byte CoordinateCount = 5;
        [FieldOrder(0)] [FieldCount(CoordinateCount)] public byte[] Coordinates { get; set; } = new byte[CoordinateCount];
        //[FieldOrder(0)] [FieldEndianness(Endianness.Big)] [FieldBitLength(13)] public ushort StartY { get; set; }
        //[FieldOrder(1)] [FieldEndianness(Endianness.Big)] [FieldBitLength(13)] public ushort EndY { get; set; }
        //[FieldOrder(2)] [FieldEndianness(Endianness.Big)] [FieldBitLength(14)] public ushort StartX { get; set; }
        [FieldOrder(1)] public byte Gray { get; set; }

        [Ignore] public ushort StartY => (ushort) ((((Coordinates[0] << 8) + Coordinates[1]) >> 3) & 0x1FFF); // 13 bits

        [Ignore] public ushort EndY => (ushort)((((Coordinates[1] << 16) + (Coordinates[2] << 8) + Coordinates[3]) >> 6) & 0x1FFF); // 13 bits

        [Ignore] public ushort StartX => (ushort)(((Coordinates[3] << 8) + Coordinates[4]) & 0x3FFF); // 14 bits
        [Ignore] public ushort Length => (ushort) (EndY - StartY);

        public static byte[] GetBytes(ushort startY, ushort endY, ushort startX, byte gray)
        {
            var bytes = new byte[CoordinateCount + 1];
            bytes[0] = (byte)((startY >> 5) & 0xFF);
            bytes[1] = (byte)(((startY << 3) + (endY >> 10)) & 0xFF);
            bytes[2] = (byte)((endY >> 2) & 0xFF);
            bytes[3] = (byte)(((endY << 6) + (startX >> 8)) & 0xFF);
            bytes[4] = (byte)startX;
            bytes[5] = gray;
            return bytes;
        }

        public LayerLine() { }

        public LayerLine(ushort startY, ushort endY, ushort startX, byte gray)
        {
            Coordinates[0] = (byte) ((startY >> 5) & 0xFF);
            Coordinates[1] = (byte) (((startY << 3) + (endY >> 10)) & 0xFF);
            Coordinates[2] = (byte) ((endY >> 2) & 0xFF);
            Coordinates[3] = (byte)(((endY << 6) + (startX >> 8)) & 0xFF);
            Coordinates[4] = (byte) startX;
            /*StartY = startY;
            EndY = endY;
            StartX = startX;*/
            Gray = gray;
        }

        public override string ToString()
        {
            return $"{nameof(Gray)}: {Gray}, {nameof(StartY)}: {StartY}, {nameof(EndY)}: {EndY}, {nameof(StartX)}: {StartX}, {nameof(Length)}: {Length}";
        }
    }

    public sealed class PageBreak
    {
        public static byte[] Bytes => new byte[] {0x0D, 0x0A};

        [FieldOrder(0)] public byte Line { get; set; } = 0x0D;
        [FieldOrder(1)] public byte Break { get; set; } = 0x0A;
    }

    #endregion

    #region Footer
    public sealed class Footer
    {
        /// <summary>
        /// Gets the size of the header
        /// </summary>
        [FieldOrder(0)]
        [FieldEndianness(Endianness.Big)]
        public uint FooterSize { get; set; } = MAGIC_SIZE;

        /// <summary>
        /// Gets the header name
        /// </summary>
        [FieldOrder(1)]
        [FieldLength(MAGIC_SIZE)]
        [SerializeAs(SerializedType.TerminatedString)]
        public string FooterValue { get; set; } = MAGIC_VALUE;

        /*[FieldOrder(2)]
        [FieldEndianness(Endianness.Big)]
        public uint CheckSum { get; set; }*/

        public void Validate()
        {
            /*if (FooterSize != HEADER_SIZE || FooterValue != HEADER_VALUE)
            {
                throw new FileLoadException($"Not a valid CXDLP file! Incorrect footer data.\n{ToString()}");
            }*/

            if (FooterSize-1 != FooterValue.Length || !FooterValue.StartsWith(HEADER_VALUE_GENERIC))
            {
                throw new FileLoadException($"Invalid footer data for CXDLP file.\n{ToString()}");
            }
        }

        public override string ToString()
        {
            return $"{nameof(FooterSize)}: {FooterSize}, {nameof(FooterValue)}: {FooterValue}";
        }
    }
    #endregion

    #endregion

    #region Properties

    public Header HeaderSettings { get; private set; } = new();
    public SlicerInfo SlicerInfoSettings { get; private set; } = new();
    public SlicerInfoV3 SlicerInfoV3Settings { get; private set; } = new();
    public Footer FooterSettings { get; private set; } = new();

    public override FileFormatType FileType => FileFormatType.Binary;

    public override string ConvertMenuGroup => "Creality CXDLP";

    public override FileExtension[] FileExtensions { get; } = {
        new(typeof(CrealityCXDLPFile), "cxdlp", "Creality CXDLP"),
    };

    public override SpeedUnit FormatSpeedUnit => SpeedUnit.MillimetersPerSecond;

    public override PrintParameterModifier[] PrintParameterModifiers { get; } =
    {
        PrintParameterModifier.BottomLayerCount,

        PrintParameterModifier.WaitTimeBeforeCure,

        PrintParameterModifier.BottomExposureTime,
        PrintParameterModifier.ExposureTime,

        PrintParameterModifier.BottomLiftHeight,
        PrintParameterModifier.BottomLiftSpeed,
        PrintParameterModifier.LiftHeight,
        PrintParameterModifier.LiftSpeed,
        PrintParameterModifier.RetractSpeed,

        PrintParameterModifier.BottomLightPWM,
        PrintParameterModifier.LightPWM,
    };

    public override Size[] ThumbnailsOriginalSize { get; } =
    {
        new(116, 116),
        new(290, 290),
        new(290, 290)
    };

    public override uint[] AvailableVersions { get; } = { 2, 3 };

    public override uint DefaultVersion => DEFAULT_VERSION;

    public override uint Version
    {
        get => HeaderSettings.Version;
        set
        {
            base.Version = value;
            HeaderSettings.Version = (ushort)base.Version;
        }
    }

    public override uint ResolutionX
    {
        get => HeaderSettings.ResolutionX;
        set => base.ResolutionX = HeaderSettings.ResolutionX = (ushort) value;
    }

    public override uint ResolutionY
    {
        get => HeaderSettings.ResolutionY;
        set => base.ResolutionY = HeaderSettings.ResolutionY = (ushort)value;
    }

    public override float DisplayWidth
    {
        get => (float)Math.Round(float.Parse(SlicerInfoSettings.DisplayWidth, CultureInfo.InvariantCulture), 2);
        set
        {
            value = (float)Math.Round(value, 2);
            SlicerInfoSettings.DisplayWidth = value.ToString(CultureInfo.InvariantCulture);
            base.DisplayWidth = value;
        }
    }

    public override float DisplayHeight
    {
        get => (float)Math.Round(float.Parse(SlicerInfoSettings.DisplayHeight, CultureInfo.InvariantCulture), 3);
        set
        {
            value = (float)Math.Round(value, 2);
            SlicerInfoSettings.DisplayHeight = value.ToString(CultureInfo.InvariantCulture);
            base.DisplayHeight = value;
        }
    }

    public override float LayerHeight
    {
        get => Layer.RoundHeight(float.Parse(SlicerInfoSettings.LayerHeight, CultureInfo.InvariantCulture));
        set
        {
            SlicerInfoSettings.LayerHeight = Layer.RoundHeight(value).ToString(CultureInfo.InvariantCulture);
            base.LayerHeight = value;
        }
    }

    public override uint LayerCount
    {
        get => base.LayerCount;
        set => base.LayerCount = HeaderSettings.LayerCount = (ushort) base.LayerCount;
    }

    public override ushort BottomLayerCount
    {
        get => SlicerInfoSettings.BottomLayersCount;
        set => base.BottomLayerCount = SlicerInfoSettings.BottomLayersCount = value;
    }

    /*public override float BottomLightOffDelay => SlicerInfoSettings.WaitTimeBeforeCure;

    public override float LightOffDelay
    {
        get => SlicerInfoSettings.WaitTimeBeforeCure;
        set => base.LightOffDelay = SlicerInfoSettings.WaitTimeBeforeCure = (ushort)value;
    }*/

    public override float BottomWaitTimeBeforeCure => WaitTimeBeforeCure;

    public override float WaitTimeBeforeCure
    {
        get => SlicerInfoSettings.WaitTimeBeforeCure;
        set => base.WaitTimeBeforeCure = SlicerInfoSettings.WaitTimeBeforeCure = (ushort)Math.Max(1, value);
    }

    public override float BottomExposureTime
    {
        get => SlicerInfoSettings.BottomExposureTime;
        set => base.BottomExposureTime = SlicerInfoSettings.BottomExposureTime = (ushort) value;
    }

    public override float ExposureTime
    {
        get => (float)Math.Round(SlicerInfoSettings.ExposureTime / 10.0f, 1);
        set
        {
            value = (float)Math.Round(value, 1);
            SlicerInfoSettings.ExposureTime = (ushort) (value * 10);
            base.ExposureTime = value;
        }
    }

    public override float BottomLiftHeight
    {
        get => SlicerInfoSettings.BottomLiftHeight;
        set => base.BottomLiftHeight = SlicerInfoSettings.BottomLiftHeight = (ushort) value;
    }

    public override float BottomLiftSpeed
    {
        get => SpeedConverter.Convert(SlicerInfoSettings.BottomLiftSpeed, FormatSpeedUnit, CoreSpeedUnit);
        set => base.BottomLiftSpeed = SlicerInfoSettings.BottomLiftSpeed = SlicerInfoSettings.BottomLiftSpeed = (ushort)SpeedConverter.Convert(value, CoreSpeedUnit, FormatSpeedUnit);
    }

    public override float LiftHeight
    {
        get => SlicerInfoSettings.LiftHeight;
        set => base.LiftHeight = SlicerInfoSettings.LiftHeight = (ushort)value;
    }

    public override float LiftSpeed
    {
        get => SpeedConverter.Convert(SlicerInfoSettings.LiftSpeed, FormatSpeedUnit, CoreSpeedUnit);
        set => base.LiftSpeed = SlicerInfoSettings.LiftSpeed = (ushort)SpeedConverter.Convert(value, CoreSpeedUnit, FormatSpeedUnit);
    }

    public override float BottomRetractSpeed => RetractSpeed;

    public override float RetractSpeed
    {
        get => SpeedConverter.Convert(SlicerInfoSettings.RetractSpeed, FormatSpeedUnit, CoreSpeedUnit);
        set => base.RetractSpeed = SlicerInfoSettings.RetractSpeed = (ushort)SpeedConverter.Convert(value, CoreSpeedUnit, FormatSpeedUnit);
    }

    public override byte BottomLightPWM
    {
        get => (byte) SlicerInfoSettings.BottomLightPWM;
        set => base.BottomLightPWM = (byte) (SlicerInfoSettings.BottomLightPWM = value);
    }

    public override byte LightPWM
    {
        get => (byte)SlicerInfoSettings.LightPWM;
        set => base.LightPWM = (byte) (SlicerInfoSettings.LightPWM = value);
    }

    public override string MachineName
    {
        get => HeaderSettings.PrinterModel.ValueNotNull;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && !value.StartsWith("CL-") && !value.StartsWith("CT-"))
            {
                // Parse from machine name, if coming from PrusaSlicer this will help
                var match = Regex.Match(value, @"(CL|CT)-?[0-9]+[a-zA-Z]?");
                if (match is {Success: true, Groups.Count: > 1})
                {
                    value = match.Value;
                }
            }
            base.MachineName = HeaderSettings.PrinterModel.Value = value;
        }
    }

    public override string? MaterialName
    {
        get => SlicerInfoV3Settings.MaterialName.Value;
        set => base.MaterialName = SlicerInfoV3Settings.MaterialName.Value = value;
    }

    public override object[] Configs => new object[] { HeaderSettings, SlicerInfoSettings, SlicerInfoV3Settings, FooterSettings };

    #endregion

    #region Constructors
    #endregion

    #region Methods

    private void SanitizeProperties()
    {
        SlicerInfoSettings.WaitTimeBeforeCure = (ushort)Math.Max(1, WaitTimeBeforeCure);
    }

    protected override void EncodeInternally(OperationProgress progress)
    {
        using var outputFile = new FileStream(TemporaryOutputFileFullPath, FileMode.Create, FileAccess.ReadWrite);

       if (string.IsNullOrWhiteSpace(MachineName) || (!MachineName.StartsWith("CL") && !MachineName.StartsWith("CT")))
        {
            bool found = false;
            foreach (var machine in Machine.Machines
                         .Where(machine => machine.Brand == PrinterBrand.Creality
                                           && (machine.Model.StartsWith("CL") || machine.Model.StartsWith("CT"))
                                           ))
            {
                if (ResolutionX == machine.ResolutionX && ResolutionY == machine.ResolutionY)
                {
                    found = true;
                    MachineName = machine.Model;
                    break;
                }
            }
            
            if(!found) throw new InvalidDataException("Unable to detect the printer model from resolution, check if resolution is well defined on slicer for your printer model.");
        }

        SanitizeProperties();

        var pageBreak = PageBreak.Bytes;

        outputFile.WriteSerialize(HeaderSettings);

        var previews = new byte[ThumbnailCountFileShouldHave][];

        // Previews
        Parallel.For(0, previews.Length, CoreSettings.GetParallelOptions(progress), previewIndex =>
        {
            progress.PauseIfRequested();
            var encodeLength = ThumbnailsOriginalSize[previewIndex].Area() * 2;
            
            previews[previewIndex] = EncodeImage(DATATYPE_RGB565_BE, Thumbnails[previewIndex]);

            if (encodeLength != previews[previewIndex].Length)
            {
                throw new FileLoadException($"Preview encode incomplete encode, expected: {previews[previewIndex].Length}, encoded: {encodeLength}");
            }
        });

        for (int i = 0; i < previews.Length; i++)
        {
            outputFile.WriteSerialize(previews[i]);
            outputFile.WriteBytes(pageBreak);
            previews[i] = null!;
        }
        outputFile.WriteSerialize(SlicerInfoSettings);

        progress.Reset(OperationProgress.StatusEncodeLayers, LayerCount);
        //var preLayers = new PreLayer[LayerCount];
        //var layerDefs = new LayerDef[LayerCount];
        //var layersStreams = new MemoryStream[LayerCount];


        var layerAreaPosition = outputFile.Position;
        outputFile.Seek(4 * LayerCount, SeekOrigin.Current);
        outputFile.WriteBytes(pageBreak);

        if (HeaderSettings.Version >= 3)
        {
            outputFile.WriteSerialize(SlicerInfoV3Settings);
        }

        var layerLargestContourArea = new uint[LayerCount];
        var layerBytes = new List<byte>[LayerCount];
        var pixelArea = PixelArea;
        foreach (var batch in BatchLayersIndexes())
        {
            Parallel.ForEach(batch, CoreSettings.GetParallelOptions(progress), layerIndex =>
            {
                progress.PauseIfRequested();
                var layer = this[layerIndex];
                using (var mat = layer.LayerMat)
                {
                    using var matRoi = mat.Roi(layer.BoundingRectangle);
                    using var contours = matRoi.FindContours(RetrType.External);
                    layerLargestContourArea[layerIndex] = (uint)(EmguContours.GetLargestContourArea(contours) * pixelArea * 1000);
                    //Debug.WriteLine($"Area: {contourArea} ({contourArea * PixelArea * 1000})  BR: {max.Bounds.Area()} ({max.Bounds.Area() * PixelArea * 1000})");

                    var span = mat.GetDataByteReadOnlySpan();

                    layerBytes[layerIndex] = new();

                    uint lineCount = 0;

                    for (int x = layer.BoundingRectangle.X; x < layer.BoundingRectangle.Right; x++)
                    {
                        int y = layer.BoundingRectangle.Y;
                        int startY = -1;
                        byte lastColor = 0;
                        for (; y < layer.BoundingRectangle.Bottom; y++)
                        {
                            int pos = mat.GetPixelPos(x, y);
                            byte color = span[pos];

                            if (lastColor == color && color != 0) continue;

                            if (startY >= 0)
                            {
                                layerBytes[layerIndex].AddRange(LayerLine.GetBytes((ushort)startY, (ushort)(y - 1),
                                    (ushort)x, lastColor));
                                lineCount++;
                            }

                            startY = color == 0 ? -1 : y;

                            lastColor = color;
                        }

                        if (startY >= 0)
                        {
                            layerBytes[layerIndex].AddRange(LayerLine.GetBytes((ushort)startY, (ushort)(y - 1),
                                (ushort)x, lastColor));
                            lineCount++;
                        }
                    }

                    layerBytes[layerIndex].InsertRange(0, LayerDef.GetHeaderBytes(layerLargestContourArea[layerIndex], lineCount));
                    layerBytes[layerIndex].AddRange(pageBreak);
                }

                progress.LockAndIncrement();
            });

            foreach (var layerIndex in batch)
            {
                outputFile.WriteBytes(layerBytes[layerIndex].ToArray());
                layerBytes[layerIndex] = null!;
            }
        }

        // Write layer largest contour area (mm^2 * 1000)
        outputFile.Seek(layerAreaPosition, SeekOrigin.Begin);
        foreach (var area in layerLargestContourArea)
        {
            outputFile.WriteUIntBigEndian(area);
        }

        outputFile.Seek(0, SeekOrigin.End);
        outputFile.WriteSerialize(FooterSettings);

        progress.Reset("Calculating checksum");
        uint checkSum = CalculateCheckSum(outputFile, false);

        outputFile.WriteUIntBigEndian(checkSum);

        Debug.WriteLine("Encode Results:");
        Debug.WriteLine(HeaderSettings);
        Debug.WriteLine("-End-");
    }

    protected override void DecodeInternally(OperationProgress progress)
    {
        using var inputFile = new FileStream(FileFullPath!, FileMode.Open, FileAccess.Read);

        inputFile.Seek(0, SeekOrigin.Begin);

        HeaderSettings = Helpers.Deserialize<Header>(inputFile);
        Debug.WriteLine(HeaderSettings);
        HeaderSettings.Validate();

        var position = inputFile.Position;

        progress.Reset("Validating checksum");
        var expectedCheckSum = CalculateCheckSum(inputFile, false, -4);
        uint checkSum;
        if (HeaderSettings.Version <= 2)
        {
            inputFile.Seek(3, SeekOrigin.Current);
            checkSum = (uint) inputFile.ReadByte();
        }
        else
        {
            checkSum = inputFile.ReadUIntBigEndian();
        }

        if (expectedCheckSum != checkSum)
        {
            throw new FileLoadException($"Checksum fails, expecting: {expectedCheckSum} but got: {checkSum}.\n" +
                                        $"Try to reslice the file.", FileFullPath);
        }

        inputFile.Seek(position, SeekOrigin.Begin);
        for (int i = 0; i < ThumbnailCountFileShouldHave; i++)
        {
            progress.PauseOrCancelIfRequested();
            var bytes = inputFile.ReadBytes(ThumbnailsOriginalSize[i].Area() * 2);
            inputFile.Seek(2, SeekOrigin.Current);
            Thumbnails.Add(DecodeImage(DATATYPE_RGB565_BE, bytes, ThumbnailsOriginalSize[i]));
        }

        SlicerInfoSettings = Helpers.Deserialize<SlicerInfo>(inputFile);
        Debug.WriteLine(SlicerInfoSettings);

        Init(HeaderSettings.LayerCount, DecodeType == FileDecodeType.Partial);
        inputFile.Seek(LayerCount * 4 + 2, SeekOrigin.Current); // Skip pre layers

        if (HeaderSettings.Version >= 3) // New informative header v3
        {
            SlicerInfoV3Settings = Helpers.Deserialize<SlicerInfoV3>(inputFile);
            Debug.WriteLine(SlicerInfoV3Settings);
        }


        if (DecodeType == FileDecodeType.Full)
        {
            progress.Reset(OperationProgress.StatusDecodeLayers, LayerCount);

            var linesBytes = new byte[LayerCount][];
            foreach (var batch in BatchLayersIndexes())
            {
                foreach (var layerIndex in batch)
                {
                    progress.PauseOrCancelIfRequested();

                    inputFile.Seek(4, SeekOrigin.Current);
                    var lineCount = BitExtensions.ToUIntBigEndian(inputFile.ReadBytes(4));

                    linesBytes[layerIndex] = new byte[lineCount * 6];
                    inputFile.ReadBytes(linesBytes[layerIndex]);
                    inputFile.Seek(2, SeekOrigin.Current);
                }

                Parallel.ForEach(batch, CoreSettings.GetParallelOptions(progress), layerIndex =>
                {
                    progress.PauseIfRequested();
                    using (var mat = EmguExtensions.InitMat(Resolution))
                    {

                        for (int i = 0; i < linesBytes[layerIndex].Length; i++)
                        {
                            LayerLine line = new()
                            {
                                Coordinates =
                                {
                                    [0] = linesBytes[layerIndex][i++],
                                    [1] = linesBytes[layerIndex][i++],
                                    [2] = linesBytes[layerIndex][i++],
                                    [3] = linesBytes[layerIndex][i++],
                                    [4] = linesBytes[layerIndex][i++]
                                },
                                Gray = linesBytes[layerIndex][i]
                            };

                            CvInvoke.Line(mat, new Point(line.StartX, line.StartY),
                                new Point(line.StartX, line.EndY),
                                new MCvScalar(line.Gray));
                        }

                        linesBytes[layerIndex] = null!;

                       _layers[layerIndex] = new Layer((uint)layerIndex, mat, this);
                    }

                    progress.LockAndIncrement();
                });
            }
        }
        else // Partial read
        {
            inputFile.Seek(-Helpers.Serializer.SizeOf(FooterSettings), SeekOrigin.End);
        }

        FooterSettings = Helpers.Deserialize<Footer>(inputFile);
        FooterSettings.Validate();
    }

    protected override void PartialSaveInternally(OperationProgress progress)
    {
        var offset = Helpers.Serializer.SizeOf(HeaderSettings);
        foreach (var size in ThumbnailsOriginalSize)
        {
            offset += size.Area() * 2 + 2; // + page break
        }

        SanitizeProperties();
            
        using var outputFile = new FileStream(TemporaryOutputFileFullPath, FileMode.Open, FileAccess.ReadWrite);
        outputFile.Seek(offset, SeekOrigin.Begin);
        outputFile.WriteSerialize(SlicerInfoSettings);

        if (HeaderSettings.Version >= 3)
        {
            outputFile.Seek(LayerCount * 4 + 2, SeekOrigin.Current); // Skip pre layers
            outputFile.WriteSerialize(SlicerInfoV3Settings);
        }

        progress.Reset("Calculating checksum");
        uint checkSum = CalculateCheckSum(outputFile, false, -4);
        outputFile.WriteUIntBigEndian(checkSum);
    }

    private uint CalculateCheckSum(FileStream fs, bool restorePosition = true, int offsetSize = 0)
    {
        uint checkSum = 0;
        var position = fs.Position;
        var dataSize = fs.Length + offsetSize;
        const int bufferSize = 50 * 1024 * 1024;

        fs.Seek(0, SeekOrigin.Begin);

        if (HeaderSettings.Version >= 3)
        {
            // https://github.com/dotnet/runtime/blob/main/src/libraries/System.IO.Hashing/src/System/IO/Hashing/Crc32.Table.cs
            var table = new uint[256];

            for (uint i = 0; i < 256; i++)
            {
                uint val = i;

                for (int j = 0; j < 8; j++)
                {
                    if ((val & 0b0000_0001) == 0)
                    {
                        val >>= 1;
                    }
                    else
                    {
                        val = (val >> 1) ^ 0xEDB88320u;
                    }
                }

                table[i] = val;
            }

            for (
                int chunkSize = (int)Math.Min(bufferSize, dataSize - fs.Position);
                chunkSize > 0;
                chunkSize = (int)Math.Min(chunkSize, dataSize - fs.Position))
            {
                var bytes = fs.ReadBytes(chunkSize);
                for (int i = 0; i < bytes.Length; i++)
                {
                    // https://github.com/dotnet/runtime/blob/main/src/libraries/System.IO.Hashing/src/System/IO/Hashing/Crc32.cs
                    byte idx = (byte)checkSum;
                    idx ^= bytes[i];
                    checkSum = table[idx] ^ (checkSum >> 8);
                }
            }
        }
        else
        {
            for (
                int chunkSize = (int)Math.Min(bufferSize, dataSize - fs.Position);
                chunkSize > 0;
                chunkSize = (int)Math.Min(chunkSize, dataSize - fs.Position))
            {
                var bytes = fs.ReadBytes(chunkSize);
                for (int i = 0; i < bytes.Length; i++)
                {
                    checkSum ^= bytes[i];
                }
            }
        }
            

        if (restorePosition) fs.Seek(position, SeekOrigin.Begin);

        return checkSum;
    }

    #endregion
}