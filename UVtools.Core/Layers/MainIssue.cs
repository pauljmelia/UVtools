﻿/*
 *                     GNU AFFERO GENERAL PUBLIC LICENSE
 *                       Version 3, 19 November 2007
 *  Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
 *  Everyone is permitted to copy and distribute verbatim copies
 *  of this license document, but changing it is not allowed.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Text.Json.Serialization;
using System.Xml.Serialization;
using UVtools.Core.Extensions;
using ZLinq;

namespace UVtools.Core.Layers;

[XmlInclude(typeof(IssueOfContours))]
[XmlInclude(typeof(IssueOfPoints))]
public class MainIssue : IReadOnlyList<Issue>
{
    public enum IssueType : byte
    {
        Island,
        Overhang,
        ResinTrap,
        SuctionCup,
        TouchingBound,
        PrintHeight,
        EmptyLayer,
        Debug
        //HoleSandwich,
    }

    public bool IsIsland => Type == IssueType.Island;
    public bool IsOverhang => Type == IssueType.Overhang;
    public bool IsResinTrap => Type == IssueType.ResinTrap;
    public bool IsSuctionCup => Type == IssueType.SuctionCup;
    public bool IsTouchingBound => Type == IssueType.TouchingBound;
    public bool IsPrintHeight => Type == IssueType.PrintHeight;
    public bool IsEmptyLayer => Type == IssueType.EmptyLayer;
    public bool IsDebug => Type == IssueType.Debug;

    /// <summary>
    /// Gets the issue type associated
    /// </summary>
    public IssueType Type { get; init; }

    /// <summary>
    /// Gets the layer where issue is present and starts
    /// </summary>
    [JsonIgnore]
    public Layer StartLayer => Childs[0].Layer;

    /// <summary>
    /// Gets the layer where issue ends
    /// </summary>
    [JsonIgnore]
    public Layer EndLayer => Childs[^1].Layer;

    /// <summary>
    /// Gets the layer index
    /// </summary>
    public uint StartLayerIndex => StartLayer.Index;

    /// <summary>
    /// Gets the layer index
    /// </summary>
    public uint EndLayerIndex => EndLayer.Index;

    /// <summary>
    /// Gets the number of layers in this range
    /// </summary>
    public uint LayerRangeCount => 1 + EndLayerIndex - StartLayerIndex;

    public string LayerInfoString => StartLayerIndex == EndLayerIndex
        ? $"{StartLayerIndex}"
        : $"{StartLayerIndex}-{EndLayerIndex}  ({LayerRangeCount})";

    /// <summary>
    /// Gets the total height that represents this issue
    /// </summary>
    public float TotalHeight => Layer.RoundHeight(StartLayer.LayerHeight + EndLayer.PositionZ - StartLayer.PositionZ);

    /// <summary>
    /// Gets the bounding rectangle of the area
    /// </summary>
    public Rectangle BoundingRectangle { get; init; }

    /// <summary>
    /// Gets the area of the issue
    /// </summary>
    public uint PixelCount { get; init; }

    /// <summary>
    /// Gets the area of the issue
    /// </summary>
    public double Area { get; init; }

    /// <summary>
    /// Gets the area character, either ² or ³
    /// </summary>
    public char AreaChar => LayerRangeCount > 1 ? '³' : '²';

    /// <summary>
    /// Gets all issues inside this main issue
    /// </summary>
    public Issue[] Childs { get; init; } = [];

    public MainIssue() { }

    public MainIssue(IssueType type, Rectangle boundingRectangle = default)
    {
        Type = type;
        BoundingRectangle = boundingRectangle;
        Area = boundingRectangle.Area();
    }

    public MainIssue(IssueType type, Issue issue) : this(type, issue.BoundingRectangle)
    {
        Childs = [issue];
        issue.Parent = this;
        PixelCount = issue.PixelsCount;
        Area = issue switch
        {
            IssueOfPoints => issue.PixelsCount,
            IssueOfContours => issue.Area,
            _ => issue.Area
        };
    }

    public MainIssue(IssueType type, IEnumerable<Issue> issues) : this(type)
    {
        foreach (var issue in issues)
        {
            issue.Parent = this;
            var layerHeightInPixels = issue.Layer.SlicerFile.MillimetersToPixelsF(issue.Layer.LayerHeight, 20);
            Area += issue.Area * layerHeightInPixels;
            PixelCount += issue.PixelsCount;
            if (issue.BoundingRectangle.IsEmpty) continue;
            if (BoundingRectangle.IsEmpty)
            {
                BoundingRectangle = issue.BoundingRectangle;
                continue;
            }

            BoundingRectangle = Rectangle.Union(BoundingRectangle, issue.BoundingRectangle);
        }

        if (Childs.Length == 1)
        {
            Area = Childs[0].Area;
            Childs = issues.AsValueEnumerable().ToArray();
        }
        else
        {
            Childs = issues.AsValueEnumerable().OrderBy(issue => issue.LayerIndex).ToArray();
        }

        Area = Math.Round(Area, 3);
    }

    private void Sort()
    {
        if (Childs.Length < 1) return;
        Array.Sort(Childs, (issue, issue1) => issue.LayerIndex.CompareTo(issue1.LayerIndex));
    }

    public bool IsIssueInBetween(int layerIndex) => layerIndex >= StartLayerIndex && layerIndex <= EndLayerIndex;
    public bool IsIssueInBetween(uint layerIndex) => layerIndex >= StartLayerIndex && layerIndex <= EndLayerIndex;
    public bool IsIssueInBetween(Layer layer) => IsIssueInBetween(layer.Index);


    public IEnumerator<Issue> GetEnumerator()
    {
        return ((IEnumerable<Issue>)Childs).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return Childs.GetEnumerator();
    }

    public int Count => Childs.Length;

    public Issue this[int index] => Childs[index];

    protected bool Equals(MainIssue other)
    {
        return Type == other.Type && BoundingRectangle.Equals(other.BoundingRectangle) && PixelCount == other.PixelCount && Area.Equals(other.Area) && Childs.AsValueEnumerable().SequenceEqual(other.Childs);
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((MainIssue)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine((int)Type, BoundingRectangle, PixelCount, Area, Childs);
    }
}