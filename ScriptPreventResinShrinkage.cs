using System;
using System.Drawing;
using System.Threading.Tasks;
using UVtools.Core;
using UVtools.Core.Extensions;
using UVtools.Core.Scripting;
using UVtools.Core.Layers;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;

namespace UVtools.ScriptSample;

public class ScriptPreventResinShrinkage : ScriptGlobals
{
    readonly ScriptNumericalInput<int> GrainSize = new()
    {
        Label = "Size of the initial grains",
        Unit = "px",
        Minimum = 1,
        Maximum = 500,
        Increment = 1,
        Value = 11,
    };

    readonly ScriptNumericalInput<int> Spacing = new()
    {
        Label = "Free space between the grains",
        Unit = "px",
        Minimum = 1,
        Maximum = 500,
        Increment = 1,
        Value = 9,
    };

    /// <summary>
    /// Set configurations here, this function trigger just after load a script
    /// </summary>
    public void ScriptInit()
    {
        Script.Name = "Preventing the effects of resin shrinkage";
        Script.Description = "Cures a layer in multiple exposures to mitigate resin shrinkage effects";
        Script.Author = "Jan Mrázek";
        Script.Version = new Version(0, 1);
        Script.UserInputs.Add(GrainSize);
        Script.UserInputs.Add(Spacing);
    }

    /// <summary>
    /// Validate user inputs here, this function trigger when user click on execute
    /// </summary>
    /// <returns>A error message, empty or null if validation passes.</returns>
    public string? ScriptValidate()
    {
        return null;
        // return SlicerFile.CanUseAnyLightOffDelay || SlicerFile.CanUseAnyWaitTimeBeforeCure ? null : "Your printer/file format is not supported.";
    }

    private Mat GenerateDotPattern() {
        var pattern = EmguExtensions.InitMat(SlicerFile.Resolution);

        var white = new MCvScalar(255, 255, 255);
        var xStep = GrainSize.Value + Spacing.Value;
        var yStep = (GrainSize.Value + Spacing.Value) / 2;
        var evenRow = false;
        for (int y = 0; y < pattern.Size.Height; y += yStep) {
            for (int x = 0; x < pattern.Size.Width; x += xStep) {
                CvInvoke.Circle(pattern,
                    new Point(x + (evenRow ? xStep / 2 : 0), y),
                    GrainSize.Value / 2,
                    white,
                    -1, LineType.FourConnected);
            }
            evenRow = !evenRow;
        }

        return pattern;
    }

    private Mat GenerateLinePattern() {
        var pattern = EmguExtensions.InitMat(SlicerFile.Resolution);

        var width = GrainSize.Value / 5;
        if (width == 0)
            width = 1;
        var white = new MCvScalar(255, 255, 255);
        var step = GrainSize.Value + Spacing.Value;
        for (int x = 0; x < pattern.Size.Width; x += step) {
            CvInvoke.Line(pattern,
                new Point(x, 0),
                new Point(x + pattern.Size.Height, pattern.Size.Height),
                white, width, LineType.FourConnected);
            CvInvoke.Line(pattern,
                new Point(x, pattern.Size.Height),
                new Point(x + pattern.Size.Height, 0),
                white, width, LineType.FourConnected);
        }
        for (int y = 0; y < pattern.Size.Height; y += step) {
            CvInvoke.Line(pattern,
                new Point(0, y),
                new Point(pattern.Size.Height, y + pattern.Size.Height),
                white, width, LineType.FourConnected);
            CvInvoke.Line(pattern,
                new Point(0, y),
                new Point(pattern.Size.Height, y - pattern.Size.Height),
                white, width, LineType.FourConnected);
        }
        return pattern;
    }

    /// <summary>
    /// Execute the script, this function trigger when when user click on execute and validation passes
    /// </summary>
    /// <returns>True if executes successfully to the end, otherwise false.</returns>
    public bool ScriptExecute()
    {
        Progress.Reset("Changing layers", Operation.LayerRangeCount); // Sets the progress name and number of items to process

        Layer[] newLayers = new Layer[3 * SlicerFile.LayerCount];
        var dotPattern = GenerateDotPattern();
        var linePattern = GenerateLinePattern();

        var inverseDotPattern = EmguExtensions.InitMat(SlicerFile.Resolution);
        CvInvoke.BitwiseNot(dotPattern, inverseDotPattern);
        CvInvoke.Dilate(inverseDotPattern, inverseDotPattern,
            EmguExtensions.Kernel3x3Rectangle,
            new Point(-1, -1), 1, BorderType.Reflect101, default);

        var dotLinePattern = EmguExtensions.InitMat(SlicerFile.Resolution);
        CvInvoke.BitwiseAnd(inverseDotPattern, linePattern, dotLinePattern);

        Parallel.For( Operation.LayerIndexStart, Operation.LayerIndexEnd + 1,
            CoreSettings.GetParallelOptions(Progress),
            layerIndex =>
        {
            var layer = SlicerFile.Layers[layerIndex];

            var coresLayer1 = layer.Clone();
            var coresLayer2 = layer.Clone();
            var fullLayer = layer.Clone();

            var coresMat1 = coresLayer1.LayerMat.Clone();
            // Ensure there is something we can attach to in the previous layer
            if (layerIndex != 0)
                CvInvoke.BitwiseAnd(coresMat1,
                                    SlicerFile.Layers[layerIndex - 1].LayerMat,
                                    coresMat1);
            var coresMat2 = coresMat1.Clone();

            CvInvoke.BitwiseAnd(coresMat1,
                                dotPattern,
                                coresMat1);
            CvInvoke.BitwiseAnd(coresMat2,
                                dotLinePattern,
                                coresMat2);

            coresLayer1.LayerMat = coresMat1;
            coresLayer2.LayerMat = coresMat2;

            newLayers[3 * layerIndex] = coresLayer1;
            newLayers[3 * layerIndex + 1] = coresLayer2;
            newLayers[3 * layerIndex + 2] = fullLayer;

            Progress.LockAndIncrement();
        });

        SlicerFile.SuppressRebuildPropertiesWork(() => {
            SlicerFile.Layers = newLayers;
        });
        // return true if not cancelled by user
        return !Progress.Token.IsCancellationRequested;
    }
}
