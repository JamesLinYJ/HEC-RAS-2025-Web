using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors.MeshTopology;

namespace Geospatial.Vectors;

public static class MeshMetrics
{
	public enum MetricType
	{
		Skew = 8,
		AreaRatio = 16,
		CenterSpacing = 24,
		Orthogonality = 32,
		Circumcenter = 40,
		Degree = 48,
		Aspect = 56,
		Custom = 160
	}

	public enum MetricElement
	{
		Cell = 1,
		Face,
		Node
	}

	public static float CellSkewness(SlidingMesh mesh, int cIdx)
	{
		List<HalfEdge> cellFace = mesh.CellFaces[cIdx];
		int count = cellFace.Count;
		double[] doubleValues = new double[count];
		for (int i = 0; i < count; i++)
		{
			doubleValues[i] = mesh.FaceSegment(cellFace[i]).Bearing;
		}
		return BearingsToSkewness(doubleValues);
	}

	public static float CellSkewness(Polygon poly)
	{
		int count = poly.Count;
		double[] doubleValues = new double[count];
		for (int i = 0; i < count; i++)
		{
			doubleValues[i] = poly.Segments[i].Bearing;
		}
		return BearingsToSkewness(doubleValues);
	}

	public static float CellSkewness(Mesh mesh, int cIdx)
	{
		Point[] cellCornerPoints = mesh.CellCornerPoints(cIdx);
		double[] doubleValues = new double[cellCornerPoints.Length - 1];
		for (int i = 0; i < doubleValues.Count(); i++)
		{
			doubleValues[i] = (cellCornerPoints[i + 1] - cellCornerPoints[i]).MathBearing;
		}
		return BearingsToSkewness(doubleValues);
	}

	public static float BearingsToSkewness(double[] bearings)
	{
		int bearingLength = bearings.Length;
		double scaledPi = Math.PI * 2.0;
		double computed = 0.0;
		for (int i = 0; i < bearingLength; i++)
		{
			double piSum = bearings[(i + 1) % bearingLength] - bearings[i];
			if (piSum < -Math.PI)
			{
				piSum += Math.PI * 2.0;
			}
			if (piSum > Math.PI)
			{
				piSum -= Math.PI * 2.0;
			}
			double piAbovePiSum = Math.PI - piSum;
			scaledPi = Math.Min(scaledPi, piAbovePiSum);
			computed = Math.Max(computed, piAbovePiSum);
		}
		double piBearingLengthTerm = Math.PI - Math.PI * 2.0 / (double)bearingLength;
		return (float)Math.Max((computed - piBearingLengthTerm) / (Math.PI - piBearingLengthTerm), (piBearingLengthTerm - scaledPi) / piBearingLengthTerm);
	}

	private static double RegularArea(int nSides)
	{
		double[] coefficients = new double[2] { 0.4330127, 1.0 };
		return coefficients[Math.Min(coefficients.Length - 1, nSides - 3)];
	}

	public static float FaceAreaRatio(SlidingMesh mesh, int fIdx, bool normalize = true)
	{
		Duple<int> duple = mesh.FaceCells[fIdx];
		if (duple.A < 0 || duple.B < 0)
		{
			return 0f;
		}
		double cellSimpleArea = mesh.CellSimpleArea(duple.A);
		double cellSimpleArea2 = mesh.CellSimpleArea(duple.B);
		if (normalize)
		{
			cellSimpleArea /= RegularArea(mesh.CellFaces[duple.A].Count);
			cellSimpleArea2 /= RegularArea(mesh.CellFaces[duple.B].Count);
		}
		return (float)(Math.Max(cellSimpleArea, cellSimpleArea2) / Math.Min(cellSimpleArea, cellSimpleArea2)) - 1f;
	}

