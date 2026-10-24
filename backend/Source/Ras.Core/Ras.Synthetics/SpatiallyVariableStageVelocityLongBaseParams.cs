using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Payloads;
using Ras.Synthetics.SyntheticParams;

namespace Ras.Synthetics;

public abstract class SpatiallyVariableStageVelocityLongBaseParams : InOutPlanarParams
{
	public const double Length = 2000.0;

	public const double Width = 100.0;

	public static readonly double[] StationYs = new double[5] { 10.0, 30.0, 50.0, 70.0, 90.0 };

	public static readonly string[] InletNames = new string[5] { "Inlet 1", "Inlet 2", "Inlet 3", "Inlet 4", "Inlet 5" };

	public static readonly string[] OutletNames = new string[5] { "Outlet 1", "Outlet 2", "Outlet 3", "Outlet 4", "Outlet 5" };

	public static readonly string[] MidNames = new string[5] { "Mid 1", "Mid 2", "Mid 3", "Mid 4", "Mid 5" };

	public const int SignalStationIndex = 2;

	public const string SignalStationName = "Outlet 3";

	public const double NorthEdgeY = 60.0;

	private const double RowHalfHeight = 10.0;

	public const double NorthSampleY = 50.0;

	public static readonly double[] NorthStationXs = RegularStackXs(1000.0).ToArray();

	public static readonly string[] NorthNames = new string[5] { "North 1", "North 2", "North 3", "North 4", "North 5" };

	public const double SignalPulseStartTime = 120.0;

	public const double SignalPulseDuration = 60.0;

	private const double StackSpacing = 200.0;

	private Mesh _mesh;

	protected double[] _westStations;

	protected List<Memory<double>> _westValues;

	protected double[] _northStations;

	protected List<Memory<double>> _northValues;

	public double EastFlow { get; set; } = -200.0;

	public double InitialStage { get; set; } = 10.0;

	protected SpatiallyVariableStageVelocityLongBaseParams()
	{
		SolveDt = 1.0;
		SolveDuration = 1200.0;
		NValue = 0.05f;
		Slope = 0.001;
		RampSeconds = 60.0;
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return (Point p) => (float)InitialStage;
	}

	protected Mesh CreateRectangularMesh(double meshStartX, double yMin, double yMax, double[] cellYs = null, double[] stackXs = null)
	{
		if (_mesh != null)
		{
			return _mesh;
		}
		if (cellYs == null)
		{
			cellYs = StationYs;
		}
		int ordinal = 4;
		List<Point> points = new List<Point>(ordinal);
		CollectionsMarshal.SetCount(points, ordinal);
		Span<Point> pointsSpan = CollectionsMarshal.AsSpan(points);
		int ordinal2 = 0;
		pointsSpan[ordinal2] = new Point(meshStartX, yMin);
		ordinal2++;
		pointsSpan[ordinal2] = new Point(2000.0, yMin);
		ordinal2++;
		pointsSpan[ordinal2] = new Point(2000.0, yMax);
		ordinal2++;
		pointsSpan[ordinal2] = new Point(meshStartX, yMax);
		Polygon perimeter = new Polygon(points);
		List<Point> pointsList = new List<Point>();
		foreach (double xComponent in stackXs ?? RegularStackXs(meshStartX))
		{
			double[] cellYsArray = cellYs;
			foreach (double yComponent in cellYsArray)
			{
				if (yComponent >= yMin && yComponent <= yMax)
				{
					pointsList.Add(new Point(xComponent, yComponent));
				}
			}
		}
		MeshGenerationParams meshGenerationParams = MeshGenerationParams.Default();
		meshGenerationParams.CreateVirtualCells = false;
		if (!MeshFactory.TryCreateMesh(perimeter, pointsList, null, out _mesh, out var error, meshGenerationParams))
		{
			throw new Exception("SpatiallyVariableStageVelocityLong mesh generation failed: " + error.GetMeshErrorMessage());
		}
		return _mesh;
	}

	internal static IEnumerable<double> RegularStackXs(double meshStartX)
	{
		for (double x = meshStartX + 100.0; x < 2000.0; x += 200.0)
		{
			yield return x;
		}
	}

