namespace Ras.Layers;

public class PlottingTolerance
{
	public const float DefaultMinWSPlotTolerance = 0.001f;

	public float MinWSPlotTolerance = 0.001f;

	public bool UseFaceCentroidAdjustment;

	public static PlottingTolerance DefaultTolerances()
	{
		return new PlottingTolerance(0.001f);
	}

	public static PlottingTolerance PedanticTolerances()
	{
		return new PlottingTolerance(0f);
	}

	public PlottingTolerance(float minWs)
	{
	}
}
