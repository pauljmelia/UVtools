﻿/*
 *                     GNU AFFERO GENERAL PUBLIC LICENSE
 *                       Version 3, 19 November 2007
 *  Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
 *  Everyone is permitted to copy and distribute verbatim copies
 *  of this license document, but changing it is not allowed.
 */

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.OpenSsl;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Serialization;
using UVtools.Core.Converters;
using UVtools.Core.Extensions;
using UVtools.Core.GCode;
using UVtools.Core.Layers;
using UVtools.Core.Operations;
using ZLinq;

namespace UVtools.Core.FileFormats;


[XmlRoot(ElementName = "Print")]
public class ZCodePrint
{

    [XmlRoot(ElementName = "Device")]
    public class ZcodePrintDevice
    {
        [XmlAttribute("z")]
        public float MachineZ { get; set; } = 220;

        [XmlAttribute("height")]
        public uint ResolutionY { get; set; } = 2400;

        [XmlAttribute("width")]
        public uint ResolutionX { get; set; } = 3840;

        [XmlAttribute("type")]
        public string MachineModel { get; set; } = "IBEE";

        [XmlAttribute("pixel_size")]
        public float PixelSize { get; set; } = 50;

    }


    [XmlRoot(ElementName = "Profile")]
    public class ZcodePrintProfile
    {
        [XmlAttribute("name")]
        public string Name { get; set; } = "UVtools";


        [XmlRoot(ElementName = "Slice")]
        public class ZcodePrintProfileSlice
        {
            [XmlAttribute("bottom_layers")]
            public ushort BottomLayerCount { get; set; } = FileFormat.DefaultBottomLayerCount;

            [XmlAttribute("exposure_bottom")]
            public uint BottomExposureTime { get; set; } = (uint) (FileFormat.DefaultBottomExposureTime * 1000);

            [XmlAttribute("exposure")]
            public uint ExposureTime { get; set; } = (uint) (FileFormat.DefaultExposureTime * 1000);

            [XmlAttribute("height_bottom")]
            public float BottomLiftHeight { get; set; } = FileFormat.DefaultBottomLiftHeight;

            [XmlAttribute("speed_bottom")]
            public float BottomLiftSpeed { get; set; } = FileFormat.DefaultBottomLiftSpeed;

            [XmlAttribute("height")]
            public float LiftHeight { get; set; } = FileFormat.DefaultLiftHeight;

            [XmlAttribute("speed")]
            public float LiftSpeed { get; set; } = FileFormat.DefaultLiftSpeed;

            [XmlAttribute("cooldown_bottom")]
            public uint BottomWaitTimeBeforeCure { get; set; }

            [XmlAttribute("cooldown")]
            public uint WaitTimeBeforeCure { get; set; }

            [XmlAttribute("thickness")]
            public float LayerHeight { get; set; } = FileFormat.DefaultLayerHeight;

            [XmlAttribute("anti_aliasing_level")]
            public byte AntiAliasing { get; set; }

            [XmlAttribute("anti_aliasing_grey_level")]
            public byte AntiAliasingGrey { get; set; }

            [XmlAttribute("led_power")]
            public ushort LedPower { get; set; } = ZCodeFile.MaxLEDPower;
        }

        public ZcodePrintProfileSlice Slice { get; set; } = new();
    }


    [XmlRoot(ElementName = "Job")]
    public class ZcodePrintJob
    {
        [XmlElement("name")]
        public string? StlName { get; set; }

        [XmlElement("previewImage")]
        public string PreviewImage { get; set; } = ZCodeFile.PreviewFilename;

        [XmlElement("layers")]
        public uint LayerCount { get; set; }

        [XmlElement("time")]
        public uint PrintTime { get; set; }

        [XmlElement("volumn")]
        public float VolumeMl { get; set; }

        [XmlElement("thickness")]
        public float LayerHeight { get; set; } = FileFormat.DefaultLayerHeight;

        [XmlElement("price")]
        public float Price { get; set; }

        [XmlElement("weight")]
        public float WeightG { get; set; }
    }

