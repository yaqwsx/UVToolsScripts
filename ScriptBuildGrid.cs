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
    readonly ScriptNumericalInput<int> GridSpacing = new()
    {
        Label = "Size of the initial grains",
        Unit = "px",
        Minimum = 1,
        Maximum = 10000,
        Increment = 1,
        Value = 200,
    };

    readonly ScriptNumericalInput<int> GridWidth = new()
    {
        Label = "Width of line",
        Unit = "px",
        Minimum = 1,
        Maximum = 500,
        Increment = 1,
        Value = 1,
    };

    /// <summary>
    /// Set configurations here, this function trigger just after load a script
    /// </summary>
    public void ScriptInit()
    {
        Script.Name = "Build grid pattern";
        Script.Description = "Useful for measuring backlight quality";
        Script.Author = "Jan Mrázek";
        Script.Version = new Version(0, 1);
        Script.UserInputs.Add(GridSpacing);
        Script.UserInputs.Add(GridWidth);
    }

    /// <summary>
    /// Validate user inputs here, this function trigger when user click on execute
    /// </summary>
    /// <returns>A error message, empty or null if validation passes.</returns>
    public string? ScriptValidate()
    {
        return null;
    }

    private Mat GenerateGridPattern() {
        var pattern = EmguExtensions.InitMat(SlicerFile.Resolution);
        var white = new MCvScalar(255, 255, 255);

        for (int x = pattern.Size.Width / 2; x < pattern.Size.Width; x += GridSpacing.Value) {
            CvInvoke.Line(pattern,
                new Point(x, 0),
                new Point(x, pattern.Size.Height),
                white, GridWidth.Value, LineType.FourConnected);
            CvInvoke.Line(pattern,
                new Point(pattern.Size.Width - x, 0),
                new Point(pattern.Size.Width - x, pattern.Size.Height),
                white, GridWidth.Value, LineType.FourConnected);
        }

        for (int y = pattern.Size.Height / 2; y < pattern.Size.Height; y += GridSpacing.Value) {
            CvInvoke.Line(pattern,
                new Point(0, y),
                new Point(pattern.Size.Width, y),
                white, GridWidth.Value, LineType.FourConnected);
            CvInvoke.Line(pattern,
                new Point(0, pattern.Size.Height - y),
                new Point(pattern.Size.Width, pattern.Size.Height - y),
                white, GridWidth.Value, LineType.FourConnected);
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

        Layer[] newLayers = new Layer[2];
        var pattern = GenerateGridPattern();

        newLayers[0] = SlicerFile.Layers[0];
        newLayers[0].LayerMat = pattern;

        newLayers[1] = SlicerFile.Layers[1];
        newLayers[1].LayerMat = pattern;

        SlicerFile.SuppressRebuildPropertiesWork(() => {
            SlicerFile.Layers = newLayers;
        });
        return !Progress.Token.IsCancellationRequested;
    }
}
