using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Geospatial.Transformers;
using Geospatial.Vectors.Topology;

namespace Geospatial.Vectors.MeshTopology;

public class PatchCommon
{
	public enum TransformMethod
	{
		COONS,
		LAPLACIAN
	}

	public TransformMethod transformMethod;

	public static TransformMethod defaultTransformMethod = TransformMethod.LAPLACIAN;

	public ISpatialTransformer transform;

	public SlidingMesh result;

	protected List<PatchSide> _sides;

	protected ConceptualMesh cmesh;

	protected int regionIdx;

	public List<PatchSide> Sides
	{
		get
		{
			return _sides;
		}
		private set
		{
			_sides = value;
		}
	}

	public Polyline fSouth => Sides[0].Smooth;

	public Polyline fEast => Sides[1].Smooth;

	public Polyline fNorth => Sides[2].Smooth;

	public Polyline fWest => Sides[3].Smooth;

	public Polyline fSouthNodes => Sides[0].Nodes;

	public Polyline fEastNodes => Sides[1].Nodes;

	public Polyline fNorthNodes => Sides[2].Nodes;

	public Polyline fWestNodes => Sides[3].Nodes;

	public PatchCommon(ConceptualMesh cmesh, int regionIdx)
	{
		this.cmesh = cmesh;
		this.regionIdx = regionIdx;
		transformMethod = defaultTransformMethod;
	}

	public void SetSides()
	{
		VirtualConstraints constraints = ((result != null) ? new VirtualConstraints() : null);
		_sides = cmesh.PatchSides(regionIdx, constraints);
		if (result != null)
		{
			result.constraints = constraints;
		}
	}

	public static void RotateSides(ConceptualMesh cmesh, int cIdx, ref List<PatchSide> sides, Span<int> corners)
	{
		if (sides[0].Smooth.Length + sides[2].Smooth.Length < sides[1].Smooth.Length + sides[3].Smooth.Length)
		{
			PatchSide.RotateSidesOnce(sides, corners);
		}
	}

	public bool SideBCIsDirichlet(PatchSide side)
	{
		foreach (HalfEdge sidePiece in side.SidePieces)
		{
			if (cmesh.FaceDiscretization[sidePiece.fIdx] == null || cmesh.FaceDiscretization[sidePiece.fIdx].LaplaceBCMode != LaplaceBCOptions.Dirichlet)
			{
				return false;
			}
		}
		return true;
	}

	protected ConstraintState UVToConstraint(List<PatchSide> sides, double u, double v, bool IsNode, out Point point)
	{
		double tolerance = 1E-10;
		ConstraintState constraintState;
		Point point2;
		if (u < tolerance)
		{
			constraintState = sides[3].GetConstraint(1.0 - v, IsNode, out point2);
		}
		else if (u > 1.0 - tolerance)
		{
			constraintState = sides[1].GetConstraint(v, IsNode, out point2);
		}
		else
		{
			point2 = Point.NullPoint;
			constraintState = ConstraintState.FREE;
		}
		ConstraintState constraintState2;
		Point point3;
		if (v < tolerance)
		{
			constraintState2 = sides[0].GetConstraint(u, IsNode, out point3);
		}
		else if (v > 1.0 - tolerance)
		{
			constraintState2 = sides[2].GetConstraint(1.0 - u, IsNode, out point3);
		}
		else
		{
			point3 = Point.NullPoint;
			constraintState2 = ConstraintState.FREE;
		}
		if (constraintState == ConstraintState.FIXED)
		{
			point = point2;
			return constraintState;
		}
		if (constraintState2 == ConstraintState.FIXED)
		{
			point = point3;
			return constraintState2;
		}
		if (constraintState >= (ConstraintState)0 && constraintState2 == ConstraintState.FREE)
		{
			point = point2;
			return constraintState;
		}
		if (constraintState2 >= (ConstraintState)0 && constraintState == ConstraintState.FREE)
		{
			point = point3;
			return constraintState2;
		}
		if (constraintState2 == ConstraintState.FREE && constraintState == ConstraintState.FREE)
		{
			point = Point.NullPoint;
			return constraintState2;
		}
		throw new DynamicMeshException("Quad constraints are corrupted");
	}

	protected ConstraintState UVToConstraint(List<PatchSide> sides, double u, double v, bool IsNode)
	{
		Point point;
		return UVToConstraint(sides, u, v, IsNode, out point);
	}

