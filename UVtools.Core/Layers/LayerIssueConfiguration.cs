﻿/*
 *                     GNU AFFERO GENERAL PUBLIC LICENSE
 *                       Version 3, 19 November 2007
 *  Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
 *  Everyone is permitted to copy and distribute verbatim copies
 *  of this license document, but changing it is not allowed.
 */

using System;
using System.Collections.Generic;

namespace UVtools.Core.Layers;

#region LayerIssue Class

public sealed class IssuesDetectionConfiguration
{
    public IslandDetectionConfiguration IslandConfig { get; set; } = new();
    public OverhangDetectionConfiguration OverhangConfig { get; set; } = new();
    public ResinTrapDetectionConfiguration ResinTrapConfig { get; set; } = new();
    public TouchingBoundDetectionConfiguration TouchingBoundConfig { get; set; } = new();
    public PrintHeightDetectionConfiguration PrintHeightConfig { get; set; } = new();
    public EmptyLayerDetectionConfiguration EmptyLayerConfig { get; set; } = new();

    public DetectionConfiguration[] Configurations =>
    [
        IslandConfig,
        OverhangConfig,
        ResinTrapConfig,
        TouchingBoundConfig,
        PrintHeightConfig,
        EmptyLayerConfig
    ];

    public IssuesDetectionConfiguration() { }

    public IssuesDetectionConfiguration(
        IslandDetectionConfiguration islandConfig,
        OverhangDetectionConfiguration overhangConfig, 
        ResinTrapDetectionConfiguration resinTrapConfig, 
        TouchingBoundDetectionConfiguration touchingBoundConfig,
        PrintHeightDetectionConfiguration printHeightConfig,
        EmptyLayerDetectionConfiguration emptyLayerConfig)
    {
        IslandConfig = islandConfig;
        OverhangConfig = overhangConfig;
        ResinTrapConfig = resinTrapConfig;
        TouchingBoundConfig = touchingBoundConfig;
        PrintHeightConfig = printHeightConfig;
        EmptyLayerConfig = emptyLayerConfig;
    }

    public void Deconstruct(out IslandDetectionConfiguration islandConfig, out OverhangDetectionConfiguration overhangConfig, out ResinTrapDetectionConfiguration resinTrapConfig, out TouchingBoundDetectionConfiguration touchingBoundConfig, out PrintHeightDetectionConfiguration printHeightConfig, out EmptyLayerDetectionConfiguration emptyLayerConfig)
    {
        islandConfig = IslandConfig;
        overhangConfig = OverhangConfig;
        resinTrapConfig = ResinTrapConfig;
        touchingBoundConfig = TouchingBoundConfig;
        printHeightConfig = PrintHeightConfig;
        emptyLayerConfig = EmptyLayerConfig;
    }


    public void EnableAll()
    {
        foreach (var config in Configurations)
        {
            config.Enabled = true;
        }
    }

    public void DisableAll()
    {
        foreach (var config in Configurations)
        {
            config.Enabled = false;
        }
    }

    public IssuesDetectionConfiguration Clone()
    {
        var config = new IssuesDetectionConfiguration(
            (IslandDetectionConfiguration) IslandConfig.Clone(),
            (OverhangDetectionConfiguration) OverhangConfig.Clone(),
            (ResinTrapDetectionConfiguration) ResinTrapConfig.Clone(),
            (TouchingBoundDetectionConfiguration) TouchingBoundConfig.Clone(),
            (PrintHeightDetectionConfiguration) PrintHeightConfig.Clone(),
            (EmptyLayerDetectionConfiguration) EmptyLayerConfig.Clone()
            );
        return config;
    }
}

public class DetectionConfiguration : ICloneable
{
    /// <summary>
    /// Gets or sets if the detection is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    public DetectionConfiguration() { }

    public DetectionConfiguration(bool enabled)
    {
        Enabled = enabled;
    }

    public void Enable() => Enabled = true;
    public void Disable() => Enabled = false;

