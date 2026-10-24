using System;
using Geospatial.Vectors.Meshes;

namespace Geospatial.Hydraulics.Analysis;

public static class MeshPropertyFaceCurveEvaluator
{
	private const double ConveyanceReconstructionExponent = 1.6666666666666667;

	public static double EvaluateArea(MeshPropertyTables tables, int faceIndex, double elevation)
	{
		ValidateFace(tables, faceIndex);
		GetTableRange(tables, faceIndex, out var start, out var end);
		if (elevation <= (double)tables.FaceElevationTable[start])
		{
			return tables.FaceAreaTable[start];
		}
		if (elevation > (double)tables.FaceElevationTable[end])
		{
			double faceSimpleLength = tables.Mesh.MetadataCache.FaceSimpleLengths[faceIndex];
			return (double)tables.FaceAreaTable[end] + faceSimpleLength * (elevation - (double)tables.FaceElevationTable[end]);
		}
		int upperIndex = FindUpperIndex(tables.FaceElevationTable, start, end, elevation);
		return Interpolate(tables.FaceElevationTable[upperIndex - 1], tables.FaceElevationTable[upperIndex], tables.FaceAreaTable[upperIndex - 1], tables.FaceAreaTable[upperIndex], elevation);
	}

	public static double EvaluateConveyance(MeshPropertyTables tables, int faceIndex, double elevation)
	{
		ValidateFace(tables, faceIndex);
		if (tables.FaceConveyanceTable == null || tables.FaceConveyanceA == null || tables.FaceConveyanceB == null)
		{
			throw new InvalidOperationException("Face conveyance has not been prepared on the mesh property tables.");
		}
		GetTableRange(tables, faceIndex, out var start, out var end);
		double x;
		if (elevation <= (double)tables.FaceElevationTable[start])
		{
			x = tables.FaceConveyanceTable[start];
		}
		else if (elevation > (double)tables.FaceElevationTable[end])
		{
			x = (double)tables.FaceConveyanceA[faceIndex] + (double)tables.FaceConveyanceB[faceIndex] * elevation;
		}
		else
		{
			int upperIndex = FindUpperIndex(tables.FaceElevationTable, start, end, elevation);
			x = Interpolate(tables.FaceElevationTable[upperIndex - 1], tables.FaceElevationTable[upperIndex], tables.FaceConveyanceTable[upperIndex - 1], tables.FaceConveyanceTable[upperIndex], elevation);
		}
		return Math.Pow(x, 1.6666666666666667);
	}

	public static double[] CopyElevations(MeshPropertyTables tables, int faceIndex)
	{
		ValidateFace(tables, faceIndex);
		int faceTableStart = tables.FaceTableStart[faceIndex];
		int faceTableCount = tables.FaceTableCount[faceIndex];
		double[] doubleValues = new double[faceTableCount];
		for (int i = 0; i < faceTableCount; i++)
		{
			doubleValues[i] = tables.FaceElevationTable[faceTableStart + i];
		}
		return doubleValues;
	}

	private static void ValidateFace(MeshPropertyTables tables, int faceIndex)
	{
		if (tables == null)
		{
			throw new ArgumentNullException("tables");
		}
		if (faceIndex < 0 || faceIndex >= tables.Mesh.FaceCount)
		{
			throw new ArgumentOutOfRangeException("faceIndex");
		}
		if (tables.FaceTableCount[faceIndex] <= 0)
		{
			throw new ArgumentException("The face does not have a property-table row.", "faceIndex");
		}
	}

	private static void GetTableRange(MeshPropertyTables tables, int faceIndex, out int start, out int end)
	{
		start = tables.FaceTableStart[faceIndex];
		end = start + tables.FaceTableCount[faceIndex] - 1;
	}

	private static int FindUpperIndex(float[] elevations, int start, int end, double elevation)
	{
		int upperIndex = start + 1;
		int end2 = end;
		while (upperIndex < end2)
		{
			int end22 = upperIndex + (end2 - upperIndex) / 2;
			if (elevation <= (double)elevations[end22])
			{
				end2 = end22;
			}
			else
			{
				upperIndex = end22 + 1;
			}
		}
		return upperIndex;
	}

	private static double Interpolate(double x0, double x1, double y0, double y1, double x)
	{
		double submergenceRatio = (x - x0) / (x1 - x0);
		return y0 + submergenceRatio * (y1 - y0);
	}
}
