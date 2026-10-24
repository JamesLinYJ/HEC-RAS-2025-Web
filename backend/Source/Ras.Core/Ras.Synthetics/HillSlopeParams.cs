using System;
using Geospatial;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Ras.Engine;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Payloads;
using Utility.Units;

namespace Ras.Synthetics;

public class HillSlopeParams : BasicRectangleParams
{
	public enum DownstreamBC
	{
		NormalDepth,
		Stage
	}

	public UnitSystem BuildUnits = UnitSystem.SI;

	public double RainIntensity = 3.0000024E-06;

	public double RainDuration = 5400.0;

	public const double m = 1.6666666666666667;

	public DownstreamBC DownstreamBCType;

	public double StageBCIntervalSeconds = 10.0;

	public override UnitSystem Units => BuildUnits;

	private double ManningKn
	{
		get
		{
			if (Units != UnitSystem.USCustomary)
			{
				return 1.0;
			}
			return 1.486;
		}
	}

	public static HillSlopeParams Default()
	{
		return new HillSlopeParams
		{
			CellSize = 20.0,
			CellsTall = 50,
			CellsWide = 40,
			NValue = 0.015f,
			Slope = 0.05,
			RainIntensity = 3.0000024E-06,
			RainDuration = 5400.0,
			SolveDt = 1.0,
			SolveDuration = 10800.0
		};
	}

