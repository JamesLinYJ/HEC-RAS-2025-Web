namespace Geospatial.Rasters;

public readonly struct ContourLayerStats
{
	public readonly double ReferenceSlope;

	public readonly double ValueMin;

	public readonly double ValueMax;

	public readonly double[] ScaleCellSizes;

	public readonly double[] ScaleSlopes;

	public static readonly ContourLayerStats None = new ContourLayerStats(double.NaN, double.NaN, double.NaN);

	public bool HasRange
	{
		get
		{
			if (!double.IsNaN(ValueMin) && !double.IsNaN(ValueMax))
			{
				return ValueMax > ValueMin;
			}
			return false;
		}
	}

	public ContourLayerStats(double referenceSlope, double valueMin, double valueMax, double[] scaleCellSizes = null, double[] scaleSlopes = null)
	{
		ReferenceSlope = referenceSlope;
		ValueMin = valueMin;
		ValueMax = valueMax;
		ScaleCellSizes = scaleCellSizes;
		ScaleSlopes = scaleSlopes;
	}
}
