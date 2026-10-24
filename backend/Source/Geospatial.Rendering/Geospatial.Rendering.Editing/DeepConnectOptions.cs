using Geospatial.Rendering.Editing.Options;

namespace Geospatial.Rendering.Editing;

public static class DeepConnectOptions
{
	public static readonly OptionKey<double> SnapTolerancePixels = OptionKey<double>.Define("DeepConnect.SnapTolerancePixels", "Snap Tolerance (px)", "Maximum screen-pixel distance from the cursor to a target line for the snap to engage.", 12.0);
}
