using System;
using System.Collections.Generic;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Ras.Layers;
using Ras.Layers.Results;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Optimization;

public sealed class ResultSeriesExtractor : ISimulatedSeriesExtractor
{
	private static readonly ILogger _logger = LogHelper.GetLogger("ResultSeriesExtractor");

	public bool TryExtractSeries(Gauge gauge, Result result, out double[] timesSeconds, out double[] values, ProgressReporter pr = null)
	{
		timesSeconds = null;
		values = null;
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		if (result == null || result.BaseOutputBlock == null)
		{
			return false;
		}
		IReadOnlyList<DateTime> times = result.BaseOutputBlock.Times;
		int count = times.Count;
		if (count == 0)
		{
			return false;
		}
		double[] doubleValues = new double[count];
		DateTime dateTime = times[0];
		for (int i = 0; i < count; i++)
		{
			doubleValues[i] = (times[i] - dateTime).TotalSeconds;
		}
		return gauge.Variable switch
		{
			GaugeVariable.Discharge => TryExtractDischarge(gauge, result, doubleValues, out timesSeconds, out values, pr), 
			GaugeVariable.WaterSurfaceElevation => TryExtractStage(gauge, result, doubleValues, count, out timesSeconds, out values, pr), 
			GaugeVariable.Velocity => TryExtractVelocity(gauge, result, doubleValues, count, out timesSeconds, out values, pr), 
			_ => false, 
		};
	}

	private bool TryExtractDischarge(Gauge gauge, Result result, double[] seconds, out double[] timesSeconds, out double[] values, ProgressReporter pr)
	{
		timesSeconds = null;
		values = null;
		if (!result.ComputeFlowTimeSeries(gauge.CrossSection, out var flowTimeSeries, pr) || flowTimeSeries == null || flowTimeSeries.Length == 0)
		{
			_logger.LogWarning("Discharge extraction failed for gauge {Gauge}.", gauge.Name);
			return false;
		}
		int count = Math.Min(flowTimeSeries.Length, seconds.Length);
		timesSeconds = Slice(seconds, count);
		values = Slice(flowTimeSeries, count);
		return true;
	}

	private bool TryExtractStage(Gauge gauge, Result result, double[] seconds, int profiles, out double[] timesSeconds, out double[] values, ProgressReporter pr)
	{
		timesSeconds = null;
		values = null;
		if (!TryFindCell(result, gauge, out var cell))
		{
			return false;
		}
		PostProcessor postProcessor = result.GetPostProcessor();
		postProcessor.EnsureDefaultDatasets(pr);
		float[] floatValues = new float[profiles];
		if (!postProcessor.GetCellWaterSurfaceTimeSeries(cell, floatValues.AsMemory()))
		{
			_logger.LogWarning("WSE extraction failed for gauge {Gauge} at cell {Cell}.", gauge.Name, cell);
			return false;
		}
		values = new double[profiles];
		for (int i = 0; i < profiles; i++)
		{
			values[i] = floatValues[i];
		}
		timesSeconds = seconds;
		return true;
	}

	private bool TryExtractVelocity(Gauge gauge, Result result, double[] seconds, int profiles, out double[] timesSeconds, out double[] values, ProgressReporter pr)
	{
		timesSeconds = null;
		values = null;
		if (!TryFindCell(result, gauge, out var cell))
		{
			return false;
		}
		PostProcessor postProcessor = result.GetPostProcessor();
		postProcessor.EnsureDefaultDatasets(pr);
		double[] doubleValues = new double[profiles];
		if (!postProcessor.GetCellVelocityMagnitudeTimeSeries(cell, doubleValues.AsMemory()))
		{
			_logger.LogWarning("Velocity extraction failed for gauge {Gauge} at cell {Cell}.", gauge.Name, cell);
			return false;
		}
		values = doubleValues;
		timesSeconds = seconds;
		return true;
	}

	private bool TryFindCell(Result result, Gauge gauge, out int cell)
	{
		cell = -1;
		Mesh mesh = result.Geometry?.Mesh;
		if (mesh == null)
		{
			return false;
		}
		Point[] points = new Point[1] { gauge.Location };
		int[] cells = mesh.FindCells(points.AsMemory());
		cell = cells[0];
		if (cell < 0)
		{
			_logger.LogWarning("Gauge {Gauge} location ({X},{Y}) is outside the mesh.", gauge.Name, gauge.Location.X, gauge.Location.Y);
			return false;
		}
		return true;
	}

	private static double[] Slice(double[] source, int count)
	{
		if (source.Length == count)
		{
			return source;
		}
		double[] doubleValues = new double[count];
		Array.Copy(source, doubleValues, count);
		return doubleValues;
	}
}