	public static float FaceAreaRatioNormalized(Mesh mesh, int fIdx)
	{
		Face face = mesh.Faces[fIdx];
		if (face.cellA < 0 || face.cellB < 0)
		{
			return 0f;
		}
		double cellSimpleArea = mesh.CellSimpleArea(face.cellA);
		double cellSimpleArea2 = mesh.CellSimpleArea(face.cellB);
		cellSimpleArea /= RegularArea(mesh.Cells[face.cellA].Faces.Length);
		cellSimpleArea2 /= RegularArea(mesh.Cells[face.cellB].Faces.Length);
		return (float)(Math.Max(cellSimpleArea, cellSimpleArea2) / Math.Min(cellSimpleArea, cellSimpleArea2)) - 1f;
	}

	public static float CellAreaRatio(SlidingMesh mesh, int cIdx, bool normalize = true)
	{
		double cellSimpleArea = mesh.CellSimpleArea(cIdx);
		if (normalize)
		{
			cellSimpleArea /= RegularArea(mesh.CellFaces[cIdx].Count);
		}
		double computed = 1.0;
		foreach (HalfEdge cellFace in mesh.CellFaces[cIdx])
		{
			int halfEdgeToOppositeCell = mesh.HalfEdgeToOppositeCell(cellFace);
			if (halfEdgeToOppositeCell >= 0)
			{
				double cellSimpleArea2 = mesh.CellSimpleArea(halfEdgeToOppositeCell);
				if (normalize)
				{
					cellSimpleArea2 /= RegularArea(mesh.CellFaces[halfEdgeToOppositeCell].Count);
				}
				double cellSimpleArea2OverCellSimpleArea = ((!(cellSimpleArea2 < cellSimpleArea)) ? (cellSimpleArea2 / cellSimpleArea) : (cellSimpleArea / cellSimpleArea2));
				computed = Math.Max(computed, cellSimpleArea2OverCellSimpleArea);
			}
		}
		return (float)computed - 1f;
	}

	public static float CellAreaRatio(Mesh mesh, int cIdx, bool normalize = true)
	{
		if (normalize)
		{
			return CellAreaRatioNormalized(mesh, cIdx);
		}
		return CellAreaRatioUnnormalized(mesh, cIdx);
	}

	public static float CellAreaRatioUnnormalized(Mesh mesh, int cIdx)
	{
		Cell cell = mesh.Cells[cIdx];
		if (cell.Faces.Length < 3)
		{
			return 1f;
		}
		double cellSimpleArea = mesh.CellSimpleArea(cIdx);
		double computed = 1.0;
		foreach (int cellsAroundCell in mesh.GetCellsAroundCell(cIdx))
		{
			double cellSimpleArea2 = mesh.CellSimpleArea(cellsAroundCell);
			if (cellSimpleArea2 != 0.0)
			{
				double cellSimpleArea2OverCellSimpleArea = ((!(cellSimpleArea2 < cellSimpleArea)) ? (cellSimpleArea2 / cellSimpleArea) : (cellSimpleArea / cellSimpleArea2));
				computed = Math.Max(computed, cellSimpleArea2OverCellSimpleArea);
			}
		}
		return (float)computed - 1f;
	}

	public static float CellAreaRatioNormalized(Mesh mesh, int cIdx)
	{
		Cell cell = mesh.Cells[cIdx];
		if (cell.Faces.Length < 3)
		{
			return 1f;
		}
		double cellSimpleArea = mesh.CellSimpleArea(cIdx);
		cellSimpleArea /= RegularArea(cell.Faces.Length);
		double computed = 1.0;
		foreach (int cellsAroundCell in mesh.GetCellsAroundCell(cIdx))
		{
			double cellSimpleArea2 = mesh.CellSimpleArea(cellsAroundCell);
			if (cellSimpleArea2 != 0.0)
			{
				cellSimpleArea2 /= RegularArea(mesh.Cells[cellsAroundCell].Faces.Length);
				double cellSimpleArea2OverCellSimpleArea = ((!(cellSimpleArea2 < cellSimpleArea)) ? (cellSimpleArea2 / cellSimpleArea) : (cellSimpleArea / cellSimpleArea2));
				computed = Math.Max(computed, cellSimpleArea2OverCellSimpleArea);
			}
		}
		return (float)computed - 1f;
	}

