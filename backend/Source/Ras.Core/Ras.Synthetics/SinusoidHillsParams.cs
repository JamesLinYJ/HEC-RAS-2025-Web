using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Ras.Engine;
using Ras.Layers;
using Utility.Units;

namespace Ras.Synthetics;

public class SinusoidHillsParams : BasicRectangleParams
{
	public enum PrecipCase
	{
		UniformConstant,
		GriddedConstant,
		TimeVaryingUniform,
		TimeVaryingNonuniform,
		UnalignedNonuniform
	}

	public readonly record struct PrecipSpec(double[] BandTimesSeconds, Func<int, Point, float> RateMetersPerSecond, bool Gridded);

	public double D0 = 2.0;

	public bool StartDry;

	public double Wavelength = 2500.0;

	public PrecipCase? Precip;

	public double PrecipIntensity = 0.001;

	public const double MetersPerSecondToMmPerHr = 3600000.0;

	public double Ldomain => (double)CellsWide * CellSize;

	public static SinusoidHillsParams Default()
	{
		return new SinusoidHillsParams
		{
			CellsTall = 1,
			CellsWide = 100,
			CellSize = 10.0,
			NValue = 0f,
			Slope = 0.0,
			D0 = 2.0,
			SolveDt = 0.5,
			SolveDuration = 500.0
		};
	}

	public override float TerrainElevation(Point pt)
	{
		return (float)(D0 * Math.Cos(pt.X * 2.0 * Math.PI / Wavelength));
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double t) => (float)(-0.5 * D0);
	}

	public override Func<Point, float> InitialStageFunction()
	{
		if (StartDry)
		{
			return null;
		}
		Func<Point, double, float> fxn = AnalyticalStageFunction();
		return (Point pt) => fxn(pt, 0.0);
	}

	public PrecipSpec GetPrecipSpec(Extent extent)
	{
		if (!Precip.HasValue)
		{
			throw new InvalidOperationException("GetPrecipSpec called with no Precip case set.");
		}
		float rate = (float)PrecipIntensity;
		double midX = extent.Center.X;
		switch (Precip.Value)
		{
		case PrecipCase.UniformConstant:
			return new PrecipSpec(new double[2] { 0.0, SolveDuration }, (int _, Point _) => rate, Gridded: false);
		case PrecipCase.GriddedConstant:
		{
			int count = 1 + (int)(SolveDuration / 300.0);
			double[] doubleValues = new double[count];
			for (int intervalIndex = 0; intervalIndex < count; intervalIndex++)
			{
				doubleValues[intervalIndex] = (double)intervalIndex * 300.0;
			}
			return new PrecipSpec(doubleValues, (int _, Point _) => rate, Gridded: true);
		}
		case PrecipCase.TimeVaryingUniform:
			return new PrecipSpec(new double[4] { 0.0, 100.0, 500.0, 3600.0 }, (int band, Point _) => (band >= 2) ? 0f : rate, Gridded: true);
		case PrecipCase.TimeVaryingNonuniform:
			return new PrecipSpec(new double[5] { 0.0, 100.0, 500.0, 1000.0, 3600.0 }, (int band, Point pt) =>
			{
				if (band >= 2)
				{
					if (band >= 3)
					{
						return 0f;
					}
					if (!(pt.X >= midX))
					{
						return 0f;
					}
					return rate;
				}
				return (!(pt.X < midX)) ? 0f : rate;
			}, Gridded: true);
		case PrecipCase.UnalignedNonuniform:
			return new PrecipSpec(new double[5] { 0.0, 100.0, 498.0, 1003.0, 3600.0 }, (int band, Point pt) =>
			{
				if (band >= 2)
				{
					if (band >= 3)
					{
						return 0f;
					}
					if (!(pt.X >= midX))
					{
						return 0f;
					}
					return rate;
				}
				return (!(pt.X < midX)) ? 0f : rate;
			}, Gridded: true);
		default:
			throw new NotSupportedException($"Unhandled {"PrecipCase"} {Precip.Value}.");
		}
	}

	public override void AddCustomEngineBCs(BoundaryConditionCollection bcs, Mesh mesh)
	{
		if (!Precip.HasValue)
		{
			return;
		}
		PrecipSpec spec = GetPrecipSpec(mesh.Extent);
		if (!spec.Gridded)
		{
			float[] rate = new float[1] { spec.RateMetersPerSecond(0, mesh.Extent.Center) };
			bcs.SetPrecipitation(spec.BandTimesSeconds, (int _) => rate, 1f, JaggedWeights<float>.ConstantField(mesh.CellCount));
			return;
		}
		RasterDefinition rasterDef = new RasterDefinition(mesh.Extent.Extend(20.0), 20.0);
		IMultiBandRaster<float> native = new MultiBandFunctionalResampler<float>(spec.BandTimesSeconds.Length, rasterDef.Extent, 0f, (int band, Point pt) => spec.RateMetersPerSecond(band, pt)).AsBandedRaster(rasterDef);
		bcs.SetPrecipitation(spec.BandTimesSeconds, (int i) => native.GetBand(i), 1f, rasterDef.GetBilinearInterpolationWeights<float>(mesh.CellCenters));
	}

	public override void ConfigurePrecipitation(Ras.Layers.BoundaryCondition bc)
	{
		if (!Precip.HasValue)
		{
			return;
		}
		PrecipSpec spec = GetPrecipSpec(SystemExtent);
		if (!spec.Gridded)
		{
			float constantValue = (float)((double)spec.RateMetersPerSecond(0, SystemExtent.Center) * 3600000.0);
			bc.Precipitation.SetConstantValue(constantValue);
			return;
		}
		RasterDefinition raster = new RasterDefinition(SystemExtent.Extend(20.0), 20.0);
		MultiBandFunctionalResampler<float> resampler = new MultiBandFunctionalResampler<float>(spec.BandTimesSeconds.Length, raster.Extent, 0f, (int band, Point pt) => (float)((double)spec.RateMetersPerSecond(band, pt) * 3600000.0));
		DateTime t0 = new DateTime(2024, 4, 18);
		List<DateTime> timesteps = spec.BandTimesSeconds.Select((double s) => t0 + TimeSpan.FromSeconds(s)).ToList();
		ITimeSeriesRaster<float> timed = resampler.AsTimeSeriesRaster(timesteps, raster);
		bc.Precipitation.ImportRaster(timed, null, "per-avg", PeriodAveragePrecipitation.MillimeterPerHour.Name);
	}
}