	protected static IReadOnlyList<NamedResultPoint> CreateResultPoints(double? inletSampleX, double outletSampleX, double yMin, double yMax, double? midSampleX = null, bool includeNorthEdge = false)
	{
		List<NamedResultPoint> namedResultPoints = new List<NamedResultPoint>();
		if (inletSampleX.HasValue)
		{
			for (int i = 0; i < StationYs.Length; i++)
			{
				if (!(StationYs[i] < yMin) && !(StationYs[i] > yMax))
				{
					namedResultPoints.Add(new NamedResultPoint(new Point(inletSampleX.Value, StationYs[i]), InletNames[i]));
				}
			}
		}
		for (int j = 0; j < StationYs.Length; j++)
		{
			if (!(StationYs[j] < yMin) && !(StationYs[j] > yMax))
			{
				namedResultPoints.Add(new NamedResultPoint(new Point(outletSampleX, StationYs[j]), OutletNames[j]));
			}
		}
		if (midSampleX.HasValue)
		{
			for (int k = 0; k < StationYs.Length; k++)
			{
				if (!(StationYs[k] < yMin) && !(StationYs[k] > yMax))
				{
					namedResultPoints.Add(new NamedResultPoint(new Point(midSampleX.Value, StationYs[k]), MidNames[k]));
				}
			}
		}
		if (includeNorthEdge)
		{
			for (int l = 0; l < NorthStationXs.Length; l++)
			{
				namedResultPoints.Add(new NamedResultPoint(new Point(NorthStationXs[l], 50.0), NorthNames[l]));
			}
		}
		return namedResultPoints;
	}

	public void SetWestStageVelocitySeries(double[] stations, List<Memory<double>> values)
	{
		ValidateStageVelocitySeries(stations, values, "West");
		_westStations = stations;
		_westValues = values;
	}

	public void SetNorthStageVelocitySeries(double[] stations, List<Memory<double>> values)
	{
		ValidateStageVelocitySeries(stations, values, "North");
		_northStations = stations;
		_northValues = values;
	}

	protected BoundaryConditionLine BuildWestBc(BoundaryCondition bc, Point bottom, Point top, List<Memory<double>> valuesForPayload)
	{
		return BuildSvsvBc(bc, bottom, top, _westStations, valuesForPayload, "West");
	}

	protected BoundaryConditionLine BuildSvsvBc(BoundaryCondition bc, Point from, Point to, double[] stations, List<Memory<double>> valuesForPayload, string name)
	{
		if (stations == null || valuesForPayload == null)
		{
			throw new InvalidOperationException(name + " spatially-variable stage/velocity series is required. Call Set" + name + "StageVelocitySeries(...) before building boundary conditions.");
		}
		Polyline pl = new Polyline(new Point[2] { from, to });
		SpatiallyVariableTimeSeriesPayload payload = new SpatiallyVariableTimeSeriesPayload("StageVelocityVelocity", new RegularTimeSeriesDefinition(TimeSpan.FromSeconds(SolveDt)), stations, valuesForPayload);
		BoundaryConditionLine boundaryConditionLine = BoundaryConditionLine.SpatiallyVariableStageVelocity(bc, pl, payload);
		boundaryConditionLine.Name = name;
		return boundaryConditionLine;
	}

	protected List<Memory<double>> RampValues(List<Memory<double>> values, int stationCount)
	{
		List<Memory<double>> doubleSpanList = new List<Memory<double>>(values.Count);
		for (int i = 0; i < values.Count; i++)
		{
			double rampFactor = BaseParams.RampFactor((double)i * SolveDt, RampSeconds);
			double[] doubleValues = values[i].ToArray();
			for (int j = 0; j < stationCount; j++)
			{
				doubleValues[j] = InitialStage + (doubleValues[j] - InitialStage) * rampFactor;
			}
			for (int k = stationCount; k < doubleValues.Length; k++)
			{
				doubleValues[k] *= rampFactor;
			}
			doubleSpanList.Add(doubleValues);
		}
		return doubleSpanList;
	}

	private static void ValidateStageVelocitySeries(double[] stations, List<Memory<double>> values, string edge)
	{
		if (stations == null)
		{
			throw new ArgumentNullException("stations");
		}
		if (values == null)
		{
			throw new ArgumentNullException("values");
		}
		if (stations.Length == 0)
		{
			throw new ArgumentException("At least one " + edge + " station is required.", "stations");
		}
		if (values.Count == 0)
		{
			throw new ArgumentException("At least one " + edge + " timestep row is required.", "values");
		}
		int scaledLength = stations.Length * 3;
		for (int i = 0; i < values.Count; i++)
		{
			if (values[i].Length != scaledLength)
			{
				throw new ArgumentException($"{edge} timestep row {i} has length {values[i].Length}, expected {scaledLength} (stage {stations.Length} + velocity {stations.Length}).", "values");
			}
		}
	}
}
