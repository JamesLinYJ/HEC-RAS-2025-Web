using System;
using System.Collections.Generic;
using System.Linq;

namespace Geospatial.Vectors.MeshTopology;

public class QuadTransformBase : PatchCommon
{
	public enum NodeMode
	{
		SIMPLE_QUAD,
		LENGTH,
		SMOOTH,
		SMOOTH_ADAPTIVE
	}

	public QuadTransformBase(ConceptualMesh mesh, int cIdx)
		: base(mesh, cIdx)
	{
	}

	public Point Origin()
	{
		return Sides[0].Smooth[0];
	}

	public ConceptualMesh SidesToConceptualMesh(out ISizeFunction sizeFunction, List<PatchSide> sidesIn, out List<PatchSide> sidesOut, double trianglesPerCell = 1.0)
	{
		sidesOut = new List<PatchSide>();
		ConceptualMesh localMesh = new ConceptualMesh();
		int cIdx = localMesh.AddCell();
		double maxValue = double.MaxValue;
		for (int i = 0; i < 4; i++)
		{
			sidesIn[i].ApproximateLocalDiscretization(out var totalLength, out var totalCount);
			double totalLengthOverTotalCount = totalLength / (double)totalCount;
			if (totalLengthOverTotalCount < maxValue)
			{
				maxValue = totalLengthOverTotalCount;
			}
		}
		double maxValueOverTrianglesPerCell = maxValue / trianglesPerCell;
		List<int> intList = sidesIn.Select((PatchSide side) => localMesh.AddNode(side.Smooth.First())).ToList();
		for (int intervalIndex = 0; intervalIndex < sidesIn.Count; intervalIndex++)
		{
			int addFace = localMesh.AddFace(intList[intervalIndex], intList[(intervalIndex + 1) % 4]);
			localMesh.FaceRawLine[addFace] = sidesIn[intervalIndex].Smooth;
			localMesh.CellAddFace(cIdx, addFace, 0);
			PatchSide patchSide = new PatchSide(localMesh);
			patchSide.Smooth = localMesh.FaceRawLine[addFace];
			patchSide.SidePieces.Add(new HalfEdge(addFace, 0));
			patchSide.OriginalPieces = sidesIn[intervalIndex].OriginalPieces;
			sidesOut.Add(patchSide);
		}
		double sqrtScaledArea = Math.Sqrt(localMesh.CellSmoothPolygon(cIdx).Area / 50000.0);
		if (maxValueOverTrianglesPerCell < sqrtScaledArea)
		{
			maxValueOverTrianglesPerCell = sqrtScaledArea;
		}
		sizeFunction = new ConstantSizeFunction(maxValueOverTrianglesPerCell);
		for (int intervalIndex2 = 0; intervalIndex2 < sidesOut.Count; intervalIndex2++)
		{
			int ordinal = intervalIndex2;
			DiscretizeLine discretizeLine = new DiscretizeLine(maxValueOverTrianglesPerCell, upsampleOnly: true);
			localMesh.SetArcNodesFromSmooth(ordinal, discretizeLine);
			sidesOut[intervalIndex2].Nodes = localMesh.FaceNodeString[ordinal];
		}
		return localMesh;
	}

	public ISizeFunction AdaptiveSizeFunction(List<PatchSide> sides, double adaptiveCellsAcross = 5.0)
	{
		Polygon polygon = new Polygon(sides[0].Smooth.PointCount + sides[1].Smooth.PointCount + sides[2].Smooth.PointCount + sides[3].Smooth.PointCount - 4);
		polygon.TrackZ();
		polygon.TrackM();
		for (int i = 0; i < sides.Count; i++)
		{
			Polyline smooth = sides[i].Smooth;
			double lengthOverAdaptiveCellsAcross = smooth.Length / adaptiveCellsAcross;
			for (int j = 0; j < smooth.PointCount - 1; j++)
			{
				double lengthOverAdaptiveCellsAcross2 = lengthOverAdaptiveCellsAcross;
				int ordinal = 1 << i;
				if (j == 0)
				{
					int ordinal2 = (i + 3) % 4;
					ordinal |= 1 << ordinal2;
					lengthOverAdaptiveCellsAcross2 = Math.Min(lengthOverAdaptiveCellsAcross2, sides[ordinal2].Smooth.Length / adaptiveCellsAcross);
				}
				polygon.Add(smooth.Points[j], lengthOverAdaptiveCellsAcross2, ordinal);
			}
		}
		polygon.Reverse();
		DynamicTINFactory<NativePredicates> dynamicTINFactory = new DynamicTINFactory<NativePredicates>(new TriangulationParameters
		{
			EnforcedMBR = polygon.Extent.RatioAroundCenter(5.0)
		});
		dynamicTINFactory.AddPerimeter(polygon);
		dynamicTINFactory.ComputeTIN();
		int[] sideMembership = dynamicTINFactory.Tin.M.Select((double m) => (int)m).ToArray();
		Tin tin = dynamicTINFactory.Tin;
		List<TriangleDefinition> triangleDefinition = tin.TriangleDefinition;
		for (int intervalIndex = 0; intervalIndex < tin.TriangleCount; intervalIndex++)
		{
			if (triangleDefinition[intervalIndex].IsDeleted)
			{
				continue;
			}
			for (int intervalIndex2 = 0; intervalIndex2 < 3; intervalIndex2++)
			{
				int index = triangleDefinition[intervalIndex].AdjacentTriangleIdx(intervalIndex2);
				if (triangleDefinition[index].IsDeleted)
				{
					continue;
				}
				int point = triangleDefinition[intervalIndex].Point(intervalIndex2);
				int point2 = triangleDefinition[intervalIndex].Point((intervalIndex2 + 1) % 3);
				int point3 = triangleDefinition[intervalIndex].Point((intervalIndex2 + 2) % 3);
				if (OppositeSides(point, point2))
				{
					bool notIsDeleted = !triangleDefinition[triangleDefinition[intervalIndex].AdjacentTriangleIdx((intervalIndex2 + 1) % 3)].IsDeleted;
					bool notIsDeleted2 = !triangleDefinition[triangleDefinition[intervalIndex].AdjacentTriangleIdx((intervalIndex2 + 2) % 3)].IsDeleted;
					double distanceToPointsTerm;
					double distanceToPointSegmentTerm = (distanceToPointsTerm = tin.Points[point].DistanceTo(tin.Points[point2]));
					if (!notIsDeleted && OppositeSides(point, point3))
					{
						distanceToPointsTerm = new Segment(tin.Points[point2], tin.Points[point3]).DistanceToPoint(tin.Points[point]);
					}
					if (!notIsDeleted2 && OppositeSides(point2, point3))
					{
						distanceToPointSegmentTerm = new Segment(tin.Points[point3], tin.Points[point]).DistanceToPoint(tin.Points[point2]);
					}
					tin.Z[point] = Math.Min(tin.Z[point], distanceToPointsTerm / adaptiveCellsAcross);
					tin.Z[point2] = Math.Min(tin.Z[point2], distanceToPointSegmentTerm / adaptiveCellsAcross);
				}
			}
		}
		TinSizeFunction.NodeState[] nodeState = new TinSizeFunction.NodeState[0];
		return new TinSizeFunction(tin, 0.2, nodeState);
		bool OppositeSides(int n0, int n1)
		{
			int sideMembershipN0Term = sideMembership[n0] | sideMembership[n1];
			if (sideMembershipN0Term != 5)
			{
				return sideMembershipN0Term == 10;
			}
			return true;
		}
	}
}
