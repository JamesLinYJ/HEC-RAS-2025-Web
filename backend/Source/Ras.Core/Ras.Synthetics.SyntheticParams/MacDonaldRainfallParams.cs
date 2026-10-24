using System;
using Geospatial;
using Geospatial.Vectors;
using Ras.Engine;
using Ras.Layers;
using Utility.Units;

namespace Ras.Synthetics.SyntheticParams;

public class MacDonaldRainfallParams : InOutPlanarParams
{
	private const double Gravity = 9.81;

	private const double SampleDx = 0.02;

	public double UpstreamUnitDischarge = 1.0;

	public double RainIntensity = 0.001;

	public double DepthBase = 0.8;

	public double DepthAmplitude = 0.1;

	private readonly object _precomputeLock = new object();

	private double[] _bed;

	private double[] _stage;

	public override UnitSystem Units => UnitSystem.SI;

	public new static MacDonaldRainfallParams Default()
	{
		MacDonaldRainfallParams macDonaldRainfallParams = new MacDonaldRainfallParams
		{
			CellsWide = 200,
			CellsTall = 4,
			CellSize = 0.5,
			NValue = 0.03f,
			RampSeconds = 0.0,
			SolveDt = 0.1,
			SolveDuration = 600.0
		};
		macDonaldRainfallParams.UpstreamFlow = macDonaldRainfallParams.UpstreamUnitDischarge * macDonaldRainfallParams.SystemExtent.Height;
		macDonaldRainfallParams.DownstreamStage = macDonaldRainfallParams.Stage(macDonaldRainfallParams.SystemExtent.MaxX);
		macDonaldRainfallParams.UpstreamEGSlope = macDonaldRainfallParams.FrictionSlope(0.0);
		return macDonaldRainfallParams;
	}

	private double UnitDischarge(double x)
	{
		return UpstreamUnitDischarge + RainIntensity * x;
	}

	private double Depth(double x)
	{
		return DepthBase + DepthAmplitude * Math.Sin(Math.PI * x / SystemExtent.Width);
	}

	private double DepthDerivative(double x)
	{
		return DepthAmplitude * Math.PI / SystemExtent.Width * Math.Cos(Math.PI * x / SystemExtent.Width);
	}

	private double FrictionSlope(double x)
	{
		double unitDischarge = UnitDischarge(x);
		double x2 = Depth(x);
		return (double)(NValue * NValue) * unitDischarge * unitDischarge / Math.Pow(x2, 3.3333333333333335);
	}

	private double BedDerivative(double x)
	{
		double unitDischarge = UnitDischarge(x);
		double depth = Depth(x);
		double depthDerivative = DepthDerivative(x);
		double unitDischargeOverDepth = unitDischarge / depth;
		double rainIntensityDepthTerm = RainIntensity / depth - unitDischarge * depthDerivative / (depth * depth);
		return 0.0 - depthDerivative - unitDischargeOverDepth * rainIntensityDepthTerm / 9.81 - FrictionSlope(x);
	}

	public override float TerrainElevation(Point pt)
	{
		EnsurePrecomputed();
		return (float)Interpolate(pt.X, _bed);
	}

	public double Stage(double x)
	{
		EnsurePrecomputed();
		return Interpolate(x, _stage);
	}

	private double Velocity(double x)
	{
		return UnitDischarge(x) / Depth(x);
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double _) => (float)Stage(pt.X);
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return (Point pt) => (float)Stage(pt.X);
	}

	public override Func<Point, double, Vector> AnalyticalVelocityFunction()
	{
		return (Point pt, double _) => new Vector(Velocity(pt.X), 0.0);
	}

	public override Func<Point, double, float> AnalyticalVelocityMagnitudeFunction()
	{
		return (Point pt, double _) => (float)Math.Abs(Velocity(pt.X));
	}

	public override Func<Point, double, float> AnalyticalVelocityXFunction()
	{
		return (Point pt, double _) => (float)Velocity(pt.X);
	}

	public override Func<Point, double, float> AnalyticalVelocityYFunction()
	{
		return (Point _, double _) => 0f;
	}

	public override Func<Point, float> InitialVelocityXFunction()
	{
		return (Point pt) => (float)Velocity(pt.X);
	}

	public override Func<Point, float> InitialVelocityYFunction()
	{
		return (Point _) => 0f;
	}

	public override void AddCustomEngineBCs(BoundaryConditionCollection boundaryConditions, Mesh mesh)
	{
		double[] precipTimesteps = new double[2]
		{
			0.0,
			SolveDuration + 1.0
		};
		boundaryConditions.SetPrecipitation(precipTimesteps, GetPrecipitation, 1f, JaggedWeights<float>.Identity(mesh.CellCount));
		float[] GetPrecipitation(int _)
		{
			float[] precipitation = new float[mesh.CellCount];
			Array.Fill(precipitation, (float)RainIntensity);
			return precipitation;
		}
	}

	public override void ConfigurePrecipitation(Ras.Layers.BoundaryCondition boundaryCondition)
	{
		boundaryCondition.Precipitation.SetConstantValue((float)(RainIntensity * 1000.0 * 3600.0));
	}

	private void EnsurePrecomputed()
	{
		if (_bed != null)
		{
			return;
		}
		lock (_precomputeLock)
		{
			if (_bed == null)
			{
				int count = (int)Math.Ceiling(SystemExtent.Width / 0.02) + 1;
				double[] bed = new double[count];
				double[] stage = new double[count];
				for (int i = 1; i < count; i++)
				{
					double computed = (double)(i - 1) * 0.02;
					double widthSystemExtentTerm = Math.Min((double)i * 0.02, SystemExtent.Width);
					double computed2 = widthSystemExtentTerm - computed;
					double bedDerivative = BedDerivative(computed);
					double bedDerivative2 = BedDerivative(computed + 0.5 * computed2);
					double bedDerivative3 = BedDerivative(computed + 0.5 * computed2);
					double bedDerivative4 = BedDerivative(widthSystemExtentTerm);
					bed[i] = bed[i - 1] + computed2 * (bedDerivative + 2.0 * bedDerivative2 + 2.0 * bedDerivative3 + bedDerivative4) / 6.0;
				}
				for (int j = 0; j < count; j++)
				{
					double x = Math.Min((double)j * 0.02, SystemExtent.Width);
					stage[j] = bed[j] + Depth(x);
				}
				_stage = stage;
				_bed = bed;
			}
		}
	}

	private double Interpolate(double x, double[] values)
	{
		x = Math.Clamp(x, 0.0, SystemExtent.Width);
		double computed = x / 0.02;
		int ordinal = Math.Min((int)computed, values.Length - 2);
		double computed2 = computed - (double)ordinal;
		return values[ordinal] + computed2 * (values[ordinal + 1] - values[ordinal]);
	}
}
