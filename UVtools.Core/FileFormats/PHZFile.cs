﻿ /*
 *                     GNU AFFERO GENERAL PUBLIC LICENSE
 *                       Version 3, 19 November 2007
 *  Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
 *  Everyone is permitted to copy and distribute verbatim copies
 *  of this license document, but changing it is not allowed.
 */

// https://github.com/cbiffle/catibo/blob/master/doc/cbddlp-ctb.adoc

using BinarySerialization;
using Emgu.CV;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UVtools.Core.Extensions;
using UVtools.Core.Layers;
using UVtools.Core.Operations;
using ZLinq;

namespace UVtools.Core.FileFormats;

public sealed class PHZFile : FileFormat
{
    #region Constants
    private const uint MAGIC_PHZ = 0x9FDA83AE;
    #endregion

    #region Sub Classes
    #region Header
    public class Header
    {
        private string _machineName = DefaultMachineName;

        /// <summary>
        /// Gets a magic number identifying the file type.
        /// 0x12fd_0019 for cbddlp
        /// 0x12fd_0086 for ctb
        /// 0x9FDA83AE for phz
        /// </summary>
        [FieldOrder(0)] public uint Magic { get; set; } = MAGIC_PHZ;

        /// <summary>
        /// Gets the software version
        /// </summary>
        [FieldOrder(1)] public uint Version { get; set; } = 2;

        /// <summary>
        /// Gets the layer height setting used at slicing, in millimeters. Actual height used by the machine is in the layer table.
        /// </summary>
        [FieldOrder(2)] public float LayerHeightMilimeter { get; set; }

        /// <summary>
        /// Gets the exposure time setting used at slicing, in seconds, for normal (non-bottom) layers, respectively. Actual time used by the machine is in the layer table.
        /// </summary>
        [FieldOrder(3)] public float LayerExposureSeconds { get; set; }

        /// <summary>
        /// Gets the exposure time setting used at slicing, in seconds, for bottom layers. Actual time used by the machine is in the layer table.
        /// </summary>
        [FieldOrder(4)] public float BottomExposureSeconds { get; set; }

        /// <summary>
        /// Gets number of layers configured as "bottom." Note that this field appears in both the file header and ExtConfig..
        /// </summary>
        [FieldOrder(5)] public uint BottomLayersCount { get; set; } = 10;

        /// <summary>
        /// Gets the printer resolution along X axis, in pixels. This information is critical to correctly decoding layer images.
        /// </summary>
        [FieldOrder(6)] public uint ResolutionX { get; set; }

        /// <summary>
        /// Gets the printer resolution along Y axis, in pixels. This information is critical to correctly decoding layer images.
        /// </summary>
        [FieldOrder(7)] public uint ResolutionY { get; set; }

        /// <summary>
        /// Gets the file offsets of ImageHeader records describing the larger preview images.
        /// </summary>
        [FieldOrder(8)] public uint PreviewLargeOffsetAddress { get; set; }

        /// <summary>
        /// Gets the file offset of a table of LayerHeader records giving parameters for each printed layer.
        /// </summary>
        [FieldOrder(9)] public uint LayersDefinitionOffsetAddress { get; set; }

        /// <summary>
        /// Gets the number of records in the layer table for the first level set. In ctb files, that’s equivalent to the total number of records, but records may be multiplied in antialiased cbddlp files.
        /// </summary>
        [FieldOrder(10)] public uint LayerCount { get; set; }

        /// <summary>
        /// Gets the file offsets of ImageHeader records describing the smaller preview images.
        /// </summary>
        [FieldOrder(11)] public uint PreviewSmallOffsetAddress { get; set; }

        /// <summary>
        /// Gets the estimated duration of print, in seconds.
        /// </summary>
        [FieldOrder(12)] public uint PrintTime { get; set; }

        /// <summary>
        /// Gets the records whether this file was generated assuming normal (0) or mirrored (1) image projection. LCD printers are "mirrored" for this purpose.
        /// </summary>
        [FieldOrder(13)] public uint ProjectorType { get; set; }

        /// <summary>
        /// Gets the number of times each layer image is repeated in the file.
        /// This is used to implement antialiasing in cbddlp files. When greater than 1,
        /// the layer table will actually contain layer_table_count * level_set_count entries.
        /// See the section on antialiasing for details.
        /// </summary>
        [FieldOrder(14)] public uint AntiAliasLevel { get; set; } = 1;

        /// <summary>
        /// Gets the PWM duty cycle for the UV illumination source on normal levels, respectively.
        /// This appears to be an 8-bit quantity where 0xFF is fully on and 0x00 is fully off.
        /// </summary>
        [FieldOrder(15)] public ushort LightPWM { get; set; } = 255;

        /// <summary>
        /// Gets the PWM duty cycle for the UV illumination source on bottom levels, respectively.
        /// This appears to be an 8-bit quantity where 0xFF is fully on and 0x00 is fully off.
        /// </summary>
        [FieldOrder(16)] public ushort BottomLightPWM { get; set; } = 255;

