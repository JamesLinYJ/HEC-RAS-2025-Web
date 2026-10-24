using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Geospatial.Transformers;
using Geospatial.Vectors.Topology;
using Utility.Logging;
using Utility.Progress;

namespace Geospatial.Vectors.MeshTopology;

public class HybridQuadPatch : PatchCommon, IRegionGenerator
{
	[DebuggerDisplay("Div(count={count} at fraction={fraction})")]
	public readonly struct DivCount
	{
		public readonly double fraction;

		public readonly int count;

		public readonly double fractionalCount;

		public readonly double localLength;

		public DivCount ReplaceCount(int newCount)
		{
			return new DivCount(fraction, newCount, fractionalCount, localLength);
		}

		public DivCount IncrCount(int increment)
		{
			return new DivCount(fraction, count + increment, fractionalCount, localLength);
		}

		public DivCount(double fraction, int count, double fractionalCount = -1.0, double localLength = -1.0)
		{
			this.fraction = fraction;
			this.count = count;
			if (fractionalCount > 0.0)
			{
				this.fractionalCount = fractionalCount;
			}
			else
			{
				this.fractionalCount = count;
			}
			this.localLength = localLength;
		}
	}

	public static class MessageCodes
	{
		public static readonly MessageClass CoonsOverride = new MessageClass("DynamicTin_UnderResolvedChannel", MessageSeverity.Information, "Region {RegionIndex} will use Coons mapping instead of Laplacian mapping.");

		public static Message GetCoonsOverride(int ctrlPt)
		{
			return CoonsOverride.Create(ctrlPt);
		}
	}

	public double sizeTolerance = 1.0;

	public ISizeFunction sizeFunctionNS;

	public ISizeFunction sizeFunctionEW;

	public ISpatialTransformer pointTransform => transform;

	public HybridQuadPatch(ConceptualMesh mesh, int cIdx)
		: base(mesh, cIdx)
	{
	}

	private void OverrideTransformMethod(OperationResult res)
	{
		bool condition = false;
		if (cmesh.CellRegionMetadata[regionIdx].TINResolution < 0f)
		{
			condition = true;
		}
		else
		{
			condition = true;
			foreach (PatchSide side in Sides)
			{
				if (side.SidePieces.Count > 1 || !SideBCIsDirichlet(side) || side.Smooth.PointCount > 2)
				{
					condition = false;
					break;
				}
			}
		}
		if (condition)
		{
			transformMethod = TransformMethod.COONS;
			int controlPointIndex = cmesh.CellRegionMetadata[regionIdx].ControlPointIndex;
			res.AddMessage(MessageCodes.GetCoonsOverride(controlPointIndex));
		}
	}

