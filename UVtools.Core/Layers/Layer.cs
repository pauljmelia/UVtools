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
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Text.Json.Serialization;
using System.Threading;
using System.Xml.Serialization;
using UVtools.Core.EmguCV;
using UVtools.Core.Extensions;
using UVtools.Core.FileFormats;
using UVtools.Core.Objects;
using UVtools.Core.Operations;

namespace UVtools.Core.Layers;

#region Enums

public enum LayerCompressionCodec : byte
{
    [Description("PNG: Compression=High | Speed=Slow (Use with low RAM)")]
    Png,
    [Description("GZip: Compression=Medium | Speed=Medium")]
    GZip,
    [Description("Deflate: Compression=Medium | Speed=Medium")]
    Deflate,
    [Description("Brotli: Compression=High | Speed=Fast (Optimal)")]
    Brotli,
    [Description("LZ4: Compression=Low | Speed=VeryFast (Use with high RAM)")]
    Lz4,
    //[Description("None: Compression=None | Speed=Fastest (Your soul belongs to RAM)")]
    //None

}

#endregion

/// <summary>
/// Represent a Layer
/// </summary>
public class Layer : BindableBase, IEquatable<Layer>, IEquatable<uint>
{
    #region Constants
    public const byte HeightPrecision = 3;
    public const decimal HeightPrecisionIncrement = 0.001M;
    public const decimal MinimumHeight = 0.01M;
    public const decimal MaximumHeight = 0.2M;

    public const float MinimumHeightFloat = (float)MinimumHeight;
    public const float MaximumHeightFloat = (float)MaximumHeight;
    public const float HeightPrecisionIncrementFloat = (float)HeightPrecisionIncrement;
    #endregion

    #region Members

    public Lock Mutex = new();
    private LayerCompressionCodec _compressionCodec;
    private CMat _compressedMat;
    private uint _nonZeroPixelCount;
    private Rectangle _boundingRectangle = Rectangle.Empty;
    private uint _firstPixelIndex;
    private uint _lastPixelIndex;
    private Point _firstPixelPosition;
    private Point _lastPixelPosition;
    private bool _isModified;
    private uint _index;
    private float _positionZ;
    private float _lightOffDelay;
    private float _waitTimeBeforeCure;
    private float _exposureTime;
    private float _waitTimeAfterCure;
    private float _liftHeight = FileFormat.DefaultLiftHeight;
    private float _liftSpeed = FileFormat.DefaultLiftSpeed;
    private float _liftAcceleration;
    private float _liftHeight2 = FileFormat.DefaultLiftHeight2;
    private float _liftSpeed2 = FileFormat.DefaultLiftSpeed2;
    private float _liftAcceleration2;
    private float _waitTimeAfterLift;
    private float _retractSpeed = FileFormat.DefaultRetractSpeed;
    private float _retractAcceleration;
    private float _retractHeight2 = FileFormat.DefaultRetractHeight2;
    private float _retractSpeed2 = FileFormat.DefaultRetractSpeed2;
    private float _retractAcceleration2;
    private byte _lightPWM = FileFormat.DefaultLightPWM;
    private bool _pause;
    private bool _changeResin;

    private float _materialMilliliters;
    private EmguContours? _contours;

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the parent SlicerFile
    /// </summary>
    public FileFormat SlicerFile { get; set; }

    /// <summary>
    /// Image resolution X
    /// </summary>
    public uint ResolutionX => (uint)CompressedMat.Width;

    /// <summary>
    /// Image resolution Y
    /// </summary>
    public uint ResolutionY => (uint)CompressedMat.Height;

    /// <summary>
    /// Image resolution
    /// </summary>
    public Size Resolution => CompressedMat.Size;

    /// <summary>
    /// Gets the number of non zero pixels on this layer image
    /// </summary>
    public uint NonZeroPixelCount
    {
        get => _nonZeroPixelCount;
        internal set
        {
            if (!RaiseAndSetIfChanged(ref _nonZeroPixelCount, value)) return;
            RaisePropertyChanged(nameof(NonZeroPixelRatio));
            RaisePropertyChanged(nameof(NonZeroPixelPercentage));
            RaisePropertyChanged(nameof(Area));
            RaisePropertyChanged(nameof(Volume));
            MaterialMilliliters = -1; // Recalculate
        }
    }

    /// <summary>
    /// Gets the ratio between non zero pixels and display number of pixels
    /// </summary>
    public double NonZeroPixelRatio
    {
        get
        {
            var displayPixelCount = SlicerFile.DisplayPixelCount;
            if (displayPixelCount == 0) return double.NaN;
            return (double)_nonZeroPixelCount / displayPixelCount;
        }
    }

    /// <summary>
    /// Gets the percentage of non zero pixels relative to the display number of pixels
    /// </summary>
    public double NonZeroPixelPercentage
    {
        get
        {
            var pixelRatio = NonZeroPixelRatio;
            if (double.IsNaN(pixelRatio)) return double.NaN;
            return pixelRatio * 100.0;
        }
    }

    /// <summary>
    /// Gets if this layer is empty/all black pixels
    /// </summary>
    public bool IsEmpty => _nonZeroPixelCount == 0;

    /// <summary>
    /// Gets if this layer is a dummy layer to bypass a firmware constrain, that is contained at most one pixel and exposure time no more than 0.01s
    /// </summary>
    public bool IsDummy => _nonZeroPixelCount <= 1 || _exposureTime <= 0.01;

    /// <summary>
    /// Gets the layer area (XY)  in mm²
    /// Pixel size * number of pixels
    /// </summary>
    public float Area => GetArea(3);

    /// <summary>
    /// Gets the layer volume (XYZ) in mm^3
    /// Pixel size * number of pixels * layer height
    /// </summary>
    public float Volume => GetVolume(3);

    /// <summary>
    /// Gets the bounding rectangle for the image area
    /// </summary>
    public Rectangle BoundingRectangle
    {
        get => _boundingRectangle;
        internal set
        {
            if (!RaiseAndSetIfChanged(ref _boundingRectangle, value)) return;
            RaisePropertyChanged(nameof(BoundingRectangleMillimeters));
        }
    }

    /// <summary>
    /// Gets the bounding rectangle for the image area in millimeters
    /// </summary>
    public RectangleF BoundingRectangleMillimeters
    {
        get
        {
            var pixelSize = SlicerFile.PixelSize;
            return new RectangleF(
                MathF.Round(_boundingRectangle.X * pixelSize.Width, 2),
                MathF.Round(_boundingRectangle.Y * pixelSize.Height, 2),
                MathF.Round(_boundingRectangle.Width * pixelSize.Width, 2),
                MathF.Round(_boundingRectangle.Height * pixelSize.Height, 2));
        }
    }

    /// <summary>
    /// Gets the first pixel index on the <see cref="BoundingRectangle"/>
    /// </summary>
    public uint BoundingRectangleFirstPixelIndex => (uint)(_boundingRectangle.Y * ResolutionX + _boundingRectangle.X);

    /// <summary>
    /// Gets the last pixel index on the <see cref="BoundingRectangle"/>
    /// </summary>
    public uint BoundingRectangleLastPixelIndex => (uint)(_boundingRectangle.Bottom * ResolutionX + _boundingRectangle.Right);

    /// <summary>
    /// Gets the first pixel <see cref="Point"/> on the <see cref="BoundingRectangle"/>
    /// </summary>
    public Point BoundingRectangleFirstPixelPosition => _boundingRectangle.Location;

    /// <summary>
    /// Gets the last pixel <see cref="Point"/> on the <see cref="BoundingRectangle"/>
    /// </summary>
    public Point BoundingRectangleLastPixelPosition => new (_boundingRectangle.Right, _boundingRectangle.Bottom);

    /// <summary>
    /// Gets the first pixel index on this layer
    /// </summary>
    public uint FirstPixelIndex
    {
        get => _firstPixelIndex;
        private set => RaiseAndSetIfChanged(ref _firstPixelIndex, value);
    }

    /// <summary>
    /// Gets the last pixel index on this layer
    /// </summary>
    public uint LastPixelIndex
    {
        get => _lastPixelIndex;
        private set => RaiseAndSetIfChanged(ref _lastPixelIndex, value);
    }

    /// <summary>
    /// Gets the first pixel <see cref="Point"/> on this layer
    /// </summary>
    public Point FirstPixelPosition
    {
        get => _firstPixelPosition;
        private set => RaiseAndSetIfChanged(ref _firstPixelPosition, value);
    }

