using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Geospatial.Hydraulics;

public sealed class FaceGeometry
{
	public string Name { get; set; } = "";

	public int MaximumCellCount { get; }

	public int CellCount { get; }

	public float[] FaceStations { get; }

	public float[] FaceBedElevations { get; }

	public float[] CellMannings { get; }

	public FaceGeometry(float[] yf, float[] zf, float[] manning)
	{
		CellCount = yf.Length - 1;
		MaximumCellCount = CellCount;
		FaceStations = yf;
		FaceBedElevations = zf;
		CellMannings = manning;
	}

	public FaceGeometry(float[] yf, float[] zf, float[] manning, int cellCount, int maximumCellCount)
	{
		CellCount = cellCount;
		MaximumCellCount = maximumCellCount;
		FaceStations = yf;
		FaceBedElevations = zf;
		CellMannings = manning;
	}

	public float[] GetCellStations()
	{
		float[] cellStations = new float[CellCount];
		for (int i = 0; i < CellCount; i++)
		{
			cellStations[i] = 0.5f * (FaceStations[i] + FaceStations[i + 1]);
		}
		return cellStations;
	}

	public static FaceGeometry Refine(FaceGeometry source, int maximumCellCount, float dyMin = 0.1f)
	{
		float faceStation = source.FaceStations[0];
		float faceStation2 = source.FaceStations[^1];
		float faceStation2AboveFaceStation = faceStation2 - faceStation;
		float y = faceStation2AboveFaceStation / (float)maximumCellCount;
		y = MathF.Max(dyMin, y);
		int count = (int)MathF.Ceiling(faceStation2AboveFaceStation / y) + 1;
		float[] floatValues = new float[count];
		for (int i = 0; i < count; i++)
		{
			floatValues[i] = faceStation + (float)i * y;
		}
		if (floatValues[^1] < faceStation2)
		{
			Array.Resize(ref floatValues, count + 1);
			floatValues[^1] = faceStation2;
		}
		else
		{
			floatValues[^1] = faceStation2;
		}
		int count2 = floatValues.Length - 1;
		int queryCount = floatValues.Length;
		float[] result = null;
		InterpolateLinear(source.FaceStations, source.FaceBedElevations, source.CellCount + 1, floatValues, queryCount, ref result);
		float[] floatValuesArray = new float[count2];
		for (int j = 0; j < count2; j++)
		{
			floatValuesArray[j] = 0.5f * (floatValues[j] + floatValues[j + 1]);
		}
		float[] result2 = null;
		float[] cellStations = source.GetCellStations();
		MapNearestNeighbor(cellStations, source.CellMannings, cellStations.Length, floatValuesArray, count2, ref result2);
		int count3 = maximumCellCount + 1;
		float[] floatValues2 = new float[count3];
		float[] floatValues3 = new float[count3];
		float[] floatValues4 = new float[maximumCellCount];
		Array.Copy(floatValues, floatValues2, floatValues.Length);
		Array.Copy(result, floatValues3, result.Length);
		Array.Copy(result2, floatValues4, result2.Length);
		return new FaceGeometry(floatValues2, floatValues3, floatValues4, count2, maximumCellCount)
		{
			Name = source.Name
		};
	}