	public SlidingMesh Generate(Action<SlidingMesh> Progress = null, ProgressReporter reporter = null, OperationResult res = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		if (double.IsNaN(sizeTolerance))
		{
			sizeTolerance = 0.5;
		}
		else if (sizeTolerance < 0.0)
		{
			sizeTolerance = 0.0;
		}
		result = new SlidingMesh();
		SetSides();
		OverrideTransformMethod(res);
		reporter.ReportProgress(10);
		transform = CreateTransform();
		ChooseRowColCounts(Sides, transform, out var rowsPerU, out var columnsPerV);
		Progress?.Invoke(result);
		reporter.ReportProgress(20);
		try
		{
			GenerateAndSplice(result, rowsPerU, columnsPerV, transform, Sides, Progress);
		}
		catch (Exception ex)
		{
			if (ex is DynamicMeshException || ex is StructuredException)
			{
				int controlPointIndex = cmesh.CellRegionMetadata[regionIdx].ControlPointIndex;
				res.AddFailure(MeshExtensions.MessageCodes.GetErrorDistortedQuadPatch(controlPointIndex, regionIdx, ex.Message));
				return null;
			}
			throw;
		}
		Progress?.Invoke(result);
		reporter.ReportProgress(50);
		if (!result.CheckMesh(out var _))
		{
			int controlPointIndex2 = cmesh.CellRegionMetadata[regionIdx].ControlPointIndex;
			res.AddFailure(MeshExtensions.MessageCodes.GetErrorDiscretizingQuadPatch(controlPointIndex2, regionIdx, "Mesh checks failed"));
		}
		List<int> cellIslandCells = cmesh.CellIslandCells(regionIdx);
		List<int> cellBreaklines = cmesh.CellBreaklines(regionIdx);
		if (cellIslandCells.Count > 0 || cellBreaklines.Count > 0)
		{
			try
			{
				result.ClipToBoundary(cmesh, regionIdx, SlidingMesh.ClipMode.RaggedWithScale, useSmoothBoundary: false, includeExterior: false);
			}
			catch (StructuredException)
			{
				int controlPointIndex3 = cmesh.CellRegionMetadata[regionIdx].ControlPointIndex;
				res.AddFailure(MeshExtensions.MessageCodes.GetErrorDiscretizingQuadPatch(controlPointIndex3, regionIdx, "Intermediate mesh for quad patch was invalid. Consider triangles or splitting complex shape into simpler shapes."));
				return null;
			}
			HashSet<HalfEdge> origPerimeter = new HashSet<HalfEdge>();
			result.AddNodeArcsWithConstraints(cmesh, regionIdx, includeExterior: false, includeBreaklines: true, origPerimeter);
			result.FillGaps(null, null, origPerimeter);
		}
		reporter.ReportProgress(100);
		result.transform = pointTransform;
		return result;
	}

	private void GenerateAndSplice(SlidingMesh mesh, List<DivCount> rowsPerU, List<DivCount> columnsPerV, ISpatialTransformer localTransform, List<PatchSide> sides, Action<SlidingMesh> Progress)
	{
		List<int> newFaces = new List<int>();
		List<Point> points = new List<Point>();
		List<int[,]> intValuesList = new List<int[,]>();
		List<int[,]> intValuesListList = null;
		Point ll = default;
		Point ur = default;
		for (int i = 0; i < rowsPerU.Count - 1; i++)
		{
			double fraction = rowsPerU[i].fraction;
			double fraction2 = rowsPerU[i + 1].fraction;
			int count = rowsPerU[i].count;
			List<int[,]> intValuesList2 = new List<int[,]>();
			int index = 0;
			for (int j = 0; j < columnsPerV.Count - 1; j++)
			{
				double fraction3 = columnsPerV[index].fraction;
				double fraction4 = columnsPerV[j + 1].fraction;
				int countOffset = columnsPerV[j].count - 1;
				int countOffset2 = count - 1;
				int fractionTerm = (int)Math.Ceiling(fraction * (double)countOffset);
				int fraction3Term = (int)Math.Ceiling(fraction3 * (double)countOffset2);
				int countOffset3 = ((fraction2 >= 1.0) ? countOffset : ((int)Math.Ceiling(fraction2 * (double)countOffset) - 1));
				int countOffset4 = ((fraction4 >= 1.0) ? countOffset2 : ((int)Math.Ceiling(fraction4 * (double)countOffset2) - 1));
				if (1 + countOffset4 - fraction3Term < 1)
				{
					int[,] elements = new int[0, 0];
					intValuesList2.Add(elements);
					continue;
				}
				index = j + 1;
				if (1 + countOffset3 - fractionTerm < 1)
				{
					if (intValuesListList != null)
					{
						int[,] intValues = new int[1, intValuesListList[j].GetLength(1)];
						for (int k = 0; k < intValues.GetLength(1); k++)
						{
							intValues[0, k] = intValuesListList[j][intValuesListList[j].GetLength(0) - 1, k];
						}
						intValuesList2.Add(intValues);
					}
					else
					{
						int[,] elements2 = new int[0, 0];
						intValuesList2.Add(elements2);
					}
					continue;
				}
				if (countOffset2 == 0)
				{
					ll.Y = 0.5;
					ur.Y = 0.5;
				}
				else
				{
					ll.Y = (double)fraction3Term / (double)countOffset2;
					ur.Y = (double)countOffset4 / (double)countOffset2;
				}
				if (countOffset == 0)
				{
					ll.X = 0.5;
					ur.X = 0.5;
				}
				else
				{
					ll.X = (double)fractionTerm / (double)countOffset;
					ur.X = (double)countOffset3 / (double)countOffset;
				}
				int[,] addCartesian = mesh.AddCartesian(1 + countOffset3 - fractionTerm, 1 + countOffset4 - fraction3Term, ll, ur);
				int count2 = (addCartesian.GetLength(0) + 1) * (addCartesian.GetLength(1) + 1) / 4;
				points.AddRange(mesh.NodePoints.TakeLast(count2));
				intValuesList2.Add(addCartesian);
				mesh.TransformSelected(localTransform, addCartesian);
				Progress?.Invoke(mesh);
			}
			int[,] stitchQuadStrip = StitchQuadStrip(mesh, intValuesList2, 0, newFaces);
			intValuesList.Add(stitchQuadStrip);
			Progress?.Invoke(mesh);
			intValuesListList = intValuesList2;
		}
		try
		{
			StitchQuadStrip(mesh, intValuesList, 1, newFaces);
		}
		catch (StructuredException ex)
		{
			int controlPointIndex = cmesh.CellRegionMetadata[regionIdx].ControlPointIndex;
			ex.Messages.Add(MeshExtensions.MessageCodes.GetErrorDistortedQuadPatch(controlPointIndex, regionIdx, ex.Message));
			throw;
		}
		SetTargetLengths(mesh, points, localTransform, sides);
		SetConstraints(mesh, sides, points);
	}

