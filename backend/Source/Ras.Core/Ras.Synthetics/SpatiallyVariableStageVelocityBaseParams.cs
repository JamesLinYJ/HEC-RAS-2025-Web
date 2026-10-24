using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Ras.Synthetics;

public abstract class SpatiallyVariableStageVelocityBaseParams : InOutPlanarParams
{
	public const double InletLength = 200.0;

	public const double InletWidth = 500.0;

	public const double ConstrictionLength = 100.0;

	public const double CenterLength = 200.0;

	public const double CenterWidth = 100.0;

	public const double ExpansionLength = 100.0;

	public const double OutletLength = 100.0;

	public const double OutletWidth = 500.0;

	private Mesh _mesh;

	public double EastFlow { get; set; } = -1000.0;

	public double InitialStage { get; set; } = 10.0;

	protected SpatiallyVariableStageVelocityBaseParams()
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

	protected Mesh CreateDumbbellMesh(double meshStartX, bool includeLeftInletStack)
	{
		if (_mesh != null)
		{
			return _mesh;
		}
		List<Point> points = new List<Point>();
		double computed = 200.0;
		double computed2 = computed + 100.0;
		double computed3 = computed2 + 200.0;
		double computed4 = computed3 + 100.0;
		double x = computed4 + 100.0;
		double computed5 = 0.0;
		double y = computed5 + 500.0;
		double computed6 = 200.0;
		double y2 = computed6 + 100.0;
		double computed7 = computed5 - 0.0;
		double y3 = computed7 + 500.0;
		AddPerimeterPoint(points, meshStartX, computed5);
		AddPerimeterPoint(points, computed, computed5);
		AddPerimeterPoint(points, computed2, computed6);
		AddPerimeterPoint(points, computed3, computed6);
		AddPerimeterPoint(points, computed4, computed7);
		AddPerimeterPoint(points, x, computed7);
		AddPerimeterPoint(points, x, y3);
		AddPerimeterPoint(points, computed4, y3);
		AddPerimeterPoint(points, computed3, y2);
		AddPerimeterPoint(points, computed2, y2);
		AddPerimeterPoint(points, computed, y);
		AddPerimeterPoint(points, meshStartX, y);
		Polygon perimeter = new Polygon(points);
		List<Point> pointsList = new List<Point>();
		if (includeLeftInletStack)
		{
			AddVerticalStack(pointsList, 99.0);
		}
		double x2 = Math.Max(100.0, meshStartX + 20.0);
		AddVerticalStack(pointsList, x2);
		pointsList.Add(new Point(computed2 - 50.0, 250.0));
		pointsList.Add(new Point((computed2 + computed3) / 2.0, 250.0));
		pointsList.Add(new Point(computed3 + 50.0, 250.0));
		AddVerticalStack(pointsList, computed4 + 50.0);
		MeshGenerationParams meshGenerationParams = MeshGenerationParams.Default();
		meshGenerationParams.CreateVirtualCells = false;
		if (!MeshFactory.TryCreateMesh(perimeter, pointsList, null, out _mesh, out var error, meshGenerationParams))
		{
			throw new Exception("SpatiallyVariableStageVelocity mesh generation failed: " + error.GetMeshErrorMessage());
		}
		return _mesh;
	}

	private static void AddPerimeterPoint(List<Point> perimeter, double x, double y)
	{
		Point point = new Point(x, y);
		int count = perimeter.Count;
		if (count <= 0 || !(perimeter[count - 1] == point))
		{
			perimeter.Add(point);
		}
	}

	protected static void AddVerticalStack(List<Point> cells, double x)
	{
		cells.Add(new Point(x, 50.0));
		cells.Add(new Point(x, 150.0));
		cells.Add(new Point(x, 250.0));
		cells.Add(new Point(x, 350.0));
		cells.Add(new Point(x, 450.0));
	}

	protected static IReadOnlyList<NamedResultPoint> CreateResultPoints(double inletSampleX)
	{
		double x = 700.0 - 50.0 - 0.1;
		return new List<NamedResultPoint>
		{
			new NamedResultPoint(new Point(inletSampleX, 50.0), "Inlet 1"),
			new NamedResultPoint(new Point(inletSampleX, 150.0), "Inlet 2"),
			new NamedResultPoint(new Point(inletSampleX, 250.0), "Inlet 3"),
			new NamedResultPoint(new Point(inletSampleX, 350.0), "Inlet 4"),
			new NamedResultPoint(new Point(inletSampleX, 450.0), "Inlet 5"),
			new NamedResultPoint(new Point(x, 50.0), "Outlet 1"),
			new NamedResultPoint(new Point(x, 150.0), "Outlet 2"),
			new NamedResultPoint(new Point(x, 250.0), "Outlet 3"),
			new NamedResultPoint(new Point(x, 350.0), "Outlet 4"),
			new NamedResultPoint(new Point(x, 450.0), "Outlet 5")
		};
	}
}
