using System;
using System.Collections.Generic;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;

namespace Ras.Synthetics;

public class SpatiallyVariableStageVelocityLongInterpolationManyStationsParams : SpatiallyVariableStageVelocityLongBaseParams
{
	public const double WestPolylineInset = 1.0;

	public static readonly double[] Stations = BuildDenseStations();

	public static readonly double[] StageValues = BuildTriangularProfile(9.0, 12.0);

	public static readonly double[] VelocityXValues = BuildTriangularProfile(-0.5, -2.5);

	public static readonly double[] CellYs = new double[4] { 5.0, 35.0, 65.0, 95.0 };

	public static SpatiallyVariableStageVelocityLongInterpolationManyStationsParams Create()
	{
		return new SpatiallyVariableStageVelocityLongInterpolationManyStationsParams();
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

	private static double[] BuildDenseStations()
	{
		List<double> doubleList = new List<double>();
		for (double computed = 5.0; computed <= 95.0; computed += 3.6)
		{
			doubleList.Add(computed);
		}
		return doubleList.ToArray();
	}

	private static double[] BuildTriangularProfile(double baseline, double peak)
	{
		int stationLength = Stations.Length;
		double stationLengthTerm = (double)(stationLength - 1) / 2.0;
		double[] triangularProfile = new double[stationLength];
		for (int i = 0; i < stationLength; i++)
		{
			triangularProfile[i] = baseline + (peak - baseline) * (1.0 - Math.Abs((double)i - stationLengthTerm) / stationLengthTerm);
		}
		return triangularProfile;
	}
}