    public object Clone()
    {
        return MemberwiseClone();
    }
}

public sealed class IslandDetectionConfiguration : DetectionConfiguration
{
    /// <summary>
    /// Gets or sets a list of layers to check for islands, absent layers will not be checked.
    /// Set to null to check every layer
    /// </summary>
    public List<uint>? WhiteListLayers { get; set; } = null;

    /// <summary>
    /// Combines the island and overhang detections for a better more realistic detection and to discard false-positives. (Slower)
    /// If enabled, and when a island is found, it will check for overhangs on that same island, if no overhang found then the island will be discarded and considered safe, otherwise it will flag as an island issue.
    /// Note: Overhangs settings will be used to configure the detection. Enabling Overhangs is not required for this procedure to work.
    /// </summary>
    public bool EnhancedDetection { get; set; } = true;

    /// <summary>
    /// Gets the setting for whether or not diagonal bonds are considered when evaluation islands.
    /// If true, all 8 neighbors of a pixel (including diagonals) will be considered when finding
    /// individual components on the layer, if false only 4 neighbors (right, left, above, below)
    /// will be considered..
    /// </summary>
    public bool AllowDiagonalBonds { get; set; } = false;

    /// <summary>
    /// Gets or sets the binary threshold, all pixels equal or below this value will turn in black, otherwise white
    /// Set to 0 to disable this operation 
    /// </summary>
    public byte BinaryThreshold { get; set; } = 1;

    /// <summary>
    /// Gets the required pixel area to consider process a island (0-65535)
    /// </summary>
    public ushort RequiredAreaToProcessCheck { get; set; } = 1;

    /// <summary>
    /// Gets the required brightness for check a pixel under a island (0-255)
    /// </summary>
    public byte RequiredPixelBrightnessToProcessCheck { get; set; } = 10;

    /// <summary>
    /// Gets the required number of pixels to support a island and discard it as a issue (0-255)
    /// </summary>
    public byte RequiredPixelsToSupport { get; set; } = 10;

    /// <summary>
    /// Gets the required multiplier from the island pixels to support same island and discard it as a issue
    /// </summary>
    public decimal RequiredPixelsToSupportMultiplier { get; set; } = 0.25m;

    /// <summary>
    /// Gets the required brightness of supporting pixels to count as a valid support (0-255)
    /// </summary>
    public byte RequiredPixelBrightnessToSupport { get; set; } = 150;

    public IslandDetectionConfiguration()
    { }

    public IslandDetectionConfiguration(bool enabled) : base(enabled)
    { }
}

/// <summary>
/// Overhang configuration
/// </summary>
public sealed class OverhangDetectionConfiguration : DetectionConfiguration
{
    /// <summary>
    /// Gets or sets a list of layers to check for overhangs, absent layers will not be checked.
    /// Set to null to check every layer
    /// </summary>
    public List<uint>? WhiteListLayers { get; set; } = null;

    /// <summary>
    /// Gets or sets if should take in consideration the islands, if yes a island can't be a overhang at same time, otherwise islands and overhangs can be shared
    /// </summary>
    public bool IndependentFromIslands { get; set; } = true;

    /// <summary>
    /// After compute overhangs, masses with a number of pixels bellow this number will be discarded (Not a overhang)
    /// </summary>
    public byte RequiredPixelsToConsider { get; set; } = 1;
        
    /// <summary>
    /// Previous layer will be subtracted from current layer, after will erode by this value.
    /// The survived pixels are potential overhangs.
    /// </summary>
    public byte ErodeIterations { get; set; } = 40;

    public OverhangDetectionConfiguration()
    { }

    public OverhangDetectionConfiguration(bool enabled) : base(enabled)
    { }
}

public sealed class ResinTrapDetectionConfiguration : DetectionConfiguration
{
    /// <summary>
    /// Gets or sets the starting layer index for the detection which will also be considered a drain layer.
    /// Use this setting to bypass complicated rafts by selected the model first real layer.
    /// </summary>
    public uint StartLayerIndex { get; set; }

