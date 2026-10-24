using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Ras.Synthetics;

public readonly record struct MeasuredStageProfile(string Name, Polyline Line, double[] Values, double Time)
{
	public const double NoData = double.NaN;

	public static bool IsNoDataValue(double stage)
	{
		return double.IsNaN(stage);
	}

	public bool IsNoData(int vertex)
	{
		return double.IsNaN(Values[vertex]);
	}

	public static MeasuredStageProfile FromFractions(string name, Polyline line, IReadOnlyList<double> fractions, IReadOnlyList<double> stages, double time = -1.0)
	{
		if (fractions.Count != stages.Count)
		{
			throw new ArgumentException("fractions and stages must have equal length.");
		}
		ProfileLineBuilder.Build(line, fractions, out var resolvedLine, out var vertexSampleIndex);
		double[] doubleValues = new double[resolvedLine.PointCount];
		for (int i = 0; i < doubleValues.Length; i++)
		{
			int vertexSampleIndex2 = vertexSampleIndex[i];
			doubleValues[i] = ((vertexSampleIndex2 >= 0) ? stages[vertexSampleIndex2] : double.NaN);
		}
		return new MeasuredStageProfile(name, resolvedLine, doubleValues, time);
	}
}
