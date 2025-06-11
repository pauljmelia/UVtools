﻿using CommunityToolkit.HighPerformance;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Util;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using UVtools.Core.EmguCV;
using UVtools.Core.Extensions;
using UVtools.Core.FileFormats;
using UVtools.Core.Layers;
using UVtools.Core.Operations;
using UVtools.Core.PixelEditor;
using ZLinq;

namespace UVtools.Core.Managers;

public sealed class IssueManager : RangeObservableCollection<MainIssue>
{
    public FileFormat SlicerFile { get; }

    public List<MainIssue> IgnoredIssues { get; } = [];

    public bool HaveIssues => Count > 0;

    public IssueManager(FileFormat slicerFile)
    {
        SlicerFile = slicerFile;
    }

    /// <summary>
    /// Gets the visible <see cref="MainIssue"/> aka not ignored
    /// </summary>
    /// <returns></returns>
    public MainIssue[] GetVisible()
    {
        return this.AsValueEnumerable().Where(mainIssue => !IgnoredIssues.Contains(mainIssue)).ToArray();
    }

    public static Issue[] GetIssues(IEnumerable<MainIssue> issues)
    {
        var result = new List<Issue>();
        foreach (var mainIssue in issues)
        {
            result.AddRange(mainIssue);
        }

        return result.ToArray();
    }

    public static Issue[] GetIssuesBy(IEnumerable<MainIssue> issues, MainIssue.IssueType type, uint layerIndex)
    {
        var result = new List<Issue>();
        foreach (var mainIssue in issues)
        {
            if (mainIssue.Type != type) continue;
            if (!mainIssue.IsIssueInBetween(layerIndex)) continue;
            foreach (var issue in mainIssue)
            {
                if (issue.LayerIndex != layerIndex) continue;
                result.Add(issue);
            }
        }

        return result.ToArray();
    }

    public static Issue[] GetIssuesBy(IEnumerable<MainIssue> issues, MainIssue.IssueType type)
    {
        var result = new List<Issue>();
        foreach (var mainIssue in issues)
        {
            if (mainIssue.Type != type) continue;
            result.AddRange(mainIssue);
        }

        return result.ToArray();
    }


    public static Issue[] GetIssuesBy(IEnumerable<MainIssue> issues, uint layerIndex)
    {
        var result = new List<Issue>();
        foreach (var mainIssue in issues)
        {
            if (!mainIssue.IsIssueInBetween(layerIndex)) continue;
            foreach (var issue in mainIssue)
            {
                if (issue.LayerIndex != layerIndex) continue;
                result.Add(issue);
            }
        }

        return result.ToArray();
    }

    public Issue[] GetIssues()
    {
        return GetIssues(this);
    }

    public Issue[] GetIssuesBy(MainIssue.IssueType type)
    {
        return GetIssuesBy(this, type);
    }

    public Issue[] GetIssuesBy(MainIssue.IssueType type, uint layerIndex)
    {
        return GetIssuesBy(this, type, layerIndex);
    }

    public Issue[] GetIssuesBy(uint layerIndex)
    {
        return GetIssuesBy(this, layerIndex);
    }