	public void ChooseRowColCounts(List<PatchSide> sides, ISpatialTransformer localTransform, out List<DivCount> rowsPerU, out List<DivCount> columnsPerV)
	{
		if (sizeTolerance >= 1.0)
		{
			ChooseDivCounts(sides[0].Nodes, sides[2].Nodes, out columnsPerV);
			ChooseDivCounts(sides[3].Nodes, sides[1].Nodes, out rowsPerU);
			return;
		}
		if (sizeFunctionNS == null || sizeFunctionEW == null)
		{
			QuadSizeFunction.InferSizeFunctions(cmesh, regionIdx, transform, out sizeFunctionNS, out sizeFunctionEW, fromNodes: true);
		}
		int nominalStepsAcross = (sides[3].Nodes.Count + sides[1].Nodes.Count) / 2;
		ChooseDivCountsByScale(sides[0].Nodes, sides[2].Nodes, sizeFunctionEW, nominalStepsAcross, isXAligned: true, out columnsPerV, sizeTolerance);
		int nominalStepsAcross2 = (sides[0].Nodes.Count + sides[2].Nodes.Count) / 2;
		ChooseDivCountsByScale(sides[3].Nodes, sides[1].Nodes, sizeFunctionNS, nominalStepsAcross2, isXAligned: false, out rowsPerU, sizeTolerance);
	}

	public void ChooseDivCounts(Polyline fA, Polyline fB, out List<DivCount> divCounts)
	{
		divCounts = new List<DivCount>
		{
			new DivCount(0.0, fA.Count)
		};
		int count = fB.Count - fA.Count;
		int sign = 1;
		if (count < 0)
		{
			sign = -1;
			count *= -1;
		}
		for (int i = 1; i <= count; i++)
		{
			divCounts.Add(new DivCount((double)i / ((double)count + 1.0), fA.Count + sign * i));
		}
		double tolerance = 1E-06;
		if (divCounts[0].count == 1)
		{
			DivCount divCount = new DivCount(tolerance, 2);
			if (divCounts[1].count == 2)
			{
				divCounts[1] = divCount;
			}
			else
			{
				divCounts.Insert(1, divCount);
			}
		}
		else if (divCounts.Last().count == 1)
		{
			divCounts[divCounts.Count - 1] = new DivCount(1.0 - tolerance, 1);
		}
		divCounts.Add(new DivCount(1.01, fB.Count));
	}