	public static double RegularSpacing(DynamicMesh mesh, int cIdx)
	{
		int count = mesh.CellFaces[cIdx].Count;
		double sqrtCellSimpleAreaOverRegularArea = Math.Sqrt(mesh.CellSimpleArea(cIdx) / RegularArea(count));
		double piCountTerm = Math.PI - Math.PI * 2.0 / (double)count;
		return 0.5 * sqrtCellSimpleAreaOverRegularArea * Math.Tan(piCountTerm / 2.0);
	}

	public static double RegularSpacing(Mesh mesh, int cIdx)
	{
		int length = mesh.Cells[cIdx].Faces.Length;
		double sqrtCellSimpleAreaOverRegularArea = Math.Sqrt(mesh.CellSimpleArea(cIdx) / RegularArea(length));
		double piLengthTerm = Math.PI - Math.PI * 2.0 / (double)length;
		return 0.5 * sqrtCellSimpleAreaOverRegularArea * Math.Tan(piLengthTerm / 2.0);
	}

	public static float CellNumSides(Mesh mesh, int cIdx)
	{
		return mesh.Cells[cIdx].Faces.Length;
	}

	public static float CellDegree(Mesh mesh, int cIdx)
	{
		int facesFacePointsTerm = 0;
		_ = ref mesh.Cells[cIdx];
		foreach (int cellFacePoint in mesh.CellFacePoints(cIdx))
		{
			facesFacePointsTerm = Math.Max(facesFacePointsTerm, mesh.FacePoints[cellFacePoint].Faces.Count);
		}
		return facesFacePointsTerm;
	}

	public static float NodeDegree(Mesh mesh, int nIdx)
	{
		return mesh.FacePoints[nIdx].Faces.Count;
	}

	public static float NodeAspectRatio(Mesh mesh, int nIdx)
	{
		double maxValue = double.MaxValue;
		double faceSimpleLengthSquaredTerm = 0.0;
		foreach (int face in mesh.FacePoints[nIdx].Faces)
		{
			double faceSimpleLengthSquared = mesh.FaceSimpleLengthSquared(face);
			maxValue = Math.Min(maxValue, faceSimpleLengthSquared);
			faceSimpleLengthSquaredTerm = Math.Max(faceSimpleLengthSquaredTerm, faceSimpleLengthSquared);
		}
		if (faceSimpleLengthSquaredTerm == 0.0)
		{
			return 0f;
		}
		return MathF.Sqrt((float)(faceSimpleLengthSquaredTerm / maxValue)) - 1f;
	}

	public static float NodeAspectRatio(SlidingMesh mesh, int nIdx)
	{
		double maxValue = double.MaxValue;
		double distanceSquaredToTerm = 0.0;
		Point point = mesh.NodePoints[nIdx];
		foreach (HalfEdge nodeToHalfEdge in mesh.NodeToHalfEdges(nIdx))
		{
			double distanceSquaredTo = point.DistanceSquaredTo(mesh.NodePoints[mesh.HalfEdgeNodeForward(nodeToHalfEdge)]);
			maxValue = Math.Min(maxValue, distanceSquaredTo);
			distanceSquaredToTerm = Math.Max(distanceSquaredToTerm, distanceSquaredTo);
		}
		if (distanceSquaredToTerm == 0.0)
		{
			return 0f;
		}
		return MathF.Sqrt((float)(distanceSquaredToTerm / maxValue)) - 1f;
	}

