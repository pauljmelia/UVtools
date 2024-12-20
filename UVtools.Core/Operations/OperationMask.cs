﻿/*
 *                     GNU AFFERO GENERAL PUBLIC LICENSE
 *                       Version 3, 19 November 2007
 *  Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
 *  Everyone is permitted to copy and distribute verbatim copies
 *  of this license document, but changing it is not allowed.
 */

using Emgu.CV;
using Emgu.CV.CvEnum;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using UVtools.Core.Extensions;
using UVtools.Core.FileFormats;

namespace UVtools.Core.Operations;


public class OperationMask : Operation
{
    #region Overrides
    public override string IconClass => "fa-solid fa-mask";
    public override string Title => "Mask";
    public override string Description =>
        "Mask the intensity of the LCD output using a greyscale input image.\n\n" +
        "Useful to correct LCD light uniformity for a specific printer.\n\n" +
        "NOTE: This operation should be run only after repairs and other transformations. The provided " +
        "input mask image must match the output resolution of the target printer.";

    public override string ConfirmationText =>
        $"mask layers from {LayerIndexStart} through {LayerIndexEnd}";

    public override string ProgressTitle =>
        $"Masking layers from {LayerIndexStart} through {LayerIndexEnd}";

    public override string ProgressAction => "Masked layers";

    public override bool CanHaveProfiles => false;

    public override string? ValidateInternally()
    {
        var sb = new StringBuilder();
        if (!HaveInputMask)
        {
            sb.AppendLine("The mask can not be empty.");
        }

        return sb.ToString();
    }
    #endregion

    #region Properties
    [XmlIgnore]
    public Mat? Mask { get; set; }

    public bool HaveInputMask => Mask is not null;
    #endregion

    #region Constructor

    public OperationMask() { }

    public OperationMask(FileFormat slicerFile) : base(slicerFile) { }

    #endregion

    #region Methods

    /// <summary>
    /// Loads mask from a image file
    /// </summary>
    /// <param name="filePath"></param>
    /// <param name="invertMask"></param>
    /// <param name="maskSize"></param>
    public void LoadFromFile(string filePath, bool invertMask = false, Size maskSize = default)
    {
        Mask = CvInvoke.Imread(filePath, ImreadModes.Grayscale);
        if (maskSize is {Width: > 0, Height: > 0} && Mask.Size != maskSize)
        {
            CvInvoke.Resize(Mask, Mask, maskSize);
        }

        if (invertMask)
        {
            InvertMask();
        }
    }

    public void InvertMask()
    {
        if (!HaveInputMask) return;
        CvInvoke.BitwiseNot(Mask, Mask);
    }

    protected override bool ExecuteInternally(OperationProgress progress)
    {
        Parallel.For(LayerIndexStart, LayerIndexEnd + 1, CoreSettings.GetParallelOptions(progress), layerIndex =>
        {
            progress.PauseIfRequested();
            using var mat = SlicerFile[layerIndex].LayerMat;
            Execute(mat);
            SlicerFile[layerIndex].LayerMat = mat;
            progress.LockAndIncrement();
        });

        return !progress.Token.IsCancellationRequested;
    }

    public override bool Execute(Mat mat, params object[]? arguments)
    {
        using var target = GetRoiOrDefault(mat);
        using var mask = GetMask(mat);
        if (Mask!.Size != target.Size) return false;
        //CvInvoke.BitwiseAnd(target, Mask, target, mask);
        CvInvoke.Multiply(target, Mask, target, EmguExtensions.ByteScale);
        return true;
    }

    #endregion
}