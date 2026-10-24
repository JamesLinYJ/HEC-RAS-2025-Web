using System;
using System.Collections.Generic;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;

namespace Ras.Synthetics;

public class SpatiallyVariableStageVelocityLongInterpolationEqualCountParams : SpatiallyVariableStageVelocityLongBaseParams
{
	public const double WestPolylineInset = 1.0;

	public static readonly double[] Stations = new double[5] { 10.0, 32.0, 50.0, 68.0, 90.0 };

	public static readonly double[] StageValues = new double[5] { 11.0, 9.0, 11.5, 9.5, 12.0 };

	public static readonly double[] VelocityXValues = new double[5] { -2.0, -0.5, -2.5, -1.0, -3.0 };

	public static readonly double[] CellYs = new double[5] { 10.0, 30.0, 50.0, 70.0, 90.0 };

	public static SpatiallyVariableStageVelocityLongInterpolationEqualCountParams Create()
	{
		return new SpatiallyVariableStageVelocityLongInterpolationEqualCountParams();
	}

	public override Mesh CreateMesh()
	{
		return CreateRectangularMesh(0.0, 0.0, 100.0, CellYs, new double[1] { 1000.0 });
	}

	public override List<BoundaryConditionLine> GetBCLines(BoundaryCondition bc)
	{
		Extent extent = CreateMesh().Extent;
		double[] doubleValues = new double[Stations.Length * 3];
		for (int i = 0; i < Stations.Length; i++)
		{
			doubleValues[i] = StageValues[i];
			doubleValues[Stations.Length + i] = VelocityXValues[i];
			doubleValues[2 * Stations.Length + i] = 0.0;
		}
		double[] doubleValuesArray = new double[Stations.Length];
		for (int j = 0; j < Stations.Length; j++)
		{
			doubleValuesArray[j] = Stations[j] - 1.0;
		}
		SetWestStageVelocitySeries(doubleValuesArray, new List<Memory<double>> { doubleValues });
		BoundaryConditionLine westBc = BuildWestBc(bc, new Point(extent.MinX, 1.0), new Point(extent.MinX, 99.0), _westValues);
		Polyline pl = new Polyline(new Point[2]
		{
			new Point(extent.MaxX, 1.0),
			new Point(extent.MaxX, 99.0)
		});
		BoundaryConditionLine boundaryConditionLine = BoundaryConditionLine.ConstantFlow(bc, pl, EastFlow);
		boundaryConditionLine.Name = "East";
		return new List<BoundaryConditionLine> { westBc, boundaryConditionLine };
	}

	public override IReadOnlyList<NamedResultPoint> GetResultPoints()
	{
		return Array.Empty<NamedResultPoint>();
	}
}