    public List<MainIssue> DetectIssues(IssuesDetectionConfiguration? config = null, OperationProgress? progress = null)
    {
        if (SlicerFile.DecodeType == FileFormat.FileDecodeType.Partial) return [];

        config ??= new IssuesDetectionConfiguration();
        var (
            islandConfig,
            overhangConfig,
            resinTrapConfig,
            touchBoundConfig,
            printHeightConfig,
            emptyLayerConfig
            ) = config;

        progress ??= new OperationProgress();

        var result = new ConcurrentBag<MainIssue>();
        //var layerHollowAreas = new ConcurrentDictionary<uint, List<LayerHollowArea>>();
        var resinTraps = new List<VectorOfVectorOfPoint>?[SlicerFile.LayerCount];
        var suctionCups = new List<VectorOfVectorOfPoint>?[SlicerFile.LayerCount];
        var externalContours = new VectorOfVectorOfPoint?[SlicerFile.LayerCount];
        var hollows = new List<VectorOfVectorOfPoint>?[SlicerFile.LayerCount];
        var airContours = new List<VectorOfVectorOfPoint>?[SlicerFile.LayerCount];
        var resinTrapsContoursArea = new double[SlicerFile.LayerCount][];

        bool IsIgnored(MainIssue issue) => IgnoredIssues.Count > 0 && IgnoredIssues.Contains(issue);

        bool AddIssue(MainIssue issue)
        {
            if (IsIgnored(issue)) return false;
            result.Add(issue);
            return true;
        }

        List<MainIssue> GetResult()
        {
            return result.AsValueEnumerable().OrderBy(mainIssue => mainIssue.Type).ThenBy(issue => issue.StartLayerIndex).ThenByDescending(issue => issue.Area).ToList();
        }

        void GenerateAirMap(IInputArray input, IInputOutputArray output, VectorOfVectorOfPoint? externals)
        {
            CvInvoke.BitwiseNot(input, output);
            if (externals is null || externals.Size == 0) return;
            CvInvoke.DrawContours(output, externals, -1, EmguExtensions.BlackColor, -1);
        }

        if (printHeightConfig.Enabled && SlicerFile.MachineZ > 0)
        {
            float printHeightWithOffset = Layer.RoundHeight(SlicerFile.MachineZ + printHeightConfig.Offset);
            if (SlicerFile.PrintHeight > printHeightWithOffset)
            {
                var issues = (from layer in SlicerFile where layer.PositionZ > printHeightWithOffset select new Issue(layer)).ToList();

                if(issues.Count > 0) AddIssue(new MainIssue(MainIssue.IssueType.PrintHeight, issues));
            }
        }

        if (emptyLayerConfig.Enabled)
        {
            for (var layerIndex = 0; layerIndex < SlicerFile.Count; layerIndex++)
            {
                var layer = SlicerFile[layerIndex];
                if (!layer.IsEmpty) continue;

                if (!emptyLayerConfig.IgnoreStartingEmptyLayers
                    && emptyLayerConfig is {IgnoreLooseEmptyLayers: false, IgnoreEndingEmptyLayers: false})
                {
                    AddIssue(new MainIssue(MainIssue.IssueType.EmptyLayer, new Issue(layer)));
                    continue;
                }

                // 1 = Starting
                // 2 = Loose
                // 3 = Ending
                byte emptyLayerPosType = 0;
                int i;

                for (i = 0; i < layerIndex && SlicerFile[i].IsEmpty; layerIndex++) { }

                if (i == layerIndex)
                {
                    emptyLayerPosType = 1;
                }
                else
                {
                    for (i = (int) SlicerFile.LastLayerIndex; i > layerIndex && SlicerFile[i].IsEmpty; layerIndex--) { }
                    emptyLayerPosType = i == layerIndex ? (byte) 3 : (byte) 2;
                }

                if (emptyLayerPosType == 1)
                {
                    if(!emptyLayerConfig.IgnoreStartingEmptyLayers) AddIssue(new MainIssue(MainIssue.IssueType.EmptyLayer, new Issue(layer)));
                }
                else if (emptyLayerPosType == 2)
                {
                    if (!emptyLayerConfig.IgnoreLooseEmptyLayers) AddIssue(new MainIssue(MainIssue.IssueType.EmptyLayer, new Issue(layer)));
                }
                else if (emptyLayerPosType == 3)
                {
                    if (!emptyLayerConfig.IgnoreEndingEmptyLayers) AddIssue(new MainIssue(MainIssue.IssueType.EmptyLayer, new Issue(layer)));
                }
                else
                {
                    AddIssue(new MainIssue(MainIssue.IssueType.EmptyLayer, new Issue(layer)));
                }
            }
        }

        if (islandConfig.Enabled || overhangConfig.Enabled || resinTrapConfig.Enabled || touchBoundConfig.Enabled)
        {
            progress.Reset(OperationProgress.StatusIslands, SlicerFile.LayerCount);

            var firstLayer = SlicerFile.FirstLayer;

            int overhangsIterations = overhangConfig.ErodeIterations;
            using var overhangsKernel = EmguExtensions.GetDynamicKernel(ref overhangsIterations, ElementShape.Cross);

            // Detect contours
            Parallel.For(0, SlicerFile.LayerCount, CoreSettings.ParallelOptions, layerIndexInt =>
            {
                progress.PauseIfRequested();
                if (progress.Token.IsCancellationRequested)
                {
                    return;
                }
                uint layerIndex = (uint)layerIndexInt;
                var layer = SlicerFile[layerIndex];

                if (layer.IsEmpty)
                {
                    progress.LockAndIncrement();
                    return;
                }

                // Spare a decoding cycle
                if (!touchBoundConfig.Enabled &&
                    !resinTrapConfig.Enabled &&
                    (!overhangConfig.Enabled || overhangConfig.Enabled && (layerIndex == 0 || layer.PositionZ <= firstLayer!.PositionZ || overhangConfig.WhiteListLayers is not null && !overhangConfig.WhiteListLayers.Contains(layerIndex))) &&
                    (!islandConfig.Enabled || islandConfig.Enabled && (layerIndex == 0 || layer.PositionZ <= firstLayer!.PositionZ || islandConfig.WhiteListLayers is not null && !islandConfig.WhiteListLayers.Contains(layerIndex)))
                   )
                {
                    progress.LockAndIncrement();
                    return;
                }

                using (var image = layer.GetLayerMat(layerIndex == 0 ? SlicerFile.BoundingRectangle : Layer.GetBoundingRectangleUnion(SlicerFile[layerIndex - 1], layer)))
                {
                    var sourceSpan = image.SourceMat.GetDataByteReadOnlySpan2D();
                    var roiSpan = image.RoiMat.GetDataByteReadOnlySpan2D();

                    if (touchBoundConfig.Enabled)
                    {
                        // TouchingBounds Checker
                        List<Point> pixels = [];
                        bool touchTop = layer.BoundingRectangle.Top <= touchBoundConfig.MarginTop;
                        bool touchBottom = layer.BoundingRectangle.Bottom >= image.SourceMat.Height - touchBoundConfig.MarginBottom;
                        bool touchLeft = layer.BoundingRectangle.Left <= touchBoundConfig.MarginLeft;
                        bool touchRight = layer.BoundingRectangle.Right >= image.SourceMat.Width - touchBoundConfig.MarginRight;

                        int minx = int.MaxValue;
                        int miny = int.MaxValue;
                        int maxx = 0;
                        int maxy = 0;

                        if (touchTop || touchBottom)
                        {
                            for (int x = layer.BoundingRectangle.X; x < layer.BoundingRectangle.Right; x++) // Check Top and Bottom bounds
                            {
                                if (touchTop)
                                {
                                    for (int y = layer.BoundingRectangle.Y; y < touchBoundConfig.MarginTop; y++) // Top
                                    {
                                        if (sourceSpan.DangerousGetReferenceAt(y, x) >= touchBoundConfig.MinimumPixelBrightness)
                                        {
                                            pixels.Add(new Point(x, y));
                                            minx = Math.Min(minx, x);
                                            miny = Math.Min(miny, y);
                                            maxx = Math.Max(maxx, x);
                                            maxy = Math.Max(maxy, y);
                                        }
                                    }
                                }

                                if (touchBottom)
                                {
                                    for (int y = image.SourceMat.Height - touchBoundConfig.MarginBottom;
                                         y < layer.BoundingRectangle.Bottom;
                                         y++) // Bottom
                                    {
                                        if (sourceSpan.DangerousGetReferenceAt(y, x) >= touchBoundConfig.MinimumPixelBrightness)
                                        {
                                            pixels.Add(new Point(x, y));
                                            minx = Math.Min(minx, x);
                                            miny = Math.Min(miny, y);
                                            maxx = Math.Max(maxx, x);
                                            maxy = Math.Max(maxy, y);
                                        }
                                    }
                                }
                            }
                        }

                        if (touchLeft || touchRight)
                        {
                            for (int y = layer.BoundingRectangle.Y + touchBoundConfig.MarginTop;
                                 y < layer.BoundingRectangle.Bottom - touchBoundConfig.MarginBottom;
                                 y++) // Check Left and Right bounds
                            {
                                if (touchLeft)
                                {
                                    for (int x = layer.BoundingRectangle.X; x < touchBoundConfig.MarginLeft; x++) // Left
                                    {
                                        if (sourceSpan.DangerousGetReferenceAt(y, x) >= touchBoundConfig.MinimumPixelBrightness)
                                        {
                                            pixels.Add(new Point(x, y));
                                            minx = Math.Min(minx, x);
                                            miny = Math.Min(miny, y);
                                            maxx = Math.Max(maxx, x);
                                            maxy = Math.Max(maxy, y);
                                        }
                                    }
                                }

                                if (touchRight)
                                {
                                    for (int x = layer.BoundingRectangle.Right - touchBoundConfig.MarginRight;
                                         x < layer.BoundingRectangle.Right;
                                         x++) // Right
                                    {
                                        if (sourceSpan.DangerousGetReferenceAt(y, x) >= touchBoundConfig.MinimumPixelBrightness)
                                        {
                                            pixels.Add(new Point(x, y));
                                            minx = Math.Min(minx, x);
                                            miny = Math.Min(miny, y);
                                            maxx = Math.Max(maxx, x);
                                            maxy = Math.Max(maxy, y);
                                        }
                                    }
                                }
                            }
                        }

                        if (pixels.Count > 0)
                        {
                            AddIssue(new MainIssue(MainIssue.IssueType.TouchingBound, new IssueOfPoints(layer, pixels,
                                new Rectangle(minx, miny, maxx - minx + 1, maxy - miny + 1))));
                        }
                    }

                    if (layerIndex > 0 && layer.PositionZ > firstLayer!.PositionZ) // No islands nor overhangs for layer 0 or on plate
                    {
                        MatRoi? previousImage = null;
                        ReadOnlySpan2D<byte> previousSpan = null;
                        Mat? overhangImage = null;
                        var previousLayer = SlicerFile[layerIndex - 1];


                        // Overhangs
                        //var overhangCount = 0;
                        var overhangs = new List<MainIssue>();
                        //if (!islandConfig.Enabled && overhangConfig.Enabled ||
                        //    (islandConfig.Enabled && overhangConfig.Enabled && overhangConfig.IndependentFromIslands))
                        if (overhangConfig.Enabled)
                        {
                            bool canProcessCheck = true;
                            if (overhangConfig.WhiteListLayers is not null) // Check white list
                            {
                                if (!overhangConfig.WhiteListLayers.Contains(layerIndex))
                                {
                                    canProcessCheck = false;
                                }
                            }

                            if (canProcessCheck)
                            {
                                previousImage ??= previousLayer.GetLayerMat(Layer.GetBoundingRectangleUnion(previousLayer, layer));

                                overhangImage = new Mat();
                                using var vecPoints = new VectorOfPoint();

                                CvInvoke.Subtract(image.RoiMat, previousImage.RoiMat, overhangImage);
                                CvInvoke.Threshold(overhangImage, overhangImage, 127, 255, ThresholdType.Binary);

                                CvInvoke.Erode(overhangImage, overhangImage, overhangsKernel,
                                    EmguExtensions.AnchorCenter, overhangsIterations, BorderType.Default, default);

                                //CvInvoke.MorphologyEx(subtractedImage, subtractedImage, MorphOp.Open, EmguExtensions.Kernel3x3Rectangle,
                                //    EmguExtensions.AnchorCenter, 2, BorderType.Reflect101, default);

                                using var contours = overhangImage.FindContours(out var hierarchy, RetrType.Tree, ChainApproxMethod.ChainApproxSimple, image.RoiLocation);
                                var contoursInGroups = EmguContours.GetPositiveContoursInGroups(contours, hierarchy);

                                foreach (var contourGroup in contoursInGroups)
                                {
                                    if (contourGroup[0].Size < 3) continue; // Single contour, single line, ignore
                                    var area = EmguContours.GetContourArea(contourGroup);
                                    if (area >= overhangConfig.RequiredPixelsToConsider)
                                    {
                                        var rect = CvInvoke.BoundingRectangle(contourGroup[0]);
                                        var overhangIssue = new MainIssue(MainIssue.IssueType.Overhang, new IssueOfContours(layer, contourGroup.ToArrayOfArray(), rect, area));
                                        overhangs.Add(overhangIssue);
                                        AddIssue(overhangIssue);
                                    }
                                }
                            }
                        }

                        if (islandConfig.Enabled)
                        {
                            bool canProcessCheck = true;
                            if (islandConfig.WhiteListLayers is not null) // Check white list
                            {
                                if (!islandConfig.WhiteListLayers.Contains(layerIndex))
                                {
                                    canProcessCheck = false;
                                }
                            }

                            if (canProcessCheck)
                            {
                                bool needDispose = false;
                                Mat islandImage;
                                if (islandConfig.BinaryThreshold > 0)
                                {
                                    needDispose = true;
                                    islandImage = new();
                                    CvInvoke.Threshold(image.RoiMat, islandImage, islandConfig.BinaryThreshold, byte.MaxValue, ThresholdType.Binary);
                                }
                                else
                                {
                                    islandImage = image.RoiMat;
                                }

                                using Mat labels = new();
                                using Mat stats = new();
                                using Mat centroids = new();
                                var numLabels = CvInvoke.ConnectedComponentsWithStats(islandImage, labels, stats,
                                    centroids,
                                    islandConfig.AllowDiagonalBonds
                                        ? LineType.EightConnected
                                        : LineType.FourConnected);

                                if (needDispose)
                                {
                                    islandImage.Dispose();
                                }

                                // Get array that contains details of each connected component
                                //var ccStats = stats.GetData();
                                //stats[i][0]: Left Edge of Connected Component
                                //stats[i][1]: Top Edge of Connected Component
                                //stats[i][2]: Width of Connected Component
                                //stats[i][3]: Height of Connected Component
                                //stats[i][4]: Total Area (in pixels) in Connected Component
                                var ccStats = stats.GetDataReadOnlySpan<int>();
                                var labelSpan = labels.GetDataSpan2D<int>();

                                for (int i = 1; i < numLabels; i++)
                                {
                                    int pos = i * stats.Cols;
                                    Rectangle rect = new(
                                        ccStats[pos + (int)ConnectedComponentsTypes.Left],
                                        ccStats[pos + (int)ConnectedComponentsTypes.Top],
                                        ccStats[pos + (int)ConnectedComponentsTypes.Width],
                                        ccStats[pos + (int)ConnectedComponentsTypes.Height]
                                        /*(int)ccStats.GetValue(i, (int)ConnectedComponentsTypes.Left)!,
                                        (int)ccStats.GetValue(i, (int)ConnectedComponentsTypes.Top)!,
                                        (int)ccStats.GetValue(i, (int)ConnectedComponentsTypes.Width)!,
                                        (int)ccStats.GetValue(i, (int)ConnectedComponentsTypes.Height)!*/
                                        );

                                    if (ccStats[pos + (int)ConnectedComponentsTypes.Area] < islandConfig.RequiredAreaToProcessCheck)
                                        continue;

                                    previousImage ??= previousLayer.GetLayerMat(Layer.GetBoundingRectangleUnion(previousLayer, layer));

                                    if (previousSpan == null)
                                    {
                                        previousSpan = previousImage.RoiMat.GetDataByteReadOnlySpan2D();
                                    }

                                    List<Point> points = [];
                                    uint pixelsSupportingIsland = 0;

                                    for (int y = rect.Y; y < rect.Bottom; y++)
                                    for (int x = rect.X; x < rect.Right; x++)
                                    {
                                        if (labelSpan.DangerousGetReferenceAt(y, x) != i || // Background pixel or a pixel from another component within the bounding rectangle
                                            roiSpan.DangerousGetReferenceAt(y, x) < islandConfig.RequiredPixelBrightnessToProcessCheck // Low brightness, ignore
                                        ) continue;

                                        points.Add(new Point(image.Roi.X + x, image.Roi.Y + y));

                                        //int pixel = roiStep * y + x;
                                        if (previousSpan.DangerousGetReferenceAt(y, x) >= islandConfig.RequiredPixelBrightnessToSupport)
                                        {
                                            pixelsSupportingIsland++;
                                        }
                                    }

                                    if (points.Count == 0) continue; // Should never happen

                                    var requiredSupportingPixels = Math.Max(1, points.Count * islandConfig.RequiredPixelsToSupportMultiplier);

                                    /*if (pixelsSupportingIsland >= islandConfig.RequiredPixelsToSupport)
                                            isIsland = false; // Not a island, bounding is strong, i think...
                                        else if (pixelsSupportingIsland > 0 &&
                                            points.Count < islandConfig.RequiredPixelsToSupport &&
                                            pixelsSupportingIsland >= Math.Max(1, points.Count / 2))
                                            isIsland = false; // Not a island, but maybe weak bounding...*/

                                    if (pixelsSupportingIsland >= requiredSupportingPixels) continue;

                                    var islandBoundingRectangle = rect.OffsetBy(image.RoiLocation);

                                    // Check for overhangs in islands
                                    if (islandConfig.EnhancedDetection && pixelsSupportingIsland >= 10 && pixelsSupportingIsland >= requiredSupportingPixels / 4)
                                        // && (!overhangConfig.Enabled || (overhangConfig.Enabled && overhangCount > 0))
                                    {
                                        if (overhangConfig.Enabled &&  // No overhangs nor intersecting = discard island
                                            overhangs.TrueForAll(overhang => !overhang.BoundingRectangle.IntersectsWith(islandBoundingRectangle)))
                                        {
                                            continue;
                                        }

                                        using var islandRoi = image.RoiMat.Roi(rect);
                                        using var previousIslandRoi = previousImage.RoiMat.Roi(rect);

                                        var islandOverhangMat = overhangImage;
                                        bool wasNull = false;
                                        if (islandOverhangMat is null)
                                        {
                                            wasNull = true;
                                            islandOverhangMat = new Mat();
                                            CvInvoke.Subtract(islandRoi, previousIslandRoi, islandOverhangMat);
                                            CvInvoke.Threshold(islandOverhangMat, islandOverhangMat, 127, 255, ThresholdType.Binary);

                                            CvInvoke.Erode(islandOverhangMat, islandOverhangMat, overhangsKernel,
                                                EmguExtensions.AnchorCenter, overhangsIterations, BorderType.Default, default);
                                        }

                                        using var subtractedImage = islandOverhangMat.Roi(wasNull ? Rectangle.Empty : rect, EmptyRoiBehaviour.CaptureSource);

                                        var subtractedSpan = subtractedImage.GetDataByteReadOnlySpan2D();
                                        var subtractedStep = subtractedImage.GetRealStep();

                                        int overhangPixels = 0;

                                        for (int y = 0; y < subtractedImage.Height && overhangPixels < overhangConfig.RequiredPixelsToConsider; y++)
                                        for (int x = 0; x < subtractedStep && overhangPixels < overhangConfig.RequiredPixelsToConsider; x++)
                                        {
                                            int labelX = rect.X + x;
                                            int labelY = rect.Y + y;
                                            if (labelSpan[labelY, labelX] != i || subtractedSpan.DangerousGetReferenceAt(y, x) == 0) continue;

                                            overhangPixels++;
                                        }

                                        if (!ReferenceEquals(overhangImage, islandOverhangMat)) islandOverhangMat.Dispose();

                                        if (overhangPixels < overhangConfig.RequiredPixelsToConsider) // No overhang = no island
                                        {
                                            continue;
                                        }
                                    }

                                    AddIssue(new MainIssue(MainIssue.IssueType.Island, new IssueOfPoints(layer, points, islandBoundingRectangle)));
                                }
                            }
                        }

                        previousImage?.Dispose();
                        overhangImage?.Dispose();
                    }

                    if (resinTrapConfig.Enabled)
                    {
                        /* this used to calculate all contours for the layers, however new algorithm crops the layers to the overall bounding box
                         * so the contours produced here are not translated properly. We will generate contours during the algorithm itself later */

                        bool needDispose = false;
                        Mat resinTrapImage;
                        if (resinTrapConfig.BinaryThreshold > 0)
                        {
                            needDispose = true;
                            resinTrapImage = new Mat();
                            CvInvoke.Threshold(image.SourceMat, resinTrapImage, resinTrapConfig.BinaryThreshold, byte.MaxValue, ThresholdType.Binary);
                        }
                        else
                        {
                            resinTrapImage = image.SourceMat;
                        }
                        using var contourLayer = resinTrapImage.Roi(SlicerFile.BoundingRectangle);

                        using var contours = contourLayer.FindContours(out var hierarchy, RetrType.Tree);
                        externalContours[layerIndex] = EmguContours.GetExternalContours(contours, hierarchy);
                        hollows[layerIndex] = EmguContours.GetNegativeContoursInGroups(contours, hierarchy);
                        resinTrapsContoursArea[layerIndex] = EmguContours.GetContoursArea(hollows[layerIndex]);

                        if (needDispose)
                        {
                            resinTrapImage.Dispose();
                        }

                        /*//
                        //hierarchy[i][0]: the index of the next contour of the same level
                        //hierarchy[i][1]: the index of the previous contour of the same level
                        //hierarchy[i][2]: the index of the first child
                        //hierarchy[i][3]: the index of the parent
                        //
                        var listHollowArea = new List<LayerHollowArea>();
                        var hollowGroups = EmguContours.GetNegativeContoursInGroups(contours, hierarchy);
                        var areas = EmguContours.GetContoursArea(hollowGroups);

                        for (var i = 0; i < hollowGroups.Count; i++)
                        {
                            if (areas[i] < resinTrapConfig.RequiredAreaToProcessCheck) continue;
                            var rect = CvInvoke.BoundingRectangle(hollowGroups[i][0]);
                            listHollowArea.Add(new LayerHollowArea(hollowGroups[i].ToArrayOfArray(),
                                rect,
                                areas[i],
                                layer.Index <= resinTrapConfig.StartLayerIndex ||
                                layer.Index == LayerCount - 1 // First and Last layers, always drains
                                    ? LayerHollowArea.AreaType.Drain
                                    : LayerHollowArea.AreaType.Unknown));
                        }

                        if (listHollowArea.Count > 0) layerHollowAreas.TryAdd(layer.Index, listHollowArea);*/
                    }
                }

                progress.LockAndIncrement();
            }); // Parallel end
        }

        if (progress.Token.IsCancellationRequested) return GetResult();

        if (resinTrapConfig.Enabled)
        {
            //progress.Reset("Detecting Air Boundaries (Resin traps)", LayerCount);
            //if (progress.Token.IsCancellationRequested) return result.OrderBy(issue => issue.Type).ThenBy(issue => issue.LayerIndex).ThenBy(issue => issue.Area).ToList();
            progress.Reset("Detection pass 1 of 2 (Resin traps)", SlicerFile.LayerCount, resinTrapConfig.StartLayerIndex);

            using var matCache = new MatCacheManager(SlicerFile, 0, 2);
            matCache.AfterCacheAction = mats =>
            {
                mats[1] = mats[0].Roi(SlicerFile.BoundingRectangle);
                if (resinTrapConfig.MaximumPixelBrightnessToDrain > 0)
                {
                    CvInvoke.Threshold(mats[1], mats[1], resinTrapConfig.MaximumPixelBrightnessToDrain, byte.MaxValue, ThresholdType.Binary);
                }
            };

            /* define all mats up front, reducing allocations */

            using var layerAirMap = new Mat();
            Mat? currentAirMap = null;
            /* the first pass does bottom to top, and tracks anything it thinks is a resin trap */
            for (var layerIndex = resinTrapConfig.StartLayerIndex; layerIndex < SlicerFile.LayerCount; layerIndex++)
            {
                if (progress.Token.IsCancellationRequested) return GetResult();

                var curLayer = matCache.Get(layerIndex, 1);
                //CacheLayers(layerIndex, true);
                //var curLayer = matTargetCache[layerIndex];

                //curLayer.Save($"D:\\dump\\{layerIndex}_a.png");

                /* find hollows of current layer */
                GenerateAirMap(curLayer, layerAirMap, externalContours[layerIndex]);

                //layerAirMap.Save($"D:\\dump\\{layerIndex}_b.png");

                if (layerIndex == resinTrapConfig.StartLayerIndex)
                {
                    currentAirMap = layerAirMap.Clone();
                }

                //currentAirMap.Save($"D:\\dump\\{layerIndex}_c.png");

                /* remove solid areas of current layer from the air map */
                CvInvoke.Subtract(currentAirMap, curLayer, currentAirMap);

                //currentAirMap.Save($"D:\\dump\\{layerIndex}_d.png");

                /* add in areas of air in current layer to air map */
                CvInvoke.BitwiseOr(layerAirMap, currentAirMap, currentAirMap);

                //currentAirMap.Save($"D:\\dump\\{layerIndex}_e.png");

                if (hollows[layerIndex] is not null)
                {
                    resinTraps[layerIndex] = [];
                    airContours[layerIndex] = [];
                    Parallel.For(0, hollows[layerIndex].Count, CoreSettings.ParallelOptions, i =>
                    {
                        progress.PauseIfRequested();
                        //for (var i = 0; i < hollows[layerIndex].Count; i++)
                        //{
                        if (progress.Token.IsCancellationRequested) return;
                        if (resinTrapsContoursArea[layerIndex][i] < resinTrapConfig.RequiredAreaToProcessCheck) return;

                        /* intersect current contour, with the current airmap. */
                        using var currentContour = curLayer.NewZeros();
                        using var airOverlap = new Mat();
                        CvInvoke.DrawContours(currentContour, hollows[layerIndex][i], -1, EmguExtensions.WhiteColor, -1);
                        CvInvoke.BitwiseAnd(currentAirMap, currentContour, airOverlap);
                        var overlapCount = CvInvoke.CountNonZero(airOverlap);

                        lock (SlicerFile[layerIndex].Mutex)
                        {
                            if (overlapCount == 0)
                            {
                                /* this countour does *not* overlap known air */

                                /* add a resin trap (for now... will be revisited in part 2) */
                                resinTraps[layerIndex].Add(hollows[layerIndex][i]);
                            }
                            else
                            {
                                if (overlapCount >= resinTrapConfig.RequiredBlackPixelsToDrain)
                                {
                                    /* this contour does overlap air, add it to the current air map and remember this contour was air-connected for 2nd pass */
                                    airContours[layerIndex].Add(hollows[layerIndex][i]);

                                    CvInvoke.BitwiseOr(currentContour, currentAirMap, currentAirMap);
                                }
                                else
                                {
                                    /* it overlapped ,but not by enough, treat as solid */
                                    CvInvoke.Subtract(currentAirMap, currentContour, currentAirMap);
                                }
                            }
                        }

                    });
                }

                //matCache[layerIndex].Dispose();
                //matCache[layerIndex] = null;
                //matTargetCache[layerIndex] = null;
                matCache.Consume(layerIndex);

                progress++;
            }

            if (progress.Token.IsCancellationRequested) return GetResult();
            progress.Reset("Detection pass 2 of 2 (Resin traps)", SlicerFile.LayerCount, resinTrapConfig.StartLayerIndex);
            /* starting over again but this time from the top to the bottom */
            if (currentAirMap is not null)
            {
                currentAirMap.Dispose();
                currentAirMap = null;
            }

            var resinTrapGroups = new List<List<(VectorOfVectorOfPoint contour, uint layerIndex)>>();

            matCache.Direction = false;
            matCache.Clear();

            for (int layerIndex = resinTraps.Length - 1; layerIndex >= resinTrapConfig.StartLayerIndex; layerIndex--)
            {
                if (progress.Token.IsCancellationRequested) return GetResult();

                var curLayer = matCache.Get((uint)layerIndex, 1);

                if (layerIndex == resinTraps.Length - 1)
                {
                    /* this is subtly different that for the first pass, we don't use GenerateAirMap for the initial airmap */
                    /* instead we use a bitwise not, this way anything that is open/hollow on the top layer is treated as air */
                    currentAirMap = new Mat();
                    CvInvoke.BitwiseNot(curLayer, currentAirMap);
                }

                /* we still modify the airmap like normal, where we account for the air areas of the layer, and any contours that might overlap...*/
                GenerateAirMap(curLayer, layerAirMap, externalContours[layerIndex]);

                /* Update air map with any hollows that were found to be air-connected during first pass */
                if (airContours[layerIndex] is not null)
                {
                    Parallel.ForEach(airContours[layerIndex], CoreSettings.ParallelOptions, vec =>
                        {
                            progress.PauseIfRequested();
                            CvInvoke.DrawContours(layerAirMap, vec, -1, EmguExtensions.WhiteColor, -1);
                        }
                    );
                }

                /* remove solid areas of current layer from the air map */
                CvInvoke.Subtract(currentAirMap, curLayer, currentAirMap);

                /* add in areas of air in current layer to air map */
                CvInvoke.BitwiseOr(layerAirMap, currentAirMap, currentAirMap);

                if (resinTraps[layerIndex] is not null)
                {
                    suctionCups[layerIndex] = [];
                    /* here we don't worry about finding contours on the layer, the bottom to top pass did that already */
                    /* all we care about is contours the first pass thought were resin traps, since there was no access to air from the bottom */
                    Parallel.For(0, resinTraps[layerIndex].Count, CoreSettings.ParallelOptions, x =>
                    {
                        progress.PauseIfRequested();
                        if (progress.Token.IsCancellationRequested) return;

                        /* check if each contour overlaps known air */
                        using var currentContour = curLayer.NewZeros();
                        using var airOverlap = new Mat();
                        CvInvoke.DrawContours(currentContour, resinTraps[layerIndex][x], -1, EmguExtensions.WhiteColor, -1);

                        CvInvoke.BitwiseAnd(currentAirMap, currentContour, airOverlap);
                        var overlapCount = CvInvoke.CountNonZero(airOverlap);

                        //lock (SlicerFile[layerIndex].Mutex)
                        //{
                        if (overlapCount >= resinTrapConfig.RequiredBlackPixelsToDrain)
                        {
                            /* this contour does overlap air, add this it our air map */
                            CvInvoke.BitwiseOr(currentContour, currentAirMap, currentAirMap, currentContour);
                            /* Always add the removed contour to suctionTraps (even if we aren't reporting suction traps)
                             * This is because contours that are placed on here get removed from resin traps in the next stage
                             * if you don't put them here, they never get removed even if they should :) */

                            /* if we haven't defined a suctionTrap list for this layer, do so */

                            lock (SlicerFile[layerIndex].Mutex)
                            {
                                /* since we know it isn't a resin trap, it becomes a suction trap */
                                suctionCups[layerIndex].Add(resinTraps[layerIndex][x]);

                                for (var groupIndex = resinTrapGroups.Count - 1; groupIndex >= 0; groupIndex--)
                                {
                                    var group = resinTrapGroups[groupIndex];
                                    if (group[^1].layerIndex > layerIndex + 1)
                                    {
                                        // this group is disconnected from current layer by at least 1 layer, no need to process anything from here anymore
                                        //group.Clear();
                                        //resinTrapGroups.Remove(group);
                                        continue;
                                    }

                                    for (var contourIndex = group.Count - 1; contourIndex >= 0; contourIndex--)
                                    {
                                        if (group[contourIndex].layerIndex > layerIndex + 1) break;
                                        var testContour = group[contourIndex].contour;

                                        if (!EmguContours.ContoursIntersect(testContour, resinTraps[layerIndex][x])) continue;
                                        // if any contours in this group, that are on the previous layer, overlap the new suction area, they are all suction areas

                                        foreach (var item in group)
                                        {
                                            suctionCups[item.layerIndex].Add(item.contour);
                                            if (item.layerIndex != layerIndex)
                                            {
                                                resinTraps[item.layerIndex].Remove(item.contour);
                                            }
                                        }

                                        group.Clear();
                                        resinTrapGroups.Remove(group);
                                        break;
                                    }
                                }
                            }
                            /* to keep things tidy while we iterate resin traps, it will be left in the list for now, and removed later */
                        }
                        else
                        {
                            /* doesn't overlap by enough, remove from air map */
                            CvInvoke.Subtract(currentAirMap, currentContour, currentAirMap, currentContour);

                            lock (SlicerFile[layerIndex].Mutex)
                            {
                                /* put it in a group of resin traps, used when a subsequent layer becomes a suction cup, it can convert any overlapping groups to suction cup */
                                /* select new LayerIssue(this[layerIndex], LayerIssue.IssueType.ResinTrap, area.Contour, area.BoundingRectangle)) */
                                var overlappingGroupIndexes = new List<int>();
                                for (var groupIndex = 0; groupIndex < resinTrapGroups.Count; groupIndex++)
                                {
                                    if (resinTrapGroups[groupIndex][^1].layerIndex != layerIndex && resinTrapGroups[groupIndex][^1].layerIndex != layerIndex + 1) continue;

                                    if (EmguContours.ContoursIntersect(resinTrapGroups[groupIndex][^1].contour, resinTraps[layerIndex][x]))
                                    {
                                        overlappingGroupIndexes.Add(groupIndex);
                                    }
                                }

                                if (overlappingGroupIndexes.Count == 0)
                                {
                                    // no overlaps, make a single issue
                                    resinTrapGroups.Add([(resinTraps[layerIndex][x], (uint)layerIndex)]);
                                }
                                else if (overlappingGroupIndexes.Count == 1)
                                {
                                    resinTrapGroups[overlappingGroupIndexes[0]].Add((resinTraps[layerIndex][x], (uint)layerIndex));
                                }
                                else
                                {
                                    var combinedGroup = new List<(VectorOfVectorOfPoint contour, uint layerIndex)>();
                                    foreach (var index in overlappingGroupIndexes)
                                    {
                                        combinedGroup.AddRange(resinTrapGroups[index]);
                                    }

                                    for (var index = overlappingGroupIndexes.Count - 1; index >= 0; index--)
                                    {
                                        resinTrapGroups[overlappingGroupIndexes[index]].Clear();
                                        resinTrapGroups.RemoveAt(overlappingGroupIndexes[index]);
                                    }

                                    combinedGroup.Add((resinTraps[layerIndex][x], (uint)layerIndex));
                                    resinTrapGroups.Add(combinedGroup);
                                }
                            }
                        }
                        //}
                    });

                    /* anything that converted to a suction trap needs to removed from resinTraps. Loop backwards so indexes don't shift */
                    if (suctionCups[layerIndex] is not null)
                    {
                        for (var i = suctionCups[layerIndex].Count - 1; i >= 0; i--)
                        {
                            resinTraps[layerIndex].Remove(suctionCups[layerIndex][i]);
                            if (resinTraps[layerIndex].Count > 0) continue;
                            resinTraps[layerIndex] = null!;
                            break;
                        }
                    }
                }

                matCache.Consume((uint)layerIndex);

                progress++;
            }

            if (currentAirMap is not null)
            {
                currentAirMap.Dispose();
                currentAirMap = null;
            }

            if (progress.Token.IsCancellationRequested) return GetResult();

            /* translate all contour points by ROI x and y */
            var offsetBy = new Point(SlicerFile.BoundingRectangle.X, SlicerFile.BoundingRectangle.Y);
            foreach (var listOfLayers in new[] { resinTraps, suctionCups })
            {
                Parallel.ForEach(listOfLayers.Where(list => list is not null), contoursGroups =>
                {
                    progress.PauseIfRequested();
                    for (var groupIndex = 0; groupIndex < contoursGroups.Count; groupIndex++)
                    {
                        var contours = contoursGroups[groupIndex];

                        var arrayOfArrayOfPoints = contours.ToArrayOfArray();

                        foreach (var pointArray in arrayOfArrayOfPoints)
                            for (var i = 0; i < pointArray.Length; i++)
                                pointArray[i].Offset(offsetBy);

                        contoursGroups[groupIndex].Dispose();
                        contoursGroups[groupIndex] = new VectorOfVectorOfPoint(arrayOfArrayOfPoints);
                    }

                    //progress.LockAndIncrement();
                });
            }

            if (progress.Token.IsCancellationRequested) return GetResult();

            if (resinTrapConfig.DetectSuctionCups)
                progress.Reset("Interpolating areas (Resin traps & suction cups)", (uint)(resinTraps.Count(list => list is not null) + suctionCups.Count(list => list is not null)));
            else
                progress.Reset("Interpolating areas (Resin traps)", (uint)(resinTraps.Count(list => list is not null)));

            Parallel.Invoke(() =>
                {
                    var resinTrapGroups = new List<List<IssueOfContours>>();

                    for (var layerIndex = resinTraps.Length - 1; layerIndex >= 0; layerIndex--)
                    {
                        if (resinTraps[layerIndex] is null) continue;

                        /* select new LayerIssue(this[layerIndex], LayerIssue.IssueType.ResinTrap, area.Contour, area.BoundingRectangle)) */
                        foreach (var trap in resinTraps[layerIndex])
                        {
                            progress.PauseIfRequested();
                            if (progress.Token.IsCancellationRequested) return;

                            var area = EmguContours.GetContourArea(trap);
                            var rect = CvInvoke.BoundingRectangle(trap[0]);
                            var trapIssue = new IssueOfContours(SlicerFile[layerIndex], trap.ToArrayOfArray(), rect, area);

                            var overlappingGroupIndexes = new List<int>();
                            for (var x = 0; x < resinTrapGroups.Count; x++)
                            {
                                if (resinTrapGroups[x][^1].LayerIndex != layerIndex && resinTrapGroups[x][^1].LayerIndex != layerIndex + 1) continue;

                                using var vec = new VectorOfVectorOfPoint(resinTrapGroups[x][^1].Contours);
                                if (EmguContours.ContoursIntersect(trap, vec))
                                {
                                    overlappingGroupIndexes.Add(x);
                                }
                            }

                            if (overlappingGroupIndexes.Count == 0)
                            {
                                /* no overlaps, make a single issue */
                                resinTrapGroups.Add([trapIssue]);
                            }
                            else if (overlappingGroupIndexes.Count == 1)
                            {
                                resinTrapGroups[overlappingGroupIndexes[0]].Add(trapIssue);
                            }
                            else
                            {
                                var combinedGroup = new List<IssueOfContours>();
                                foreach (var index in overlappingGroupIndexes)
                                {
                                    combinedGroup.AddRange(resinTrapGroups[index]);
                                }

                                for (var index = overlappingGroupIndexes.Count - 1; index >= 0; index--)
                                {
                                    resinTrapGroups[overlappingGroupIndexes[index]].Clear();
                                    resinTrapGroups.RemoveAt(overlappingGroupIndexes[index]);
                                }

                                combinedGroup.Add(trapIssue);
                                resinTrapGroups.Add(combinedGroup);
                            }
                        }
                        progress.LockAndIncrement();
                    }

                    foreach (var group in resinTrapGroups)
                    {
                        if(group.AsValueEnumerable().Any(issue => issue.LayerIndex == 0)) continue; // Not a trap if on plate
                        AddIssue(new MainIssue(MainIssue.IssueType.ResinTrap, group));
                    }
                },
                () =>
                {
                    /* only report suction cup issues if enabled */
                    if (resinTrapConfig.DetectSuctionCups)
                    {
                        var minimumSuctionArea = resinTrapConfig.RequiredAreaToConsiderSuctionCup;
                        var suctionGroups = new List<List<IssueOfContours>>();

                        for (var layerIndex = suctionCups.Length - 1; layerIndex >= 0; layerIndex--)
                        {
                            if (suctionCups[layerIndex] is null) continue;

                            foreach (var trap in suctionCups[layerIndex])
                            {
                                progress.PauseIfRequested();
                                if (progress.Token.IsCancellationRequested) return;

                                var area = EmguContours.GetContourArea(trap);
                                if (area < minimumSuctionArea) continue;
                                var rect = CvInvoke.BoundingRectangle(trap[0]);

                                var trapIssue = new IssueOfContours(SlicerFile[layerIndex], trap.ToArrayOfArray(), rect, area);

                                var overlappingGroupIndexes = new List<int>();
                                for (var x = 0; x < suctionGroups.Count; x++)
                                {
                                    if (suctionGroups[x][^1].LayerIndex != layerIndex && suctionGroups[x][^1].LayerIndex != layerIndex + 1) continue;
                                    using var vec = new VectorOfVectorOfPoint(suctionGroups[x][^1].Contours);
                                    if (EmguContours.ContoursIntersect(trap, vec))
                                    {
                                        overlappingGroupIndexes.Add(x);
                                    }
                                }

                                if (overlappingGroupIndexes.Count == 0)
                                {
                                    /* no overlaps, make a new group */
                                    suctionGroups.Add([trapIssue]);
                                }
                                else if (overlappingGroupIndexes.Count == 1)
                                {
                                    suctionGroups[overlappingGroupIndexes[0]].Add(trapIssue);
                                }
                                else
                                {
                                    var combinedGroup = new List<IssueOfContours>();
                                    /* iterate backwards to not screw up indexes */
                                    for (var i = overlappingGroupIndexes.Count - 1; i >= 0; i--)
                                    {
                                        var index = overlappingGroupIndexes[i];
                                        combinedGroup.AddRange(suctionGroups[index]);
                                        suctionGroups[index].Clear();
                                        suctionGroups.RemoveAt(index);
                                    }
                                    combinedGroup.Add(trapIssue);
                                    suctionGroups.Add(combinedGroup);
                                }
                            }
                            progress.LockAndIncrement();
                        }

                        foreach (var group in suctionGroups)
                        {
                            var mainIssue = new MainIssue(MainIssue.IssueType.SuctionCup, group);
                            if ((decimal)mainIssue.TotalHeight >= resinTrapConfig.RequiredHeightToConsiderSuctionCup)
                            {
                                AddIssue(mainIssue);
                            }
                        }
                    }
                });

            // Dispose
            foreach (var listOfVectors in new[] { resinTraps, suctionCups, hollows, airContours })
            {
                foreach (var vectorArray in listOfVectors)
                {
                    if (vectorArray is null) continue;
                    foreach (var vector in vectorArray)
                    {
                        vector?.Dispose();
                    }
                }
            }

            foreach (var vector in externalContours)
            {
                vector?.Dispose();
            }

        }

        return GetResult();
    }

