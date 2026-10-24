using System.Collections.Generic;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;

namespace Ras.Synthetics;

public class SpatiallyVariableStageVelocityCoarseParams : SpatiallyVariableStageVelocityBaseParams
{
	public double WestStage { get; set; }

	public static SpatiallyVariableStageVelocityCoarseParams Create()
	{
		return new SpatiallyVariableStageVelocityCoarseParams
		{
			WestStage = 10.0
		};
	}

	public override Mesh CreateMesh()
	{
		return CreateDumbbellMesh(0.0, includeLeftInletStack: true);
	}

	public override List<BoundaryConditionLine> GetBCLines(BoundaryCondition bc)
	{
		Extent extent = CreateMesh().Extent;
		Polyline pl = new Polyline(new Point[2]
		{
			new Point(extent.MinX, 1.0),
			new Point(extent.MinX, 499.0)
		});
		BoundaryConditionLine boundaryConditionLine = BoundaryConditionLine.ConstantStage(bc, pl, WestStage);
		boundaryConditionLine.Name = "West";
		Polyline pl2 = new Polyline(new Point[2]
		{
			new Point(extent.MaxX, 1.0),
			new Point(extent.MaxX, 499.0)
		});
		BoundaryConditionLine boundaryConditionLine2 = BoundaryConditionLine.ConstantFlow(bc, pl2, EastFlow);
		boundaryConditionLine2.Name = "East";
		return new List<BoundaryConditionLine> { boundaryConditionLine, boundaryConditionLine2 };
	}

	public override IReadOnlyList<NamedResultPoint> GetResultPoints()
	{
		return SpatiallyVariableStageVelocityBaseParams.CreateResultPoints(100.1);
	}
}