	public static float NodeFaceLengthError(SlidingMesh mesh, int nIdx)
	{
		float computed = 0f;
		int ordinal = 0;
		foreach (HalfEdge nodeToHalfEdge in mesh.NodeToHalfEdges(nIdx))
		{
			float faceTargetLength = mesh.FaceTargetLength[nodeToHalfEdge.fIdx];
			if (!double.IsNaN(faceTargetLength) && !(faceTargetLength <= 0f) && !((double)faceTargetLength > 100000000.0))
			{
				double faceLengthOverFaceTargetLength = mesh.FaceLength(nodeToHalfEdge.fIdx) / (double)faceTargetLength;
				faceLengthOverFaceTargetLength = ((!(faceLengthOverFaceTargetLength < 1.0)) ? (faceLengthOverFaceTargetLength - 1.0) : (1.0 / faceLengthOverFaceTargetLength - 1.0));
				computed += (float)(faceLengthOverFaceTargetLength * faceLengthOverFaceTargetLength);
				ordinal++;
			}
		}
		if (ordinal > 0)
		{
			return computed / (float)ordinal;
		}
		return 0f;
	}

	public static void FaceCenterMetrics(SlidingMesh mesh, int fIdx, out double perpendicularDistance, out double angleErrorDegrees)
	{
		Duple<int> duple = mesh.FaceCells[fIdx];
		if (duple.A < 0 || duple.B < 0)
		{
			perpendicularDistance = double.NaN;
			angleErrorDegrees = double.NaN;
			return;
		}
		Point point = mesh.CellCircumcenter(duple.A, allPoints: true);
		Point point2 = mesh.CellCircumcenter(duple.B, allPoints: true);
		Vector other = (mesh.NodePoints[mesh.FaceNodes[fIdx].B] - mesh.NodePoints[mesh.FaceNodes[fIdx].A]).UnitVector().Perpendicular(clockwise: true);
		Vector vec = point2 - point;
		double regularSpacingMeshTerm = RegularSpacing(mesh, duple.A) + RegularSpacing(mesh, duple.B);
		perpendicularDistance = vec.Dot(other) / regularSpacingMeshTerm;
		angleErrorDegrees = Math.Abs(other.AngleBetweenRads(in vec)) * 180.0 / Math.PI;
	}

	public static double FaceAngleErrorDegrees(SlidingMesh mesh, int fIdx)
	{
		FaceCenterMetrics(mesh, fIdx, out var _, out var angleErrorDegrees);
		return angleErrorDegrees;
	}

	public static double FaceCenterSpacing(SlidingMesh mesh, int fIdx)
	{
		FaceCenterMetrics(mesh, fIdx, out var perpendicularDistance, out var _);
		return perpendicularDistance;
	}

	public static float CellCenterSpacing(SlidingMesh mesh, int cIdx)
	{
		Point point = mesh.CellCircumcenter(cIdx, allPoints: true);
		double regularSpacing = RegularSpacing(mesh, cIdx);
		double maxValue = double.MaxValue;
		foreach (HalfEdge cellFace in mesh.CellFaces[cIdx])
		{
			Vector other = mesh.FaceSegment(cellFace).AsVector().UnitVector()
				.Perpendicular(clockwise: true);
			maxValue = Math.Min(maxValue, (mesh.NodePoints[mesh.FaceNodes[cellFace.fIdx].A] - point).Dot(other));
		}
		return (float)(maxValue / regularSpacing);
	}

	public static float CellCenterSpacing(Mesh mesh, int cIdx)
	{
		if (mesh.Cells[cIdx].Faces.Length < 3)
		{
			return 1f;
		}
		Point point = mesh.CellCenters[cIdx];
		double regularSpacing = RegularSpacing(mesh, cIdx);
		double maxValue = double.MaxValue;
		Point[] cellCornerPoints = mesh.CellCornerPoints(cIdx);
		for (int i = 0; i < cellCornerPoints.Length - 1; i++)
		{
			Segment segment = new Segment(cellCornerPoints[i], cellCornerPoints[i + 1]);
			Vector other = segment.AsVector().UnitVector().Perpendicular(clockwise: true);
			maxValue = Math.Min(maxValue, (segment.Midpoint() - point).Dot(other));
		}
		return (float)(maxValue / regularSpacing);
	}