        [FieldOrder(17)] public uint Padding1 { get; set; }
        [FieldOrder(18)] public uint Padding2 { get; set; }

        /// <summary>
        /// Gets the height of the model described by this file, in millimeters.
        /// </summary>
        [FieldOrder(19)] public float OverallHeightMilimeter { get; set; }

        /// <summary>
        /// Gets dimensions of the printer’s X output volume, in millimeters.
        /// </summary>
        [FieldOrder(20)]  public float BedSizeX { get; set; }

        /// <summary>
        /// Gets dimensions of the printer’s Y output volume, in millimeters.
        /// </summary>
        [FieldOrder(21)]  public float BedSizeY { get; set; }

        /// <summary>
        /// Gets dimensions of the printer’s Z output volume, in millimeters.
        /// </summary>
        [FieldOrder(22)]  public float BedSizeZ { get; set; }

        /// <summary>
        /// Gets the key used to encrypt layer data, or 0 if encryption is not used.
        /// </summary>
        [FieldOrder(23)] public uint EncryptionKey { get; set; }

        /// <summary>
        /// Gets the light off time setting used at slicing, for bottom layers, in seconds. Actual time used by the machine is in the layer table. Note that light_off_time_s appears in both the file header and ExtConfig.
        /// </summary>
        [FieldOrder(24)] public float BottomLightOffDelay { get; set; } = 1;

        /// <summary>
        /// Gets the light off time setting used at slicing, for normal layers, in seconds. Actual time used by the machine is in the layer table. Note that light_off_time_s appears in both the file header and ExtConfig.
        /// </summary>
        [FieldOrder(25)] public float LightOffDelay     { get; set; } = 1;

        /// <summary>
        /// Gets number of layers configured as "bottom." Note that this field appears in both the file header and ExtConfig.
        /// </summary>
        [FieldOrder(26)] public uint BottomLayersCount2 { get; set; } = 10;

        [FieldOrder(27)] public uint Padding3 { get; set; }

        /// <summary>
        /// Gets the distance to lift the build platform away from the vat after bottom layers, in millimeters.
        /// </summary>
        [FieldOrder(28)] public float BottomLiftHeight { get; set; } = 5;

        /// <summary>
        /// Gets the speed at which to lift the build platform away from the vat after bottom layers, in millimeters per minute.
        /// </summary>
        [FieldOrder(29)] public float BottomLiftSpeed { get; set; } = 300;

        /// <summary>
        /// Gets the distance to lift the build platform away from the vat after normal layers, in millimeters.
        /// </summary>
        [FieldOrder(30)] public float LiftHeight { get; set; } = 5;

        /// <summary>
        /// Gets the speed at which to lift the build platform away from the vat after normal layers, in millimeters per minute.
        /// </summary>
        [FieldOrder(31)] public float LiftSpeed { get; set; } = 300;

        /// <summary>
        /// Gets the speed to use when the build platform re-approaches the vat after lift, in millimeters per minute.
        /// </summary>
        [FieldOrder(32)] public float RetractSpeed { get; set; } = 300;

        /// <summary>
        /// Gets the estimated required resin, measured in milliliters. The volume number is derived from the model.
        /// </summary>
        [FieldOrder(33)] public float VolumeMl { get; set; }

        /// <summary>
        /// Gets the estimated grams, derived from volume using configured factors for density.
        /// </summary>
        [FieldOrder(34)] public float WeightG { get; set; }

        /// <summary>
        /// Gets the estimated cost based on currency unit the user had configured. Derived from volume using configured factors for density and cost.
        /// </summary>
        [FieldOrder(35)] public float CostDollars { get; set; }

        [FieldOrder(36)] public uint Padding4 { get; set; }

        /// <summary>
        /// Gets the machine name offset to a string naming the machine type, and its length in bytes.
        /// </summary>
        [FieldOrder(37)] public uint MachineNameAddress { get; set; }

        /// <summary>
        /// Gets the machine size in bytes
        /// </summary>
        [FieldOrder(38)] public uint MachineNameSize { get; set; } = (uint)(string.IsNullOrEmpty(DefaultMachineName) ? 0 : DefaultMachineName.Length);

        /// <summary>
        /// Gets the machine name. string is not nul-terminated.
        /// The character encoding is currently unknown — all observed files in the wild use 7-bit ASCII characters only.
        /// Note that the machine type here is set in the software profile, and is not the name the user assigned to the machine.
        /// </summary>
        [Ignore]
        public string MachineName
        {
            get => _machineName;
            set
            {
                if (string.IsNullOrEmpty(value)) value = DefaultMachineName;
                _machineName = value;
                MachineNameSize = string.IsNullOrEmpty(_machineName) ? 0 : (uint)_machineName.Length;
            }
        }

        [FieldOrder(39)] public uint Padding5 { get; set; }
        [FieldOrder(40)] public uint Padding6 { get; set; }
        [FieldOrder(41)] public uint Padding7 { get; set; }
        [FieldOrder(42)] public uint Padding8 { get; set; }
        [FieldOrder(43)] public uint Padding9 { get; set; }
        [FieldOrder(44)] public uint Padding10 { get; set; }