    public ZcodePrintDevice Device = new();

    public ZcodePrintProfile Profile = new();

    public ZcodePrintJob Job = new();

}
public sealed class ZCodeFile : FileFormat
{
    #region Constants

    public const string GCodeFilename = "lcd.gcode";
    public const string ManifestFilename = "task.xml";
    public const string PreviewFilename = "preview.png";
    public const ushort MaxLEDPower = 300;

    public const string GCodeStart = "G21;{0}" +        // Set units to be mm
                                     "G90;{0}" +        // Absolute Positioning
                                     "M106 S0;{0}" +    // Light Off
                                     "G28 Z0;{0}";      // Home

    public const string GCodeEnd = "M106 S0{0}" +               // Light Off
                                   "G1 Z{1} F10;{0}" +          // Raize Z
                                   "M18;{0}";                   // Disable Motors


    public const string Secret1 = "eHtZQkIuNhIfOk8/PjoDFyAqTyc2DHtZQkJBeRgfPS05PToXFzAuIS4UPiccBAYrSiJmNi4+KTUUFycsLjhLIjETKlgtFBAXNQQqLUUXODJcADhKFCZfIBY2NSYmRF01PgcBJhYiOi1KYwYEDBs4KzAgRQw/LxA5GgEAGDQkGzdHCV0mMSUGFj8dFj4iGhUCXCYgBBhHJwUaKjMFIhdYCUUiHzAuPi0xFD02Bg0vPS8HWz8sCjIBPgwXLSA9MA45Ah8OPSYUMCtXHhAPHxAOHg0WIhBfOgU5HyQfQwomXColZCJdKhZBbRAeJCwpGhhlQCsXOUoFDCAVNj9AJxUnBy4FNhRvBR9WRyEiDwIMGT0mDTogXQ0dOBw5RxwXGUQZBzIiWzo6MTIlXjwhJT0lNyY+KARhP0NbIx01Z25BJhVbRCMhRSI3KUNjGjwkJC4xFzdHLgYeQgQYNgcBDyIcMS0JIFgnGT1MAwkDFiI5AgQAOgovBTZEKUM2AhgeGCxVOBghOTctOgoSBQcsJj03PCUMGgsjBwN9DVwsXz8THhkjXAc6YzoMFx0fCwEcLCIoATIjMD42CB06BB8cLiQuBy8PETUtWEUQAhsrPBwWDDoGIj4FBwYBAgUIIjQ4IV9XDi5cHQ8xJh1mXnh7WUIqIjd1BiYmOS0nEHY/KjZBXnh7WQ==";
    public const string Secret2 = "eHtZQkIuNhIfOk8/OTEZHzdPJCkqeHtZQkJmPhMhAys+NTkeOS4mBxoQGxclKi0uIhQSJxguGyAUHDYuIAspLTJCKkA9ODM8BwI9DjgxGBk6DTlFAiwyLj8JGWMOODpeXwFsDjAYASYgYic5KV4GJCFlTQY+DSdnLEJXFSEwZyYAFjoHNzEuQFhdJEM5NRFcGh8wFCExLi49TmhcWUJCQV4QGDBPPzkxGR83TyQpKnh7WUJC";
    public static readonly string GCodeRSAPrivateKey = CryptExtensions.XORCipherString(CryptExtensions.Base64DecodeString(Secret1), About.Software);
    public static readonly string GCodeRSAPublicKey = CryptExtensions.XORCipherString(CryptExtensions.Base64DecodeString(Secret2), About.Software);

    #endregion

    #region Sub Classes

    #endregion

    #region Properties
    public ZCodePrint ManifestFile { get; set; } = new ();

    public override FileFormatType FileType => FileFormatType.Archive;