	public static float FaceCenterCenterSpacingSquaredRatio(Mesh mesh, int fIdx)
	{
		if (mesh.Faces[fIdx].cellA < 0 || mesh.Faces[fIdx].cellB < 0)
		{
			return 1f;
		}
		Point point = mesh.CellCenters[mesh.Faces[fIdx].cellA];
		Point point2 = mesh.CellCenters[mesh.Faces[fIdx].cellB];
		Point other = mesh.FaceMidPoint(fIdx);
		double distanceSquaredTo = point.DistanceSquaredTo(other);
		double distanceSquaredTo2 = point2.DistanceSquaredTo(other);
		if (distanceSquaredTo > distanceSquaredTo2)
		{
			return (float)(distanceSquaredTo / distanceSquaredTo2);
		}
		return (float)(distanceSquaredTo2 / distanceSquaredTo);
	}

	public static float FaceCenterCenterSpacing(Mesh mesh, int fIdx)
	{
		return (float)Math.Sqrt(FaceCenterCenterSpacingSquaredRatio(mesh, fIdx)) - 1f;
	}

	public static float CellCenterCenterSpacing(Mesh mesh, int cIdx)
	{
		if (mesh.Cells[cIdx].Faces.Length < 3)
		{
			return 0f;
		}
		float faceCenterCenterSpacingSquaredRatioMeshTerm = 1f;
		Span<int> faces = mesh.Cells[cIdx].Faces.Span;
		for (int i = 0; i < faces.Length; i++)
		{
			faceCenterCenterSpacingSquaredRatioMeshTerm = Math.Max(faceCenterCenterSpacingSquaredRatioMeshTerm, FaceCenterCenterSpacingSquaredRatio(mesh, faces[i]));
		}
		return (float)Math.Sqrt(faceCenterCenterSpacingSquaredRatioMeshTerm) - 1f;
	}

	public static int FaceNodeDegree(SlidingMesh mesh, int fIdx)
	{
		return mesh.NodeDegree(mesh.FaceNodes[fIdx].A) + mesh.NodeDegree(mesh.FaceNodes[fIdx].B);
	}

	public static float CellOrthogonality(Mesh mesh, int cIdx)
	{
		Cell cell = mesh.Cells[cIdx];
		double absoluteDotTerm = 0.0;
		Span<int> faces = cell.Faces.Span;
		for (int i = 0; i < faces.Length; i++)
		{
			int faceIndex = faces[i];
			Face face = mesh.Faces[faceIndex];
			if (face.cellB >= 0)
			{
				Segment segment = mesh.CellToCellSegment(face.cellA, face.cellB);
				double absoluteDot = Math.Abs(mesh.FaceSegment(faceIndex).UnitVector().Dot(segment.UnitVector()));
				absoluteDotTerm = Math.Max(absoluteDotTerm, absoluteDot);
			}
		}
		if (!(absoluteDotTerm >= 1.0))
		{
			return (float)(Math.Asin(absoluteDotTerm) * 180.0 / Math.PI);
		}
		return 90f;
	}

	public static float FaceOrthogonality(Mesh mesh, int fIdx)
	{
		Face face = mesh.Faces[fIdx];
		if (face.cellA < 0 || face.cellB < 0)
		{
			return 0f;
		}
		Segment segment = mesh.CellToCellSegment(face.cellA, face.cellB);
		double absoluteDot = Math.Abs(mesh.FaceSegment(fIdx).UnitVector().Dot(segment.UnitVector()));
		if (!(absoluteDot >= 1.0))
		{
			return (float)(Math.Asin(absoluteDot) * 180.0 / Math.PI);
		}
		return 90f;
	}

