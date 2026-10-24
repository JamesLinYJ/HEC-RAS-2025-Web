using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;

namespace Ras.Synthetics;

public class SpatiallyVariableStageVelocityLongNorthInsetParams : SpatiallyVariableStageVelocityLongBaseParams
{
	public const double YMin = 0.0;

	public const double YMax = 60.0;

	public static readonly double[] RetainedStationYs = SpatiallyVariableStageVelocityLongBaseParams.StationYs.Where((double y) => y >= 0.0 && y <= 60.0).ToArray();

	public double ScaledEastFlow => EastFlow * (double)RetainedStationYs.Length / (double)SpatiallyVariableStageVelocityLongBaseParams.StationYs.Length;

	public static SpatiallyVariableStageVelocityLongNorthInsetParams Create()
	{
		return new SpatiallyVariableStageVelocityLongNorthInsetParams();
	}

	public override Mesh CreateMesh()
	{
		return CreateRectangularMesh(1000.0, 0.0, 60.0);
	}

	public static double[] WestPayloadStations()
	{
		return RetainedStationYs.Select((double y) => y - 0.0 - 1.0).ToArray();
	}

	public static double[] NorthPayloadStations()
	{
		return SpatiallyVariableStageVelocityLongBaseParams.NorthStationXs.Select((double x) => x - 1000.0 - 1.0).ToArray();
	}

	public override List<BoundaryConditionLine> GetBCLines(BoundaryCondition bc)
	{
		Extent extent = CreateMesh().Extent;
		BoundaryConditionLine svsvBc = BuildSvsvBc(bc, new Point(extent.MinX, 1.0), new Point(extent.MinX, 59.0), _westStations, RampValues(_westValues, _westStations.Length), "West");
		BoundaryConditionLine svsvBc2 = BuildSvsvBc(bc, new Point(extent.MinX + 1.0, extent.MaxY), new Point(extent.MaxX - 1.0, extent.MaxY), _northStations, RampValues(_northValues, _northStations.Length), "North");
		Polyline pl = new Polyline(new Point[2]
		{
			new Point(extent.MaxX, 1.0),
			new Point(extent.MaxX, 59.0)
		});
		BoundaryConditionLine boundaryConditionLine = BoundaryConditionLine.ConstantFlow(bc, pl, ScaledEastFlow);
		boundaryConditionLine.Name = "East";
		return new List<BoundaryConditionLine> { svsvBc, svsvBc2, boundaryConditionLine };
	}

	public override IReadOnlyList<NamedResultPoint> GetResultPoints()
	{
		return SpatiallyVariableStageVelocityLongBaseParams.CreateResultPoints(null, 1950.0, 0.0, 60.0);
	}
}