	public void SetConstraints(SlidingMesh result, List<PatchSide> sides, List<Point> unitCoords)
	{
		for (int i = 0; i < result.NodeCount; i++)
		{
			result.NodeConstraints[i] = UVToConstraint(sides, unitCoords[i].X, unitCoords[i].Y, IsNode: true, out var point);
			if (!point.IsNull())
			{
				result.NodePoints[i] = point;
			}
		}
		result.UpdatePointOnPolylineMANY();
		List<bool> boolList = sides.Select((PatchSide side) => side.Nodes.Count == 1).ToList();
		for (int intervalIndex = 0; intervalIndex < result.FaceCount; intervalIndex++)
		{
			int faceCellsTerm = ((result.FaceCells[intervalIndex][1] >= 0) ? ((result.FaceCells[intervalIndex][0] < 0) ? 1 : (-1)) : 0);
			Duple<int> duple = result.FaceNodes[intervalIndex];
			Point point2 = unitCoords[duple[0]];
			Point point3 = unitCoords[duple[1]];
			if (faceCellsTerm >= 0)
			{
				for (int intervalIndex2 = 0; intervalIndex2 < 2; intervalIndex2++)
				{
					Point point4 = ((intervalIndex2 != 0) ? point3 : point2);
					if (result.NodeConstraints[duple[intervalIndex2]] == ConstraintState.FIXED)
					{
						if (boolList[0] && point4.Y == 0.0)
						{
							if (intervalIndex2 == faceCellsTerm)
							{
								point4.X = 1.0;
							}
							else
							{
								point4.X = 0.0;
							}
						}
						else if (boolList[2] && point4.Y == 1.0)
						{
							if (intervalIndex2 == faceCellsTerm)
							{
								point4.X = 0.0;
							}
							else
							{
								point4.X = 1.0;
							}
						}
						if (boolList[1] && point4.X == 1.0)
						{
							if (intervalIndex2 == faceCellsTerm)
							{
								point4.Y = 1.0;
							}
							else
							{
								point4.Y = 0.0;
							}
						}
						else if (boolList[3] && point4.X == 0.0)
						{
							if (intervalIndex2 == faceCellsTerm)
							{
								point4.Y = 0.0;
							}
							else
							{
								point4.Y = 1.0;
							}
						}
					}
					if (intervalIndex2 == 0)
					{
						point2 = point4;
					}
					else
					{
						point3 = point4;
					}
				}
			}
			Point point5 = 0.5 * (point2 + point3);
			result.FaceConstraints[intervalIndex] = UVToConstraint(sides, point5.X, point5.Y, IsNode: false);
		}
	}

	public void SetTargetLengths(SlidingMesh result, List<Point> unitNodePoints, ISpatialTransformer localTransform, List<PatchSide> sides)
	{
		PrecomputeContourLength(localTransform, sides, out var XContourLength, out var YContourLength);
		for (int i = 0; i < result.FaceCount; i++)
		{
			Point unitA = unitNodePoints[result.FaceNodes[i][0]];
			Point unitB = unitNodePoints[result.FaceNodes[i][1]];
			SetTargetLengthLocalPatchSize(result, i, unitA, unitB, XContourLength, YContourLength);
		}
	}

	public void SetTargetLengthLocalPatchSize(SlidingMesh mesh, int fIdx, Point unitA, Point unitB, double[] XContourLength, double[] YContourLength)
	{
		Point point = 0.5 * (unitA + unitB);
		double contourLength = GetContourLength(point.Y, YContourLength);
		double contourLength2 = GetContourLength(point.X, XContourLength);
		double contourLengthUnitATerm = contourLength * Math.Abs(unitA.X - unitB.X);
		double contourLength2UnitATerm = contourLength2 * Math.Abs(unitA.Y - unitB.Y);
		mesh.FaceTargetLength[fIdx] = (float)Math.Sqrt(contourLengthUnitATerm * contourLengthUnitATerm + contourLength2UnitATerm * contourLength2UnitATerm);
	}

	private void PrecomputeContourLength(ISpatialTransformer localTransform, List<PatchSide> sides, out double[] XContourLength, out double[] YContourLength)
	{
		int count = (int)(0.5 * (double)(fSouthNodes.Count + fNorthNodes.Count));
		int count2 = (int)(0.5 * (double)(fEastNodes.Count + fWestNodes.Count));
		XContourLength = new double[count];
		YContourLength = new double[count2];
		for (int i = 0; i < count; i++)
		{
			XContourLength[i] = ContourLength(localTransform, sides, (double)i / ((double)count - 1.0));
		}
		for (int j = 0; j < count2; j++)
		{
			YContourLength[j] = ContourLength(localTransform, sides, -1.0, (double)j / ((double)count2 - 1.0));
		}
	}

	private double GetContourLength(double coord, double[] contourLength)
	{
		double coordContourLengthTerm = coord * ((double)contourLength.Length - 1.0);
		int ordinal = (int)coordContourLengthTerm;
		int contourLengthTerm = Math.Min(ordinal + 1, contourLength.Length - 1);
		double computed = coordContourLengthTerm - (double)ordinal;
		return (1.0 - computed) * contourLength[ordinal] + computed * contourLength[contourLengthTerm];
	}