	public static float CellCircumcenter(Mesh mesh, int cIdx)
	{
		Point point = mesh.CellCenters[cIdx];
		double crossProductUnitVectorTerm = 1.0;
		Point[] cellCornerPoints = mesh.CellCornerPoints(cIdx);
		for (int i = 0; i < cellCornerPoints.Length - 1; i++)
		{
			Segment segment = new Segment(cellCornerPoints[i], cellCornerPoints[i + 1]);
			crossProductUnitVectorTerm = Math.Min(crossProductUnitVectorTerm, (segment.Midpoint() - point).UnitVector().CrossProduct(segment.UnitVector()));
		}
		if (crossProductUnitVectorTerm < -1.0)
		{
			return 180f;
		}
		return (float)(180.0 / Math.PI * Math.Acos(crossProductUnitVectorTerm));
	}

	public static float CellFaceLength(SlidingMesh mesh, int cIdx, float target)
	{
		double computed = 0.0;
		foreach (HalfEdge cellFace in mesh.CellFaces[cIdx])
		{
			double faceLength = mesh.FaceLength(cellFace.fIdx);
			computed += Math.Pow(faceLength / (double)target - 1.0, 2.0) + Math.Pow((double)target / faceLength - 1.0, 2.0);
		}
		return (float)computed / (float)mesh.CellNumSides(cIdx);
	}

	public static float CellFaceLength(Polygon poly, float target)
	{
		double computed = 0.0;
		foreach (Segment segment in poly.Segments)
		{
			double length = segment.Length;
			computed += Math.Pow(length / (double)target - 1.0, 2.0) + Math.Pow((double)target / length - 1.0, 2.0);
		}
		return (float)computed / (float)poly.SegmentCount;
	}

	public static Func<Mesh, int, float> GetFunc(MetricElement elementType, MetricType metricType)
	{
		switch (elementType)
		{
		case MetricElement.Cell:
			switch (metricType)
			{
			case MetricType.Skew:
				return CellSkewness;
			case MetricType.AreaRatio:
				return CellAreaRatioNormalized;
			case MetricType.CenterSpacing:
				return CellCenterCenterSpacing;
			case MetricType.Orthogonality:
				return CellOrthogonality;
			case MetricType.Circumcenter:
				return CellCircumcenter;
			case MetricType.Degree:
				return CellDegree;
			}
			break;
		case MetricElement.Face:
			switch (metricType)
			{
			case MetricType.AreaRatio:
				return FaceAreaRatioNormalized;
			case MetricType.CenterSpacing:
				return FaceCenterCenterSpacing;
			case MetricType.Orthogonality:
				return FaceOrthogonality;
			}
			break;
		case MetricElement.Node:
			switch (metricType)
			{
			case MetricType.Degree:
				return NodeDegree;
			case MetricType.Aspect:
				return NodeAspectRatio;
			}
			break;
		}
		throw new ArgumentOutOfRangeException($"Undefined mesh quality metric {metricType} at {elementType}s");
	}

	public static Func<Mesh, int, float> GetCustomFunc(MetricElement elementType, float norm, List<(MetricType, float)> metrics)
	{
		return metrics.Count switch
		{
			0 => throw new ArgumentOutOfRangeException("Custom metric must have at least one term"), 
			1 => GetCustomFunc(elementType, norm, metrics[0]), 
			2 => GetCustomFunc(elementType, norm, metrics[0], metrics[1]), 
			3 => GetCustomFunc(elementType, norm, metrics[0], metrics[1], metrics[2]), 
			4 => GetCustomFunc(elementType, norm, metrics[0], metrics[1], metrics[2], metrics[3]), 
			_ => throw new ArgumentOutOfRangeException("Custom metric cannot have more than 4 terms"), 
		};
	}