    public MainIssue[] DrillSuctionCupsForIssues(IEnumerable<MainIssue> issues, int ventHoleDiameter, OperationProgress progress)
    {
        var drillOps = new List<PixelOperation>();
        var drilledIssues = new List<MainIssue>();
        var radius = SlicerFile.PixelsToNormalizedPitch(ventHoleDiameter / 2);
        //var suctionReliefSize = (ushort)Math.Max(SlicerFile.PpmmMax * 0.8, 17);
        /* for each suction cup issue that is an initial layer */
        foreach (var mainIssue in issues)
        {
            var drillPoint = GetDrillLocation((IssueOfContours)mainIssue[0], radius);
            if (drillPoint.IsAnyNegative()) continue;
            drillOps.Add(new PixelDrainHole(mainIssue.StartLayerIndex, drillPoint, (ushort)ventHoleDiameter));
            drilledIssues.Add(mainIssue);
        }

        SlicerFile.DrawModifications(drillOps, progress);

        return drilledIssues.ToArray();
    }

    public static Point GetDrillLocation(IssueOfContours issue, Size radius)
    {
        using var vecCentroid = new VectorOfPoint(issue.Contours[0]);
        var centroid = EmguContour.GetCentroid(vecCentroid);
        if (centroid.IsAnyNegative()) return centroid;
        using var circleCheck = EmguExtensions.InitMat(issue.BoundingRectangle.Size);
        using var contourMat = EmguExtensions.InitMat(issue.BoundingRectangle.Size);

        var inverseOffset = new Point(issue.BoundingRectangle.X * -1, issue.BoundingRectangle.Y * -1);
        using var vec = new VectorOfVectorOfPoint(issue.Contours);
        CvInvoke.DrawContours(contourMat, vec, -1, EmguExtensions.WhiteColor, -1, LineType.EightConnected, null, int.MaxValue, inverseOffset);
        circleCheck.DrawCircle(new(centroid.X + inverseOffset.X, centroid.Y + inverseOffset.Y), radius, EmguExtensions.WhiteColor, -1);
        CvInvoke.BitwiseAnd(circleCheck, contourMat, circleCheck);

        return CvInvoke.HasNonZero(circleCheck)
            ? centroid       /* 5px centroid is inside layer! drill baby drill */
            : new Point(-1,-1); /* centroid is not inside the actual contour, no drill */
    }
}