using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Geospatial.Hydraulics;

internal sealed class FaceHydraulics
{
	private static readonly int VectorWidth = Vector<float>.Count;

	internal readonly float[] faceDepth;

	internal readonly float[] faceWidth;

	internal readonly float[] faceSolid;

	internal readonly float[] cellWidth;

	internal readonly float[] cellWettedWidth;

	internal readonly float[] cellDepth;

	internal readonly float[] cellArea;

	internal readonly float[] cellPerim;

	internal readonly float[] cellHydRadCbrt;

	internal readonly float[] cellDragVeg;

	internal readonly float[] cellManningComp;

	internal readonly float[] cellAreaVeg;

	internal readonly float[] cellSolid;

	internal readonly bool[] cellWet;

	internal int minWetCell;

	internal int maxWetCell;

	internal int minWetFace;

	internal int maxWetFace;

	internal bool AllDry => minWetCell < 0;

	internal FaceHydraulics(int maximumCellCount)
	{
		int count = maximumCellCount + 1;
		faceDepth = new float[count];
		faceWidth = new float[count];
		faceSolid = new float[count];
		cellDepth = new float[maximumCellCount];
		cellArea = new float[maximumCellCount];
		cellPerim = new float[maximumCellCount];
		cellHydRadCbrt = new float[maximumCellCount];
		cellDragVeg = new float[maximumCellCount];
		cellManningComp = new float[maximumCellCount];
		cellAreaVeg = new float[maximumCellCount];
		cellSolid = new float[maximumCellCount];
		cellWidth = new float[maximumCellCount];
		cellWettedWidth = new float[maximumCellCount];
		cellWet = new bool[maximumCellCount];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void ClearTails<T>(T[] arr, int wetFirst, int wetLast, int totalLen)
	{
		if (wetFirst > 0)
		{
			Array.Clear(arr, 0, wetFirst);
		}
		if (wetLast + 1 < totalLen)
		{
			Array.Clear(arr, wetLast + 1, totalLen - wetLast - 1);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal void Preprocess(FaceGeometry geom, FaceHydraulicInputs inputs, float wse)
	{
		int cellCount = geom.CellCount;
		int count = cellCount + 1;
		float gravity = inputs.Gravity;
		float scaledGravity = 1f / gravity;
		float manningUnitFactor = inputs.ManningUnitFactor;
		float manningUnitFactorPolynomial = manningUnitFactor * manningUnitFactor;
		float vegetationDragCoefficient = inputs.VegetationDragCoefficient;
		int vectorWidth = VectorWidth;
		minWetFace = -1;
		maxWetFace = -1;
		for (int i = 0; i < count; i++)
		{
			if (wse > geom.FaceBedElevations[i] + 1E-06f)
			{
				if (minWetFace < 0)
				{
					minWetFace = i;
				}
				maxWetFace = i;
			}
		}
		if (minWetFace < 0)
		{
			Array.Clear(cellWet, 0, cellCount);
			Array.Clear(cellArea, 0, cellCount);
			Array.Clear(cellPerim, 0, cellCount);
			Array.Clear(cellDepth, 0, cellCount);
			Array.Clear(cellWettedWidth, 0, cellCount);
			Array.Clear(cellHydRadCbrt, 0, cellCount);
			Array.Clear(cellDragVeg, 0, cellCount);
			Array.Clear(cellAreaVeg, 0, cellCount);
			Array.Clear(cellSolid, 0, cellCount);
			Array.Clear(cellManningComp, 0, cellCount);
			Array.Clear(faceDepth, 0, count);
			Array.Clear(faceSolid, 0, count);
			minWetCell = -1;
			maxWetCell = -1;
			return;
		}
		for (int j = 0; j < cellCount; j++)
		{
			cellWidth[j] = geom.FaceStations[j + 1] - geom.FaceStations[j];
		}
		faceWidth[0] = cellWidth[0];
		faceWidth[cellCount] = cellWidth[cellCount - 1];
		Vector<float> vector = new Vector<float>(0.5f);
		int k;
		for (k = 1; k + vectorWidth <= cellCount; k += vectorWidth)
		{
			(vector * (new Vector<float>(cellWidth, k - 1) + new Vector<float>(cellWidth, k))).CopyTo(faceWidth, k);
		}
		for (; k < cellCount; k++)
		{
			faceWidth[k] = 0.5f * (cellWidth[k - 1] + cellWidth[k]);
		}
		ClearTails(faceDepth, minWetFace, maxWetFace, count);
		ClearTails(faceSolid, minWetFace, maxWetFace, count);
		Vector<float> vector2 = new Vector<float>(wse);
		Vector<float> zero = Vector<float>.Zero;
		int l = minWetFace;
		int count2;
		for (count2 = maxWetFace + 1; l + vectorWidth <= count2; l += vectorWidth)
		{
			Vector.Max(vector2 - new Vector<float>(geom.FaceBedElevations, l), zero).CopyTo(faceDepth, l);
		}
		for (; l < count2; l++)
		{
			faceDepth[l] = MathF.Max(wse - geom.FaceBedElevations[l], 0f);
		}
		int minWetFaceOffset = Math.Max(0, minWetFace - 1);
		int count3 = Math.Min(cellCount - 1, maxWetFace);
		minWetCell = -1;
		maxWetCell = -1;
		for (int m = minWetFaceOffset; m <= count3; m++)
		{
			float faceDepth2 = faceDepth[m];
			float faceDepth3 = faceDepth[m + 1];
			if (faceDepth2 > 0f && faceDepth3 > 0f)
			{
				float faceDepth2FaceDepth3Term = 0.5f * (faceDepth2 + faceDepth3);
				cellArea[m] = cellWidth[m] * faceDepth2FaceDepth3Term;
				float faceDepth3AboveFaceDepth2 = faceDepth3 - faceDepth2;
				cellPerim[m] = MathF.Sqrt(cellWidth[m] * cellWidth[m] + faceDepth3AboveFaceDepth2 * faceDepth3AboveFaceDepth2);
				cellWettedWidth[m] = cellWidth[m];
			}
			else if (faceDepth2 > 0f)
			{
				float cellWettedWidth2 = FaceGeometry.LineIntersection(geom.FaceStations[m], geom.FaceBedElevations[m], geom.FaceStations[m + 1], geom.FaceBedElevations[m + 1], geom.FaceStations[m], wse, geom.FaceStations[m + 1], wse).x - geom.FaceStations[m];
				cellArea[m] = cellWettedWidth2 * 0.5f * faceDepth2;
				cellPerim[m] = MathF.Sqrt(cellWettedWidth2 * cellWettedWidth2 + faceDepth2 * faceDepth2);
				cellWettedWidth[m] = cellWettedWidth2;
			}
			else if (faceDepth3 > 0f)
			{
				float lineIntersectionFaceGeometryTerm = FaceGeometry.LineIntersection(geom.FaceStations[m], geom.FaceBedElevations[m], geom.FaceStations[m + 1], geom.FaceBedElevations[m + 1], geom.FaceStations[m], wse, geom.FaceStations[m + 1], wse).x;
				float cellWettedWidth3 = geom.FaceStations[m + 1] - lineIntersectionFaceGeometryTerm;
				cellArea[m] = cellWettedWidth3 * 0.5f * faceDepth3;
				cellPerim[m] = MathF.Sqrt(cellWettedWidth3 * cellWettedWidth3 + faceDepth3 * faceDepth3);
				cellWettedWidth[m] = cellWettedWidth3;
			}
			else
			{
				cellArea[m] = 0f;
				cellPerim[m] = 0f;
				cellWettedWidth[m] = 0f;
			}
			cellDepth[m] = cellArea[m] / cellWidth[m];
			cellWet[m] = cellArea[m] > 1E-06f;
			float cellAreaOverCellPerim = ((cellPerim[m] > 0f) ? (cellArea[m] / cellPerim[m]) : 0f);
			float cellHydRadCbrt2 = ((cellWet[m] && cellAreaOverCellPerim > 0f) ? MathF.Cbrt(cellAreaOverCellPerim) : 0f);
			cellHydRadCbrt[m] = cellHydRadCbrt2;
			cellDragVeg[m] = ((cellHydRadCbrt2 > 0f) ? (geom.CellMannings[m] * geom.CellMannings[m] * gravity / (manningUnitFactorPolynomial * cellHydRadCbrt2)) : 0f);
			if (cellWet[m])
			{
				if (minWetCell < 0)
				{
					minWetCell = m;
				}
				maxWetCell = m;
			}
		}
		if (minWetCell < 0)
		{
			Array.Clear(cellWet, 0, cellCount);
			Array.Clear(cellArea, 0, cellCount);
			Array.Clear(cellPerim, 0, cellCount);
			Array.Clear(cellDepth, 0, cellCount);
			Array.Clear(cellWettedWidth, 0, cellCount);
			Array.Clear(cellHydRadCbrt, 0, cellCount);
			Array.Clear(cellDragVeg, 0, cellCount);
			Array.Clear(cellAreaVeg, 0, cellCount);
			Array.Clear(cellSolid, 0, cellCount);
			Array.Clear(cellManningComp, 0, cellCount);
			return;
		}
		ClearTails(cellWet, minWetCell, maxWetCell, cellCount);
		ClearTails(cellArea, minWetCell, maxWetCell, cellCount);
		ClearTails(cellPerim, minWetCell, maxWetCell, cellCount);
		ClearTails(cellDepth, minWetCell, maxWetCell, cellCount);
		ClearTails(cellWettedWidth, minWetCell, maxWetCell, cellCount);
		ClearTails(cellHydRadCbrt, minWetCell, maxWetCell, cellCount);
		ClearTails(cellDragVeg, minWetCell, maxWetCell, cellCount);
		ClearTails(cellAreaVeg, minWetCell, maxWetCell, cellCount);
		ClearTails(cellSolid, minWetCell, maxWetCell, cellCount);
		ClearTails(cellManningComp, minWetCell, maxWetCell, cellCount);
		bool vegetationStemDensityInputsTerm = inputs.VegetationStemDensity > 0f;
		float vegetationStemDensity = (vegetationStemDensityInputsTerm ? inputs.VegetationStemDensity : 0f);
		float vegetationStemDiameter = inputs.VegetationStemDiameter;
		float vegetationStemDensityTimesVegetationStemDiameter = vegetationStemDensity * vegetationStemDiameter;
		float vegetationStemDensityPiTerm = (vegetationStemDensityInputsTerm ? (1f - vegetationStemDensity * (float)Math.PI * vegetationStemDiameter * vegetationStemDiameter * 0.25f) : 1f);
		for (int n = minWetCell; n <= maxWetCell; n++)
		{
			cellAreaVeg[n] = (vegetationStemDensityInputsTerm ? (vegetationStemDensityTimesVegetationStemDiameter * cellDepth[n]) : 0f);
			cellSolid[n] = (cellWet[n] ? vegetationStemDensityPiTerm : 0f);
			cellManningComp[n] = geom.CellMannings[n];
			float cellHydRadCbrt3 = cellHydRadCbrt[n];
			if (cellHydRadCbrt3 > 0f)
			{
				cellManningComp[n] = manningUnitFactor * MathF.Sqrt(cellHydRadCbrt3 * (cellDragVeg[n] + 0.5f * vegetationDragCoefficient * cellAreaVeg[n]) * scaledGravity);
			}
		}
		if (minWetFace == 0)
		{
			faceSolid[0] = cellSolid[0];
		}
		if (maxWetFace == cellCount)
		{
			faceSolid[cellCount] = cellSolid[cellCount - 1];
		}
		int vectorWidthSum = Math.Max(1, minWetFace);
		int count4;
		for (count4 = Math.Min(maxWetFace + 1, cellCount); vectorWidthSum + vectorWidth <= count4; vectorWidthSum += vectorWidth)
		{
			Vector.SquareRoot(new Vector<float>(cellSolid, vectorWidthSum - 1) * new Vector<float>(cellSolid, vectorWidthSum)).CopyTo(faceSolid, vectorWidthSum);
		}
		for (; vectorWidthSum < count4; vectorWidthSum++)
		{
			faceSolid[vectorWidthSum] = MathF.Sqrt(cellSolid[vectorWidthSum - 1] * cellSolid[vectorWidthSum]);
		}
	}

	internal void Preprocess(ReadOnlySpan<float> widths, ReadOnlySpan<float> areas, ReadOnlySpan<float> wettedPerimeters, ReadOnlySpan<float> mannings, ReadOnlySpan<float> faceBedElevations, FaceHydraulicInputs inputs, float wse)
	{
		int length = widths.Length;
		int count = length + 1;
		Array.Clear(faceDepth, 0, count);
		Array.Clear(faceSolid, 0, count);
		Array.Clear(cellWet, 0, length);
		Array.Clear(cellArea, 0, length);
		Array.Clear(cellPerim, 0, length);
		Array.Clear(cellDepth, 0, length);
		Array.Clear(cellWettedWidth, 0, length);
		Array.Clear(cellHydRadCbrt, 0, length);
		Array.Clear(cellDragVeg, 0, length);
		Array.Clear(cellAreaVeg, 0, length);
		Array.Clear(cellSolid, 0, length);
		Array.Clear(cellManningComp, 0, length);
		float gravity = inputs.Gravity;
		float manningUnitFactorPolynomial = inputs.ManningUnitFactor * inputs.ManningUnitFactor;
		bool vegetationStemDensityInputsTerm = inputs.VegetationStemDensity > 0f;
		float vegetationStemDensityTimesVegetationStemDiameter = (vegetationStemDensityInputsTerm ? (inputs.VegetationStemDensity * inputs.VegetationStemDiameter) : 0f);
		float vegetationStemDensityInputsTerm2 = (vegetationStemDensityInputsTerm ? (1f - inputs.VegetationStemDensity * (float)Math.PI * inputs.VegetationStemDiameter * inputs.VegetationStemDiameter * 0.25f) : 1f);
		minWetCell = -1;
		maxWetCell = -1;
		for (int i = 0; i < length; i++)
		{
			float width = widths[i];
			float area = areas[i];
			float wettedPerimeter = wettedPerimeters[i];
			bool cellWet2 = area > 1E-06f;
			cellWidth[i] = width;
			cellArea[i] = (cellWet2 ? area : 0f);
			cellPerim[i] = (cellWet2 ? wettedPerimeter : 0f);
			cellDepth[i] = (cellWet2 ? (area / width) : 0f);
			cellWettedWidth[i] = (cellWet2 ? width : 0f);
			cellWet[i] = cellWet2;
			cellSolid[i] = (cellWet2 ? vegetationStemDensityInputsTerm2 : 0f);
			cellAreaVeg[i] = (vegetationStemDensityInputsTerm ? (vegetationStemDensityTimesVegetationStemDiameter * cellDepth[i]) : 0f);
			float areaOverWettedPerimeter = (cellWet2 ? (area / wettedPerimeter) : 0f);
			float cellHydRadCbrt2 = ((areaOverWettedPerimeter > 0f) ? MathF.Cbrt(areaOverWettedPerimeter) : 0f);
			cellHydRadCbrt[i] = cellHydRadCbrt2;
			cellDragVeg[i] = ((cellHydRadCbrt2 > 0f) ? (mannings[i] * mannings[i] * gravity / (manningUnitFactorPolynomial * cellHydRadCbrt2)) : 0f);
			cellManningComp[i] = mannings[i];
			if (cellHydRadCbrt2 > 0f)
			{
				cellManningComp[i] = inputs.ManningUnitFactor * MathF.Sqrt(cellHydRadCbrt2 * (cellDragVeg[i] + 0.5f * inputs.VegetationDragCoefficient * cellAreaVeg[i]) / gravity);
			}
			if (cellWet2)
			{
				if (minWetCell < 0)
				{
					minWetCell = i;
				}
				maxWetCell = i;
			}
		}
		minWetFace = -1;
		maxWetFace = -1;
		for (int j = 0; j < count; j++)
		{
			if (j == 0)
			{
				faceWidth[j] = cellWidth[0];
				faceSolid[j] = cellSolid[0];
			}
			else if (j == length)
			{
				faceWidth[j] = cellWidth[length - 1];
				faceSolid[j] = cellSolid[length - 1];
			}
			else
			{
				faceWidth[j] = 0.5f * (cellWidth[j - 1] + cellWidth[j]);
				faceSolid[j] = MathF.Sqrt(cellSolid[j - 1] * cellSolid[j]);
			}
			float faceBedElevation = faceBedElevations[j];
			if (!(wse <= faceBedElevation + 1E-06f))
			{
				faceDepth[j] = wse - faceBedElevation;
				if (minWetFace < 0)
				{
					minWetFace = j;
				}
				maxWetFace = j;
			}
		}
	}
}