        /// <summary>
        /// Gets the parameter used to control encryption.
        /// Not totally understood. 0 for cbddlp files, 0xF for ctb files, 0x1c (28) for phz
        /// </summary>
        [FieldOrder(45)] public uint EncryptionMode { get; set; } = 28;

        /// <summary>
        /// Gets the minutes since Jan 1, 1970 UTC
        /// </summary>
        [FieldOrder(46)] public uint ModifiedTimestampMinutes { get; set; } = (uint)DateTimeExtensions.Timestamp.TotalMinutes;

        [Ignore] public string ModifiedDate => DateTimeExtensions.GetDateTimeFromTimestampMinutes(ModifiedTimestampMinutes).ToString("dd/MM/yyyy HH:mm");

        [FieldOrder(47)] public uint AntiAliasLevelInfo { get; set; }

        [FieldOrder(48)] public uint SoftwareVersion { get; set; } = 0x01060300;

        [FieldOrder(49)] public uint Padding11 { get; set; }
        [FieldOrder(50)] public uint Padding12 { get; set; }
        [FieldOrder(51)] public uint Padding13 { get; set; }
        [FieldOrder(52)] public uint Padding14 { get; set; }
        [FieldOrder(53)] public uint Padding15 { get; set; }
        [FieldOrder(54)] public uint Padding16{ get; set; }

        public override string ToString()
        {
            return $"{nameof(Magic)}: {Magic}, {nameof(Version)}: {Version}, {nameof(LayerHeightMilimeter)}: {LayerHeightMilimeter}, {nameof(LayerExposureSeconds)}: {LayerExposureSeconds}, {nameof(BottomExposureSeconds)}: {BottomExposureSeconds}, {nameof(BottomLayersCount)}: {BottomLayersCount}, {nameof(ResolutionX)}: {ResolutionX}, {nameof(ResolutionY)}: {ResolutionY}, {nameof(PreviewLargeOffsetAddress)}: {PreviewLargeOffsetAddress}, {nameof(LayersDefinitionOffsetAddress)}: {LayersDefinitionOffsetAddress}, {nameof(LayerCount)}: {LayerCount}, {nameof(PreviewSmallOffsetAddress)}: {PreviewSmallOffsetAddress}, {nameof(PrintTime)}: {PrintTime}, {nameof(ProjectorType)}: {ProjectorType}, {nameof(AntiAliasLevel)}: {AntiAliasLevel}, {nameof(LightPWM)}: {LightPWM}, {nameof(BottomLightPWM)}: {BottomLightPWM}, {nameof(Padding1)}: {Padding1}, {nameof(Padding2)}: {Padding2}, {nameof(OverallHeightMilimeter)}: {OverallHeightMilimeter}, {nameof(BedSizeX)}: {BedSizeX}, {nameof(BedSizeY)}: {BedSizeY}, {nameof(BedSizeZ)}: {BedSizeZ}, {nameof(EncryptionKey)}: {EncryptionKey}, {nameof(BottomLightOffDelay)}: {BottomLightOffDelay}, {nameof(LightOffDelay)}: {LightOffDelay}, {nameof(BottomLayersCount2)}: {BottomLayersCount2}, {nameof(Padding3)}: {Padding3}, {nameof(BottomLiftHeight)}: {BottomLiftHeight}, {nameof(BottomLiftSpeed)}: {BottomLiftSpeed}, {nameof(LiftHeight)}: {LiftHeight}, {nameof(LiftSpeed)}: {LiftSpeed}, {nameof(RetractSpeed)}: {RetractSpeed}, {nameof(VolumeMl)}: {VolumeMl}, {nameof(WeightG)}: {WeightG}, {nameof(CostDollars)}: {CostDollars}, {nameof(Padding4)}: {Padding4}, {nameof(MachineNameAddress)}: {MachineNameAddress}, {nameof(MachineNameSize)}: {MachineNameSize}, {nameof(MachineName)}: {MachineName}, {nameof(Padding5)}: {Padding5}, {nameof(Padding6)}: {Padding6}, {nameof(Padding7)}: {Padding7}, {nameof(Padding8)}: {Padding8}, {nameof(Padding9)}: {Padding9}, {nameof(Padding10)}: {Padding10}, {nameof(EncryptionMode)}: {EncryptionMode}, {nameof(ModifiedTimestampMinutes)}: {ModifiedTimestampMinutes}, {nameof(ModifiedDate)}: {ModifiedDate}, {nameof(AntiAliasLevelInfo)}: {AntiAliasLevelInfo}, {nameof(SoftwareVersion)}: {SoftwareVersion}, {nameof(Padding11)}: {Padding11}, {nameof(Padding12)}: {Padding12}, {nameof(Padding13)}: {Padding13}, {nameof(Padding14)}: {Padding14}, {nameof(Padding15)}: {Padding15}, {nameof(Padding16)}: {Padding16}";
        }
    }
    #endregion

