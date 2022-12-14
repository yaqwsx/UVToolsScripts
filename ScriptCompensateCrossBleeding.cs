using System;
using System.Drawing;
using System.Threading.Tasks;
using UVtools.Core;
using UVtools.Core.Extensions;
using UVtools.Core.Scripting;
using Emgu.CV;

namespace UVtools.ScriptSample;

public class ScriptCompensateCrossBleeding : ScriptGlobals
{
    readonly ScriptNumericalInput<ushort> LayerBleed = new()
    {
        Label = "Number of layers the exposure bleeds through",
        Unit = "layers",
        Minimum = 1,
        Maximum = 500,
        Increment = 1,
        Value = 5,
    };

    public void ScriptInit()
    {
        Script.Name = "Mitigates effects of cross-layer bleeding";
        Script.Description = "Adjusts overhands so we can compensate for cross-layer curing";
        Script.Author = "Jan Mrázek";
        Script.Version = new Version(0, 1);
        Script.UserInputs.Add(LayerBleed);
    }

    public string? ScriptValidate()
    {
        return SlicerFile.LayerCount < 2 
            ? "This script requires at least 2 layers in order to run." 
            : null;
    }

    public bool ScriptExecute()
    {
        Progress.Reset("Changing layers", Operation.LayerRangeCount); // Sets the progress name and number of items to process

        // Jan
        /*
        Layer[] newLayers = new Layer[SlicerFile.LayerCount];
        Parallel.For(Operation.LayerIndexStart, Operation.LayerIndexEnd + 1,
            CoreSettings.GetParallelOptions(Progress),
            layerIndex =>
            {
                var layersBelowCount = layerIndex > LayerBleed.Value ? LayerBleed.Value : layerIndex;

                using var layerMat = SlicerFile.Layers[layerIndex].LayerMat; // -> Don't use verbose .Layers, instead use: SlicerFile[layerIndex].LayerMat
                var source = layerMat.GetDataByteSpan2D();

                var targetMat = EmguExtensions.InitMat(new Size(layerMat.Width, layerMat.Height));
                var target = targetMat.GetDataByteSpan2D(); // Never use Span2D when you don't require y,x, instead use Span

                var occupancyMat = EmguExtensions.InitMat(new Size(layerMat.Width, layerMat.Height));
                for (int i = 0; i != layersBelowCount; i++)
                {
                    using var mat = SlicerFile.Layers[layerIndex - i - 1].LayerMat;
                    CvInvoke.Threshold(mat, mat, 1, 1, Emgu.CV.CvEnum.ThresholdType.Binary);
                    CvInvoke.Add(mat, occupancyMat, occupancyMat);
                    // var span = mat.GetDataByteSpan2D();

                    // for (int y = 0; y != layerMat.Height; y++) {
                    //     for (int x = 0; x != layerMat.Width; x++) {
                    //         if (span[y, x] > 0) {
                    //             occupancy[y, x] += 1;
                    //         }
                    //     }
                    // }
                }

                var occupancy = occupancyMat.GetDataByteSpan2D();
                for (int y = 0; y != layerMat.Height; y++)
                {
                    for (int x = 0; x != layerMat.Width; x++)
                    {
                        if (layersBelowCount == 0 || occupancy[y, x] == layersBelowCount)
                            target[y, x] = source[y, x];
                    }
                }

                // --> Will not be required, less operations and allocations
                var newLayer = SlicerFile.Layers[layerIndex].Clone();
                newLayer.LayerMat = targetMat;
                newLayers[layerIndex] = newLayer;
                Progress.LockAndIncrement();
            });

        // --> Will not be required, less operations and allocations
        SlicerFile.SuppressRebuildPropertiesWork(() => {
            Parallel.For(Operation.LayerIndexStart, Operation.LayerIndexEnd + 1,
            CoreSettings.GetParallelOptions(Progress),
            layerIndex =>
            {
                SlicerFile.Layers[layerIndex] = newLayers[layerIndex];
            });
        });
        */

        // sn4k3
        var originalLayers = SlicerFile.CloneLayers();
        Parallel.For(Operation.LayerIndexStart, Operation.LayerIndexEnd + 1,
            CoreSettings.GetParallelOptions(Progress),
            layerIndex =>
            {
                var layersBelowCount = layerIndex > LayerBleed.Value ? LayerBleed.Value : layerIndex;

                using var sourceMat = originalLayers[layerIndex].LayerMat;
                var source = sourceMat.GetDataByteSpan();

                using var targetMat = sourceMat.NewBlank();
                var target = targetMat.GetDataByteSpan();

                using var occupancyMat = sourceMat.NewBlank();
                var occupancy = occupancyMat.GetDataByteSpan();

                var sumRectangle = Rectangle.Empty;
                for (int i = 0; i < layersBelowCount; i++)
                {
                    using var mat = originalLayers[layerIndex - i - 1].LayerMat;
                    CvInvoke.Threshold(mat, mat, 1, 1, Emgu.CV.CvEnum.ThresholdType.Binary);
                    CvInvoke.Add(mat, occupancyMat, occupancyMat);
                    sumRectangle = sumRectangle.IsEmpty 
                        ? originalLayers[layerIndex - i - 1].BoundingRectangle 
                        : Rectangle.Union(sumRectangle, originalLayers[layerIndex - i - 1].BoundingRectangle);
                }

                // Spare a few useless cycles depending on model volume on LCD
                var optimizedStatingPixelIndex = sourceMat.GetPixelPos(sumRectangle.Location);
                var optimizedEndingPixelIndex = sourceMat.GetPixelPos(sumRectangle.Right, sumRectangle.Bottom);
                for (var i = optimizedStatingPixelIndex; i < optimizedEndingPixelIndex; i++)
                {
                    if (layersBelowCount == 0 || occupancy[i] == layersBelowCount)
                        target[i] = source[i];
                }

                SlicerFile[layerIndex].LayerMat = targetMat;
                Progress.LockAndIncrement();
            });

        // return true if not cancelled by user
        return !Progress.Token.IsCancellationRequested;
    }
}
