﻿/*
 *                     GNU AFFERO GENERAL PUBLIC LICENSE
 *                       Version 3, 19 November 2007
 *  Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
 *  Everyone is permitted to copy and distribute verbatim copies
 *  of this license document, but changing it is not allowed.
 */

using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using UVtools.Core.Extensions;
using ZLinq;

namespace UVtools.Core.EmguCV;

/// <summary>
/// A contour cache for OpenCV
/// </summary>
#pragma warning disable CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
public class EmguContour : IReadOnlyList<Point>, IComparable<EmguContour>, IComparer<EmguContour>, IDisposable
#pragma warning restore CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
{
    #region Constants

    public const byte HierarchyNextSameLevel = 0;
    public const byte HierarchyPreviousSameLevel = 1;
    public const byte HierarchyFirstChild = 2;
    public const byte HierarchyParent = 3;

    #endregion

    #region Members

    private VectorOfPoint _vector;
    private Rectangle? _bounds;
    private RotatedRect? _boundsBestFit;
    private CircleF? _minEnclosingCircle;
    private bool? _isConvex;
    private double _area = double.NaN;
    private double _perimeter = double.NaN;
    private Moments? _moments;
    private Point? _centroid;

    #endregion

    #region Properties
    public int XMin => BoundingRectangle.X;

    public int YMin => BoundingRectangle.Y;

    public int XMax => BoundingRectangle.Right;

    public int YMax => BoundingRectangle.Bottom;

    public Rectangle BoundingRectangle => _bounds ??= CvInvoke.BoundingRectangle(_vector);

    public RotatedRect BoundsBestFit => _boundsBestFit ??= CvInvoke.MinAreaRect(_vector);

    public CircleF MinEnclosingCircle => _minEnclosingCircle ??= CvInvoke.MinEnclosingCircle(_vector);

    public bool IsConvex => _isConvex ??= CvInvoke.IsContourConvex(_vector);

    /// <summary>
    /// Gets the area of the contour
    /// </summary>
    public double Area
    {
        get
        {
            if (double.IsNaN(_area))
            {
                _area = CvInvoke.ContourArea(_vector);
            }

            return _area;
        }
    }

    /// <summary>
    /// Gets the perimeter of the contours
    /// </summary>
    public double Perimeter
    {
        get
        {
            if (double.IsNaN(_perimeter))
            {
                _perimeter = CvInvoke.ArcLength(_vector, true);
            }
            return _perimeter;
        }
    }

    /// <summary>
    /// Gets if the contour is closed
    /// </summary>
    public bool IsClosed => IsConvex || Area > Perimeter;

    /// <summary>
    /// Gets if the contour is open
    /// </summary>
    public bool IsOpen => !IsClosed;

    public Moments Moments => _moments ??= CvInvoke.Moments(_vector);

    /// <summary>
    /// Gets the centroid of the contour
    /// </summary>
    public Point Centroid => _centroid ??= Moments.M00 == 0 ? new Point(-1,-1) :
        new Point(
            (int)Math.Round(Moments.M10 / Moments.M00),
            (int)Math.Round(Moments.M01 / Moments.M00));

    /// <summary>
    /// Gets or sets the contour points
    /// </summary>
    public VectorOfPoint Vector
    {
        get => _vector;
        set
        {
            Dispose();
            _vector = value ?? throw new ArgumentNullException(nameof(Vector));
        }
    }


    /// <summary>
    /// Gets if this contour have any point
    /// </summary>
    public bool IsEmpty => _vector.Size == 0;
    #endregion

    #region Constructor

    public EmguContour(VectorOfPoint point)
    {
        _vector = point;
    }

    public EmguContour(Point[] points) : this(new VectorOfPoint(points))
    {
    }
    #endregion

    #region Methods

    /// <summary>
    /// Checks if a given <see cref="Point"/> is inside the contour rectangle bounds
    /// </summary>
    /// <param name="point"></param>
    /// <returns></returns>
    public bool IsInsideBounds(Point point) => BoundingRectangle.Contains(point);

    /// <summary>
    /// Gets if a given <see cref="Point"/> is inside the contour
    /// </summary>
    /// <param name="point"></param>
    /// <returns></returns>
    public bool IsInside(Point point)
    {
        if (!IsInsideBounds(point)) return false;
        return CvInvoke.PointPolygonTest(_vector, point, false) >= 0;
    }

    public double MeasureDist(Point point)
    {
        if (!IsInsideBounds(point)) return -1;
        return CvInvoke.PointPolygonTest(_vector, point, true);
    }

    public IOutputArray ContourApproximation(double epsilon = 0.1)
    {
        var mat = new Mat();
        CvInvoke.ApproxPolyDP(_vector, mat, epsilon*Perimeter, true);
        return mat;
    }

    /*
    /// <summary>
    /// Calculate the X/Y min/max boundary
    /// </summary>
    private void CalculateMinMax()
    {
        Bounds = Rectangle.Empty;

        if (_contourPoints.Length == 0)
        {
            _xMin = -1;
            _yMin = -1;
            _xMax = -1;
            _yMax = -1;
            return;
        }

        _xMin = int.MaxValue;
        _yMin = int.MaxValue;
        _xMax = int.MinValue;
        _yMax = int.MinValue;

        for (int i = 0; i < _contourPoints.Length; i++)
        {
            _xMin = Math.Min(_xMin, _contourPoints[i].X);
            _yMin = Math.Min(_yMin, _contourPoints[i].Y);

            _xMax = Math.Max(_xMax, _contourPoints[i].X);
            _yMax = Math.Max(_yMax, _contourPoints[i].Y);
        }

        Bounds = new Rectangle(_xMin, _yMin, _xMax - _xMin, _yMax - _yMin);
    }
    */

    public void FitCircle(Mat src, MCvScalar color, int thickness = 1, LineType lineType = LineType.EightConnected, int shift = 0)
    {
        CvInvoke.Circle(src,
            MinEnclosingCircle.Center.ToPoint(),
            (int) Math.Round(MinEnclosingCircle.Radius),
            color,
            thickness,
            lineType,
            shift);
    }

    /*public void FitEllipse(Mat src, MCvScalar color, int thickness = 1, LineType lineType = LineType.EightConnected, int shift = 0)
    {
        var ellipse = CvInvoke.FitEllipse(_points);
        CvInvoke.Ellipse(src, ellipse.Center.ToPoint(), ellipse.Size.ToSize(), ellipse.Angle, 0, 0);
    }*/
    #endregion

    #region Static methods
    public static Point GetCentroid(VectorOfPoint points)
    {
        if (points.Length == 0) return EmguExtensions.AnchorCenter;
        using var moments = CvInvoke.Moments(points);
        return moments.M00 == 0 ? EmguExtensions.AnchorCenter :
            new Point(
                (int)Math.Round(moments.M10 / moments.M00),
                (int)Math.Round(moments.M01 / moments.M00));
    }
    #endregion

    #region Implementations

    public EmguContour Clone()
    {
        return new EmguContour(Vector.ToArray());
    }

    public IEnumerator<Point> GetEnumerator()
    {
        return (IEnumerator<Point>) _vector.ToArray().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public int Count => _vector.Size;

    public Point this[sbyte index] => _vector[index];
    public Point this[byte index] => _vector[index];
    public Point this[short index] => _vector[index];
    public Point this[ushort index] => _vector[index];
    public Point this[int index] => _vector[index];
    public Point this[uint index] => _vector[(int) index];
    public Point this[long index] => _vector[(int) index];
    public Point this[ulong index] => _vector[(int) index];

    public void Dispose()
    {
        _vector.Dispose();
        _moments?.Dispose();
        _moments = null;
    }
    #endregion

    #region Equality

    protected bool Equals(EmguContour other)
    {
        if (Count != other.Count) return false;
        return _vector.ToArray().AsValueEnumerable().SequenceEqual(other.AsValueEnumerable().ToArray());
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((EmguContour) obj);
    }

    public int CompareTo(EmguContour? other)
    {
        if (ReferenceEquals(this, other)) return 0;
        if (ReferenceEquals(null, other)) return 1;
        return _area.CompareTo(other._area);
    }

    public int Compare(EmguContour? x, EmguContour? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (ReferenceEquals(null, y)) return 1;
        if (ReferenceEquals(null, x)) return -1;
        return x._area.CompareTo(y._area);
    }
    #endregion
}