	public static HillSlopeParams DefaultUS()
	{
		HillSlopeParams hillSlopeParams = Default();
		hillSlopeParams.BuildUnits = UnitSystem.USCustomary;
		hillSlopeParams.CellSize *= 3.28084;
		hillSlopeParams.RainIntensity *= 3.28084;
		return hillSlopeParams;
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double time) =>
		{
			double terrainElevation = TerrainElevation(pt);
			double unitDischarge = ComputeUnitDischarge(pt, time);
			if (unitDischarge <= 0.0)
			{
				return (float)terrainElevation;
			}
			double manningKnSlopeTerm = ManningKn * Math.Sqrt(Slope) / (double)NValue;
			double unitDischargeTerm = Math.Pow(unitDischarge / manningKnSlopeTerm, 0.6);
			return (float)(terrainElevation + unitDischargeTerm);
		};
	}

	public override Func<Point, double, Vector> AnalyticalVelocityFunction()
	{
		return (Point pt, double time) =>
		{
			double unitDischarge = ComputeUnitDischarge(pt, time);
			if (unitDischarge <= 0.0)
			{
				return new Vector(0.0, 0.0);
			}
			double manningKnSlopeTerm = ManningKn * Math.Sqrt(Slope) / (double)NValue;
			double unitDischargeTerm = Math.Pow(unitDischarge / manningKnSlopeTerm, 0.6);
			return new Vector(unitDischarge / unitDischargeTerm, 0.0);
		};
	}

	public override BoundaryConditionLine GetDownstreamBC(Ras.Layers.BoundaryCondition bc, Polyline pline)
	{
		if (DownstreamBCType == DownstreamBC.Stage)
		{
			return GetDownstreamStageBC(bc, pline);
		}
		BoundaryConditionLine boundaryConditionLine = BoundaryConditionLine.NormalDepth(bc, pline, Slope);
		boundaryConditionLine.Name = "Downstream";
		return boundaryConditionLine;
	}

	private BoundaryConditionLine GetDownstreamStageBC(Ras.Layers.BoundaryCondition bc, Polyline pline)
	{
		if (SolveDuration <= 0.0)
		{
			throw new InvalidOperationException($"{"SolveDuration"} must be > 0 to build a downstream stage time series (got {SolveDuration}).");
		}
		double stageBCIntervalSeconds = StageBCIntervalSeconds;
		Func<Point, double, float> func = AnalyticalStageFunction();
		Point point = new Point(SystemExtent.MaxX, SystemExtent.Center.Y);
		int count = (int)Math.Ceiling(SolveDuration / stageBCIntervalSeconds);
		double[] doubleValues = new double[count + 1];
		for (int i = 0; i <= count; i++)
		{
			doubleValues[i] = func(point, (double)i * stageBCIntervalSeconds);
		}
		return new BoundaryConditionLine(bc, pline)
		{
			Name = "Downstream",
			Payloads = { (PayloadBase)new TimeSeriesPayload("Stage", new RegularTimeSeriesDefinition(TimeSpan.FromSeconds(stageBCIntervalSeconds)), doubleValues) }
		};
	}

	public override void AddCustomEngineBCs(BoundaryConditionCollection bcs, Mesh mesh)
	{
		double solveDurationRainDurationTerm = Math.Max(SolveDuration, RainDuration) + 1.0;
		double[] precipTimesteps = new double[4]
		{
			0.0,
			RainDuration,
			RainDuration + 0.001,
			solveDurationRainDurationTerm
		};
		float[] rates = new float[4]
		{
			(float)RainIntensity,
			(float)RainIntensity,
			0f,
			0f
		};
		bcs.SetPrecipitation(precipTimesteps, GetPrecip, 1f, JaggedWeights<float>.Identity(mesh.CellCount));
		float[] GetPrecip(int band)
		{
			float[] precip = new float[mesh.CellCount];
			Array.Fill(precip, rates[band]);
			return precip;
		}
	}

	public override void ConfigurePrecipitation(Ras.Layers.BoundaryCondition bc)
	{
		double unitsUsCustomaryTerm = ((Units == UnitSystem.USCustomary) ? 12.0 : 1000.0) * 3600.0;
		bc.Precipitation.SetConstantValue((float)(RainIntensity * unitsUsCustomaryTerm));
	}

	private double ComputeUnitDischarge(Point pt, double time)
	{
		double timeOfEquilibrium = TimeOfEquilibrium(pt.X);
		double manningKnSlopeTerm = ManningKn * Math.Sqrt(Slope) / (double)NValue;
		double maxX = SystemExtent.MaxX;
		if (time < 0.0)
		{
			return 0.0;
		}
		if (time <= timeOfEquilibrium)
		{
			return (float)(manningKnSlopeTerm * Math.Pow(RainIntensity * time, 1.6666666666666667));
		}
		if (time < RainDuration)
		{
			return (float)(RainIntensity * pt.X);
		}
		double computed = 0.0;
		double computed2 = 0.0;
		double rainIntensity = RainIntensity;
		double rainDuration = RainDuration;
		double x = ManningKn * Math.Sqrt(Slope) / ((double)NValue * maxX);
		double rainIntensityTimeTerm = (0.0 - computed2 + rainIntensity - 1.6666666666666667 * Math.Pow(x, 0.6) * rainIntensity * Math.Pow(computed2, 0.4) * (time - rainDuration)) / rainIntensity;
		do
		{
			computed2 += 0.01;
			rainIntensityTimeTerm = (0.0 - computed2 + rainIntensity - 1.6666666666666667 * Math.Pow(x, 0.6) * rainIntensity * Math.Pow(computed2, 0.4) * (time - rainDuration)) / rainIntensity;
		}
		while (!(rainIntensityTimeTerm < 0.0));
		double computed3 = computed;
		double computed4 = computed2;
		while (true)
		{
			computed2 = 0.5 * (computed4 + computed3);
			rainIntensityTimeTerm = (0.0 - computed2 + rainIntensity - 1.6666666666666667 * Math.Pow(x, 0.6) * rainIntensity * Math.Pow(computed2, 0.4) * (time - rainDuration)) / rainIntensity;
			if (Math.Abs(rainIntensityTimeTerm) < 1E-10)
			{
				break;
			}
			if (rainIntensityTimeTerm < 0.0)
			{
				computed4 = computed2;
			}
			else
			{
				computed3 = computed2;
			}
		}
		return computed2 * pt.X;
	}

	private double TimeOfEquilibrium(double x)
	{
		double manningKnSlopeTerm = ManningKn * Math.Sqrt(Slope) / (double)NValue;
		return Math.Pow(RainIntensity * x / manningKnSlopeTerm, 0.6) / RainIntensity;
	}
}