	public void ChooseDivCountsByScale(Polyline fA, Polyline fB, ISizeFunction sizeFunction, int nominalStepsAcross, bool isXAligned, out List<DivCount> divCounts, double filterStrength)
	{
		int count = Math.Max(fA.Count, fB.Count);
		DiscretizeLine discretizeLine = new DiscretizeLine(sizeFunction);
		List<DivCount> divCountsList = new List<DivCount>();
		for (int i = 0; i < nominalStepsAcross; i++)
		{
			double xComponent = (double)i / (double)(nominalStepsAcross - 1);
			Polyline polyline = new Polyline();
			for (int j = 0; j < count; j++)
			{
				double yComponent = (double)j / (double)(count - 1);
				Point transform2 = ISpatialTransformerExtensions.Transform(pt: (!isXAligned) ? new Point(xComponent, yComponent) : new Point(yComponent, xComponent), xf: transform);
				polyline.Add(transform2);
			}
			double fractionalCount = discretizeLine.FractionalCount(polyline);
			divCountsList.Add(new DivCount(xComponent, Math.Max(2, (int)Math.Round(fractionalCount)), Math.Max(1.51, fractionalCount), polyline.Length));
		}
		divCountsList[0] = divCountsList[0].ReplaceCount(fA.Count);
		divCountsList[divCountsList.Count - 1] = new DivCount(0.9999, fB.Count);
		divCountsList.Add(new DivCount(1.01, fB.Count));
		if (filterStrength > 0.0)
		{
			FilterTransitions(divCountsList, filterStrength);
		}
		divCounts = new List<DivCount>();
		for (int k = 0; k < divCountsList.Count; k++)
		{
			if (k == 0 || k >= divCountsList.Count - 1)
			{
				divCounts.Add(divCountsList[k]);
			}
			else if (divCountsList[k].count != divCountsList[k - 1].count)
			{
				divCounts.Add(divCountsList[k]);
			}
		}
	}

	public void FilterTransitions(List<DivCount> divCounts, double scaleFilter)
	{
		int ordinal = 1000;
		double[] doubleValues = new double[divCounts.Count];
		double[] doubleValuesArray = new double[divCounts.Count];
		for (int i = 0; i < divCounts.Count; i++)
		{
			doubleValuesArray[i] = (doubleValues[i] = ((i >= 1 && i < divCounts.Count - 2) ? divCounts[i].fractionalCount : ((double)divCounts[i].count)));
		}
		while (ordinal >= 0)
		{
			for (int j = 1; j < divCounts.Count - 2; j++)
			{
				double computed = 0.5 * (doubleValues[j - 1] + doubleValues[j + 1]);
				double fractionalCountDivCountsTerm = Math.Abs(divCounts[j].fractionalCount / doubleValues[j] - 1.0);
				double fractionalCountDivCountsTerm2 = Math.Abs(divCounts[j].fractionalCount / computed - 1.0);
				if (fractionalCountDivCountsTerm2 < fractionalCountDivCountsTerm || fractionalCountDivCountsTerm2 < scaleFilter)
				{
					doubleValuesArray[j] = computed;
				}
				else
				{
					doubleValuesArray[j] = doubleValues[j];
				}
			}
			double[] doubleValues2 = doubleValuesArray;
			doubleValuesArray = doubleValues;
			doubleValues = doubleValues2;
			ordinal--;
		}
		for (int k = 1; k < divCounts.Count - 2; k++)
		{
			divCounts[k] = divCounts[k].ReplaceCount(Math.Max(2, (int)Math.Round(doubleValues[k])));
		}
	}

