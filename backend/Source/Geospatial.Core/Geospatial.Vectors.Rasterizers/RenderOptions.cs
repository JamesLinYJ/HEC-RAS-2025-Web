namespace Geospatial.Vectors.Rasterizers;

public class RenderOptions
{
	public CellRenderMethod CellRenderMode { get; set; }

	public ShallowBehavior ShallowBehaviorMode { get; set; }

	public float MinWSPlotTolerance { get; set; }

	public bool AllowNegativeDepths { get; set; }
}