    #region Preview
    /// <summary>
    /// The files contain two preview images.
    /// These are shown on the printer display when choosing which file to print, sparing the poor printer from needing to render a 3D image from scratch.
    /// </summary>
    public class Preview
    {
        /// <summary>
        /// Gets the X dimension of the preview image, in pixels.
        /// </summary>
        [FieldOrder(0)] public uint ResolutionX { get; set; }

        /// <summary>
        /// Gets the Y dimension of the preview image, in pixels.
        /// </summary>
        [FieldOrder(1)] public uint ResolutionY { get; set; }

        /// <summary>
        /// Gets the image offset of the encoded data blob.
        /// </summary>
        [FieldOrder(2)] public uint ImageOffset { get; set; }

        /// <summary>
        /// Gets the image length in bytes.
        /// </summary>
        [FieldOrder(3)] public uint ImageLength { get; set; }

        [FieldOrder(4)] public uint Unknown1    { get; set; }
        [FieldOrder(5)] public uint Unknown2    { get; set; }
        [FieldOrder(6)] public uint Unknown3    { get; set; }
        [FieldOrder(7)] public uint Unknown4    { get; set; }

        public override string ToString()
        {
            return $"{nameof(ResolutionX)}: {ResolutionX}, {nameof(ResolutionY)}: {ResolutionY}, {nameof(ImageOffset)}: {ImageOffset}, {nameof(ImageLength)}: {ImageLength}, {nameof(Unknown1)}: {Unknown1}, {nameof(Unknown2)}: {Unknown2}, {nameof(Unknown3)}: {Unknown3}, {nameof(Unknown4)}: {Unknown4}";
        }
    }

    #endregion

    #region Layer
    public class LayerDef
    {
        /// <summary>
        /// Gets the build platform Z position for this layer, measured in millimeters.
        /// </summary>
        [FieldOrder(0)] public float PositionZ      { get; set; }

        /// <summary>
        /// Gets the exposure time for this layer, in seconds.
        /// </summary>
        [FieldOrder(1)] public float ExposureTime       { get; set; }

        /// <summary>
        /// Gets how long to keep the light off after exposing this layer, in seconds.
        /// </summary>
        [FieldOrder(2)] public float LightOffDelay { get; set; }

        /// <summary>
        /// Gets the layer image offset to encoded layer data, and its length in bytes.
        /// </summary>
        [FieldOrder(3)] public uint DataAddress          { get; set; }

        /// <summary>
        /// Gets the layer image length in bytes.
        /// </summary>
        [FieldOrder(4)] public uint DataSize             { get; set; }
        [FieldOrder(5)] public uint PageNumber           { get; set; }
        [FieldOrder(6)] public uint Unknown2             { get; set; }
        [FieldOrder(7)] public uint Unknown3             { get; set; }
        [FieldOrder(8)] public uint Unknown4             { get; set; }

        [Ignore] public byte[] EncodedRle { get; set; } = null!;

        [Ignore] public PHZFile Parent { get; set; } = null!;

        public LayerDef()
        {
        }

        public LayerDef(PHZFile parent, Layer layer)
        {
            Parent = parent;
            SetFrom(layer);
        }

        public void SetFrom(Layer layer)
        {
            PositionZ = layer.PositionZ;
            ExposureTime = layer.ExposureTime;
            LightOffDelay = layer.LightOffDelay;
        }

        public void CopyTo(Layer layer)
        {
            layer.PositionZ = PositionZ;
            layer.ExposureTime = ExposureTime;
            layer.LightOffDelay = LightOffDelay;
        }

        public unsafe Mat Decode(uint layerIndex, bool consumeData = true)
        {
            var image = EmguExtensions.InitMat(Parent.Resolution);
            var span = image.GetBytePointer();

            if (Parent.HeaderSettings.EncryptionKey > 0)
            {
                LayerRleCryptBuffer(Parent.HeaderSettings.EncryptionKey, layerIndex, EncodedRle);
            }

            int limit = image.Width * image.Height;
            int index = 0;
            byte lastColor = 0;

            foreach (var code in EncodedRle)
            {
                if ((code & 0x80) == 0x80)
                {
                    //lastColor = (byte) (code << 1);
                    // // Convert from 7bpp to 8bpp (extending the last bit)
                    lastColor = (byte)(((code & 0x7f) << 1) | (code & 1));
                    if (lastColor >= 0xfc)
                    {
                        // Make 'white' actually white
                        lastColor = 0xff;

                    }

                    if (index < limit)
                    {
                        span[index] = lastColor;
                    }
                    else
                    {
                        image.Dispose();
                        throw new FileLoadException("Corrupted RLE data.");
                    }

                    index++;
                }
                else
                {
                    for (uint i = 0; i < code; i++)
                    {
                        if (index < limit)
                        {
                            span[index] = lastColor;
                        }
                        else
                        {
                            image.Dispose();
                            throw new FileLoadException("Corrupted RLE data.");
                        }
                        index++;
                    }
                }
            }

            if (consumeData)
                EncodedRle = null!;

            return image;
        }