	private double ContourLength(ISpatialTransformer localTransform, List<PatchSide> sides, double x = -1.0, double y = -1.0)
	{
		Point[] points;
		int count;
		if (x >= 0.0)
		{
			count = (int)(0.5 * (double)(sides[1].Nodes.Count + sides[3].Nodes.Count));
			count = Math.Max(count, 2);
			points = new Point[count];
			for (int i = 0; i < count; i++)
			{
				double y2 = (double)i / ((double)count - 1.0);
				points[i] = new Point(x, y2);
			}
		}
		else
		{
			if (!(y >= 0.0))
			{
				throw new Exception("ContourLength requires x or y");
			}
			count = (int)(0.5 * (double)(sides[0].Nodes.Count + sides[2].Nodes.Count));
			count = Math.Max(count, 2);
			points = new Point[count];
			for (int j = 0; j < count; j++)
			{
				double x2 = (double)j / ((double)count - 1.0);
				points[j] = new Point(x2, y);
			}
		}
		Point[] pointsArray = new Point[count];
		localTransform.Transform(points, pointsArray);
		double distanceToSum = 0.0;
		for (int k = 0; k < count - 1; k++)
		{
			distanceToSum += pointsArray[k].DistanceTo(pointsArray[k + 1]);
		}
		return distanceToSum;
	}

	public void SetTargetLength(SlidingMesh mesh, int fIdx, Point unitA, Point unitB)
	{
		Point locUnit = 0.5 * (unitA + unitB);
		Vector vector = (unitB - unitA).UnitVector();
		double absoluteDeltaX = Math.Abs(vector.DeltaX);
		double absoluteDeltaY = Math.Abs(vector.DeltaY);
		mesh.FaceTargetLength[fIdx] = (float)(absoluteDeltaX * (double)TargetXLengthAtUnitCoords(locUnit) + absoluteDeltaY * (double)TargetYLengthAtUnitCoords(locUnit));
	}

	public float TargetXLengthAtUnitCoords(Point locUnit)
	{
		int segmentIndex = fNorthNodes.PointAtFraction(1.0 - locUnit.X).SegmentIndex;
		int segmentIndex2 = fSouthNodes.PointAtFraction(locUnit.X).SegmentIndex;
		double length = fNorthNodes.Segments[segmentIndex].Length;
		double length2 = fSouthNodes.Segments[segmentIndex2].Length;
		return (float)(Math.Pow(length, locUnit.Y) * Math.Pow(length2, 1.0 - locUnit.Y));
	}

	public float TargetYLengthAtUnitCoords(Point locUnit)
	{
		int segmentIndex = fWestNodes.PointAtFraction(1.0 - locUnit.Y).SegmentIndex;
		int segmentIndex2 = fEastNodes.PointAtFraction(locUnit.Y).SegmentIndex;
		double length = fWestNodes.Segments[segmentIndex].Length;
		return (float)(Math.Pow(fEastNodes.Segments[segmentIndex2].Length, locUnit.X) * Math.Pow(length, 1.0 - locUnit.X));
	}

	private UnitCoonsPatch SidesToNodeCoons(ISpatialTransformer xform, List<PatchSide> Sides)
	{
		Polyline[] polylines = new Polyline[4];
		Polyline[] polylinesArray = new Polyline[4];
		for (int i = 0; i < 4; i++)
		{
			Point[] points = xform.ReverseTransform(Sides[i].Smooth.GetPointBufferUnsafe());
			polylines[i] = Polyline.CreateWithBuffer(points);
			points = xform.ReverseTransform(Sides[i].Nodes.GetPointBufferUnsafe());
			polylinesArray[i] = Polyline.CreateWithBuffer(points);
			if (i > 0)
			{
				polylines[i][0] = polylines[i - 1].Last();
				polylinesArray[i][0] = polylinesArray[i - 1].Last();
			}
		}
		polylines[0][0] = polylines[3].Last();
		polylinesArray[0][0] = polylinesArray[3].Last();
		return new UnitCoonsPatch(polylines[0], polylines[1], polylines[2], polylines[3], polylinesArray[0], polylinesArray[1], polylinesArray[2], polylinesArray[3]);
	}

	public ISpatialTransformer CreateTransform()
	{
		if (transformMethod == TransformMethod.COONS)
		{
			return new UnitCoonsPatch(fSouth, fEast, fNorth, fWest, fSouthNodes, fEastNodes, fNorthNodes, fWestNodes);
		}
		if (transformMethod == TransformMethod.LAPLACIAN)
		{
			QuadTransformLaplacian quadTransform = cmesh.GetQuadTransform(regionIdx);
			UnitCoonsPatch unitCoonsPatch = SidesToNodeCoons(quadTransform, Sides);
			int ordinal = 2;
			List<ISpatialTransformer> iSpatialTransformers = new List<ISpatialTransformer>(ordinal);
			CollectionsMarshal.SetCount(iSpatialTransformers, ordinal);
			Span<ISpatialTransformer> iSpatialTransformersSpan = CollectionsMarshal.AsSpan(iSpatialTransformers);
			int ordinal2 = 0;
			iSpatialTransformersSpan[ordinal2] = unitCoonsPatch;
			ordinal2++;
			iSpatialTransformersSpan[ordinal2] = quadTransform;
			return new MultiTransformer(iSpatialTransformers);
		}
		throw new NotImplementedException();
	}
}
