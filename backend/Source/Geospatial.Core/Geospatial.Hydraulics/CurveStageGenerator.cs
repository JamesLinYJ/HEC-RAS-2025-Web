using System;

namespace Geospatial.Hydraulics;

public static class CurveStageGenerator
{
	public static float[] GenerateUniform(FaceGeometry geom, float wseIncrement, int maximumWseCount)
	{
		ValidateGeometry(geom);
		ValidateGenerationInputs(wseIncrement, maximumWseCount);
		int count = geom.CellCount + 1;
		float faceBedElevation = geom.FaceBedElevations[0];
		for (int i = 1; i < count; i++)
		{
			if (geom.FaceBedElevations[i] < faceBedElevation)
			{
				faceBedElevation = geom.FaceBedElevations[i];
			}
		}
		float[] floatValues = new float[maximumWseCount];
		for (int j = 0; j < maximumWseCount; j++)
		{
			float faceBedElevationWseIncrementTerm = faceBedElevation + (float)(j + 1) * wseIncrement;
			if (!float.IsFinite(faceBedElevationWseIncrementTerm) || (j > 0 && faceBedElevationWseIncrementTerm <= floatValues[j - 1]))
			{
				throw new ArgumentOutOfRangeException("wseIncrement", "WSE increment does not produce finite, strictly ascending stages for this geometry.");
			}
			floatValues[j] = faceBedElevationWseIncrementTerm;
		}
		return floatValues;
	}

	public static float[] GenerateAdaptive(FaceGeometry geom, float wseIncrement, int maximumWseCount)
	{
		ValidateGeometry(geom);
		ValidateGenerationInputs(wseIncrement, maximumWseCount);
		float scaledWseIncrement = wseIncrement * 0.5f;
		float wseIncrement2 = wseIncrement;
		int count = geom.CellCount + 1;
		float[] floatValues = new float[count];
		Array.Copy(geom.FaceBedElevations, floatValues, count);
		Array.Sort(floatValues);
		float computed = floatValues[0];
		float computed2 = floatValues[^1];
		int ordinal = ((!(computed2 > computed)) ? 1 : 2) + 1;
		if (maximumWseCount < ordinal)
		{
			throw new ArgumentOutOfRangeException("maximumWseCount", $"WSE count maximum must be at least {ordinal} to preserve the bed endpoints and include an above-bed stage.");
		}
		float[] yOut = new float[maximumWseCount - 1];
		int refine;
		while (true)
		{
			refine = FaceGeometry.Refine(floatValues, scaledWseIncrement, wseIncrement2, ref yOut);
			if (refine > 0)
			{
				break;
			}
			scaledWseIncrement *= 1.25f;
			wseIncrement2 *= 1.25f;
			if (!float.IsFinite(scaledWseIncrement) || !float.IsFinite(wseIncrement2))
			{
				throw new ArgumentOutOfRangeException("wseIncrement", "WSE increment cannot produce a finite adaptive stage grid for this geometry.");
			}
		}
		float[] floatValuesArray = new float[maximumWseCount];
		Array.Copy(yOut, floatValuesArray, refine);
		for (int i = refine; i < maximumWseCount; i++)
		{
			float refineWseIncrementTerm = computed2 + (float)(i - refine + 1) * wseIncrement;
			if (!float.IsFinite(refineWseIncrementTerm) || refineWseIncrementTerm <= floatValuesArray[i - 1])
			{
				throw new ArgumentOutOfRangeException("wseIncrement", "WSE increment does not produce finite, strictly ascending stages for this geometry.");
			}
			floatValuesArray[i] = refineWseIncrementTerm;
		}
		return floatValuesArray;
	}

	private static void ValidateGenerationInputs(float wseIncrement, int maximumWseCount)
	{
		if (!float.IsFinite(wseIncrement) || wseIncrement <= 0f)
		{
			throw new ArgumentOutOfRangeException("wseIncrement", "WSE increment must be finite and positive.");
		}
		if (maximumWseCount < 1)
		{
			throw new ArgumentOutOfRangeException("maximumWseCount", "WSE count maximum must be positive.");
		}
	}

	private static void ValidateGeometry(FaceGeometry geom)
	{
		ArgumentNullException.ThrowIfNull(geom, "geom");
		if (geom.CellCount < 1)
		{
			throw new ArgumentException("Geometry must contain at least one cell.", "geom");
		}
		int count = geom.CellCount + 1;
		if (geom.FaceStations == null || geom.FaceStations.Length < count)
		{
			throw new ArgumentException("Geometry does not contain enough face stations.", "geom");
		}
		if (geom.FaceBedElevations == null || geom.FaceBedElevations.Length < count)
		{
			throw new ArgumentException("Geometry does not contain enough face bed elevations.", "geom");
		}
		if (geom.CellMannings == null || geom.CellMannings.Length < geom.CellCount)
		{
			throw new ArgumentException("Geometry does not contain enough cell Manning values.", "geom");
		}
		for (int i = 0; i < count; i++)
		{
			if (!float.IsFinite(geom.FaceStations[i]))
			{
				throw new ArgumentException("Face stations must be finite.", "geom");
			}
			if (!float.IsFinite(geom.FaceBedElevations[i]))
			{
				throw new ArgumentException("Face bed elevations must be finite.", "geom");
			}
			if (i > 0 && geom.FaceStations[i] <= geom.FaceStations[i - 1])
			{
				throw new ArgumentException("Face stations must be strictly ascending.", "geom");
			}
		}
		for (int j = 0; j < geom.CellCount; j++)
		{
			if (!float.IsFinite(geom.CellMannings[j]) || geom.CellMannings[j] <= 0f)
			{
				throw new ArgumentException("Cell Manning values must be finite and positive.", "geom");
			}
		}
	}
}