	public int[,] StitchQuadStrip(DynamicMesh mesh, List<int[,]> stripMappings, int axis, List<int> newFaces = null)
	{
		bool condition = true;
		bool mergeTriangles = true;
		int getLengthStripMappingsTerm = (1 + stripMappings[0].GetLength(1 - axis)) / 2;
		int i;
		for (i = 0; stripMappings[i].Length == 0 && i < stripMappings.Count - 1; i++)
		{
		}
		int[,] stripMapping2 = stripMappings[i];
		for (i++; i < stripMappings.Count; i++)
		{
			int[,] stripMapping3 = stripMappings[i];
			if (stripMapping3.Length == 0)
			{
				continue;
			}
			List<int> intList = new List<int>();
			List<int> intListList = new List<int>();
			switch (axis)
			{
			case 1:
			{
				for (int l = 0; l < stripMapping2.GetLength(axis); l += 2)
				{
					intList.Add(stripMapping2[stripMapping2.GetLength(1 - axis) - 1, l]);
				}
				for (int m = 0; m < stripMapping3.GetLength(axis); m += 2)
				{
					intListList.Add(stripMapping3[0, m]);
				}
				break;
			}
			case 0:
			{
				for (int j = 0; j < stripMapping2.GetLength(axis); j += 2)
				{
					intList.Add(stripMapping2[j, stripMapping2.GetLength(1 - axis) - 1]);
				}
				for (int k = 0; k < stripMapping3.GetLength(axis); k += 2)
				{
					intListList.Add(stripMapping3[k, 0]);
				}
				break;
			}
			default:
				throw new Exception("Bad value for axis");
			}
			getLengthStripMappingsTerm += (1 + stripMapping3.GetLength(1 - axis)) / 2;
			if (axis == 1)
			{
				if (condition)
				{
					mesh.StitchNodeStringsNearest(intList, intListList, newFaces, mergeTriangles);
				}
				else
				{
					mesh.StitchNodeStringsMixed(intList, intListList, newFaces);
				}
			}
			else if (condition)
			{
				mesh.StitchNodeStringsNearest(intListList, intList, newFaces, mergeTriangles);
			}
			else
			{
				mesh.StitchNodeStringsMixed(intListList, intList, newFaces);
			}
			stripMapping2 = stripMapping3;
		}
		int[,] intValues = ((axis != 0) ? new int[2 * getLengthStripMappingsTerm - 1, 3] : new int[3, 2 * getLengthStripMappingsTerm - 1]);
		int intervalIndex = 0;
		foreach (int[,] stripMapping in stripMappings)
		{
			if (stripMapping.Length == 0)
			{
				continue;
			}
			for (i = 0; i < stripMapping.GetLength(1 - axis); i += 2)
			{
				if (axis == 0)
				{
					intValues[0, intervalIndex] = stripMapping[0, i];
					intValues[2, intervalIndex] = stripMapping[stripMapping.GetLength(axis) - 1, i];
				}
				else
				{
					intValues[intervalIndex, 0] = stripMapping[i, 0];
					intValues[intervalIndex, 2] = stripMapping[i, stripMapping.GetLength(axis) - 1];
				}
				intervalIndex += 2;
			}
		}
		return intValues;
	}

	public ISizeFunction GetRegionSizeFunction(bool secondary, bool fromNodes)
	{
		result = new SlidingMesh();
		SetSides();
		transform = CreateTransform();
		QuadSizeFunction.InferSizeFunctions(cmesh, regionIdx, transform, out sizeFunctionNS, out sizeFunctionEW, fromNodes);
		if (secondary)
		{
			return sizeFunctionNS;
		}
		return sizeFunctionEW;
	}

	public ISizeFunction GetRegionSizeFunction(bool secondary)
	{
		return GetRegionSizeFunction(secondary, fromNodes: true);
	}
}