	public static FaceGeometry AdaptiveRefine(FaceGeometry geom, int maximumCellCount, float dyMin = 0.5f, float dyMax = 5f)
	{
		int cellCountOffset = geom.CellCount + 1;
		ReadOnlySpan<float> ySrc = geom.FaceStations.AsSpan(0, cellCountOffset);
		int count = maximumCellCount + 1;
		float[] yOut = new float[count];
		float[] result = new float[count];
		float[] result2 = new float[maximumCellCount];
		int adaptiveRefine = AdaptiveRefine(ySrc, dyMin, dyMax, count, ref yOut);
		int count2 = adaptiveRefine - 1;
		InterpolateLinear(geom.FaceStations, geom.FaceBedElevations, cellCountOffset, yOut, adaptiveRefine, ref result);
		float[] floatValues = new float[count2];
		for (int i = 0; i < count2; i++)
		{
			floatValues[i] = 0.5f * (yOut[i] + yOut[i + 1]);
		}
		float[] cellStations = geom.GetCellStations();
		MapNearestNeighbor(cellStations, geom.CellMannings, cellStations.Length, floatValues, count2, ref result2);
		return new FaceGeometry(yOut, result, result2, count2, maximumCellCount)
		{
			Name = geom.Name
		};
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private static int AdaptiveRefine(ReadOnlySpan<float> ySrc, float dyMin, float dyMax, int maxCount, ref float[] yOut)
	{
		float computed = 1.25f;
		int refine;
		while (true)
		{
			yOut = null;
			refine = Refine(ySrc, dyMin, dyMax, ref yOut);
			if (refine <= maxCount)
			{
				break;
			}
			dyMin *= computed;
			dyMax *= computed;
		}
		return refine;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static int Refine(ReadOnlySpan<float> ySrc, float dyMin, float dyMax, ref float[] yOut)
	{
		int length = ySrc.Length;
		int unity = 1;
		List<int> intList = new List<int>(unity);
		CollectionsMarshal.SetCount(intList, unity);
		Span<int> intListSpan = CollectionsMarshal.AsSpan(intList);
		int index = 0;
		intListSpan[index] = 0;
		List<int> intListList = intList;
		for (int i = 1; i < length; i++)
		{
			if (ySrc[i] - ySrc[intListList[intListList.Count - 1]] >= dyMin)
			{
				intListList.Add(i);
			}
		}
		int lengthOffset = length - 1;
		if (lengthOffset > 0)
		{
			if (intListList[intListList.Count - 1] != lengthOffset)
			{
				if (ySrc[lengthOffset] != ySrc[intListList[intListList.Count - 1]])
				{
					if (intListList.Count == 1)
					{
						intListList.Add(lengthOffset);
					}
					else
					{
						intListList[intListList.Count - 1] = lengthOffset;
					}
				}
			}
		}
		int createCheckedSum = 0;
		for (int j = 0; j < intListList.Count - 1; j++)
		{
			float ySrcStep = ySrc[intListList[j + 1]] - ySrc[intListList[j]];
			int createChecked = ((!(ySrcStep > dyMax)) ? 1 : int.CreateChecked(float.Ceiling(ySrcStep / dyMax)));
			createCheckedSum += createChecked;
		}
		createCheckedSum++;
		if (yOut == null)
		{
			yOut = new float[createCheckedSum];
		}
		else if (yOut.Length < createCheckedSum)
		{
			return -createCheckedSum;
		}
		int ordinal = 0;
		for (int k = 0; k < intListList.Count - 1; k++)
		{
			float ySrc2 = ySrc[intListList[k]];
			float ySrcYSrc2Term = ySrc[intListList[k + 1]] - ySrc2;
			int createChecked2 = ((!(ySrcYSrc2Term > dyMax)) ? 1 : int.CreateChecked(float.Ceiling(ySrcYSrc2Term / dyMax)));
			float createChecked2Term = ySrcYSrc2Term / (float)createChecked2;
			for (int l = 0; l < createChecked2; l++)
			{
				yOut[ordinal] = ySrc2 + (float)l * createChecked2Term;
				ordinal++;
			}
		}
		yOut[ordinal] = ySrc[intListList[intListList.Count - 1]];
		return createCheckedSum;
	}

	public static (float[] yf, float[] zf) LoadAndInterpolate(string filePath, float dyConst)
	{
		string[] strings = (from l in File.ReadAllLines(filePath)
			where !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#')
			select l).ToArray();
		float[] floatValues = new float[strings.Length];
		float[] floatValuesArray = new float[strings.Length];
		for (int intervalIndex = 0; intervalIndex < strings.Length; intervalIndex++)
		{
			string[] parts = strings[intervalIndex].Split(new char[3] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
			floatValues[intervalIndex] = float.Parse(parts[0]);
			floatValuesArray[intervalIndex] = float.Parse(parts[1]);
		}
		int count = (int)MathF.Ceiling((floatValues[^1] - floatValues[0]) / dyConst) + 1;
		float[] floatValues2 = new float[count];
		for (int intervalIndex2 = 0; intervalIndex2 < count; intervalIndex2++)
		{
			floatValues2[intervalIndex2] = floatValues[0] + (float)intervalIndex2 * dyConst;
		}
		if (floatValues2[^1] < floatValues[^1])
		{
			Array.Resize(ref floatValues2, count + 1);
			floatValues2[^1] = floatValues[^1];
		}
		float[] result = null;
		InterpolateLinear(floatValues, floatValuesArray, floatValues.Length, floatValues2, floatValues2.Length, ref result);
		return (yf: floatValues2, zf: result);
	}

	public static void SmoothManning(float[] n, int iterations)
	{
		int count = n.Length;
		if (count < 3)
		{
			return;
		}
		float[] floatValues = new float[count];
		for (int i = 0; i < iterations; i++)
		{
			Array.Copy(n, floatValues, count);
			for (int j = 1; j < count - 1; j++)
			{
				n[j] = 0.25f * (floatValues[j - 1] + floatValues[j + 1]) + 0.5f * floatValues[j];
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal static (float x, float y) LineIntersection(float p1x, float p1y, float p2x, float p2y, float p3x, float p3y, float p4x, float p4y)
	{
		float p2xAboveP1x = p2x - p1x;
		float p2yAboveP1y = p2y - p1y;
		float p4xAboveP3x = p4x - p3x;
		float p4yAboveP3y = p4y - p3y;
		float p1yP3yTerm = (p4xAboveP3x * (p1y - p3y) - p4yAboveP3y * (p1x - p3x)) / ((0f - p4xAboveP3x) * p2yAboveP1y + p2xAboveP1x * p4yAboveP3y);
		return (x: p1x + p1yP3yTerm * p2xAboveP1x, y: p1y + p1yP3yTerm * p2yAboveP1y);
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private static int InterpolateLinear(ReadOnlySpan<float> knots, ReadOnlySpan<float> values, int knotCount, ReadOnlySpan<float> queryPositions, int queryCount, ref float[] result)
	{
		if (queryCount == 0)
		{
			return 0;
		}
		if (result != null && result.Length < queryCount)
		{
			return -queryCount;
		}
		if (result == null)
		{
			result = new float[queryCount];
		}
		int i = 0;
		for (int j = 0; j < queryCount; j++)
		{
			float queryPosition;
			for (queryPosition = queryPositions[j]; i < knotCount - 2 && knots[i + 1] < queryPosition; i++)
			{
			}
			if (queryPosition <= knots[0])
			{
				result[j] = values[0];
				continue;
			}
			if (queryPosition >= knots[knotCount - 1])
			{
				result[j] = values[knotCount - 1];
				continue;
			}
			float interpolationFraction = (queryPosition - knots[i]) / (knots[i + 1] - knots[i]);
			result[j] = values[i] + interpolationFraction * (values[i + 1] - values[i]);
		}
		return queryCount;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private static int MapNearestNeighbor(ReadOnlySpan<float> oldPositions, ReadOnlySpan<float> oldValues, int oldCount, ReadOnlySpan<float> newPositions, int newCount, ref float[] result)
	{
		if (newCount == 0)
		{
			return 0;
		}
		if (result != null && result.Length < newCount)
		{
			return -newCount;
		}
		if (result == null)
		{
			result = new float[newCount];
		}
		int i = 0;
		for (int j = 0; j < newCount; j++)
		{
			for (; i < oldCount - 1 && float.Abs(oldPositions[i + 1] - newPositions[j]) < float.Abs(oldPositions[i] - newPositions[j]); i++)
			{
			}
			result[j] = oldValues[i];
		}
		return newCount;
	}
}