        public void Encode(Mat image, uint layerIndex)
        {
            List<byte> rawData = [];

            //byte color = byte.MaxValue >> 1;
            byte color = byte.MaxValue;
            uint stride = 0;

            void AddRep()
            {
                rawData.Add((byte)(color | 0x80));
                stride--;
                int done = 0;
                while (done < stride)
                {
                    int todo = 0x7d;

                    if (stride - done < todo)
                    {
                        todo = (int)(stride - done);
                    }

                    rawData.Add((byte)(todo));

                    done += todo;
                }
            }

            int halfWidth = image.Width / 2;

            //int pixel = 0;
            for (int y = 0; y < image.Height; y++)
            {
                var span = image.GetRowByteSpan(y);
                for (int x = 0; x < span.Length; x++)
                {

                    var grey7 = (byte)((span[x] >> 1) & 0x7f);
                    if (grey7 > 0x7c)
                    {
                        grey7 = 0x7c;
                    }

                    if (color == byte.MaxValue)
                    {
                        color = grey7;
                        stride = 1;
                    }
                    else if (grey7 != color || x == halfWidth)
                    {
                        AddRep();
                        color = grey7;
                        stride = 1;
                    }
                    else
                    {
                        stride++;
                    }
                }

                AddRep();
                color = byte.MaxValue;
            }


            EncodedRle = Parent.HeaderSettings.EncryptionKey > 0
                ? LayerRleCrypt(Parent.HeaderSettings.EncryptionKey, layerIndex, rawData)
                : rawData.ToArray();

            DataSize = (uint) EncodedRle.Length;
        }

        public override string ToString()
        {
            return $"{nameof(PositionZ)}: {PositionZ}, {nameof(ExposureTime)}: {ExposureTime}, {nameof(LightOffDelay)}: {LightOffDelay}, {nameof(DataAddress)}: {DataAddress}, {nameof(DataSize)}: {DataSize}, {nameof(PageNumber)}: {PageNumber}, {nameof(Unknown2)}: {Unknown2}, {nameof(Unknown3)}: {Unknown3}, {nameof(Unknown4)}: {Unknown4}";
        }
    }
    #endregion

    #endregion

    #region Properties

    public Header HeaderSettings { get; private set; } = new();

    public Preview[] Previews { get; }

    public LayerDef[] LayersDefinitions { get; private set; } = null!;

    public override FileFormatType FileType => FileFormatType.Binary;

    public override string ConvertMenuGroup => "Chitubox";

    public override FileExtension[] FileExtensions { get; } =
    [
        new (typeof(PHZFile), "phz", "Chitubox PHZ")
    ];

    public override PrintParameterModifier[] PrintParameterModifiers { get; } =
    [
        PrintParameterModifier.BottomLayerCount,

        PrintParameterModifier.BottomLightOffDelay,
        PrintParameterModifier.LightOffDelay,

        PrintParameterModifier.BottomExposureTime,
        PrintParameterModifier.ExposureTime,

        PrintParameterModifier.BottomLiftHeight,
        PrintParameterModifier.BottomLiftSpeed,
        PrintParameterModifier.LiftHeight,
        PrintParameterModifier.LiftSpeed,
        PrintParameterModifier.RetractSpeed,


        PrintParameterModifier.BottomLightPWM,
        PrintParameterModifier.LightPWM
    ];

    /*public override PrintParameterModifier[] PrintParameterPerLayerModifiers { get; } = {
        PrintParameterModifier.ExposureSeconds,
        PrintParameterModifier.LightOffDelay,
    };*/

    public override Size[] ThumbnailsOriginalSize { get; } =
    [
        new(400, 300),
        new(200, 125)
    ];

    public override uint[] AvailableVersions { get; } = [2];

    public override uint DefaultVersion => 2;

