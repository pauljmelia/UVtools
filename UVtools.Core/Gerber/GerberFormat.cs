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
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UVtools.Core.Extensions;
using UVtools.Core.Gerber.Apertures;
using UVtools.Core.Operations;

namespace UVtools.Core.Gerber;

/// <summary>
/// https://www.ucamco.com/files/downloads/file_en/456/gerber-layer-format-specification-revision-2022-02_en.pdf?ac97011bf6bce9aaf0b1aac43d84b05f
/// </summary>
public class GerberFormat
{
    #region Properties

    public GerberZerosSuppressionType ZerosSuppressionType { get; set; } = GerberZerosSuppressionType.NoSuppression;
    public GerberPositionType PositionType { get; set; } = GerberPositionType.Absolute;
    public GerberUnitType UnitType { get; set; } = GerberUnitType.Millimeter;
    public GerberPolarityType Polarity { get; set; } = GerberPolarityType.Dark;
    public GerberMoveType MoveType { get; set; } = GerberMoveType.Linear;
    public GerberQuadrantMode QuadrantMode { get; set; } = GerberQuadrantMode.MultiQuadrant;
    public MidpointRoundingType SizeMidpointRounding { get; set; } = MidpointRoundingType.AwayFromZero;

    public byte CoordinateXIntegers { get; set; } = 3;
    public byte CoordinateXFractionalDigits { get; set; } = 6;

    public byte CoordinateXLength => (byte)(CoordinateXIntegers + CoordinateXFractionalDigits);

    public byte CoordinateYIntegers { get; set; } = 3;
    public byte CoordinateYFractionalDigits { get; set; } = 6;

    public byte CoordinateYLength => (byte)(CoordinateYIntegers + CoordinateYFractionalDigits);

    public Dictionary<int, Aperture> Apertures { get; } = new();
    public Dictionary<string, Macro> Macros { get; } = new();

    public SizeF XYppmm { get; init; }

    /// <summary>
    /// Gets or sets the X offset for drawings in millimeters
    /// </summary>
    public float OffsetX { get; set; }

    /// <summary>
    /// Gets or sets the Y offset for drawings in millimeters
    /// </summary>
    public float OffsetY { get; set; }

    /// <summary>
    /// Gets the current polarity as <see cref="MCvScalar"/>. <see cref="InversePolarity"/> will affect the return value
    /// </summary>
    public MCvScalar PolarityColor => GetPolarityColor(Polarity);

    /// <summary>
    /// Gets or sets to inverse the polarity on drawing
    /// </summary>
    public bool InversePolarity { get; set; }

    /// <summary>
    /// Gets or sets the scale to apply to each shape drawing size.
    /// Positions and vectors aren't affected by this.
    /// </summary>
    public double SizeScale { get; set; } = 1;

    #endregion


    public GerberFormat()
    {
    }

    public GerberFormat(string filePath)
    {
    }

