using System;
using System.Collections.Generic;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Geospatial.Vectors.Topology;
using Ras.Engine;
using Ras.Engine.Structures;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Utility.Units;

namespace Ras.Synthetics;

public interface ISyntheticTestCaseParams
{
	UnitSystem Units { get; }

	double SolveDt { get; }

	double SolveDuration { get; }

	int ReportFrequency { get; }

	Extent SystemExtent { get; }

	float NValue { get; }

	List<string> Tags { get; }

	Projection GetProjection()
	{
		return null;
	}

	IResample<float> NValueResampler();

	float SpatiallyVaryingNValue(Point pt);

	IResample<float> TerrainResampler();

	float TerrainElevation(Point pt);

	Func<Point, double, float> AnalyticalStageFunction();

	Func<Point, float> InitialStageFunction();

	Func<Point, double, Vector> AnalyticalVelocityFunction();

	Func<double, double, double> AnalyticalRatingCurveFunction();

	IReadOnlyList<MeasuredStageSample> GetMeasuredStageSamples();

	IReadOnlyList<MeasuredVelocitySample> GetMeasuredVelocitySamples();

	IReadOnlyList<MeasuredVelocityProfile> GetMeasuredVelocityProfiles()
	{
		return Array.Empty<MeasuredVelocityProfile>();
	}

	IReadOnlyList<MeasuredStageProfile> GetMeasuredStageProfiles()
	{
		return Array.Empty<MeasuredStageProfile>();
	}

	IReadOnlyList<NamedResultPoint> GetResultPoints()
	{
		return Array.Empty<NamedResultPoint>();
	}

	IReadOnlyList<NamedResultLine> GetResultLines()
	{
		return Array.Empty<NamedResultLine>();
	}

	Func<Point, Vector> InitialVelocityFunction();

	Func<Point, double, float> AnalyticalVelocityMagnitudeFunction();

	Func<Point, float> InitialVelocityMagnitudeFunction();

	Func<Point, double, float> AnalyticalVelocityXFunction();

	Func<Point, float> InitialVelocityXFunction();

	Func<Point, double, float> AnalyticalVelocityYFunction();

	Func<Point, float> InitialVelocityYFunction();

	Mesh CreateMesh();

	bool TryCreateTopology(out Topology topology)
	{
		topology = null;
		return false;
	}

	List<BoundaryConditionLine> GetBCLines(Ras.Layers.BoundaryCondition bc);

	void ConfigurePrecipitation(Ras.Layers.BoundaryCondition bc)
	{
	}

	void InitPlanOptions(Plan pl);

	void InitSolverControl(SolverControl solverControl);

	void AddStructures(HydraulicStructureCollection structures, Mesh mesh);

	void UpdateInitialState(MeshWaterState state, Mesh mesh, MeshPropertyTables props);
}