    /// <summary>
    /// Gets or sets the binary threshold, all pixels equal or below this value will turn in black, otherwise white
    /// Set to 0 to disable this operation
    /// </summary>
    public byte BinaryThreshold { get; set; } = 100;

    /// <summary>
    /// Gets the required area size (x*y) to consider process a hollow area (0-255)
    /// </summary>
    public byte RequiredAreaToProcessCheck { get; set; } = 4;

    /// <summary>
    /// Gets the number of black pixels required to consider a drain
    /// </summary>
    public byte RequiredBlackPixelsToDrain { get; set; } = 10;

    /// <summary>
    /// Gets the maximum pixel brightness to be a drain pixel (0-150)
    /// </summary>
    public byte MaximumPixelBrightnessToDrain { get; set; } = 30;

    /// <summary>
    /// Gets if suction cups can also be detected together with resin traps
    /// </summary>
    public bool DetectSuctionCups { get; set; } = true;

    /// <summary>
    /// Required minimum area to be considered a suction cup
    /// </summary>
    public uint RequiredAreaToConsiderSuctionCup { get; set; } = 100;

    /// <summary>
    /// Required minimum height (in mm) to be considered a suction cup
    /// </summary>
    public decimal RequiredHeightToConsiderSuctionCup { get; set; } = 0.5m;


    public ResinTrapDetectionConfiguration()
    { }

    public ResinTrapDetectionConfiguration(bool enabled) : base(enabled)
    { }
}


public sealed class TouchingBoundDetectionConfiguration : DetectionConfiguration
{
    /// <summary>
    /// Gets the minimum pixel brightness to be a touching bound
    /// </summary>
    public byte MinimumPixelBrightness { get; set; } = 127;

    /// <summary>
    /// Gets or sets the margin in pixels from left edge to check for touching white pixels
    /// </summary>
    public byte MarginLeft { get; set; } = 5;

    /// <summary>
    /// Gets or sets the margin in pixels from top to check for touching white pixels
    /// </summary>
    public byte MarginTop { get; set; } = 5;

    /// <summary>
    /// Gets or sets the margin in pixels from right edge to check for touching white pixels
    /// </summary>
    public byte MarginRight { get; set; } = 5;

    /// <summary>
    /// Gets or sets the margin in pixels from bottom edge to check for touching white pixels
    /// </summary>
    public byte MarginBottom { get; set; } = 5;


    public TouchingBoundDetectionConfiguration()
    { }

    public TouchingBoundDetectionConfiguration(bool enabled) : base(enabled)
    { }
}

public sealed class PrintHeightDetectionConfiguration : DetectionConfiguration
{
    /// <summary>
    /// Get the offset from top to sum to printer max Z height
    /// </summary>
    public float Offset { get; set; }

    public PrintHeightDetectionConfiguration()
    { }

    public PrintHeightDetectionConfiguration(bool enabled) : base(enabled)
    { }
}

public sealed class EmptyLayerDetectionConfiguration : DetectionConfiguration
{
    /// <summary>
    /// <para>Gets or sets to ignore the starting empty layers.</para>
    /// <para>True to ignore starting empty layers, otherwise false.</para>
    /// </summary>
    public bool IgnoreStartingEmptyLayers { get; set; }

    /// <summary>
    /// <para>Gets or sets to ignore the loose empty layers that are not on start nor in end.</para>
    /// <para>True to ignore loose empty layers, otherwise false.</para>
    /// </summary>
    public bool IgnoreLooseEmptyLayers { get; set; }

    /// <summary>
    /// <para>Gets or sets to ignore the ending empty layers.</para>
    /// <para>True to ignore ending empty layers, otherwise false.</para>
    /// </summary>
    public bool IgnoreEndingEmptyLayers { get; set; }

    public EmptyLayerDetectionConfiguration()
    { }

    public EmptyLayerDetectionConfiguration(bool enabled) : base(enabled)
    { }
}

#endregion