    public static void ParseAndDraw(GerberFormat document, string filePath, Mat mat, bool enableAntiAliasing = false)
    {
        using var file = new StreamReader(filePath);

        int FSlength = "%FSLAX46Y46*%".Length;
        int MOlength = "%MOMM*%".Length;
        int LPlength = "%LPD*%".Length;

        double currentX = 0;
        double currentY = 0;
        Aperture? currentAperture = null;
        var regionPoints = new List<Point>();
        bool insideRegion = false;

        while (file.ReadLine()?.Trim() is { } line)
        {
            if (line.Length == 0) continue;
            if (line.StartsWith("M02")) break;

            var accumulatedLine = line;
            while (!accumulatedLine.Contains('*') && (line = file.ReadLine()) is not null)
            {
                line = line.Trim();
                if (line == string.Empty) continue;
                accumulatedLine += line;
            }

            line = accumulatedLine;

            if (line.StartsWith("%MO") && line.Length >= MOlength)
            {
                if (line[3] == 'M' && line[4] == 'M') document.UnitType = GerberUnitType.Millimeter;
                else if (line[3] == 'I' && line[4] == 'N') document.UnitType = GerberUnitType.Inch;
                continue;
            }

            if (line.StartsWith("G70"))
            {
                document.UnitType = GerberUnitType.Inch;
                continue;
            }


            if (line.StartsWith("G71"))
            {
                document.UnitType = GerberUnitType.Millimeter;
                continue;
            }

            if (line.StartsWith("G90"))
            {
                document.PositionType = GerberPositionType.Absolute;
                continue;
            }


            if (line.StartsWith("G91"))
            {
                document.PositionType = GerberPositionType.Relative;
                continue;
            }

            if (line.StartsWith("%FS") && line.Length >= FSlength)
            {
                // %FSLAX34Y34*%
                // 0123456789
                document.ZerosSuppressionType = line[3] switch
                {
                    'L' => GerberZerosSuppressionType.Leading,
                    'T' => GerberZerosSuppressionType.Trail,
                    _ => document.ZerosSuppressionType
                };
                document.PositionType = line[4] switch
                {
                    'A' => GerberPositionType.Absolute,
                    'I' => GerberPositionType.Relative,
                    _ => document.PositionType
                };
                if (document.PositionType == GerberPositionType.Relative) throw new NotImplementedException("Relative positions are not implemented yet.\nPlease use Absolute position for now.");
                if (line[5] != 'X') continue;
                if (byte.TryParse(line[6].ToString(), out var x1)) document.CoordinateXIntegers = x1;
                if (byte.TryParse(line[7].ToString(), out var x2)) document.CoordinateXFractionalDigits = x2;
                if (line[8] != 'Y') continue;
                if (byte.TryParse(line[9].ToString(), out var y1)) document.CoordinateYIntegers = y1;
                if (byte.TryParse(line[10].ToString(), out var y2)) document.CoordinateYFractionalDigits = y2;
                continue;
            }

            if (line.StartsWith("%LP") && line.Length >= LPlength)
            {
                document.Polarity = line[3] switch
                {
                    'D' => GerberPolarityType.Dark,
                    'C' => GerberPolarityType.Clear,
                    _ => document.Polarity
                };

                continue;
            }

            if (line.StartsWith("G01"))
            {
                document.MoveType = GerberMoveType.Linear;
                continue;
            }

            if (line.StartsWith("G02"))
            {
                document.MoveType = GerberMoveType.Arc;
                continue;
            }

            if (line.StartsWith("G03"))
            {
                document.MoveType = GerberMoveType.ArcCounterClockwise;
                continue;
            }

            if (line.StartsWith("G36"))
            {
                insideRegion = true;
                regionPoints.Clear();
                continue;
            }

            if (line.StartsWith("G37"))
            {
                insideRegion = false;
                if (regionPoints.Count > 0)
                {
                    using var vec = new VectorOfPoint(regionPoints.ToArray());
                    CvInvoke.FillPoly(mat, vec, document.PolarityColor, enableAntiAliasing ? LineType.AntiAlias : LineType.EightConnected);
                }
                //CvInvoke.Imshow("G37", mat);
                //CvInvoke.WaitKey();
                regionPoints.Clear();
                continue;
            }

            if (line.StartsWith("G74"))
            {
                document.QuadrantMode = GerberQuadrantMode.SingleQuadrant;
                throw new NotImplementedException("Single quadrant mode is not yet implemented. Provide samples and open a request to implement this function.");
            }

            if (line.StartsWith("G75"))
            {
                document.QuadrantMode = GerberQuadrantMode.MultiQuadrant;
                continue;
            }

            if (line.StartsWith("%AM"))
            {
                accumulatedLine = line[1..];
                while (!accumulatedLine.Contains('%') && (line = file.ReadLine()) is not null)
                {
                    line = line.Trim();
                    if (line == string.Empty) continue;
                    accumulatedLine += line;
                }
                line = accumulatedLine[..^2];

                var split = line.Split(['*', '%'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                
                var macro = Macro.Parse(document, split[0]);
                if (macro is null) continue;
                document.Macros.Add(macro.Name, macro);

                for (int i = 1; i < split.Length; i++)
                {
                    macro.ParsePrimitive(split[i]);
                }

                continue;
            }

            if (line.StartsWith("%ADD"))
            {
                var aperture = Aperture.Parse(line, document);
                if (aperture is null) continue;
                currentAperture = aperture;
                document.Apertures.Add(aperture.Index, aperture);
                continue;
            }

            if (line.StartsWith("G04")) // Comment
            {
                continue;
            }

            // Aperture selector
            if (line[0] == 'D' || line.StartsWith("G54"))
            {
                var matchD = Regex.Match(line, @"D([0-9]+)");
                if (!matchD.Success || matchD.Groups.Count < 2) continue;

                if (!int.TryParse(matchD.Groups[1].Value, out var d)) continue;

                if (d >= 10)
                {
                    document.Apertures.TryGetValue(d, out currentAperture);
                    continue;
                }
            }


            if (line[0] == 'X' || line[0] == 'Y' || line[0] == 'D')
            {
                var matchX = Regex.Match(line, @"X-?([0-9]+)?");
                var matchY = Regex.Match(line, @"Y-?([0-9]+)?");
                var matchD = Regex.Match(line, @"D([0-9]+)");

                double nowX = 0;
                double nowY = 0;

                if (!matchD.Success || matchD.Groups.Count < 2) continue;
                if (!int.TryParse(matchD.Groups[1].Value, out var d)) continue;

                if (matchX.Success)
                {
                    if (matchX.Groups.Count >= 2)
                    {
                        var valueStr = document.ZerosSuppressionType switch
                        {
                            GerberZerosSuppressionType.Trail => matchX.Groups[1].Value.PadRight(document.CoordinateXLength, '0'),
                            _ => matchX.Groups[1].Value.PadLeft(document.CoordinateXLength, '0'),
                        };

                        var integers = valueStr[..document.CoordinateXIntegers];
                        var fraction = valueStr[document.CoordinateXIntegers..];
                        double.TryParse($"{integers}.{fraction}", NumberStyles.Float, CultureInfo.InvariantCulture, out nowX);
                        nowX = document.GetMillimeters(nowX);
                    }
                }
                else
                {
                    nowX = currentX;
                }

                if (matchY.Success)
                {
                    if (matchY.Groups.Count >= 2)
                    {
                        var valueStr = document.ZerosSuppressionType switch
                        {
                            GerberZerosSuppressionType.Trail => matchY.Groups[1].Value.PadRight(document.CoordinateYLength, '0'),
                            _ => matchY.Groups[1].Value.PadLeft(document.CoordinateYLength, '0'),
                        };

                        var integers = valueStr[..document.CoordinateYIntegers];
                        var fraction = valueStr[document.CoordinateYIntegers..];
                        double.TryParse($"{integers}.{fraction}", NumberStyles.Float, CultureInfo.InvariantCulture, out nowY);
                        nowY = document.GetMillimeters(nowY);

                    }
                }
                else
                {
                    nowY = currentY;
                }

                if (insideRegion)
                {
                    if (d == 2)
                    {
                        if (regionPoints.Count > 0)
                        {
                            using var vec = new VectorOfPoint(regionPoints.ToArray());
                            CvInvoke.FillPoly(mat, vec, document.PolarityColor, enableAntiAliasing ? LineType.AntiAlias : LineType.EightConnected);
                        }
                        regionPoints.Clear();
                    }

                    var pt = document.PositionMmToPx(nowX, nowY);
                    if (regionPoints.Count == 0 || (regionPoints.Count > 0 && regionPoints[^1] != pt)) regionPoints.Add(pt);
                }
                else if (currentAperture is not null)
                {
                    if (d == 1)
                    {
                        if (currentAperture is CircleAperture circleAperture)
                        {
                            if (document.MoveType is GerberMoveType.Arc or GerberMoveType.ArcCounterClockwise)
                            {
                                double xOffset = 0;
                                double yOffset = 0;
                                var matchI = Regex.Match(line, @"I(-?[0-9]+)");
                                var matchJ = Regex.Match(line, @"J(-?[0-9]+)");
                                if (!matchI.Success || !matchJ.Success || matchI.Groups.Count < 2 || matchJ.Groups.Count < 2) continue;

                                // xOffset
                                var matchValue = matchI.Groups[1].Value[0] == '-'
                                    ? matchI.Groups[1].Value[1..]
                                    : matchI.Groups[1].Value;

                                var valueStr = document.ZerosSuppressionType switch
                                {
                                    GerberZerosSuppressionType.Trail => matchValue.PadRight(document.CoordinateXLength, '0'),
                                    _ => matchValue.PadLeft(document.CoordinateXLength, '0'),
                                };


                                var integers = valueStr[..document.CoordinateXIntegers];
                                var fraction = valueStr[document.CoordinateXIntegers..];
                                double.TryParse($"{(matchI.Groups[1].Value[0] == '-' ? "-" : string.Empty)}{integers}.{fraction}", NumberStyles.Float, CultureInfo.InvariantCulture, out xOffset);
                                xOffset = document.GetMillimeters(xOffset);

                                // yOffset
                                matchValue = matchJ.Groups[1].Value[0] == '-'
                                    ? matchJ.Groups[1].Value[1..]
                                    : matchJ.Groups[1].Value;

                                valueStr = document.ZerosSuppressionType switch
                                {
                                    GerberZerosSuppressionType.Trail => matchValue.PadRight(document.CoordinateYLength, '0'),
                                    _ => matchValue.PadLeft(document.CoordinateYLength, '0'),
                                };


                                integers = valueStr[..document.CoordinateYIntegers];
                                fraction = valueStr[document.CoordinateYIntegers..];
                                double.TryParse($"{(matchJ.Groups[1].Value[0] == '-' ? "-" : string.Empty)}{integers}.{fraction}", NumberStyles.Float, CultureInfo.InvariantCulture, out yOffset);
                                yOffset = document.GetMillimeters(yOffset);


                                if (currentX == nowX && currentY == nowY) // Closed circle
                                {
                                    CvInvoke.Ellipse(mat,
                                        document.PositionMmToPx(nowX + xOffset, nowY + yOffset),
                                        document.SizeMmToPx(Math.Abs(xOffset), Math.Abs(xOffset)),
                                        0, 0, 360.0, document.PolarityColor,
                                        EmguExtensions.CorrectThickness(document.SizeMmToPx(circleAperture.Diameter)),
                                        enableAntiAliasing ? LineType.AntiAlias : LineType.EightConnected
                                    );
                                }
                                else
                                {
                                    
                                    // Calculate center point and radius
                                    double realCenterX = currentX + xOffset;
                                    double realCenterY = currentY - yOffset;
                                    double gerberCenterY = currentY + yOffset;
                                    double radius = MathExtensions.Hypot(xOffset, yOffset);
                                    //double radius = Math.Sqrt(Math.Pow(currentX - realCenterX, 2) + Math.Pow(currentY - currentY + yOffset, 2));

                                    // Calculate the angle between the start and end points and the center point
                                    // Both I and J need to be negated to represent the vector to the start point FROM the arc center
                                    // BUT the Y-coord also needs to be negated back again (!) due opposing coordinate systems of Gerber (+y-up) and C# (+y-down)
                                    double angleStart = Math.Atan2(yOffset, -xOffset) * 180 / Math.PI;
                                    double angleEnd = Math.Atan2(nowY - currentY + yOffset, nowX - realCenterX) * 180 / Math.PI;
                                    
                                    if (document.MoveType == GerberMoveType.Arc)
                                    {
                                        // For CW arcs, angleEnd must be greater than angleStart...
                                        if (angleEnd < angleStart) angleEnd += 360;
                                    }
                                    else
                                    {
                                        // For CCW arcs, angleEnd must be less than angleStart...
                                        if (angleEnd > angleStart) angleEnd -= 360;
                                    }

                                    // Calculate the angle difference between the start and end points
                                    double angleDiff = angleEnd - angleStart;
                                    double angleSpan = angleStart + angleDiff;
                                    if (angleSpan == 0) angleSpan = 360;
                                    
                                    CvInvoke.Ellipse(mat, document.PositionMmToPx(realCenterX, realCenterY),
                                        document.SizeMmToPx(radius, radius), 
                                        0, document.MoveType == GerberMoveType.ArcCounterClockwise ? angleStart : -angleStart, document.MoveType == GerberMoveType.ArcCounterClockwise ? angleSpan : -angleSpan, document.PolarityColor,
                                        EmguExtensions.CorrectThickness(document.SizeMmToPx(circleAperture.Diameter)),
                                        enableAntiAliasing ? LineType.AntiAlias : LineType.EightConnected
                                    );


                                    /*CvInvoke.Circle(mat, document.PositionMmToPx(currentX, currentY), 5, new MCvScalar(127), -1);
                                    CvInvoke.Circle(mat, document.PositionMmToPx(nowX, nowY), 5, new MCvScalar(127), -1);
                                    CvInvoke.Circle(mat, document.PositionMmToPx(centerX, centerY), 5, new MCvScalar(127), -1);

                                    arcs++;
                                    CvInvoke.PutText(mat, $"{arcs}", document.PositionMmToPx(centerX, centerY), FontFace.HersheyDuplex, 2, new MCvScalar(190), 2);
                                    */

                                    //CvInvoke.Imshow("mat", mat);
                                    //CvInvoke.WaitKey();
                                    /*Debug.WriteLine($"{arcs}:");
                                    Debug.WriteLine($"Start:  X:{currentX} Y:{currentY}");
                                    Debug.WriteLine($"End:    X:{nowX} Y:{nowY}");
                                    Debug.WriteLine($"Offset: X:{xOffset} Y:{yOffset}");
                                    Debug.WriteLine(line);
                                    Debug.WriteLine("---------------------");*/
                                }

                            }
                            else
                            {
                                CvInvoke.Line(mat,
                                    document.PositionMmToPx(currentX, currentY),
                                    document.PositionMmToPx(nowX, nowY),
                                    document.PolarityColor,
                                    EmguExtensions.CorrectThickness(document.SizeMmToPx(circleAperture.Diameter)),
                                    enableAntiAliasing ? LineType.AntiAlias : LineType.EightConnected);
                                /*mat.DrawLineAccurate(PositionMmToPx(currentX, currentY, xyPpmm),
                                    PositionMmToPx(nowX, nowY, xyPpmm),
                                    document.PolarityColor
                                    SizeMmToPx(circleAperture.Diameter, xyPpmm.Max()),
                                    enableAntialiasing ? LineType.AntiAlias : LineType.EightConnected);*/

                                /*CvInvoke.DrawContours(mat, new VectorOfVectorOfPoint(new[]
                                {
                                    new []
                                    {
                                        PositionMmToPx(currentX, currentY, xyPpmm),
                                        PositionMmToPx(nowX, nowY, xyPpmm),
                                    }
                                }), -1, document.PolarityColor, SizeMmToPx(circleAperture.Diameter, xyPpmm.Max()));*/
                                //CvInvoke.Imshow("Line", mat);
                                //CvInvoke.WaitKey();
                            }

                        }
                    }
                    else if (d == 3)
                    {
                        currentAperture.DrawFlashD3(mat, new PointF((float)nowX, (float)nowY), document.PolarityColor, enableAntiAliasing ? LineType.AntiAlias : LineType.EightConnected);
                        //CvInvoke.Imshow("G37", mat);
                        //CvInvoke.WaitKey();
                    }
                }

                currentX = nowX;
                currentY = nowY;
                continue;
            }

            Debug.WriteLine($"Not recognized command: {line}");
        }
    }

    public static GerberFormat ParseAndDraw(OperationPCBExposure.PCBExposureFile file, Mat mat, SizeF xyPpmm, MidpointRoundingType sizeMidpointRounding = MidpointRoundingType.AwayFromZero, SizeF offset = default, bool enableAntiAliasing = false)
    {
        var document = new GerberFormat
        {
            SizeMidpointRounding = sizeMidpointRounding,
            XYppmm = xyPpmm,
            OffsetX = offset.Width,
            OffsetY = offset.Height,
            InversePolarity = file.InvertPolarity,
            SizeScale = file.SizeScale
        };

        ParseAndDraw(document, file.FilePath, mat, enableAntiAliasing);

        return document;
    }

    public static GerberFormat ParseAndDraw(string filePath, Mat mat, SizeF xyPpmm, MidpointRoundingType sizeMidpointRounding = MidpointRoundingType.AwayFromZero, SizeF offset = default, bool enableAntiAliasing = false)
    {
        var document = new GerberFormat
        {
            SizeMidpointRounding = sizeMidpointRounding,
            XYppmm = xyPpmm,
            OffsetX = offset.Width,
            OffsetY = offset.Height
        };

        ParseAndDraw(document, filePath, mat, enableAntiAliasing);

        return document;
    }

    public MCvScalar GetPolarityColor(GerberPolarityType polarity) => polarity == GerberPolarityType.Dark
        ? !InversePolarity ? EmguExtensions.WhiteColor : EmguExtensions.BlackColor
        : !InversePolarity ? EmguExtensions.BlackColor : EmguExtensions.WhiteColor;

    public MCvScalar GetPolarityColor(bool polarity) => polarity
        ? !InversePolarity ? EmguExtensions.WhiteColor : EmguExtensions.BlackColor
        : !InversePolarity ? EmguExtensions.BlackColor : EmguExtensions.WhiteColor;

    public MCvScalar GetPolarityColor(int polarity) => polarity > 0
        ? !InversePolarity ? EmguExtensions.WhiteColor : EmguExtensions.BlackColor
        : !InversePolarity ? EmguExtensions.BlackColor : EmguExtensions.WhiteColor;

    public float GetMillimeters(float size)
    {
        if (UnitType == GerberUnitType.Millimeter) return size;
        return size * (float)UnitExtensions.InchToMillimeter;
    }

    public double GetMillimeters(double size)
    {
        if (UnitType == GerberUnitType.Millimeter) return size;
        return size * UnitExtensions.InchToMillimeter;
    }

    public SizeF GetMillimeters(SizeF size)
    {
        if (UnitType == GerberUnitType.Millimeter) return size;
        return new SizeF(size.Width * (float)UnitExtensions.InchToMillimeter, size.Height * (float)UnitExtensions.InchToMillimeter);
    }

    public PointF GetMillimeters(PointF point)
    {
        if (UnitType == GerberUnitType.Millimeter) return point;
        return new PointF(point.X * (float)UnitExtensions.InchToMillimeter, point.Y * (float)UnitExtensions.InchToMillimeter);
    }

    public Point PositionMmToPx(PointF atMm)
        => new((int)Math.Round((atMm.X + OffsetX) * XYppmm.Width, MidpointRounding.AwayFromZero), (int)Math.Round((atMm.Y + OffsetY) * XYppmm.Height, MidpointRounding.AwayFromZero));

    public Point PositionMmToPx(double atXmm, double atYmm)
        => new((int)Math.Round((atXmm + OffsetX) * XYppmm.Width, MidpointRounding.AwayFromZero), (int)Math.Round((atYmm + OffsetY) * XYppmm.Height, MidpointRounding.AwayFromZero));

    public Point PositionMmToPx(float atXmm, float atYmm)
        => new((int)Math.Round((atXmm + OffsetX) * XYppmm.Width, MidpointRounding.AwayFromZero), (int)Math.Round((atYmm + OffsetY) * XYppmm.Height, MidpointRounding.AwayFromZero));

    public Size SizeMmToPx(SizeF sizeMm)
        => new((int)Math.Max(1, Math.Round(sizeMm.Width * XYppmm.Width * SizeScale, (MidpointRounding)SizeMidpointRounding)),
            (int)Math.Max(1, Math.Round(sizeMm.Height * XYppmm.Height * SizeScale, (MidpointRounding)SizeMidpointRounding)));

    public Size SizeMmToPx(double sizeXmm, double sizeYmm)
        => new((int)Math.Max(1, Math.Round(sizeXmm * XYppmm.Width * SizeScale, (MidpointRounding)SizeMidpointRounding)),
            (int)Math.Max(1, Math.Round(sizeYmm * XYppmm.Height * SizeScale, (MidpointRounding)SizeMidpointRounding)));

    public Size SizeMmToPx(float sizeXmm, float sizeYmm)
        => new((int)Math.Max(1, Math.Round(sizeXmm * XYppmm.Width * SizeScale, (MidpointRounding)SizeMidpointRounding)),
            (int)Math.Max(1, Math.Round(sizeYmm * XYppmm.Height * SizeScale, (MidpointRounding)SizeMidpointRounding)));

    public int SizeMmToPx(float sizeMm)
        => (int) Math.Max(1, Math.Round(sizeMm * XYppmm.Max() * SizeScale, (MidpointRounding)SizeMidpointRounding));

    public int SizeMmToPx(double sizeMm)
        => (int)Math.Max(1, Math.Round(sizeMm * XYppmm.Max() * SizeScale, (MidpointRounding)SizeMidpointRounding));

    public int SizeMmToPxOverride(float sizeMm, float ppmm)
        => (int)Math.Max(1, Math.Round(sizeMm * ppmm * SizeScale, (MidpointRounding)SizeMidpointRounding));

    public int SizeMmToPxOverride(double sizeMm, float ppmm)
        => (int)Math.Max(1, Math.Round(sizeMm * ppmm * SizeScale, (MidpointRounding)SizeMidpointRounding));
}


/* KIDCAD
        var document = File.ReadAllLines(@"D:\Tiago\Desktop\kisample\kisample.kicad_pcb");
        System.Drawing.PointF location = PointF.Empty;

        using var mat = EmguExtensions.InitMat(new System.Drawing.Size(2440, 1440));
        const byte pixelsPerMm = 20;

        var drillPoints = new List<KeyValuePair<Point, int>>();

        foreach (var line in document)
        {
            var parseLine = line.Trim();
            if (parseLine.StartsWith("(footprint "))
            {
                location = PointF.Empty;
                continue;
            }
            if (location.IsEmpty && parseLine.StartsWith("(at "))
            {
                parseLine = parseLine.Substring(4, parseLine.Length-5);
                var split = parseLine.Split(' ');
                location = new PointF(float.Parse(split[0], CultureInfo.InvariantCulture), float.Parse(split[1], CultureInfo.InvariantCulture));
                continue;
            }
            if (parseLine.StartsWith("(segment ") || parseLine.StartsWith("(gr_line "))
            {
                var layerMatch = Regex.Match(parseLine, @"\S.Cu");
                if (!layerMatch.Success || layerMatch.Groups.Count < 1) continue;

                var startMatch = Regex.Match(parseLine, @"\(start\s+(\S+)\s+(\S+)\)");
                if(!startMatch.Success || startMatch.Groups.Count < 3) continue;

                var endMatch = Regex.Match(parseLine, @"\(end\s+(\S+)\s+(\S+)\)");
                if (!endMatch.Success || endMatch.Groups.Count < 3) continue;

                var widthMatch = Regex.Match(parseLine, @"\(width\s+(\S+)\)");
                if (!widthMatch.Success || widthMatch.Groups.Count < 2) continue;

                var startXf = new PointF(float.Parse(startMatch.Groups[1].Value), float.Parse(startMatch.Groups[2].Value));
                var endXf = new PointF(float.Parse(endMatch.Groups[1].Value, CultureInfo.InvariantCulture), float.Parse(endMatch.Groups[2].Value, CultureInfo.InvariantCulture));
                var widthf = float.Parse(widthMatch.Groups[1].Value, CultureInfo.InvariantCulture);

                var startX = new System.Drawing.Point((int)(startXf.X * pixelsPerMm), (int)(startXf.Y * pixelsPerMm));
                var endX = new System.Drawing.Point((int)(endXf.X * pixelsPerMm), (int)(endXf.Y * pixelsPerMm));
                var width = (int) (widthf * pixelsPerMm);

                CvInvoke.Line(mat, startX, endX, EmguExtensions.WhiteColor, width);

                continue;
            }

            if (parseLine.StartsWith("(via "))
            {
                var layerMatches = Regex.Matches(parseLine, @"\S.Cu");
                if (layerMatches.Count < 1) continue;
                
                var atMatch = Regex.Match(parseLine, @"\(at\s+(\S+)\s+(\S+)\)");
                if (!atMatch.Success || atMatch.Groups.Count < 3) continue;

                var drillMatch = Regex.Match(parseLine, @"\(drill\s+(\S+)\)");


                var atf = new PointF(float.Parse(atMatch.Groups[1].Value, CultureInfo.InvariantCulture), float.Parse(atMatch.Groups[2].Value, CultureInfo.InvariantCulture));
                //var sizef = new SizeF(float.Parse(sizeMatch.Groups[1].Value, CultureInfo.InvariantCulture), float.Parse(sizeMatch.Groups[2].Value, CultureInfo.InvariantCulture));

                var at = new System.Drawing.Point((int)(atf.X * pixelsPerMm), (int)(atf.Y * pixelsPerMm));
                if (!drillMatch.Success || drillMatch.Groups.Count < 2) continue;
                var drillf = float.Parse(drillMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                var drill = (int) (drillf * pixelsPerMm / 2);

                CvInvoke.Circle(mat, at, drill, EmguExtensions.WhiteColor, -1);


                continue;
            }

            if (parseLine.StartsWith("(gr_circle "))
            {
                var layerMatch = Regex.Match(parseLine, @"\S.Cu");
                if (!layerMatch.Success || layerMatch.Groups.Count < 1) continue;

                var atMatch = Regex.Match(parseLine, @"\(center\s+(\S+)\s+(\S+)\)");
                if (!atMatch.Success || atMatch.Groups.Count < 3) continue;

                var endMatch = Regex.Match(parseLine, @"\(end\s+(\S+)\s+(\S+)\)");
                if (!endMatch.Success || endMatch.Groups.Count < 3) continue;

                var widthMatch = Regex.Match(parseLine, @"\(width\s+(\S+)\)");
                if (!widthMatch.Success || widthMatch.Groups.Count < 2) continue;

                var atf = new PointF(float.Parse(atMatch.Groups[1].Value, CultureInfo.InvariantCulture), float.Parse(atMatch.Groups[2].Value, CultureInfo.InvariantCulture));
                var at = new System.Drawing.Point((int)(atf.X * pixelsPerMm), (int)(atf.Y * pixelsPerMm));
                var endf = new PointF(float.Parse(endMatch.Groups[1].Value, CultureInfo.InvariantCulture), float.Parse(endMatch.Groups[2].Value, CultureInfo.InvariantCulture));
                var radius = (int)(Math.Max(Math.Abs(atf.X - endf.X), Math.Abs(atf.Y - endf.Y)) * pixelsPerMm);
                var widthf = float.Parse(widthMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                var width = (int)(widthf * pixelsPerMm);

                CvInvoke.Circle(mat, at, radius, EmguExtensions.WhiteColor, width);
                if (parseLine.Contains("fill solid"))
                {
                    CvInvoke.Circle(mat, at, radius, EmguExtensions.WhiteColor, -1);
                }


                continue;
            }
            if (parseLine.StartsWith("(gr_rect "))
            {
                var layerMatch = Regex.Match(parseLine, @"\S.Cu");
                if (!layerMatch.Success || layerMatch.Groups.Count < 1) continue;

                var startMatch = Regex.Match(parseLine, @"\(start\s+(\S+)\s+(\S+)\)");
                if (!startMatch.Success || startMatch.Groups.Count < 3) continue;

                var endMatch = Regex.Match(parseLine, @"\(end\s+(\S+)\s+(\S+)\)");
                if (!endMatch.Success || endMatch.Groups.Count < 3) continue;

                var widthMatch = Regex.Match(parseLine, @"\(width\s+(\S+)\)");
                if (!widthMatch.Success || widthMatch.Groups.Count < 2) continue;

                var startf = new PointF(float.Parse(startMatch.Groups[1].Value, CultureInfo.InvariantCulture), float.Parse(startMatch.Groups[2].Value, CultureInfo.InvariantCulture));
                var endf = new PointF(float.Parse(endMatch.Groups[1].Value, CultureInfo.InvariantCulture), float.Parse(endMatch.Groups[2].Value, CultureInfo.InvariantCulture));
                var widthf = float.Parse(widthMatch.Groups[1].Value, CultureInfo.InvariantCulture);

                var start = new System.Drawing.Point((int)(startf.X * pixelsPerMm), (int)(startf.Y * pixelsPerMm));
                var end = new System.Drawing.Point((int)(endf.X * pixelsPerMm), (int)(endf.Y * pixelsPerMm));
                var width = (int)(widthf * pixelsPerMm);

                CvInvoke.Rectangle(mat, new Rectangle(start, new System.Drawing.Size(end.X - start.X, end.Y - start.Y)), EmguExtensions.WhiteColor, width);
                if (parseLine.Contains("fill solid"))
                {
                    CvInvoke.Rectangle(mat, new Rectangle(start, new System.Drawing.Size(end.X - start.X, end.Y - start.Y)), EmguExtensions.WhiteColor, -1);
                }

                continue;
            }

            if (location.IsEmpty) continue;

            if (parseLine.StartsWith("(pad "))
            {
                var layerMatch = Regex.Match(parseLine, @"\S.Cu");
                if (!layerMatch.Success || layerMatch.Groups.Count < 1) continue;

                var atMatch = Regex.Match(parseLine, @"\(at\s+(\S+)\s+(\S+)\)");
                if (!atMatch.Success || atMatch.Groups.Count < 3) continue;
                
                var sizeMatch = Regex.Match(parseLine, @"\(size\s+(\S+)\s+(\S+)\)");
                if (!sizeMatch.Success || sizeMatch.Groups.Count < 3) continue;

                var drillMatch = Regex.Match(parseLine, @"\(drill\s+(\S+)\)");


                var atf = new PointF(float.Parse(atMatch.Groups[1].Value, CultureInfo.InvariantCulture), float.Parse(atMatch.Groups[2].Value, CultureInfo.InvariantCulture));
                var sizef = new SizeF(float.Parse(sizeMatch.Groups[1].Value, CultureInfo.InvariantCulture), float.Parse(sizeMatch.Groups[2].Value, CultureInfo.InvariantCulture));

                var at = new System.Drawing.Point((int)(location.X * pixelsPerMm + atf.X * pixelsPerMm), (int)(location.Y * pixelsPerMm + atf.Y * pixelsPerMm));

                if (parseLine.Contains(" rect ") || parseLine.Contains(" roundrect "))
                {
                    var size = new System.Drawing.Size((int)(sizef.Width * pixelsPerMm), (int)(sizef.Height * pixelsPerMm));
                    var rect = new Rectangle(at, size);
                    rect.Offset(-size.Width / 2, -size.Height / 2);
                    CvInvoke.Rectangle(mat, rect, EmguExtensions.WhiteColor, -1);
                }
                else if (parseLine.Contains(" oval ") || parseLine.Contains(" circle "))
                {
                    var size = new System.Drawing.Size((int)(sizef.Width / 2 * pixelsPerMm), (int)(sizef.Height / 2 * pixelsPerMm));
                    CvInvoke.Ellipse(mat, at, size, 0, 0, 360, EmguExtensions.WhiteColor, -1);
                }
                
                if (drillMatch.Success && drillMatch.Groups.Count >= 2)
                {
                    var drillf = float.Parse(drillMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                    var drill = (int)(drillf * pixelsPerMm / 2);

                    drillPoints.Add(new KeyValuePair<Point, int>(at, drill));
                }


                continue;
            }
        }

        foreach (var pair in drillPoints)
        {
            CvInvoke.Circle(mat, pair.Key, pair.Value, EmguExtensions.BlackColor, -1);
        }

        CvInvoke.Imshow("asd", mat);
        CvInvoke.WaitKey();
        return;
        */