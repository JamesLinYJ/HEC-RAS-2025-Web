using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using Geospatial.Transformers;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Geospatial.Vectors.Topology;
using Ras.Engine;
using Ras.Engine.Structures;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Utility.Units;

namespace Ras.Synthetics.SyntheticParams;

public class BaseParams : ISyntheticTestCaseParams
{
	public virtual UnitSystem Units => UnitSystem.SI;

	public virtual double SolveDt { get; set; }

	public virtual double SolveDuration { get; set; }

	public virtual int ReportFrequency { get; set; } = 1;

	public virtual float NValue { get; set; }

	public virtual List<string> Tags { get; }

	public virtual Extent SystemExtent { get; }

	public virtual IResample<float> NValueResampler()
	{
		return null;
	}

	public virtual float SpatiallyVaryingNValue(Point pt)
	{
		return NValue;
	}

	public virtual Projection GetProjection()
	{
		return null;
	}

	public virtual IResample<float> TerrainResampler()
	{
		return null;
	}

	public virtual float TerrainElevation(Point pt)
	{
		return 0f;
	}

	public virtual Func<Point, double, float> AnalyticalStageFunction()
	{
		return null;
	}

	public static double RampFactor(double time, double rampSeconds)
	{
		if (rampSeconds <= 0.0)
		{
			return 1.0;
		}
		double timeOverRampSeconds = Math.Min(Math.Max(time / rampSeconds, 0.0), 1.0);
		return 0.5 * (1.0 - Math.Cos(Math.PI * timeOverRampSeconds));
	}

	public static double[] RampTimeSteps(double rampSeconds, int rampSamples = 20)
	{
		double[] doubleValues = new double[rampSamples + 3];
		doubleValues[0] = -1E+32;
		for (int i = 0; i <= rampSamples; i++)
		{
			doubleValues[i + 1] = rampSeconds * (double)i / (double)rampSamples;
		}
		doubleValues[^1] = 1E+32;
		return doubleValues;
	}

	public virtual Func<Point, float> InitialStageFunction()
	{
		return null;
	}

	public virtual Func<Point, float> InitialFaceStageFunction()
	{
		return null;
	}

	public virtual Func<Point, double, Vector> AnalyticalVelocityFunction()
	{
		return null;
	}

	public virtual Func<double, double, double> AnalyticalRatingCurveFunction()
	{
		return null;
	}

	public virtual IReadOnlyList<MeasuredVelocityProfile> GetMeasuredVelocityProfiles()
	{
		return Array.Empty<MeasuredVelocityProfile>();
	}

	public virtual IReadOnlyList<MeasuredStageProfile> GetMeasuredStageProfiles()
	{
		return Array.Empty<MeasuredStageProfile>();
	}

	public virtual IReadOnlyList<NamedResultPoint> GetResultPoints()
	{
		return Array.Empty<NamedResultPoint>();
	}

	public virtual IReadOnlyList<NamedResultLine> GetResultLines()
	{
		return Array.Empty<NamedResultLine>();
	}

	public virtual IReadOnlyList<MeasuredStageSample> GetMeasuredStageSamples()
	{
		List<MeasuredStageSample> measuredStageSamples = new List<MeasuredStageSample>();
		measuredStageSamples.AddRange(GetMeasuredStageProfiles().SelectMany((MeasuredStageProfile p) => from i in Enumerable.Range(0, p.Line.PointCount)
			where !p.IsNoData(i)
			select new MeasuredStageSample(p.Line[i], p.Time, p.Values[i])));
		return new _003C_003Ez__ReadOnlyList<MeasuredStageSample>(measuredStageSamples);
	}

	public virtual IReadOnlyList<MeasuredVelocitySample> GetMeasuredVelocitySamples()
	{
		List<MeasuredVelocitySample> measuredVelocitySamples = new List<MeasuredVelocitySample>();
		measuredVelocitySamples.AddRange(GetMeasuredVelocityProfiles().SelectMany((MeasuredVelocityProfile p) => from i in Enumerable.Range(0, p.Line.PointCount)
			where !p.IsNoData(i)
			select new MeasuredVelocitySample(p.Line[i], p.Time, p.Values[i])));
		return new _003C_003Ez__ReadOnlyList<MeasuredVelocitySample>(measuredVelocitySamples);
	}

	public virtual Func<Point, Vector> InitialVelocityFunction()
	{
		return null;
	}

	public virtual Func<Point, double, float> AnalyticalVelocityMagnitudeFunction()
	{
		return null;
	}

	public virtual Func<Point, float> InitialVelocityMagnitudeFunction()
	{
		return null;
	}

	public virtual Func<Point, double, float> AnalyticalVelocityXFunction()
	{
		return null;
	}

	public virtual Func<Point, float> InitialVelocityXFunction()
	{
		return null;
	}

	public virtual Func<Point, double, float> AnalyticalVelocityYFunction()
	{
		return null;
	}

	public virtual Func<Point, float> InitialVelocityYFunction()
	{
		return null;
	}

	public virtual Mesh CreateMesh()
	{
		return null;
	}

	public virtual bool TryCreateTopology(out Topology topology)
	{
		topology = null;
		return false;
	}

	public virtual ISpatialTransformer GetTransformer(Mesh mesh)
	{
		return null;
	}

	public virtual int[] GetCellMapping(Mesh mesh)
	{
		return null;
	}

	public virtual List<BoundaryConditionLine> GetBCLines(Ras.Layers.BoundaryCondition bc)
	{
		return null;
	}

	public virtual void InitPlanOptions(Plan pl)
	{
		pl.UseDiffusion = false;
	}

	public virtual void InitSolverControl(SolverControl solverControl)
	{
	}

	public virtual void AddStructures(HydraulicStructureCollection structures, Mesh mesh)
	{
	}

	public virtual void ConfigureEngineBC(Ras.Engine.BoundaryCondition engineBc, BoundaryConditionLine srcLine, string variable)
	{
	}

	public virtual void AddCustomEngineBCs(BoundaryConditionCollection bcs, Mesh mesh)
	{
	}

	public virtual void ConfigurePrecipitation(Ras.Layers.BoundaryCondition bc)
	{
	}

	public virtual void UpdateInitialState(MeshWaterState state, Mesh mesh, MeshPropertyTables props)
	{
		Func<Point, float> initStage = InitialStageFunction();
		if (initStage == null)
		{
			state.SetDry();
			return;
		}
		Extent ext = mesh.Extent.Extend(0.1);
		IResample<float> initialStageFromCellCenters = new FunctionalResampler<float>(ext, -9999f, (Point pt) => initStage(pt));
		state.SetInitialStageFromCellCenters(initialStageFromCellCenters);
		Func<Point, float> initFaceStage = InitialFaceStageFunction();
		if (initFaceStage != null)
		{
			IResample<float> initialFaceStageFromFaceCenters = new FunctionalResampler<float>(ext, -9999f, (Point pt) => initFaceStage(pt));
			state.SetInitialFaceStageFromFaceCenters(initialFaceStageFromFaceCenters);
		}
		Func<Point, float> initVelX = InitialVelocityXFunction();
		Func<Point, float> initVelY = InitialVelocityYFunction();
		if (initVelX != null && initVelY != null)
		{
			IResample<float> velocityXSurface = new FunctionalResampler<float>(ext, -9999f, (Point pt) => initVelX(pt));
			IResample<float> velocityYSurface = new FunctionalResampler<float>(ext, -9999f, (Point pt) => initVelY(pt));
			state.SetInitialVelocityFromFaceCenters(velocityXSurface, velocityYSurface);
		}
	}
}