    /// <summary>
    /// Gets the last pixel <see cref="Point"/> on this layer
    /// </summary>
    public Point LastPixelPosition
    {
        get => _lastPixelPosition;
        private set => RaiseAndSetIfChanged(ref _lastPixelPosition, value);
    }


    /// <summary>
    /// Gets if is the first layer
    /// </summary>
    public bool IsFirstLayer => _index == 0;

    /// <summary>
    /// Gets if layer is between first and last layer, aka, not first nor last layer
    /// </summary>
    public bool IsIntermediateLayer => !IsFirstLayer && !IsLastLayer;

    /// <summary>
    /// Gets if is the last layer
    /// </summary>
    public bool IsLastLayer => _index >= SlicerFile.LastLayerIndex;

    /// <summary>
    /// Gets if is in the bottom layer group
    /// </summary>
    public bool IsBottomLayer => _index < SlicerFile.BottomLayerCount;

    /// <summary>
    /// Gets if this layer is in the bottom layer group by it <see cref="Index"/> and <see cref="PositionZ"/> height
    /// </summary>
    public bool IsBottomLayerByHeight
    {
        get
        {
            var bottomLayers = SlicerFile.BottomLayerCount;
            return _index < bottomLayers || RoundHeight(PositionZ / SlicerFile.LayerHeight) <= bottomLayers;
        }
    }

    /// <summary>
    /// Gets if this layer is in the normal layer group by it <see cref="Index"/> and <see cref="PositionZ"/> height
    /// </summary>
    public bool IsNormalLayerByHeight => !IsBottomLayerByHeight;


    /// <summary>
    /// Gets if is in the normal layer group
    /// </summary>
    public bool IsNormalLayer => !IsBottomLayer;

    /// <summary>
    /// Gets if this layer is also an transition layer
    /// </summary>
    public bool IsTransitionLayer => SlicerFile.TransitionLayerCount > 0 &&
                                     Index >= SlicerFile.BottomLayerCount && Index < SlicerFile.BottomLayerCount + SlicerFile.TransitionLayerCount;

    /// <summary>
    /// Gets the previous layer, returns null if no previous layer
    /// </summary>
    public Layer? PreviousLayer
    {
        get
        {
            if (IsFirstLayer || _index > SlicerFile.Count) return null;
            return SlicerFile[_index - 1];
        }
    }

    /// <summary>
    /// Gets the previous layer if available, otherwise return the calling layer itself
    /// </summary>
    public Layer PreviousLayerOrThis
    {
        get
        {
            if (IsFirstLayer || _index > SlicerFile.Count) return this;
            return SlicerFile[_index - 1];
        }
    }

    /// <summary>
    /// Gets the previous layer with a different height from the current, returns null if no previous layer
    /// </summary>
    public Layer? PreviousHeightLayer
    {
        get
        {
            if (IsFirstLayer || _index > SlicerFile.Count) return null;
            for (int i = (int)_index - 1; i >= 0; i--)
            {
                if (SlicerFile[i].PositionZ < _positionZ) return SlicerFile[i];
            }

            return null;
        }
    }

    /// <summary>
    /// Gets the previous layer matching at least <param name="numberOfPixels"/> pixels, returns null if no previous layer
    /// </summary>
    public Layer? GetPreviousLayerWithAtLeastPixelCountOf(uint numberOfPixels)
    {
        if (IsFirstLayer || _index > SlicerFile.Count) return null;
        for (int i = (int)_index - 1; i >= 0; i--)
        {
            if (SlicerFile[i].NonZeroPixelCount >= numberOfPixels) return SlicerFile[i];
        }

        return null;
    }

    /// <summary>
    /// Gets the next layer, returns null if no next layer
    /// </summary>
    public Layer? NextLayer
    {
        get
        {
            if (_index >= SlicerFile.LastLayerIndex) return null;
            return SlicerFile[_index + 1];
        }
    }

    /// <summary>
    /// Gets the next layer if available, otherwise return the calling layer itself
    /// </summary>
    public Layer NextLayerOrThis
    {
        get
        {
            if (_index >= SlicerFile.LastLayerIndex) return this;
            return SlicerFile[_index + 1];
        }
    }

    /// <summary>
    /// Gets the next layer with a different height from the current, returns null if no next layer
    /// </summary>
    public Layer? NextHeightLayer
    {
        get
        {
            if (_index >= SlicerFile.LastLayerIndex) return null;
            for (var i = _index + 1; i < SlicerFile.LayerCount; i++)
            {
                if (SlicerFile[i].PositionZ > _positionZ) return SlicerFile[i];
            }

            return null;
        }
    }

    /// <summary>
    /// Gets the next layer matching at least <param name="numberOfPixels"/> pixels, returns null if no next layer
    /// </summary>
    public Layer? GetNextLayerWithAtLeastPixelCountOf(uint numberOfPixels)
    {
        if (_index >= SlicerFile.LastLayerIndex) return null;
        for (var i = _index + 1; i < SlicerFile.LayerCount; i++)
        {
            if (SlicerFile[i].NonZeroPixelCount >= numberOfPixels) return SlicerFile[i];
        }

        return null;
    }

    /// <summary>
    /// Gets the layer hash
    /// </summary>
    public string Hash => _compressedMat.Hash;

    /// <summary>
    /// Gets the layer index
    /// </summary>
    public uint Index
    {
        get => _index;
        set
        {
            if(!RaiseAndSetIfChanged(ref _index, value)) return;
            RaisePropertyChanged(nameof(Number));
        }
    }

    /// <summary>
    /// Gets the layer number, 1 started
    /// </summary>
    public uint Number => _index + 1;

    /// <summary>
    /// Gets or sets the absolute layer position on Z in mm
    /// </summary>
    public float PositionZ
    {
        get => _positionZ;
        set
        {
            //if (value < 0) throw new ArgumentOutOfRangeException(nameof(PositionZ), "Value can't be negative");
            if (!RaiseAndSetIfChanged(ref _positionZ, RoundHeight(value))) return;
            RaisePropertyChanged(nameof(RelativePositionZ));
            RaisePropertyChanged(nameof(LayerHeight));
            //MaterialMilliliters = -1; // Recalculate
        }
    }

    /// <summary>
    /// Gets the relative layer position on Z in mm (Relative to the previous layer)
    /// </summary>
    public float RelativePositionZ
    {
        get
        {
            var previousLayer = PreviousLayer;
            return previousLayer is null ? _positionZ : RoundHeight(_positionZ - previousLayer.PositionZ);
        }
        set => PositionZ = _positionZ - RelativePositionZ + value;
    }