    public override FileExtension[] FileExtensions { get; } =
    [
        new(typeof(ZCodeFile), "zcode", "UnizMaker IBEE (ZCode)")
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

    public override Size[] ThumbnailsOriginalSize { get; } = [new(640, 480)];

    public override uint ResolutionX
    {
        get => ManifestFile.Device.ResolutionX;
        set => base.ResolutionX = ManifestFile.Device.ResolutionX = value;
    }

    public override uint ResolutionY
    {
        get => ManifestFile.Device.ResolutionY;
        set => base.ResolutionY = ManifestFile.Device.ResolutionY = value;
    }

    public override float DisplayWidth
    {
        get => MathF.Round(ManifestFile.Device.ResolutionX * ManifestFile.Device.PixelSize / 1000, 2);
        set => RaisePropertyChanged();
    }

    public override float DisplayHeight
    {
        get => MathF.Round(ManifestFile.Device.ResolutionY * ManifestFile.Device.PixelSize / 1000, 2);
        set => RaisePropertyChanged();
    }

    public override float MachineZ
    {
        get => ManifestFile.Device.MachineZ > 0 ? ManifestFile.Device.MachineZ : base.MachineZ;
        set
        {
            ManifestFile.Device.MachineZ = value;
            RaisePropertyChanged();
        }
    }

    public override FlipDirection DisplayMirror => FlipDirection.Vertically;

    public override byte AntiAliasing
    {
        get => ManifestFile.Profile.Slice.AntiAliasing;
        set => base.AntiAliasing = ManifestFile.Profile.Slice.AntiAliasing = Math.Clamp(value, (byte)1, (byte)16);
    }

    public override float LayerHeight
    {
        get => ManifestFile.Job.LayerHeight > 0 ? ManifestFile.Job.LayerHeight : ManifestFile.Profile.Slice.LayerHeight;
        set => base.LayerHeight = ManifestFile.Job.LayerHeight = ManifestFile.Profile.Slice.LayerHeight = Layer.RoundHeight(value);
    }

    public override uint LayerCount
    {
        get => base.LayerCount;
        set => base.LayerCount = ManifestFile.Job.LayerCount = base.LayerCount;
    }

    public override ushort BottomLayerCount
    {
        get => ManifestFile.Profile.Slice.BottomLayerCount;
        set => base.BottomLayerCount = ManifestFile.Profile.Slice.BottomLayerCount = value;
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
        get => TimeConverter.MillisecondsToSeconds(ManifestFile.Profile.Slice.BottomWaitTimeBeforeCure);
        set
        {
            ManifestFile.Profile.Slice.BottomWaitTimeBeforeCure = TimeConverter.SecondsToMillisecondsUint(value);
            base.BottomWaitTimeBeforeCure = base.BottomLightOffDelay = value;
        }
    }

    public override float WaitTimeBeforeCure
    {
        get => TimeConverter.MillisecondsToSeconds(ManifestFile.Profile.Slice.WaitTimeBeforeCure);
        set
        {
            ManifestFile.Profile.Slice.WaitTimeBeforeCure = TimeConverter.SecondsToMillisecondsUint(value);
            base.WaitTimeBeforeCure = base.LightOffDelay = value;
        }
    }

    public override float BottomExposureTime
    {
        get => TimeConverter.MillisecondsToSeconds(ManifestFile.Profile.Slice.BottomExposureTime);
        set
        {
            ManifestFile.Profile.Slice.BottomExposureTime = TimeConverter.SecondsToMillisecondsUint(value);
            base.BottomExposureTime = value;
        }
    }

    public override float ExposureTime
    {
        get => TimeConverter.MillisecondsToSeconds(ManifestFile.Profile.Slice.ExposureTime);
        set
        {
            ManifestFile.Profile.Slice.ExposureTime = TimeConverter.SecondsToMillisecondsUint(value);
            base.ExposureTime = value;
        }
    }

    public override float BottomLiftHeight
    {
        get => ManifestFile.Profile.Slice.BottomLiftHeight;
        set => base.BottomLiftHeight = ManifestFile.Profile.Slice.BottomLiftHeight = MathF.Round(value, 2);
    }

    public override float LiftHeight
    {
        get => ManifestFile.Profile.Slice.LiftHeight;
        set => base.LiftHeight = ManifestFile.Profile.Slice.LiftHeight = MathF.Round(value, 2);
    }

    public override float BottomLiftSpeed
    {
        get => ManifestFile.Profile.Slice.BottomLiftSpeed;
        set => base.BottomLiftSpeed = ManifestFile.Profile.Slice.BottomLiftSpeed = MathF.Round(value, 2);
    }

    public override float LiftSpeed
    {
        get => ManifestFile.Profile.Slice.LiftSpeed;
        set => base.LiftSpeed = ManifestFile.Profile.Slice.LiftSpeed = MathF.Round(value, 2);
    }

    public override byte LightPWM
    {
        get => (byte)(byte.MaxValue * ManifestFile.Profile.Slice.LedPower / MaxLEDPower);
        set
        {
            ManifestFile.Profile.Slice.LedPower = (ushort)(MaxLEDPower * value / byte.MaxValue);
            base.LightPWM = value;
            RaisePropertyChanged(nameof(BottomLightPWM));
        }
    }

    public override float PrintTime
    {
        get => base.PrintTime;
        set
        {
            base.PrintTime = value;
            ManifestFile.Job.PrintTime = (uint) base.PrintTime;
        }
    }

    public override float MaterialMilliliters
    {
        get => base.MaterialMilliliters;
        set
        {
            base.MaterialMilliliters = value;
            ManifestFile.Job.VolumeMl = base.MaterialMilliliters;
        }
    }

    public override float MaterialGrams
    {
        get => ManifestFile.Job.WeightG;
        set => base.MaterialGrams = ManifestFile.Job.WeightG = MathF.Round(value, 3);
    }

    public override float MaterialCost
    {
        get => ManifestFile.Job.Price;
        set => base.MaterialCost = ManifestFile.Job.Price = MathF.Round(value, 3);
    }

    /*public override string MaterialName
    {
        get => HeaderSettings.Resin;
        set
        {
            HeaderSettings.Resin = value;
            RaisePropertyChanged();
        }
    }*/

    public override string MachineName
    {
        get => ManifestFile.Device.MachineModel;
        set => base.MachineName = ManifestFile.Device.MachineModel = value;
    }

    public override object[] Configs => [ManifestFile.Device, ManifestFile.Job, ManifestFile.Profile.Slice];

    #endregion

    #region Constructor
    public ZCodeFile()
    {
        GCode = new GCodeBuilder
        {
            UseTailComma = true,
            UseComments = false,
            GCodePositioningType = GCodeBuilder.GCodePositioningTypes.Absolute,
            GCodeSpeedUnit = GCodeBuilder.GCodeSpeedUnits.CentimetersPerMinute,
            GCodeTimeUnit = GCodeBuilder.GCodeTimeUnits.Milliseconds,
            GCodeShowImageType = GCodeBuilder.GCodeShowImageTypes.FilenamePng1Started,
            LayerMoveCommand = GCodeBuilder.GCodeMoveCommands.G0,
            EndGCodeMoveCommand = GCodeBuilder.GCodeMoveCommands.G1,
            MaxLEDPower = MaxLEDPower,
            CommandClearImage = {Enabled = false},
        };
    }
    #endregion

    #region Methods

    protected override void EncodeInternally(OperationProgress progress)
    {
        using var outputFile = ZipFile.Open(TemporaryOutputFileFullPath, ZipArchiveMode.Create);

        EncodeThumbnailsInZip(outputFile, progress, PreviewFilename);
        EncodeLayersInZip(outputFile, IndexStartNumber.One, progress);

        outputFile.CreateEntryFromSerializeXml(ManifestFilename, ManifestFile, ZipArchiveMode.Create, XmlExtensions.SettingsIndent, true);
        outputFile.CreateEntryFromContent(GCodeFilename, EncryptGCode(progress), ZipArchiveMode.Create);
    }

    protected override void DecodeInternally(OperationProgress progress)
    {
        using var inputFile = ZipFile.Open(FileFullPath!, ZipArchiveMode.Read);
        var entry = inputFile.GetEntry(ManifestFilename);
        if (entry is null)
        {
            Clear();
            throw new FileLoadException($"{ManifestFilename} not found", FileFullPath);
        }

        try
        {
            using var stream = entry.Open();
            ManifestFile = XmlExtensions.DeserializeFromStream<ZCodePrint>(stream);

        }
        catch (Exception e)
        {
            Clear();
            throw new FileLoadException($"Unable to deserialize '{entry.Name}'\n{e}", FileFullPath);
        }

        entry = inputFile.GetEntry(GCodeFilename);
        if (entry is null)
        {
            Clear();
            throw new FileLoadException($"{GCodeFilename} not found", FileFullPath);
        }

        var encryptEngine = new RsaEngine();
        using var txtreader = new StringReader(GCodeRSAPublicKey);
        var keyParameter = (AsymmetricKeyParameter)new PemReader(txtreader).ReadObject();
        encryptEngine.Init(true, keyParameter);

        using (TextReader tr = new StreamReader(entry.Open()))
        {
            progress.Reset("Decrypting GCode", (uint) (entry.Length / 88));
            while (tr.ReadLine() is { } line)
            {
                if (string.IsNullOrEmpty(line)) continue;
                if (!line.EndsWith("==")) continue;

                byte[] data = System.Convert.FromBase64String(line);
                var decodedBytes = encryptEngine.ProcessBlock(data, 0, data.Length);
                decodedBytes = decodedBytes.AsValueEnumerable().Skip(2).SkipWhile(b => b is 255 or 0).ToArray();
                GCode!.AppendLine(Encoding.UTF8.GetString(decodedBytes));

                progress++;
            }

            tr.Close();
        }

        Init(ManifestFile.Job.LayerCount, DecodeType == FileDecodeType.Partial);

        DecodeThumbnailsFromZip(inputFile, progress, PreviewFilename);
        DecodeLayersFromZip(inputFile, IndexStartNumber.One, progress);

        GCode!.ParseLayersFromGCode(this);
    }

    protected override void PartialSaveInternally(OperationProgress progress)
    {
        using var outputFile = ZipFile.Open(TemporaryOutputFileFullPath, ZipArchiveMode.Update);

        var entriesToRemove = outputFile.Entries.AsValueEnumerable().Where(zipEntry => zipEntry.Name.EndsWith(".gcode") || zipEntry.Name.EndsWith(".xml")).ToArray();
        foreach (var zipEntry in entriesToRemove)
        {
            zipEntry.Delete();
        }

        outputFile.CreateEntryFromSerializeXml(ManifestFilename, ManifestFile, ZipArchiveMode.Update, XmlExtensions.SettingsIndent, true);
        outputFile.CreateEntryFromContent(GCodeFilename, EncryptGCode(progress), ZipArchiveMode.Update);
    }

    private string EncryptGCode(OperationProgress progress)
    {
        RebuildGCode();
        progress.Reset("Encrypting GCode", (uint) GCode!.Length);
        StringBuilder sb = new();

        var encryptEngine = new RsaEngine();
        using var txtreader = new StringReader(GCodeRSAPrivateKey);
        var keyParameter = (AsymmetricKeyParameter)new PemReader(txtreader).ReadObject();
        encryptEngine.Init(true, keyParameter);

        using StringReader sr = new(GCodeStr!);
        while (sr.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line == string.Empty || line[0] == ';') continue; // No empty lines nor comment start lines
            progress += (uint)line.Length;

            var data = Encoding.UTF8.GetBytes(line);
            List<byte> padData = new(64) {0, 1, 0};
            padData.AddRange(data);

            if (padData.Count > 64)
            {
                throw new ArgumentOutOfRangeException($"Too long gcode line to encrypt, got: {padData.Count} bytes while expecting less than 64 bytes");
            }

            while (padData.Count < 64)
            {
                padData.Insert(2, 255);
            }

            var padDataArray = padData.ToArray();
            //Debug.WriteLine(string.Join(", ", padDataArray));

            var encodedBytes = encryptEngine.ProcessBlock(padDataArray, 0, padDataArray.Length);
            sb.AppendLine(System.Convert.ToBase64String(encodedBytes));
        }

        return sb.ToString();
    }
    #endregion
}