	private static Func<Mesh, int, float> GetCustomFunc(MetricElement elementType, float norm, (MetricType, float) metric0)
	{
		Func<Mesh, int, float> func0 = GetFunc(elementType, metric0.Item1);
		return ComputeCustom;
		float ComputeCustom(Mesh mesh, int idx)
		{
			return func0(mesh, idx) / metric0.Item2;
		}
	}

	private static Func<Mesh, int, float> GetCustomFunc(MetricElement elementType, float norm, (MetricType, float) metric0, (MetricType, float) metric1)
	{
		Func<Mesh, int, float> func0 = GetFunc(elementType, metric0.Item1);
		Func<Mesh, int, float> func1 = GetFunc(elementType, metric1.Item1);
		if (float.IsFinite(norm))
		{
			return ComputeCustomNorm;
		}
		return ComputeCustomMax;
		float ComputeCustomMax(Mesh mesh, int idx)
		{
			return Math.Max(func0(mesh, idx) / metric0.Item2, func1(mesh, idx) / metric1.Item2);
		}
		float ComputeCustomNorm(Mesh mesh, int idx)
		{
			return (float)Math.Pow(Math.Pow(func0(mesh, idx) / metric0.Item2, norm) + Math.Pow(func1(mesh, idx) / metric1.Item2, norm), 1.0 / (double)norm);
		}
	}

	private static Func<Mesh, int, float> GetCustomFunc(MetricElement elementType, float norm, (MetricType, float) metric0, (MetricType, float) metric1, (MetricType, float) metric2)
	{
		Func<Mesh, int, float> func0 = GetFunc(elementType, metric0.Item1);
		Func<Mesh, int, float> func1 = GetFunc(elementType, metric1.Item1);
		Func<Mesh, int, float> func2 = GetFunc(elementType, metric2.Item1);
		if (float.IsFinite(norm))
		{
			return ComputeCustomNorm;
		}
		return ComputeCustomMax;
		float ComputeCustomMax(Mesh mesh, int idx)
		{
			return Math.Max(Math.Max(func0(mesh, idx) / metric0.Item2, func1(mesh, idx) / metric1.Item2), func2(mesh, idx) / metric2.Item2);
		}
		float ComputeCustomNorm(Mesh mesh, int idx)
		{
			return (float)Math.Pow(Math.Pow(func0(mesh, idx) / metric0.Item2, norm) + Math.Pow(func1(mesh, idx) / metric1.Item2, norm) + Math.Pow(func2(mesh, idx) / metric2.Item2, norm), 1.0 / (double)norm);
		}
	}

	private static Func<Mesh, int, float> GetCustomFunc(MetricElement elementType, float norm, (MetricType, float) metric0, (MetricType, float) metric1, (MetricType, float) metric2, (MetricType, float) metric3)
	{
		Func<Mesh, int, float> func0 = GetFunc(elementType, metric0.Item1);
		Func<Mesh, int, float> func1 = GetFunc(elementType, metric1.Item1);
		Func<Mesh, int, float> func2 = GetFunc(elementType, metric2.Item1);
		Func<Mesh, int, float> func3 = GetFunc(elementType, metric3.Item1);
		if (float.IsFinite(norm))
		{
			return ComputeCustomNorm;
		}
		return ComputeCustomMax;
		float ComputeCustomMax(Mesh mesh, int idx)
		{
			return Math.Max(Math.Max(Math.Max(func0(mesh, idx) / metric0.Item2, func1(mesh, idx) / metric1.Item2), func2(mesh, idx) / metric2.Item2), func3(mesh, idx) / metric3.Item2);
		}
		float ComputeCustomNorm(Mesh mesh, int idx)
		{
			return (float)Math.Pow(Math.Pow(func0(mesh, idx) / metric0.Item2, norm) + Math.Pow(func1(mesh, idx) / metric1.Item2, norm) + Math.Pow(func2(mesh, idx) / metric2.Item2, norm) + Math.Pow(func3(mesh, idx) / metric3.Item2, norm), 1.0 / (double)norm);
		}
	}
}
