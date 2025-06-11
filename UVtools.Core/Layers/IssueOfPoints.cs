﻿/*
 *                     GNU AFFERO GENERAL PUBLIC LICENSE
 *                       Version 3, 19 November 2007
 *  Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
 *  Everyone is permitted to copy and distribute verbatim copies
 *  of this license document, but changing it is not allowed.
 */

using System;
using System.Collections.Generic;
using System.Drawing;
using ZLinq;

namespace UVtools.Core.Layers;

public sealed class IssueOfPoints : Issue
{
    /// <summary>
    /// Gets the points containing the coordinates of the issue
    /// </summary>
    public Point[] Points { get; init; } = null!;

    public IssueOfPoints() { }

    public IssueOfPoints(Layer layer, IEnumerable<Point> points, Rectangle boundingRectangle = default) : base(layer, boundingRectangle, points.AsValueEnumerable().Count())
    {
        Points = points.AsValueEnumerable().ToArray();
        PixelsCount = (uint)Points.Length;
        FirstPoint = Points[0];
    }

    private bool Equals(IssueOfPoints other)
    {
        return Points.AsValueEnumerable().SequenceEqual(other.Points);
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || obj is IssueOfPoints other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Points);
    }
}