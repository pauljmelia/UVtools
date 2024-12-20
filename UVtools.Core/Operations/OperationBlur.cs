﻿/*
 *                     GNU AFFERO GENERAL PUBLIC LICENSE
 *                       Version 3, 19 November 2007
 *  Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
 *  Everyone is permitted to copy and distribute verbatim copies
 *  of this license document, but changing it is not allowed.
 */

using Emgu.CV;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using UVtools.Core.Extensions;
using UVtools.Core.FileFormats;
using UVtools.Core.Objects;

namespace UVtools.Core.Operations;


#pragma warning disable CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
public sealed class OperationBlur : Operation
#pragma warning restore CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
{
    #region Members
    private BlurAlgorithm _blurOperation;
    private uint _size = 1;
    #endregion

    #region Overrides

    public override string IconClass => "fa-solid fa-burn";
    public override string Title => "Blur";
    public override string Description =>
        $"Blur layer images by applying a low pass filter.\n\n" +
        "NOTE: Target printer must support AntiAliasing in order to use this function.\n" +
        "See https://docs.opencv.org/master/d4/d13/tutorial_py_filtering.html";

    public override string ConfirmationText =>
        $"blur model with {BlurOperation} from layers {LayerIndexStart} through {LayerIndexEnd}?";

    public override string ProgressTitle =>
        $"Bluring model with {BlurOperation} from layers {LayerIndexStart} through {LayerIndexEnd}";

    public override string ProgressAction => "Blured layers";

    public override string? ValidateInternally()
    {
        var sb = new StringBuilder();

        if (BlurOperation is BlurAlgorithm.StackBlur or BlurAlgorithm.GaussianBlur or BlurAlgorithm.MedianBlur)
        {
            if (Size % 2 != 1)
            {
                sb.AppendLine("Size must be a odd number.");
            }
        }

        if (BlurOperation == BlurAlgorithm.Filter2D)
        {
            if (Kernel is null)
            {
                sb.AppendLine("Kernel can not be empty.");
            }
        }

        return sb.ToString();
    }

    #endregion

    #region Enums
    public enum BlurAlgorithm
    {
        [Description("Stack Blur: Normalized stack blur")]
        StackBlur,
        [Description("Box Blur: Normalized box filter")]
        BoxBlur,
        [Description("Pyramid: Down/up-sampling step of Gaussian pyramid decomposition")]
        Pyramid,
        [Description("Median Blur: Each pixel becomes the median of its surrounding pixels")]
        MedianBlur,
        [Description("Gaussian Blur: Each pixel is a sum of fractions of each pixel in its neighborhood")]
        GaussianBlur,
        [Description("Filter 2D: Applies an arbitrary linear filter to an image")]
        Filter2D
    }
    #endregion

    #region Properties

    public BlurAlgorithm BlurOperation
    {
        get => _blurOperation;
        set
        {
            if(!RaiseAndSetIfChanged(ref _blurOperation, value)) return;
            RaisePropertyChanged(nameof(IsSizeEnabled));
            RaisePropertyChanged(nameof(IsKernelVisible));
        }
    }

    public uint Size
    {
        get => _size;
        set => RaiseAndSetIfChanged(ref _size, value);
    }

    public bool IsSizeEnabled => BlurOperation != BlurAlgorithm.Pyramid &&
                                 BlurOperation != BlurAlgorithm.Filter2D;

    public bool IsKernelVisible => BlurOperation == BlurAlgorithm.Filter2D;

    public KernelConfiguration Kernel { get; set; } = new ();

    public override string ToString()
    {
        var result = $"[{_blurOperation}] [Size: {_size}]" + LayerRangeString;
        if (!string.IsNullOrEmpty(ProfileName)) result = $"{ProfileName}: {result}";
        return result;
    }

    #endregion

    #region Constructor

    public OperationBlur() { }

    public OperationBlur(FileFormat slicerFile) : base(slicerFile) { }

    #endregion

    #region Methods

    protected override bool ExecuteInternally(OperationProgress progress)
    { 
        Parallel.For(LayerIndexStart, LayerIndexEnd + 1, CoreSettings.GetParallelOptions(progress), layerIndex =>
        {
            progress.PauseIfRequested();
            using (var mat = SlicerFile[layerIndex].LayerMat)
            {
                Execute(mat);
                SlicerFile[layerIndex].LayerMat = mat;
            }
            progress.LockAndIncrement();
        });

        return !progress.Token.IsCancellationRequested;
    }

    public override bool Execute(Mat mat, params object[]? arguments)
    {
        Size size = new((int)Size, (int)Size);
        Point anchor = Kernel.Anchor;
        if (anchor.IsEmpty) anchor = EmguExtensions.AnchorCenter;
        //if (size.IsEmpty) size = new Size(3, 3);
        //if (anchor.IsEmpty) anchor = EmguExtensions.AnchorCenter;
        using var target = GetRoiOrDefault(mat);
        using var original = mat.Clone();
        switch (BlurOperation)
        {
            case BlurAlgorithm.StackBlur:
                CvInvoke.StackBlur(target, target, size);
                break;
            case BlurAlgorithm.BoxBlur:
                CvInvoke.Blur(target, target, size, Kernel.Anchor);
                break;
            case BlurAlgorithm.Pyramid:
                CvInvoke.PyrDown(target, target);
                CvInvoke.PyrUp(target, target);
                break;
            case BlurAlgorithm.MedianBlur:
                CvInvoke.MedianBlur(target, target, (int)Size);
                break;
            case BlurAlgorithm.GaussianBlur:
                CvInvoke.GaussianBlur(target, target, size, 0);
                break;
            case BlurAlgorithm.Filter2D:
                CvInvoke.Filter2D(target, target, Kernel.GetKernel(), anchor);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        ApplyMask(original, target);

        return true;
    }


    #endregion

    #region Equality
    private bool Equals(OperationBlur other)
    {
        return _blurOperation == other._blurOperation && _size == other._size;
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || obj is OperationBlur other && Equals(other);
    }

    #endregion
}