    public override uint Version
    {
        get => HeaderSettings.Version;
        set
        {
            base.Version = value;
            HeaderSettings.Version = base.Version;
        }
    }

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
        get => HeaderSettings.BedSizeX;
        set => base.DisplayWidth = HeaderSettings.BedSizeX = RoundDisplaySize(value);
    }


    public override float DisplayHeight
    {
        get => HeaderSettings.BedSizeY;
        set => base.DisplayHeight = HeaderSettings.BedSizeY = RoundDisplaySize(value);
    }

    public override float MachineZ
    {
        get => HeaderSettings.BedSizeZ > 0 ? HeaderSettings.BedSizeZ : base.MachineZ;
        set => base.MachineZ = HeaderSettings.BedSizeZ = MathF.Round(value, 2);
    }

    public override FlipDirection DisplayMirror
    {
        get => HeaderSettings.ProjectorType == 0 ? FlipDirection.None : FlipDirection.Horizontally;
        set
        {
            HeaderSettings.ProjectorType = value == FlipDirection.None ? 0u : 1;
            RaisePropertyChanged();
        }
    }

    public override byte AntiAliasing
    {
        get => (byte) HeaderSettings.AntiAliasLevelInfo;
        set => base.AntiAliasing = (byte)(HeaderSettings.AntiAliasLevelInfo = Math.Clamp(value, 1u, 16u));
    }

    public override float LayerHeight
    {
        get => HeaderSettings.LayerHeightMilimeter;
        set => base.LayerHeight = HeaderSettings.LayerHeightMilimeter = Layer.RoundHeight(value);
    }

    public override float PrintHeight
    {
        get => base.PrintHeight;
        set => base.PrintHeight = HeaderSettings.OverallHeightMilimeter = base.PrintHeight;
    }

    public override uint LayerCount
    {
        get => base.LayerCount;
        set => HeaderSettings.LayerCount = base.LayerCount;
    }

    public override ushort BottomLayerCount
    {
        get => (ushort) HeaderSettings.BottomLayersCount;
        set => base.BottomLayerCount = (ushort) (HeaderSettings.BottomLayersCount2 = HeaderSettings.BottomLayersCount = value);
    }

    public override float BottomLightOffDelay
    {
        get => HeaderSettings.BottomLightOffDelay;
        set => base.BottomLightOffDelay = HeaderSettings.BottomLightOffDelay = MathF.Round(value, 2);
    }

    public override float LightOffDelay
    {
        get => HeaderSettings.LightOffDelay;
        set => base.LightOffDelay = HeaderSettings.LightOffDelay = MathF.Round(value, 2);
    }

    public override float BottomExposureTime
    {
        get => HeaderSettings.BottomExposureSeconds;
        set => base.BottomExposureTime = HeaderSettings.BottomExposureSeconds = MathF.Round(value, 2);
    }

    public override float BottomWaitTimeBeforeCure
    {
        get => base.BottomWaitTimeBeforeCure;
        set
        {
            SetBottomLightOffDelay(value);
            base.BottomWaitTimeBeforeCure = value;
        }
    }

    public override float WaitTimeBeforeCure
    {
        get => base.WaitTimeBeforeCure;
        set
        {
            SetNormalLightOffDelay(value);
            base.WaitTimeBeforeCure = value;
        }
    }

    public override float ExposureTime
    {
        get => HeaderSettings.LayerExposureSeconds;
        set => base.ExposureTime = HeaderSettings.LayerExposureSeconds = MathF.Round(value, 2);
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

    public override float BottomRetractSpeed => RetractSpeed;

    public override float RetractSpeed
    {
        get => HeaderSettings.RetractSpeed;
        set => base.RetractSpeed = HeaderSettings.RetractSpeed = MathF.Round(value, 2);
    }

    public override byte BottomLightPWM
    {
        get => (byte) HeaderSettings.BottomLightPWM;
        set => base.BottomLightPWM = (byte) (HeaderSettings.BottomLightPWM = value);
    }

    public override byte LightPWM
    {
        get => (byte) HeaderSettings.LightPWM;
        set => base.LightPWM = (byte) (HeaderSettings.LightPWM = value);
    }

    public override float PrintTime
    {
        get => base.PrintTime;
        set
        {
            base.PrintTime = value;
            HeaderSettings.PrintTime = (uint)base.PrintTime;
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
        set => base.MaterialGrams = HeaderSettings.WeightG = MathF.Round(value, 3);
    }

    public override float MaterialCost
    {
        get => MathF.Round(HeaderSettings.CostDollars, 3);
        set => base.MaterialCost = HeaderSettings.CostDollars = MathF.Round(value, 3);
    }

    public override string MachineName
    {
        get => HeaderSettings.MachineName;
        set
        {
            base.MachineName = HeaderSettings.MachineName = value;
            HeaderSettings.MachineNameSize = (uint)HeaderSettings.MachineName.Length;
        }
    }

    public override object[] Configs => [HeaderSettings];

    #endregion

    #region Constructors
    public PHZFile()
    {
        Previews = new Preview[ThumbnailCountFileShouldHave];
    }
    #endregion

    #region Methods
    public override void Clear()
    {
        base.Clear();

        for (byte i = 0; i < ThumbnailCountFileShouldHave; i++)
        {
            Previews[i] = new Preview();
        }

        LayersDefinitions = null!;
    }

    protected override void EncodeInternally(OperationProgress progress)
    {
        /*if (HeaderSettings.EncryptionKey == 0)
        {
            Random rnd = new Random();
            HeaderSettings.EncryptionKey = (uint)rnd.Next(short.MaxValue, int.MaxValue);
        }*/

        LayersDefinitions = new LayerDef[HeaderSettings.LayerCount];
        using var outputFile = new FileStream(TemporaryOutputFileFullPath, FileMode.Create, FileAccess.Write);
        outputFile.Seek(Helpers.Serializer.SizeOf(HeaderSettings), SeekOrigin.Begin);

        Mat?[] thumbnails = [GetSmallestThumbnail(), GetLargestThumbnail()];
        for (byte i = 0; i < thumbnails.Length; i++)
        {
            var image = thumbnails[i];
            if (image is null) continue;

            var previewBytes = EncodeChituImageRGB15Rle(image);
            if (previewBytes.Length == 0) continue;

            Preview preview = new()
            {
                ResolutionX = (uint)image.Width,
                ResolutionY = (uint)image.Height,
                ImageLength = (uint)previewBytes.Length
            };

            if (i == 0)
            {
                HeaderSettings.PreviewSmallOffsetAddress = (uint)outputFile.Position;
            }
            else
            {
                HeaderSettings.PreviewLargeOffsetAddress = (uint)outputFile.Position;
            }


            preview.ImageOffset = (uint)(outputFile.Position + Helpers.Serializer.SizeOf(preview));

            outputFile.WriteSerialize(preview);
            outputFile.WriteBytes(previewBytes);
        }

        if (HeaderSettings.MachineNameSize > 0)
        {
            HeaderSettings.MachineNameAddress = (uint)outputFile.Position;
            var machineBytes = Encoding.ASCII.GetBytes(HeaderSettings.MachineName);
            outputFile.WriteBytes(machineBytes);
        }

        progress.Reset(OperationProgress.StatusEncodeLayers, LayerCount);
        var layersHash = new Dictionary<string, LayerDef>();
        LayersDefinitions = new LayerDef[HeaderSettings.LayerCount];
        HeaderSettings.LayersDefinitionOffsetAddress = (uint)outputFile.Position;
        long layerDefCurrentOffset = HeaderSettings.LayersDefinitionOffsetAddress;
        long layerDataCurrentOffset = HeaderSettings.LayersDefinitionOffsetAddress + Helpers.Serializer.SizeOf(new LayerDef()) * LayerCount;

        foreach (var batch in BatchLayersIndexes())
        {
            Parallel.ForEach(batch, CoreSettings.GetParallelOptions(progress), layerIndex =>
            {
                progress.PauseIfRequested();
                using (var mat = this[layerIndex].LayerMat)
                {
                    LayersDefinitions[layerIndex] = new LayerDef(this, this[layerIndex]);
                    LayersDefinitions[layerIndex].Encode(mat, (uint)layerIndex);
                }
                progress.LockAndIncrement();
            });

            foreach (var layerIndex in batch)
            {
                progress.PauseOrCancelIfRequested();

                var layerDef = LayersDefinitions[layerIndex];
                LayerDef? layerDefHash = null;

                if (HeaderSettings.EncryptionKey == 0)
                {
                    string hash = CryptExtensions.ComputeSHA1Hash(layerDef.EncodedRle);
                    if (layersHash.TryGetValue(hash, out layerDefHash))
                    {
                        layerDef.DataAddress = layerDefHash.DataAddress;
                        layerDef.DataSize = layerDefHash.DataSize;
                    }
                    else
                    {
                        layersHash.Add(hash, layerDef);
                    }
                }

                if (layerDefHash is null)
                {
                    layerDef.PageNumber = (uint)(layerDataCurrentOffset / ChituboxFile.PageSize);
                    layerDef.DataAddress = (uint) (layerDataCurrentOffset - ChituboxFile.PageSize * layerDef.PageNumber);

                    outputFile.Seek(layerDataCurrentOffset, SeekOrigin.Begin);
                    layerDataCurrentOffset += outputFile.WriteBytes(layerDef.EncodedRle);
                }


                outputFile.Seek(layerDefCurrentOffset, SeekOrigin.Begin);
                layerDefCurrentOffset += outputFile.WriteSerialize(layerDef);

                layerDef.EncodedRle = null!; // Free
            }
        }

        HeaderSettings.ModifiedTimestampMinutes = (uint) DateTimeExtensions.TimestampMinutes;
        outputFile.Seek(0, SeekOrigin.Begin);
        outputFile.WriteSerialize(HeaderSettings);

        Debug.WriteLine("Encode Results:");
        Debug.WriteLine(HeaderSettings);
        Debug.WriteLine(Previews[0]);
        Debug.WriteLine(Previews[1]);
        Debug.WriteLine("-End-");
    }

    protected override void DecodeInternally(OperationProgress progress)
    {
        using var inputFile = new FileStream(FileFullPath!, FileMode.Open, FileAccess.Read);
        //HeaderSettings = Helpers.ByteToType<CbddlpFile.Header>(InputFile);
        //HeaderSettings = Helpers.Serializer.Deserialize<Header>(InputFile.ReadBytes(Helpers.Serializer.SizeOf(typeof(Header))));
        HeaderSettings = Helpers.Deserialize<Header>(inputFile);
        if (HeaderSettings.Magic != MAGIC_PHZ)
        {
            throw new FileLoadException("Not a valid PHZ file!", FileFullPath);
        }

        HeaderSettings.AntiAliasLevel = 1;

        Debug.Write("Header -> ");
        Debug.WriteLine(HeaderSettings);

        progress.Reset(OperationProgress.StatusDecodePreviews, (uint)ThumbnailCountFileShouldHave);
        var thumbnailOffsets = new[] { HeaderSettings.PreviewSmallOffsetAddress, HeaderSettings.PreviewLargeOffsetAddress };
        for (int i = 0; i < thumbnailOffsets.Length; i++)
        {
            if (thumbnailOffsets[i] == 0) continue;

            inputFile.Seek(thumbnailOffsets[i], SeekOrigin.Begin);
            Previews[i] = Helpers.Deserialize<Preview>(inputFile);

            Debug.Write($"Preview {i} -> ");
            Debug.WriteLine(Previews[i]);

            inputFile.Seek(Previews[i].ImageOffset, SeekOrigin.Begin);
            var rawImageData = new byte[Previews[i].ImageLength];
            inputFile.ReadExactly(rawImageData.AsSpan());

            Thumbnails.Add(DecodeChituImageRGB15Rle(rawImageData, Previews[i].ResolutionX, Previews[i].ResolutionY));
            progress++;
        }

        if (HeaderSettings is {MachineNameAddress: > 0, MachineNameSize: > 0})
        {
            inputFile.Seek(HeaderSettings.MachineNameAddress, SeekOrigin.Begin);
            byte[] buffer = new byte[HeaderSettings.MachineNameSize];
            inputFile.ReadExactly(buffer);
            HeaderSettings.MachineName = Encoding.ASCII.GetString(buffer);
        }


        Init(HeaderSettings.LayerCount, DecodeType == FileDecodeType.Partial);
        LayersDefinitions = new LayerDef[HeaderSettings.LayerCount];

        progress.Reset(OperationProgress.StatusDecodeLayers, HeaderSettings.LayerCount);
        foreach (var batch in BatchLayersIndexes())
        {
            foreach (var layerIndex in batch)
            {
                progress.PauseOrCancelIfRequested();

                var layerDef = Helpers.Deserialize<LayerDef>(inputFile);
                layerDef.Parent = this;
                LayersDefinitions[layerIndex] = layerDef;

                Debug.Write($"LAYER {layerIndex} -> ");
                Debug.WriteLine(layerDef);

                if (DecodeType == FileDecodeType.Full)
                {
                    inputFile.SeekDoWorkAndRewind(layerDef.PageNumber * ChituboxFile.PageSize + layerDef.DataAddress,
                        () => { layerDef.EncodedRle = inputFile.ReadBytes(layerDef.DataSize); });
                }
            }

            if (DecodeType == FileDecodeType.Full)
            {
                Parallel.ForEach(batch, CoreSettings.GetParallelOptions(progress), layerIndex =>
                {
                    progress.PauseIfRequested();

                    using (var mat = LayersDefinitions[layerIndex].Decode((uint)layerIndex))
                    {
                        _layers[layerIndex] = new Layer((uint)layerIndex, mat, this);
                    }

                    progress.LockAndIncrement();
                });
            }
        }

        for (uint layerIndex = 0; layerIndex < LayerCount; layerIndex++)
        {
            LayersDefinitions[layerIndex].CopyTo(this[layerIndex]);
        }
    }

    protected override void PartialSaveInternally(OperationProgress progress)
    {
        HeaderSettings.ModifiedTimestampMinutes = (uint) DateTimeExtensions.TimestampMinutes;
        using var outputFile = new FileStream(TemporaryOutputFileFullPath, FileMode.Open, FileAccess.Write);
        outputFile.Seek(0, SeekOrigin.Begin);
        outputFile.WriteSerialize(HeaderSettings);

        /*if (HeaderSettings.MachineNameAddress > 0 && HeaderSettings.MachineNameSize > 0)
            {
                outputFile.Seek(HeaderSettings.MachineNameAddress, SeekOrigin.Begin);
                byte[] buffer = new byte[HeaderSettings.MachineNameSize];
                outputFile.Write(Encoding.ASCII.GetBytes(HeaderSettings.MachineName), 0, (int)HeaderSettings.MachineNameSize);
            }*/

        outputFile.Seek(HeaderSettings.LayersDefinitionOffsetAddress, SeekOrigin.Begin);
        for (uint layerIndex = 0; layerIndex < HeaderSettings.LayerCount; layerIndex++)
        {
            LayersDefinitions[layerIndex].SetFrom(this[layerIndex]);
            outputFile.WriteSerialize(LayersDefinitions[layerIndex]);
        }
    }

    #endregion

    #region Static Methods
    public static byte[] LayerRleCrypt(uint seed, uint layerIndex, IEnumerable<byte> input)
    {
        var result = input.AsValueEnumerable().ToArray();
        LayerRleCryptBuffer(seed, layerIndex, result);
        return result;
    }

    public static void LayerRleCryptBuffer(uint seed, uint layerIndex, byte[] input)
    {
        if (seed == 0) return;
        seed %= 0x4324;
        var init = seed * 0x34a32231;
        var key = (layerIndex ^ 0x3fad2212) * seed * 0x4910913d;

        int index = 0;
        for (int i = 0; i < input.Length; i++)
        {
            var k = (byte)(key >> 8 * index);

            index++;

            if ((index & 3) == 0)
            {
                key += init;
                index = 0;
            }

            input[i] = (byte)(input[i] ^ k);
        }
    }
    #endregion
}