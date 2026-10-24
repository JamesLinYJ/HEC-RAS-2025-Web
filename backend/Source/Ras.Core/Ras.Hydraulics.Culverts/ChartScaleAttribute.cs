using System;

namespace Ras.Hydraulics.Culverts;

public class ChartScaleAttribute : Attribute
{
	public int Chart { get; set; }

	public int Scale { get; set; }

	public ChartScaleAttribute(int chart, int scale)
	{
		Chart = chart;
		Scale = scale;
	}
}
