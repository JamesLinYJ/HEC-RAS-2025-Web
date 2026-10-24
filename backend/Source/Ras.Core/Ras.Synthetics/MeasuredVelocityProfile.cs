using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Ras.Synthetics;

public readonly record struct MeasuredVelocityProfile(string Name, Polyline Line, Vector[] Values, double Time)
{
	public static readonly Vector NoData = new Vector(double.NaN, double.NaN);

	public static bool IsNoDataValue(Vector v)
	{
		if (!double.IsNaN(v.DeltaX))
		{
			return double.IsNaN(v.DeltaY);
		}
		return true;
	}

	public bool IsNoData(int vertex)
	{
		return IsNoDataValue(Values[vertex]);
	}

	public static MeasuredVelocityProfile FromFractions(string name, Polyline line, IReadOnlyList<double> fractions, IReadOnlyList<Vector> velocities, double time = -1.0)
	{
		if (fractions.Count != velocities.Count)
		{
			throw new ArgumentException("fractions and velocities must have equal length.");
		}
		ProfileLineBuilder.Build(line, fractions, out var resolvedLine, out var vertexSampleIndex);
		Vector[] vectors = new Vector[resolvedLine.PointCount];
		for (int i = 0; i < vectors.Length; i++)
		{
			int vertexSampleIndex2 = vertexSampleIndex[i];
			vectors[i] = ((vertexSampleIndex2 >= 0) ? velocities[vertexSampleIndex2] : NoData);
		}
		return new MeasuredVelocityProfile(name, resolvedLine, vectors, time);
	}
}
