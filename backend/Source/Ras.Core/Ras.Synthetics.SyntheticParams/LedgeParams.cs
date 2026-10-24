using System.Collections.Generic;
using Geospatial.Vectors;

namespace Ras.Synthetics.SyntheticParams;

public class LedgeParams : InOutPlanarParams
{
	public double UpperElevation = 5.0;

	public double LowerElevation;

	public double LedgeX = 10.1;

	public new static LedgeParams Default()
	{
		return new LedgeParams
		{
			CellsWide = 10,
			CellsTall = 3,
			CellSize = 2.0,
			NValue = 0.03f,
			UpstreamFlow = 0.01,
			DownstreamStage = 3.0,
			SolveDt = 10.0,
			SolveDuration = 21600.0,
			ReportFrequency = 60
		};
	}

	public override float TerrainElevation(Point pt)
	{
		if (pt.X <= LedgeX)
		{
			return (float)UpperElevation;
		}
		return (float)LowerElevation;
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