    /// <summary>
    /// Gets or sets the wait time in seconds before cure the layer
    /// AKA: Light-off delay
    /// Chitubox: Rest time after retract
    /// Lychee: Wait before print
    /// </summary>
    public float WaitTimeBeforeCure
    {
        get => _waitTimeBeforeCure;
        set
        {
            value = MathF.Round(value, 2);
            if (value < 0) value = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomWaitTimeBeforeCure, SlicerFile.WaitTimeBeforeCure);
            if (!RaiseAndSetIfChanged(ref _waitTimeBeforeCure, value)) return;
            SlicerFile.UpdatePrintTimeQueued();
        }
    }

    /// <summary>
    /// Gets or sets the exposure time in seconds
    /// </summary>
    public float ExposureTime
    {
        get => _exposureTime;
        set
        {
            value = MathF.Round(value, 2);
            if (value < 0) value = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomExposureTime, SlicerFile.ExposureTime);
            if(!RaiseAndSetIfChanged(ref _exposureTime, value)) return;
            SlicerFile.UpdatePrintTimeQueued();
        }
    }

    /// <summary>
    /// Gets or sets the wait time in seconds after cure the layer
    /// Chitubox: Rest time before lift
    /// Lychee: Wait after print
    /// </summary>
    public float WaitTimeAfterCure
    {
        get => _waitTimeAfterCure;
        set
        {
            value = MathF.Round(value, 2);
            if (value < 0) value = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomWaitTimeAfterCure, SlicerFile.WaitTimeAfterCure);
            if (!RaiseAndSetIfChanged(ref _waitTimeAfterCure, value)) return;
            SlicerFile.UpdatePrintTimeQueued();
        }
    }

    /// <summary>
    /// Gets or sets the layer off time in seconds
    /// </summary>
    public float LightOffDelay
    {
        get => _lightOffDelay;
        set
        {
            value = MathF.Round(value, 2);
            if (value < 0) value = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomLightOffDelay, SlicerFile.LightOffDelay);
            if(!RaiseAndSetIfChanged(ref _lightOffDelay, value)) return;
            SlicerFile.UpdatePrintTimeQueued();
        }
    }

    /// <summary>
    /// Gets: Total lift height (lift1 + lift2)
    /// Sets: Lift1 with value and lift2 with 0
    /// </summary>
    public float LiftHeightTotal
    {
        get => MathF.Round(_liftHeight + _liftHeight2, 2);
        set
        {
            LiftHeight = MathF.Round(value, 2);
            LiftHeight2 = 0;
        }
    }

    /// <summary>
    /// Gets or sets the lift height in mm
    /// </summary>
    public float LiftHeight
    {
        get => _liftHeight;
        set
        {
            value = MathF.Round(value, 2);
            if (value < 0) value = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomLiftHeight, SlicerFile.LiftHeight);
            if(!RaiseAndSetIfChanged(ref _liftHeight, value)) return;
            RaisePropertyChanged(nameof(LiftHeightTotal));
            RetractHeight2 = _retractHeight2; // Sanitize
            SlicerFile.UpdatePrintTimeQueued();
        }
    }

    /// <summary>
    /// Gets or sets the speed in mm/min
    /// </summary>
    public float LiftSpeed
    {
        get => _liftSpeed;
        set
        {
            value = MathF.Round(value, 2);
            if (value <= 0) value = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomLiftSpeed, SlicerFile.LiftSpeed);
            if(!RaiseAndSetIfChanged(ref _liftSpeed, value)) return;
            SlicerFile.UpdatePrintTimeQueued();
        }
    }

    /// <summary>
    /// Gets or sets the lift acceleration in mm/s²
    /// </summary>
    public float LiftAcceleration
    {
        get => _liftAcceleration;
        set => RaiseAndSetIfChanged(ref _liftAcceleration, MathF.Round(Math.Max(value, 0), 2));
    }

    /// <summary>
    /// Gets or sets the second lift height in mm
    /// </summary>
    public float LiftHeight2
    {
        get => _liftHeight2;
        set
        {
            value = MathF.Round(value, 2);
            if (value < 0) value = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomLiftHeight2, SlicerFile.LiftHeight2);
            if (!RaiseAndSetIfChanged(ref _liftHeight2, value)) return;
            RaisePropertyChanged(nameof(LiftHeightTotal));
            RetractHeight2 = _retractHeight2; // Sanitize
            SlicerFile.UpdatePrintTimeQueued();
        }
    }

    /// <summary>
    /// Gets or sets the second lift speed in mm/min
    /// </summary>
    public float LiftSpeed2
    {
        get => _liftSpeed2;
        set
        {
            value = MathF.Round(value, 2);
            if (value <= 0) value = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomLiftSpeed2, SlicerFile.LiftSpeed2);
            if (!RaiseAndSetIfChanged(ref _liftSpeed2, value)) return;
            SlicerFile.UpdatePrintTimeQueued();
        }
    }

    /// <summary>
    /// Gets or sets the second lift acceleration in mm/s²
    /// </summary>
    public float LiftAcceleration2
    {
        get => _liftAcceleration2;
        set => RaiseAndSetIfChanged(ref _liftAcceleration2, MathF.Round(Math.Max(value, 0), 2));
    }

    public float WaitTimeAfterLift
    {
        get => _waitTimeAfterLift;
        set
        {
            value = MathF.Round(value, 2);
            if (value < 0) value = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomWaitTimeAfterLift, SlicerFile.WaitTimeAfterLift);
            if (!RaiseAndSetIfChanged(ref _waitTimeAfterLift, value)) return;
            SlicerFile.UpdatePrintTimeQueued();
        }
    }

    /// <summary>
    /// Gets: Total retract height (retract1 + retract2) alias of <see cref="LiftHeightTotal"/>
    /// </summary>
    public float RetractHeightTotal => LiftHeightTotal;

    /// <summary>
    /// Gets the retract height in mm
    /// </summary>
    public float RetractHeight => MathF.Round(LiftHeightTotal - _retractHeight2, 2);

    /// <summary>
    /// Gets the speed in mm/min for the retracts
    /// </summary>
    public float RetractSpeed
    {
        get => _retractSpeed;
        set
        {
            value = MathF.Round(value, 2);
            if (value <= 0) value = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomRetractSpeed, SlicerFile.RetractSpeed);
            if (!RaiseAndSetIfChanged(ref _retractSpeed, value)) return;
            SlicerFile.UpdatePrintTimeQueued();
        }
    }

    /// <summary>
    /// Gets or sets the retract acceleration in mm/s²
    /// </summary>
    public float RetractAcceleration
    {
        get => _retractAcceleration;
        set => RaiseAndSetIfChanged(ref _retractAcceleration, MathF.Round(Math.Max(value, 0), 2));
    }

    /// <summary>
    /// Gets or sets the second retract height in mm
    /// </summary>
    public float RetractHeight2
    {
        get => _retractHeight2;
        set
        {
            value = Math.Clamp(MathF.Round(value, 2), 0, RetractHeightTotal);
            RaiseAndSetIfChanged(ref _retractHeight2, value);
            RaisePropertyChanged(nameof(RetractHeight));
            RaisePropertyChanged(nameof(RetractHeightTotal));
            SlicerFile.UpdatePrintTimeQueued();
        }
    }

    /// <summary>
    /// Gets the speed in mm/min for the retracts
    /// </summary>
    public float RetractSpeed2
    {
        get => _retractSpeed2;
        set
        {
            value = MathF.Round(value, 2);
            if (value <= 0) value = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomRetractSpeed2, SlicerFile.RetractSpeed2);
            if (!RaiseAndSetIfChanged(ref _retractSpeed2, value)) return;
            SlicerFile.UpdatePrintTimeQueued();
        }
    }

    /// <summary>
    /// Gets or sets the second retract acceleration in mm/s²
    /// </summary>
    public float RetractAcceleration2
    {
        get => _retractAcceleration2;
        set => RaiseAndSetIfChanged(ref _retractAcceleration2, MathF.Round(Math.Max(value, 0), 2));
    }

    /// <summary>
    /// Gets or sets the pwm value from 0 to 255
    /// </summary>
    public byte LightPWM
    {
        get => _lightPWM;
        set =>
            //if (value == 0) value = SlicerFile.GetInitialLayerValueOrNormal(Index, SlicerFile.BottomLightPWM, SlicerFile.LightPWM);
            //if (value == 0) value = FileFormat.DefaultLightPWM;
            RaiseAndSetIfChanged(ref _lightPWM, value);
    }

    /// <summary>
    /// Gets or sets if this layer should be paused before printing
    /// </summary>
    public bool Pause
    {
        get => _pause;
        set => RaiseAndSetIfChanged(ref _pause, value);
    }

    /// <summary>
    /// Gets or sets if printer should be paused to change resin before printing this layer
    /// </summary>
    public bool ChangeResin
    {
        get => _changeResin;
        set => RaiseAndSetIfChanged(ref _changeResin, value);
    }

    /// <summary>
    /// Gets the minimum used speed in mm/min
    /// </summary>
    public float MinimumSpeed
    {
        get
        {
            float speed = float.MaxValue;
            if (LiftSpeed > 0) speed = Math.Min(speed, LiftSpeed);
            if (LiftSpeed2 > 0) speed = Math.Min(speed, LiftSpeed2);
            if (RetractSpeed > 0) speed = Math.Min(speed, RetractSpeed);
            if (RetractSpeed2 > 0) speed = Math.Min(speed, RetractSpeed2);
            if (Math.Abs(speed - float.MaxValue) < 0.01) return 0;

            return speed;
        }
    }

    /// <summary>
    /// Gets the maximum used speed in mm/min
    /// </summary>
    public float MaximumSpeed
    {
        get
        {
            float speed = LiftSpeed;
            speed = Math.Max(speed, LiftSpeed2);
            speed = Math.Max(speed, RetractSpeed);
            speed = Math.Max(speed, RetractSpeed2);

            return speed;
        }
    }

    /// <summary>
    /// Gets if this layer can be exposed to UV light
    /// </summary>
    public bool CanExpose => _exposureTime > 0 && _lightPWM > 0;

    /// <summary>
    /// Gets if this layer should be exposed to UV light, ie: if layer is empty or no exposure time then it useless to expose it
    /// </summary>
    public bool ShouldExpose => !IsEmpty && CanExpose;

    /// <summary>
    /// Gets the layer height in millimeters of this layer
    /// </summary>
    public float LayerHeight
    {
        get
        {
            if (IsFirstLayer) return _positionZ;
            var previousLayer = this;

            while ((previousLayer = previousLayer.PreviousLayer) is not null) // This cycle returns the correct layer height if two or more layers have the same position z
            {
                var layerHeight = RoundHeight(_positionZ - previousLayer.PositionZ);
                //Debug.WriteLine($"Layer {_index}-{previousLayer.Index}: {_positionZ} - {previousLayer.PositionZ}: {layerHeight}");
                if (layerHeight == 0f) continue;
                if (layerHeight < 0f) break;
                return layerHeight;
            }

            return SlicerFile.LayerHeight;
        }
    }

    /// <summary>
    /// Gets the computed material milliliters spent on this layer
    /// </summary>
    public float MaterialMilliliters
    {
        get => _materialMilliliters;
        set
        {
            if (SlicerFile is null) return;
            //var globalMilliliters = SlicerFile.MaterialMilliliters - _materialMilliliters;
            if (value < 0)
            {
                value = MathF.Round(GetVolume() / 1000f, 4);
            }

            if(!RaiseAndSetIfChanged(ref _materialMilliliters, value)) return;
            RaisePropertyChanged(nameof(MaterialMillilitersPercent));
            SlicerFile.MaterialMilliliters = -1; // Recalculate global
            //ParentLayerManager.MaterialMillilitersTimer.Stop();
            //if(!ParentLayerManager.MaterialMillilitersTimer.Enabled)
            //    ParentLayerManager.MaterialMillilitersTimer.Start();
        }
    }

    /// <summary>
    /// Gets the computed material milliliters percentage compared to the rest of the model
    /// </summary>
    public float MaterialMillilitersPercent => SlicerFile.MaterialMilliliters > 0 ? _materialMilliliters * 100 / SlicerFile.MaterialMilliliters : float.NaN;

    /// <summary>
    /// Gets the time estimate in seconds it takes for this layer to be printed
    /// </summary>
    public float PrintTime => MathF.Round(CalculatePrintTime(), 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Gets the time estimate in minutes and seconds it takes for this layer to be printed
    /// </summary>
    public string PrintTimeString => TimeSpan.FromSeconds(PrintTime).ToTimeString();

    /// <summary>
    /// Get the start time estimate in seconds when this layer should start at
    /// </summary>
    public float StartTime => MathF.Round(CalculateStartTime(30), 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Get the start time estimate in hours, minutes and seconds when this layer should start at
    /// </summary>
    public string StartTimeString => TimeSpan.FromSeconds(StartTime).ToTimeString();

    /// <summary>
    /// Get the end time estimate in seconds when this layer should end at
    /// </summary>
    public float EndTime => MathF.Round(CalculateStartTime(30) + CalculatePrintTime(), 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Get the end time estimate in hours, minutes and seconds when this layer should end at
    /// </summary>
    public string EndTimeString => TimeSpan.FromSeconds(EndTime).ToTimeString();

    private void InvalidateImage()
    {
        IsModified = true;
        SlicerFile.BoundingRectangle = Rectangle.Empty;
        _contours?.Dispose();
        _contours = null;
    }

    private static MatCompressor LayerCompressionCodecToMatCompressor(LayerCompressionCodec compressionCodec)
    {
        return compressionCodec switch
        {
            LayerCompressionCodec.Png => MatCompressorPngGreyScale.Instance,
            LayerCompressionCodec.GZip => MatCompressorGZip.Instance,
            LayerCompressionCodec.Deflate => MatCompressorDeflate.Instance,
            LayerCompressionCodec.Brotli => MatCompressorBrotli.Instance,
            LayerCompressionCodec.Lz4 => MatCompressorLz4.Instance,
            //LayerCompressionCodec.None => MatCompressorNone.Instance,
            _ => throw new ArgumentOutOfRangeException(nameof(compressionCodec), compressionCodec, null)
        };
    }

    /// <summary>
    /// Gets or sets the compression method used to cache the image
    /// </summary>
    public LayerCompressionCodec CompressionCodec
    {
        get => _compressionCodec;
        set
        {
            CompressedMat.Compressor = LayerCompressionCodecToMatCompressor(value);
            RaiseAndSetIfChanged(ref _compressionCodec, value);
        }
    }

    public CMat CompressedMat
    {
        get => _compressedMat;
        private set
        {
            _compressedMat = value;
            InvalidateImage();
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(CompressedPngBytes));
            RaisePropertyChanged(nameof(HaveImage));
        }
    }

    public byte[] CompressedPngBytes
    {
        get
        {
            if (_compressionCodec == LayerCompressionCodec.Png || _compressedMat.IsEmpty) return _compressedMat.CompressedBytes;

            using var mat = LayerMat;
            return mat.GetPngByes();
        }
        set
        {
            if (_compressionCodec == LayerCompressionCodec.Png || value.Length == 0)
            {
                _compressedMat.SetCompressedBytes(value, MatCompressorPngGreyScale.Instance);
                InvalidateImage();
            }
            else
            {
                var mat = new Mat();
                CvInvoke.Imdecode(value, ImreadModes.Grayscale, mat);
                LayerMat = mat;
            }
        }
    }

    /// <summary>
    /// True if this layer have a valid initialized image, otherwise false
    /// </summary>
    public bool HaveImage => _compressedMat.IsInitialized;

    /// <summary>
    /// Gets or sets a new image instance
    /// </summary>
    [XmlIgnore]
    [JsonInclude]
    public Mat LayerMat
    {
        get => SlicerFile.DecodeType != FileFormat.FileDecodeType.Full ? null! : _compressedMat.Mat;
        set
        {
            GetBoundingRectangle(value, true);
            if (value is null)
            {
                _compressedMat.SetEmptyCompressedBytes();
            }
            else if (_nonZeroPixelCount == 0)
            {
                _compressedMat.SetEmptyCompressedBytes(value, true);
            }
            else
            {
                using var matRoi = new MatRoi(value, _boundingRectangle);
                _compressedMat.Compress(matRoi);
            }

            InvalidateImage();
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(CompressedPngBytes));
            RaisePropertyChanged(nameof(HaveImage));
        }
    }

    /// <summary>
    /// Gets the layer mat with roi of it bounding rectangle
    /// </summary>
    public MatRoi LayerMatBoundingRectangle => new(LayerMat, BoundingRectangle, true);

    /// <summary>
    /// Gets the layer mat with roi of model bounding rectangle
    /// </summary>
    public MatRoi LayerMatModelBoundingRectangle => new(LayerMat, SlicerFile.BoundingRectangle, true);

    /// <summary>
    /// Gets the layer mat with a specified roi
    /// </summary>
    /// <param name="roi">Region of interest</param>
    /// <param name="emptyRoiBehaviour"></param>
    /// <returns></returns>
    public MatRoi GetLayerMat(Rectangle roi, EmptyRoiBehaviour emptyRoiBehaviour = EmptyRoiBehaviour.Continue) => new(LayerMat, roi, emptyRoiBehaviour, true);

    /// <summary>
    /// Gets the layer mat with bounding rectangle mat
    /// </summary>
    /// <param name="margin">Margin from bounding rectangle</param>
    /// <returns></returns>
    public MatRoi GetLayerMatBoundingRectangle(int margin) => new(LayerMat, GetBoundingRectangle(margin), true);

    /// <summary>
    /// Gets the layer mat with bounding rectangle mat
    /// </summary>
    /// <param name="marginX">X margin from bounding rectangle</param>
    /// <param name="marginY">Y margin from bounding rectangle</param>
    /// <returns></returns>
    public MatRoi GetLayerMatBoundingRectangle(int marginX, int marginY) => new(LayerMat, GetBoundingRectangle(marginX, marginY), true);

    /// <summary>
    /// Gets the layer mat with bounding rectangle mat
    /// </summary>
    /// <param name="margin">Margin from bounding rectangle</param>
    /// <returns></returns>
    public MatRoi GetLayerMatBoundingRectangle(Size margin) => new(LayerMat, GetBoundingRectangle(margin), true);

    /// <summary>
    /// Gets a new Brg image instance
    /// </summary>
    [XmlIgnore]
    [JsonInclude]
    public Mat BrgMat
    {
        get
        {
            var mat = LayerMat;
            if (mat is null) return null!;
            CvInvoke.CvtColor(mat, mat, ColorConversion.Gray2Bgr);
            return mat;
        }
    }

    /// <summary>
    /// Gets a computed layer filename, padding zeros are equal to layer count digits
    /// </summary>
    public string Filename => FormatFileNameWithLayerDigits("layer");

    /// <summary>
    /// Gets if layer image has been modified
    /// </summary>
    public bool IsModified
    {
        get => _isModified;
        set => RaiseAndSetIfChanged(ref _isModified, value);
    }

    /// <summary>
    /// Gets if this layer have same value parameters as global settings
    /// </summary>
    public bool IsUsingGlobalParameters
    {
        get
        {
            if (SlicerFile is null) return false; // Cant verify
            const float toleranceLayerHeight = HeightPrecisionIncrementFloat;
            const float tolerance = 0.01f;
            if (IsBottomLayer)
            {
                if (
                    (SlicerFile.CanUseLayerPositionZ && Math.Abs(RoundHeight(_positionZ - SlicerFile.LayerHeight * Number)) > toleranceLayerHeight) ||
                    (SlicerFile.CanUseLayerLightOffDelay && Math.Abs(_lightOffDelay - SlicerFile.BottomLightOffDelay) > tolerance) ||
                    (SlicerFile.CanUseLayerWaitTimeBeforeCure && Math.Abs(_waitTimeBeforeCure - SlicerFile.BottomWaitTimeBeforeCure) > tolerance) ||
                    (SlicerFile.CanUseLayerExposureTime && Math.Abs(_exposureTime - SlicerFile.BottomExposureTime) > tolerance) ||
                    (SlicerFile.CanUseLayerWaitTimeAfterCure && Math.Abs(_waitTimeAfterCure - SlicerFile.BottomWaitTimeAfterCure) > tolerance) ||
                    (SlicerFile.CanUseLayerLiftHeight && Math.Abs(_liftHeight - SlicerFile.BottomLiftHeight) > tolerance && Math.Abs(_liftHeight - SlicerFile.BottomLiftHeightTotal) > tolerance) ||
                    (SlicerFile.CanUseLayerLiftSpeed && Math.Abs(_liftSpeed - SlicerFile.BottomLiftSpeed) > tolerance) ||
                    (SlicerFile.CanUseLayerLiftAcceleration && Math.Abs(_liftAcceleration - SlicerFile.BottomLiftAcceleration) > tolerance) ||
                    (SlicerFile.CanUseLayerLiftHeight2 && Math.Abs(_liftHeight2 - SlicerFile.BottomLiftHeight2) > tolerance) ||
                    (SlicerFile.CanUseLayerLiftSpeed2 && Math.Abs(_liftSpeed2 - SlicerFile.BottomLiftSpeed2) > tolerance) ||
                    (SlicerFile.CanUseLayerLiftAcceleration2 && Math.Abs(_liftAcceleration2 - SlicerFile.BottomLiftAcceleration2) > tolerance) ||
                    (SlicerFile.CanUseLayerWaitTimeAfterLift && Math.Abs(_waitTimeAfterLift - SlicerFile.BottomWaitTimeAfterLift) > tolerance) ||
                    (SlicerFile.CanUseLayerRetractSpeed && Math.Abs(_retractSpeed - SlicerFile.BottomRetractSpeed) > tolerance) ||
                    (SlicerFile.CanUseLayerRetractAcceleration && Math.Abs(_retractAcceleration - SlicerFile.BottomRetractAcceleration) > tolerance) ||
                    (SlicerFile.CanUseLayerRetractHeight2 && Math.Abs(_retractHeight2 - SlicerFile.BottomRetractHeight2) > tolerance) ||
                    (SlicerFile.CanUseLayerRetractSpeed2 && Math.Abs(_retractSpeed2 - SlicerFile.BottomRetractSpeed2) > tolerance) ||
                    (SlicerFile.CanUseLayerRetractAcceleration2 && Math.Abs(_retractAcceleration2 - SlicerFile.BottomRetractAcceleration2) > tolerance) ||
                    (SlicerFile.CanUseLayerLightPWM && _lightPWM != SlicerFile.BottomLightPWM)
                )
                    return false;
            }
            else
            {
                if (
                    (SlicerFile.CanUseLayerPositionZ && Math.Abs(RoundHeight(_positionZ - SlicerFile.LayerHeight * Number)) > toleranceLayerHeight) ||
                    (SlicerFile.CanUseLayerLightOffDelay && Math.Abs(_lightOffDelay - SlicerFile.LightOffDelay) > tolerance) ||
                    (SlicerFile.CanUseLayerWaitTimeBeforeCure && Math.Abs(_waitTimeBeforeCure - SlicerFile.WaitTimeBeforeCure) > tolerance) ||
                    (SlicerFile.CanUseLayerExposureTime && !IsTransitionLayer && Math.Abs(_exposureTime - SlicerFile.ExposureTime) > tolerance) || // Fix for can't edit settings on menu https://github.com/sn4k3/UVtools/issues/507
                    //(SlicerFile.CanUseLayerExposureTime && exposureTime != SlicerFile.ExposureTime) ||
                    (SlicerFile.CanUseLayerWaitTimeAfterCure && Math.Abs(_waitTimeAfterCure - SlicerFile.WaitTimeAfterCure) > tolerance) ||
                    (SlicerFile.CanUseLayerLiftHeight && Math.Abs(_liftHeight - SlicerFile.LiftHeight) > tolerance && Math.Abs(_liftHeight - SlicerFile.LiftHeightTotal) > tolerance) ||
                    (SlicerFile.CanUseLayerLiftSpeed && Math.Abs(_liftSpeed - SlicerFile.LiftSpeed) > tolerance) ||
                    (SlicerFile.CanUseLayerLiftAcceleration && Math.Abs(_liftAcceleration - SlicerFile.LiftAcceleration) > tolerance) ||
                    (SlicerFile.CanUseLayerLiftHeight2 && Math.Abs(_liftHeight2 - SlicerFile.LiftHeight2) > tolerance) ||
                    (SlicerFile.CanUseLayerLiftSpeed2 && Math.Abs(_liftSpeed2 - SlicerFile.LiftSpeed2) > tolerance) ||
                    (SlicerFile.CanUseLayerLiftAcceleration2 && Math.Abs(_liftAcceleration2 - SlicerFile.LiftAcceleration2) > tolerance) ||
                    (SlicerFile.CanUseLayerWaitTimeAfterLift && Math.Abs(_waitTimeAfterLift - SlicerFile.WaitTimeAfterLift) > tolerance) ||
                    (SlicerFile.CanUseLayerRetractSpeed && Math.Abs(_retractSpeed - SlicerFile.RetractSpeed) > tolerance) ||
                    (SlicerFile.CanUseLayerRetractAcceleration && Math.Abs(_retractAcceleration - SlicerFile.RetractAcceleration) > tolerance) ||
                    (SlicerFile.CanUseLayerRetractHeight2 && Math.Abs(_retractHeight2 - SlicerFile.RetractHeight2) > tolerance) ||
                    (SlicerFile.CanUseLayerRetractSpeed2 && Math.Abs(_retractSpeed2 - SlicerFile.RetractSpeed2) > tolerance) ||
                    (SlicerFile.CanUseLayerRetractAcceleration2 && Math.Abs(_retractAcceleration2 - SlicerFile.RetractAcceleration2) > tolerance) ||
                    (SlicerFile.CanUseLayerLightPWM && _lightPWM != SlicerFile.LightPWM)
                )
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// True if this layer is using TSMC values, otherwise false
    /// </summary>
    public bool IsUsingTSMC => LiftHeight2 > 0 || RetractHeight2 > 0;

    /// <summary>
    /// Gets tree contours cache for this layer, however should call <see>
    ///     <cref>GetContours</cref>
    /// </see>
    /// instead with <see cref="LayerMat"/> instance.
    /// If not set it will calculate contours first
    /// </summary>
    public EmguContours Contours
    {
        get
        {
            if (_contours is not null) return _contours;
            using var matRoi = LayerMatBoundingRectangle;
            return GetContours(matRoi);
        }
    }

    /// <summary>
    /// Gets tree contours cache for this layer. If not set it will calculate contours first
    /// </summary>
    public EmguContours GetContours(IInputOutputArray mat, Point offset = default)
    {
        return _contours ??= new EmguContours(mat, RetrType.Tree, ChainApproxMethod.ChainApproxSimple, offset);
    }

    /// <summary>
    /// Gets tree contours cache for this layer.  If not set it will calculate contours first
    /// </summary>
    public EmguContours GetContours(MatRoi matRoi)
    {
        return _contours ??= new EmguContours(matRoi.RoiMat, RetrType.Tree, ChainApproxMethod.ChainApproxSimple, matRoi.Roi.Location);
    }

    #endregion

    #region Constructor

    public Layer(uint index, FileFormat slicerFile, LayerCompressionCodec? compressionMethod = null)
    {
        compressionMethod ??= CoreSettings.DefaultLayerCompressionCodec;
        _compressionCodec = compressionMethod.Value;

        _compressedMat = new CMat(LayerCompressionCodecToMatCompressor(_compressionCodec), slicerFile.Resolution);

        SlicerFile = slicerFile;
        _index = index;

        //if (slicerFile is null) return;
        _positionZ = SlicerFile.CalculatePositionZ(index);
        ResetParameters();
    }

    public Layer(uint index, byte[] pngBytes, FileFormat slicerFile, LayerCompressionCodec? compressionMethod = null) : this(index, slicerFile, compressionMethod)
    {
        CompressedPngBytes = pngBytes;
        _isModified = false;
    }

    public Layer(uint index, Mat layerMat, FileFormat slicerFile, LayerCompressionCodec? compressionMethod = null) : this(index, slicerFile, compressionMethod)
    {
        LayerMat = layerMat;
        _isModified = false;
    }

    public Layer(uint index, Stream stream, FileFormat slicerFile, LayerCompressionCodec? compressionMethod = null) : this(index, stream.ToArray(), slicerFile, compressionMethod)
    { }

    public Layer(FileFormat slicerFile, LayerCompressionCodec? compressionMethod = null) : this(0, slicerFile, compressionMethod)
    { }

    public Layer(byte[] pngBytes, FileFormat slicerFile, LayerCompressionCodec? compressionMethod = null) : this(0, pngBytes, slicerFile, compressionMethod)
    { }

    public Layer(Mat layerMat, FileFormat slicerFile, LayerCompressionCodec? compressionMethod = null) : this(0, layerMat, slicerFile, compressionMethod)
    { }

    public Layer(Stream stream, FileFormat slicerFile, LayerCompressionCodec? compressionMethod = null) : this(0, stream, slicerFile, compressionMethod) { }

    #endregion

    #region Equatables

    public static bool operator ==(Layer obj1, Layer obj2)
    {
        return obj1.Equals(obj2);
    }

    public static bool operator !=(Layer obj1, Layer obj2)
    {
        return !obj1.Equals(obj2);
    }

    public static bool operator >(Layer obj1, Layer obj2)
    {
        return obj1.Index > obj2.Index;
    }

    public static bool operator <(Layer obj1, Layer obj2)
    {
        return obj1.Index < obj2.Index;
    }

    public static bool operator >=(Layer obj1, Layer obj2)
    {
        return obj1.Index >= obj2.Index;
    }

    public static bool operator <=(Layer obj1, Layer obj2)
    {
        return obj1.Index <= obj2.Index;
    }

    public bool Equals(uint other)
    {
        return Index == other;
    }

    public bool Equals(Layer? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_index != other._index) return false;
        return _compressedMat.Equals(other.CompressedMat);
        //return Equals(_compressedBytes, other._compressedBytes);
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((Layer)obj);
    }

    public override int GetHashCode()
    {
        return CompressedMat.GetHashCode();
    }

    private sealed class IndexRelationalComparer : IComparer<Layer>
    {
        public int Compare(Layer? x, Layer? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (ReferenceEquals(null, y)) return 1;
            if (ReferenceEquals(null, x)) return -1;
            return x.Index.CompareTo(y.Index);
        }
    }

    public static IComparer<Layer> IndexComparer { get; } = new IndexRelationalComparer();



    #endregion

    #region Formaters

    public override string ToString()
    {
        return $"{nameof(Index)}: {Index}, " +
               $"{nameof(Filename)}: {Filename}, " +
               $"{nameof(NonZeroPixelCount)}: {NonZeroPixelCount}, " +
               $"{nameof(BoundingRectangle)}: {BoundingRectangle}, " +
               $"{nameof(FirstPixelPosition)}: {FirstPixelPosition}, " +
               $"{nameof(LastPixelPosition)}: {LastPixelPosition}, " +
               $"{nameof(IsBottomLayer)}: {IsBottomLayer}, " +
               $"{nameof(IsNormalLayer)}: {IsNormalLayer}, " +
               $"{nameof(LayerHeight)}: {LayerHeight}mm, " +
               $"{nameof(PositionZ)}: {PositionZ}mm, " +
               $"{nameof(LightOffDelay)}: {LightOffDelay}s, " +
               $"{nameof(WaitTimeBeforeCure)}: {WaitTimeBeforeCure}s, " +
               $"{nameof(ExposureTime)}: {ExposureTime}s, " +
               $"{nameof(WaitTimeAfterCure)}: {WaitTimeAfterCure}s, " +
               $"{nameof(LiftHeight)}: {LiftHeight}mm, " +
               $"{nameof(LiftSpeed)}: {LiftSpeed}mm/mim, " +
               $"{nameof(LiftAcceleration)}: {LiftAcceleration}mm/s², " +
               $"{nameof(LiftHeight2)}: {LiftHeight2}mm, " +
               $"{nameof(LiftSpeed2)}: {LiftSpeed2}mm/mim, " +
               $"{nameof(LiftAcceleration2)}: {LiftAcceleration2}mm/s², " +
               $"{nameof(WaitTimeAfterLift)}: {WaitTimeAfterLift}s, " +
               $"{nameof(RetractHeight)}: {RetractHeight}mm, " +
               $"{nameof(RetractSpeed)}: {RetractSpeed}mm/mim, " +
               $"{nameof(RetractAcceleration)}: {RetractAcceleration}mm/s², " +
               $"{nameof(RetractHeight2)}: {RetractHeight2}mm, " +
               $"{nameof(RetractSpeed2)}: {RetractSpeed2}mm/mim, " +
               $"{nameof(RetractAcceleration2)}: {RetractAcceleration2}mm/s², " +
               $"{nameof(LightPWM)}: {LightPWM}, " +
               $"{nameof(Pause)}: {Pause}, " +
               $"{nameof(ChangeResin)}: {ChangeResin}, " +
               $"{nameof(IsModified)}: {IsModified}, " +
               $"{nameof(IsUsingGlobalParameters)}: {IsUsingGlobalParameters}";
    }
    #endregion

    #region Methods

    /// <summary>
    /// Creates an empty <see cref="Mat"/> of file <see cref="Resolution"/> size
    /// </summary>
    /// <param name="initMat">True to black out the mat</param>
    /// <returns></returns>
    public Mat CreateMat(bool initMat = true)
    {
        return initMat
            ? EmguExtensions.InitMat(Resolution)
            : new Mat(Resolution, DepthType.Cv8U, 1);
    }

    /// <summary>
    /// Reset all parameters to the default values from the global parameters
    /// </summary>
    public void ResetParameters()
    {
        _lightOffDelay = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomLightOffDelay, SlicerFile.LightOffDelay);
        _waitTimeBeforeCure = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomWaitTimeBeforeCure, SlicerFile.WaitTimeBeforeCure);
        _exposureTime = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomExposureTime, SlicerFile.ExposureTime);
        _waitTimeAfterCure = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomWaitTimeAfterCure, SlicerFile.WaitTimeAfterCure);
        _liftHeight = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomLiftHeight, SlicerFile.LiftHeight);
        _liftSpeed = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomLiftSpeed, SlicerFile.LiftSpeed);
        _liftAcceleration = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomLiftAcceleration, SlicerFile.LiftAcceleration);
        _liftHeight2 = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomLiftHeight2, SlicerFile.LiftHeight2);
        _liftSpeed2 = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomLiftSpeed2, SlicerFile.LiftSpeed2);
        _liftAcceleration2 = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomLiftAcceleration2, SlicerFile.LiftAcceleration2);
        _waitTimeAfterLift = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomWaitTimeAfterLift, SlicerFile.WaitTimeAfterLift);
        _retractSpeed = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomRetractSpeed, SlicerFile.RetractSpeed);
        _retractAcceleration = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomRetractAcceleration, SlicerFile.RetractAcceleration);
        _retractHeight2 = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomRetractHeight2, SlicerFile.RetractHeight2);
        _retractSpeed2 = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomRetractSpeed2, SlicerFile.RetractSpeed2);
        _retractAcceleration2 = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomRetractAcceleration2, SlicerFile.RetractAcceleration2);
        _lightPWM = SlicerFile.GetBottomOrNormalValue(this, SlicerFile.BottomLightPWM, SlicerFile.LightPWM);
    }

    /// <summary>
    /// Gets the layer area (XY) in mm²
    /// Pixel size * number of pixels
    /// </summary>
    public float GetArea() => SlicerFile.PixelArea * _nonZeroPixelCount;


    /// <summary>
    /// Gets the layer area (XY) in mm²
    /// Pixel size * number of pixels
    /// </summary>
    public float GetArea(byte roundToDigits) => MathF.Round(GetArea(), roundToDigits);

    /// <summary>
    /// Gets the layer volume (XYZ) in mm^3
    /// Pixel size * number of pixels * layer height
    /// </summary>
    public float GetVolume() => GetArea() * LayerHeight;

    /// <summary>
    /// Gets the layer volume (XYZ) in mm^3
    /// Pixel size * number of pixels * layer height
    /// </summary>
    public float GetVolume(byte roundToDigits) => MathF.Round(GetArea() * LayerHeight, roundToDigits);

    /// <summary>
    /// Calculates the time estimate in seconds it takes for this layer to be printed
    /// </summary>
    /// <returns></returns>
    public float CalculatePrintTime(float extraTime = 0)
    {
        float time = extraTime;
        var motorTime = CalculateMotorMovementTime();
        if (SlicerFile.HaveTiltingVat)
        {
            motorTime = 4;
        }

        time += WaitTimeBeforeCure + ExposureTime + WaitTimeAfterCure + WaitTimeAfterLift;
        if (SlicerFile.SupportGCode)
        {
            time += motorTime;
            if (WaitTimeBeforeCure <= 0)
            {
                time += LightOffDelay;
            }
        }
        else
        {
            time += motorTime > LightOffDelay ? motorTime : LightOffDelay;
        }

        return time;
    }

    /// <summary>
    /// Calculates the start time estimate in seconds when this layer should start at
    /// </summary>
    /// <returns></returns>
    public float CalculateStartTime(float extraTime = 0)
    {
        float time = extraTime;
        for (int i = 0; i < Index; i++)
        {
            time += SlicerFile[i].PrintTime;
        }

        return time;
    }

    /// <summary>
    /// Calculates the time the motor movements take to complete
    /// </summary>
    /// <param name="extraTime"></param>
    /// <returns></returns>
    public float CalculateMotorMovementTime(float extraTime = 0)
    {
        return OperationCalculator.LightOffDelayC.CalculateSeconds(this, extraTime);
    }

    public float CalculateLightOffDelay(float extraTime = 0)
    {
        if (SlicerFile is null) return OperationCalculator.LightOffDelayC.CalculateSeconds(this, extraTime);
        return SlicerFile.SupportGCode ? extraTime : OperationCalculator.LightOffDelayC.CalculateSeconds(this, extraTime);
    }

    public void SetLightOffDelay(float extraTime = 0)
    {
        LightOffDelay = CalculateLightOffDelay(extraTime);
    }

    /// <summary>
    /// Gets the wait time before cure, if not available calculate it from light off delay
    /// </summary>
    /// <returns></returns>
    public float GetWaitTimeBeforeCure()
    {
        if (SlicerFile.CanUseLayerWaitTimeBeforeCure)
        {
            return WaitTimeBeforeCure;
        }

        if (SlicerFile.CanUseLayerLightOffDelay)
        {
            return (float)Math.Max(0, Math.Round(LightOffDelay - CalculateLightOffDelay(), 2));
        }

        return 0;
    }

    /// <summary>
    /// Attempt to set wait time before cure if supported, otherwise fallback to light-off delay
    /// </summary>
    /// <param name="time">The time to set</param>
    /// <param name="zeroLightOffDelayCalculateBase">When true and time is zero, it will calculate light-off delay without extra time, otherwise false to set light-off delay to 0 when time is 0</param>
    public void SetWaitTimeBeforeCureOrLightOffDelay(float time = 0, bool zeroLightOffDelayCalculateBase = false)
    {
        if (SlicerFile.CanUseLayerWaitTimeBeforeCure)
        {
            LightOffDelay = 0;
            WaitTimeBeforeCure = time;
        }
        else if(SlicerFile.CanUseLayerLightOffDelay)
        {
            if (time == 0 && !zeroLightOffDelayCalculateBase)
            {
                LightOffDelay = 0;
                return;
            }
            SetLightOffDelay(time);
        }
    }

    /// <summary>
    /// Zero all 'wait times / delays' for this layer
    /// </summary>
    public void SetNoDelays()
    {
        LightOffDelay = 0;
        WaitTimeBeforeCure = 0;
        WaitTimeAfterCure = 0;
        WaitTimeAfterLift = 0;
    }

    /// <summary>
    /// Clear all pauses for this layer
    /// </summary>
    public void ClearPauses()
    {
        Pause = false;
        ChangeResin = false;
    }

    public string FormatFileName(string prepend  = "", byte padDigits = 0, IndexStartNumber layerIndexStartNumber = default, string appendExt = ".png")
    {
        var index = Index;
        if (layerIndexStartNumber == IndexStartNumber.One)
        {
            index++;
        }
        return string.Format($"{{0}}{{1:D{padDigits}}}{{2}}", prepend, index, appendExt);
    }

    public string FormatFileName(byte padDigits, IndexStartNumber layerIndexStartNumber = default, string appendExt = ".png")
        => FormatFileName(string.Empty, padDigits, layerIndexStartNumber, appendExt);

    public string FormatFileNameWithLayerDigits(string prepend = "", IndexStartNumber layerIndexStartNumber = default, string appendExt = ".png")
        => FormatFileName(prepend, SlicerFile.LayerDigits, layerIndexStartNumber, appendExt);


    public Rectangle GetBoundingRectangle(Mat? mat = null, bool reCalculate = false)
    {
        if (_nonZeroPixelCount > 0 && !reCalculate)
        {
            return _boundingRectangle;
        }
        bool needDispose = false;
        if (mat is null)
        {
            if (!HaveImage)
            {
                NonZeroPixelCount = 0;
                return Rectangle.Empty;
            }
            mat = LayerMat;
            needDispose = true;
        }


        //NonZeroPixelCount = (uint)CvInvoke.CountNonZero(mat);
        //BoundingRectangle = _nonZeroPixelCount > 0 ? CvInvoke.BoundingRectangle(mat) : Rectangle.Empty;
        BoundingRectangle = CvInvoke.BoundingRectangle(mat);
        if (_boundingRectangle.IsEmpty)
        {
            NonZeroPixelCount = 0;
        }
        else
        {
            using var roiMat = mat.Roi(_boundingRectangle);
            NonZeroPixelCount = (uint)CvInvoke.CountNonZero(roiMat);

            // Compute first and last pixel
            var span = roiMat.GetRowByteReadOnlySpan(0);
            var step = mat.GetRealStep();
            var yOffset = step * BoundingRectangle.Y;
            for (var i = 0; i < span.Length; i++)
            {
                if (span[i] == 0) continue;
                var xOffset = BoundingRectangle.X + i;
                FirstPixelIndex = (uint) (yOffset + xOffset);
                FirstPixelPosition = new Point(xOffset, BoundingRectangle.Y);
                break;
            }

            span = roiMat.GetRowByteReadOnlySpan(roiMat.Height - 1);
            yOffset = step * BoundingRectangle.Bottom;
            for (var i = span.Length - 1; i >= 0; i--)
            {
                if (span[i] == 0) continue;
                LastPixelIndex = (uint) (yOffset + BoundingRectangle.X + i);
                LastPixelPosition = new Point(BoundingRectangle.Right - (span.Length - i), BoundingRectangle.Bottom - 1);
                break;
            }
        }

        /* // Test
        mat.SetByte((int)FirstPixelIndex, 255);
        mat.SetByte(FirstPixelPosition, 255);
        mat.SetByte((int)LastPixelIndex, 255);
        mat.SetByte(LastPixelPosition, 255);
        mat.Save("D:\\test.png");
        */

        if (needDispose) mat.Dispose();

        return BoundingRectangle;
    }

    public Rectangle GetBoundingRectangle(int marginX, int marginY)
    {
        var rect = BoundingRectangle;
        if (marginX == 0 && marginY == 0) return rect;
        rect.Inflate(marginX / 2, marginY / 2);
        SlicerFile.SanitizeBoundingRectangle(ref rect);
        return rect;
    }

    public Rectangle GetBoundingRectangle(int margin) => GetBoundingRectangle(margin, margin);
    public Rectangle GetBoundingRectangle(Size margin) => GetBoundingRectangle(margin.Width, margin.Height);

    public bool SetValueFromPrintParameterModifier(FileFormat.PrintParameterModifier modifier, decimal value)
    {
        if (ReferenceEquals(modifier, FileFormat.PrintParameterModifier.PositionZ))
        {
            PositionZ = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, FileFormat.PrintParameterModifier.LightOffDelay))
        {
            LightOffDelay = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, FileFormat.PrintParameterModifier.WaitTimeBeforeCure))
        {
            WaitTimeBeforeCure = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, FileFormat.PrintParameterModifier.ExposureTime))
        {
            ExposureTime = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, FileFormat.PrintParameterModifier.WaitTimeAfterCure))
        {
            WaitTimeAfterCure = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, FileFormat.PrintParameterModifier.LiftHeight))
        {
            LiftHeight = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, FileFormat.PrintParameterModifier.LiftSpeed))
        {
            LiftSpeed = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, FileFormat.PrintParameterModifier.LiftHeight2))
        {
            LiftHeight2 = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, FileFormat.PrintParameterModifier.LiftSpeed2))
        {
            LiftSpeed2 = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, FileFormat.PrintParameterModifier.WaitTimeAfterLift))
        {
            WaitTimeAfterLift = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, FileFormat.PrintParameterModifier.RetractSpeed))
        {
            RetractSpeed = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, FileFormat.PrintParameterModifier.RetractHeight2))
        {
            RetractHeight2 = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, FileFormat.PrintParameterModifier.RetractSpeed2))
        {
            RetractSpeed2 = (float)value;
            return true;
        }

        if (ReferenceEquals(modifier, FileFormat.PrintParameterModifier.LightPWM))
        {
            LightPWM = (byte)value;
            return true;
        }

        if (ReferenceEquals(modifier, FileFormat.PrintParameterModifier.Pause))
        {
            Pause = Convert.ToBoolean(value);
            return true;
        }

        if (ReferenceEquals(modifier, FileFormat.PrintParameterModifier.ChangeResin))
        {
            ChangeResin = Convert.ToBoolean(value);
            return true;
        }

        return false;
    }

    public byte SetValuesFromPrintParametersModifiers(FileFormat.PrintParameterModifier[] modifiers)
    {
        if (modifiers.Length == 0) return 0;
        byte changed = 0;
        foreach (var modifier in modifiers)
        {
            if (!modifier.HasChanged) continue;
            SetValueFromPrintParameterModifier(modifier, modifier.NewValue);
            changed++;
        }

        return changed;
    }

    /// <summary>
    /// Copy all parameters from this layer to a target layer
    /// </summary>
    /// <param name="layer"></param>
    public void CopyParametersTo(Layer layer)
    {
        CopyWaitTimesTo(layer);
        CopyExposureTo(layer);
        CopyLiftTo(layer);
    }

    /// <summary>
    /// Copy all exposure parameters from this layer to an target layer
    /// </summary>
    /// <param name="layer"></param>
    public void CopyExposureTo(Layer layer)
    {
        layer.ExposureTime = _exposureTime;
        layer.LightPWM = _lightPWM;
    }

    /// <summary>
    /// Copy all wait parameters from this layer to an target layer
    /// </summary>
    /// <param name="layer"></param>
    public void CopyWaitTimesTo(Layer layer)
    {
        layer.LightOffDelay = _lightOffDelay;
        layer.WaitTimeBeforeCure = _waitTimeBeforeCure;
        layer.WaitTimeAfterCure = _waitTimeAfterCure;
        layer.WaitTimeAfterLift = _waitTimeAfterLift;
    }

    /// <summary>
    /// Copy all lift parameters from this layer to an target layer
    /// </summary>
    /// <param name="layer"></param>
    public void CopyLiftTo(Layer layer)
    {
        layer.LiftHeight = _liftHeight;
        layer.LiftHeight2 = _liftHeight2;
        layer.LiftSpeed = _liftSpeed;
        layer.LiftSpeed2 = _liftSpeed2;
        layer.RetractHeight2 = _retractHeight2;
        layer.RetractSpeed =  _retractSpeed;
        layer.RetractSpeed2 = _retractSpeed2;
    }

    /// <summary>
    /// Copy the image and related parameters from this layer to an target layer
    /// </summary>
    /// <param name="layer"></param>
    public void CopyImageTo(Layer layer)
    {
        _compressedMat.CopyTo(layer._compressedMat);
        layer._contours = _contours?.Clone();
        layer.BoundingRectangle = _boundingRectangle;
        layer.NonZeroPixelCount = _nonZeroPixelCount;
        layer.FirstPixelIndex = _firstPixelIndex;
        layer.FirstPixelPosition = _firstPixelPosition;
        layer.LastPixelIndex = _lastPixelIndex;
        layer.LastPixelPosition = _lastPixelPosition;
    }

    public Layer Clone()
    {
        var layer = (Layer)MemberwiseClone();
        layer._compressedMat = _compressedMat.Clone();
        layer._contours = _contours?.Clone();
        //Debug.WriteLine(ReferenceEquals(_compressedBytes, layer.CompressedBytes));
        return layer;
        /*return new (_index, CompressedBytes?.ToArray()!, SlicerFile)
        {
            _compressionMethod = _compressionMethod,
            _positionZ = _positionZ,
            _lightOffDelay = _lightOffDelay,
            _waitTimeBeforeCure = _waitTimeBeforeCure,
            _exposureTime = _exposureTime,
            _waitTimeAfterCure = _waitTimeAfterCure,
            _liftHeight = _liftHeight,
            _liftSpeed = _liftSpeed,
            _liftHeight2 = _liftHeight2,
            _liftSpeed2 = _liftSpeed2,
            _waitTimeAfterLift = _waitTimeAfterLift,
            _retractSpeed = _retractSpeed,
            _retractHeight2 = _retractHeight2,
            _retractSpeed2 = _retractSpeed2,
            _lightPWM = _lightPWM,
            _boundingRectangle = _boundingRectangle,
            _nonZeroPixelCount = _nonZeroPixelCount,
            _isModified = _isModified,
            _materialMilliliters = _materialMilliliters,
        };*/
    }

    public Layer[] Clone(uint times)
    {
        var layers = new Layer[times];
        for (int i = 0; i < times; i++)
        {
            layers[i] = Clone();
        }

        return layers;
    }
    #endregion

    #region Static Methods

    /// <summary>
    /// Gets the bounding rectangle that is the union of a collection of layers
    /// </summary>
    /// <param name="layers">Layer collection</param>
    /// <returns></returns>
    public static Rectangle GetBoundingRectangleUnion(params Layer[] layers)
    {
        var rect = Rectangle.Empty;
        foreach (var layer in layers)
        {
            if(layer.BoundingRectangle.IsEmpty) continue;
            rect = rect.IsEmpty ? layer.BoundingRectangle : Rectangle.Union(rect, layer.BoundingRectangle);
        }

        return rect;
    }

    public static float RoundHeight(float height) => MathF.Round(height, HeightPrecision, MidpointRounding.AwayFromZero);
    public static double RoundHeight(double height) => Math.Round(height, HeightPrecision, MidpointRounding.AwayFromZero);
    public static decimal RoundHeight(decimal height) => Math.Round(height, HeightPrecision, MidpointRounding.AwayFromZero);

    public static string ShowHeight(float height) => string.Format($"{{0:F{HeightPrecision}}}", height);
    public static string ShowHeight(double height) => string.Format($"{{0:F{HeightPrecision}}}", height);
    public static string ShowHeight(decimal height) => string.Format($"{{0:F{HeightPrecision}}}", height);

    public static Layer[] CloneLayers(Layer[] layers)
    {
        var clonedLayers = new Layer[layers.Length];
        for (uint layerIndex = 0; layerIndex < layers.Length; layerIndex++)
        {
            clonedLayers[layerIndex] = layers[layerIndex].Clone();
        }
        return clonedLayers;
    }

    #endregion
}