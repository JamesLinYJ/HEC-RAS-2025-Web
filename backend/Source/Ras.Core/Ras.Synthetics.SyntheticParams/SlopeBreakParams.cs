using System.Collections.Generic;
using Geospatial.Vectors;

namespace Ras.Synthetics.SyntheticParams;

public class SlopeBreakParams : InOutPlanarParams
{
	public double UpperElevation = 10.0;

	public double LowerElevation;

	public double RampStartX = 40.0;

	public double RampEndX = 50.0;

	public new static SlopeBreakParams Default()
	{
		return new SlopeBreakParams
		{
			CellsWide = 10,
			CellsTall = 1,
			CellSize = 10.0,
			NValue = 0.03f,
			UpstreamFlow = 1.0,
			DownstreamStage = 1.0,
			SolveDt = 1.0,
			SolveDuration = 7200.0,
			ReportFrequency = 60
		};
	}

	public override float TerrainElevation(Point pt)
	{
		if (pt.X <= RampStartX)
		{
			return (float)UpperElevation;
		}
		if (pt.X >= RampEndX)
		{
			return (float)LowerElevation;
		}
		double submergenceRatio = (pt.X - RampStartX) / (RampEndX - RampStartX);
		return (float)(UpperElevation + submergenceRatio * (LowerElevation - UpperElevation));
	}

	public override IReadOnlyList<NamedResultLine> GetResultLines()
	{
		Extent systemExtent = SystemExtent;
		double y = 0.5 * (systemExtent.MinY + systemExtent.MaxY);
		return new _003C_003Ez__ReadOnlySingleElementList<NamedResultLine>(new NamedResultLine(new Polyline(new Point[2]
		{
			new Point(systemExtent.MinX, y),
			new Point(systemExtent.MaxX, y)
		}), "Centerline"));
	}
}
