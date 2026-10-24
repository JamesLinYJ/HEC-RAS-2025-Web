using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Transformers;
using Geospatial.Vectors.Topology;

namespace Geospatial.Vectors.MeshTopology;

public static class DiscretizeRegion
{
	public enum ReplaceMode
	{
		USE_EXISTING,
		REFINE_EXISTING,
		COARSEN_EXISTING
	}

	public enum MatchStatus
	{
		MATCH_PREV = -1,
		MATCH_OK,
		MATCH_NEXT
	}

	public class DiscNodeState
	{
		public double unitCoord;

		public double perpCoord;

		public PointOnPolyline pop;

		public readonly FaceDiscretizationState precedingState;

		public ConstraintState nodeState;

		public readonly int precedingPieceIdx;

		public DiscNodeState(double unit, double perp, PointOnPolyline pop, FaceDiscretizationState faceState, ConstraintState nodeState, int pieceIdx)
		{
			unitCoord = unit;
			perpCoord = perp;
			this.pop = pop;
			precedingState = faceState;
			this.nodeState = nodeState;
			precedingPieceIdx = pieceIdx;
		}

		public DiscNodeState(Point unitAndPerp, PointOnPolyline pop, FaceDiscretizationState faceState, ConstraintState nodeState, int pieceIdx)
			: this(unitAndPerp.X, unitAndPerp.Y, pop, faceState, nodeState, pieceIdx)
		{
		}

		public void UpdatePop(PointOnPolyline newPop)
		{
			pop = newPop;
			unitCoord = double.NaN;
			perpCoord = double.NaN;
		}

		public void UpdateUnitPerp(Point unitPerp)
		{
			unitCoord = unitPerp.X;
			perpCoord = unitPerp.Y;
		}
	}

	private class DiscretizeState
	{
		public PatchSide side;

		public Polyline src;

		public double totLength;

		public List<PointOnPolyline> dstPoints;

		public List<DiscNodeState> nodes;

		public double nextUnitCoord = double.NaN;

		public double remainingSteps = -1.0;

		public double nextStepSize = double.NaN;

		public double remainingDistance = double.NaN;

		public PointOnPolyline nextNode;

		public bool hasSkewFaces;

		public Func<Point, Point> ToUnitPerp;

		public Point PrevPoint => dstPoints.Last().Point;

		public PointOnPolyline PrevPoP => dstPoints.Last();

		public DiscretizeState(PatchSide side, Func<Point, Point> ToUnitPerp, int regionOrient, bool subsetFixedNodes = false)
		{
			this.ToUnitPerp = ToUnitPerp;
			this.side = side;
			src = side.Smooth;
			side.smoothDistances = src.CumulativeSegmentLengths();
			totLength = side.smoothDistances.Last();
			dstPoints = new List<PointOnPolyline> { src.FirstPointOnPolyline() };
			Point unitAndPerp = ToUnitPerp(src.First());
			nodes = new List<DiscNodeState>
			{
				new DiscNodeState(unitAndPerp, src.FirstPointOnPolyline(), FaceDiscretizationState.FIXED, ConstraintState.FIXED, -1)
			};
			hasSkewFaces = false;
			foreach (HalfEdge sidePiece in side.SidePieces)
			{
				if (!sidePiece.IsNull() && side.conceptualMesh.FaceHalfArcAngle[sidePiece.fIdx][sidePiece.orient ^ regionOrient] != 0f)
				{
					hasSkewFaces = true;
					break;
				}
			}
			for (int i = 0; i < side.SidePieces.Count; i++)
			{
				HalfEdge halfEdge = side.SidePieces[i];
				FaceDiscretizationState faceDiscretizationState = ((!halfEdge.IsNull()) ? side.conceptualMesh.FaceNodeState[halfEdge.fIdx] : FaceDiscretizationState.FREE);
				if (!subsetFixedNodes && faceDiscretizationState != FaceDiscretizationState.FREE)
				{
					Polyline polyline = side.PieceNodes(i);
					for (int j = 1; j < polyline.Count - 1; j++)
					{
						PointOnPolyline pop = src.NearestPointOnPolyline(polyline[j]);
						pop.Point = polyline[j];
						Point unitAndPerp2 = ToUnitPerp(pop.Point);
						nodes.Add(new DiscNodeState(unitAndPerp2, pop, (faceDiscretizationState != FaceDiscretizationState.DISCRETIZED_SUBSET) ? FaceDiscretizationState.FIXED : FaceDiscretizationState.FREE, ConstraintState.FIXED, i));
					}
				}
				Point pt = side.PieceSmooth(i).Last();
				PointOnPolyline pop2 = src.NearestPointOnPolyline(pt);
				unitAndPerp = ToUnitPerp(pop2.Point);
				FaceDiscretizationState faceState = ((faceDiscretizationState == FaceDiscretizationState.DISCRETIZED || faceDiscretizationState == FaceDiscretizationState.FIXED) ? ((!subsetFixedNodes) ? FaceDiscretizationState.FIXED : FaceDiscretizationState.DISCRETIZED_SUPERSET) : FaceDiscretizationState.FREE);
				nodes.Add(new DiscNodeState(unitAndPerp, pop2, faceState, ConstraintState.FIXED, i));
			}
		}

		public double InterpPerpendicularCoord(double unitCoord)
		{
			if (!hasSkewFaces)
			{
				return nodes[0].perpCoord;
			}
			int i;
			for (i = 0; i < nodes.Count - 1 && !(unitCoord < nodes[i + 1].unitCoord); i++)
			{
			}
			double submergenceRatio = (unitCoord - nodes[i].unitCoord) / (nodes[i + 1].unitCoord - nodes[i].unitCoord);
			return nodes[i].perpCoord * (1.0 - submergenceRatio) + nodes[i + 1].perpCoord * submergenceRatio;
		}

		public List<Point> RecoverNodes()
		{
			int ordinal = 0;
			int count = dstPoints.Count;
			List<Point> points = new List<Point>(count - ordinal);
			for (int i = ordinal; i < count; i++)
			{
				points.Add(dstPoints[i].Point);
			}
			return points;
		}

		public double GetPrecedingSegmentScale(DiscNodeState nodeState, out float rate)
		{
			return side.SegmentScalePreceding(nodeState.pop, out rate, nodeState.precedingPieceIdx);
		}

		public void UpdateFollowingSegment(PointOnPolyline pop, int nextNodeIdx)
		{
			nextStepSize = GetFollowingSegmentScale(pop, out var rate);
			if (double.IsNaN(nextStepSize))
			{
				remainingSteps = double.NaN;
			}
			else
			{
				remainingSteps = DiscretizeLine.ComputeRemainingSteps(nextStepSize, remainingDistance, rate);
			}
			if (nodes[nextNodeIdx].precedingState == FaceDiscretizationState.DISCRETIZED_SUPERSET)
			{
				Polyline polyline = side.PieceNodes(nodes[nextNodeIdx].precedingPieceIdx);
				int nearestPointIndex = polyline.NearestPointIndex(pop.Point, out var distance);
				if (distance > 0.0001)
				{
					throw new Exception("Failed to recover piece node index");
				}
				int nearestPointIndex2 = polyline.NearestPointIndex(nodes[nextNodeIdx].pop.Point, out distance);
				if (distance > 0.0001)
				{
					throw new Exception("Failed to recover piece node index");
				}
				double nextStepSize2 = polyline[nearestPointIndex].DistanceTo(polyline[nearestPointIndex + 1]);
				int nearestPointIndex2AboveNearestPointIndex = nearestPointIndex2 - nearestPointIndex;
				if (nearestPointIndex2AboveNearestPointIndex < 1)
				{
					throw new Exception("Recovered bad piece node index");
				}
				if (nextStepSize < 1.4 * nextStepSize2)
				{
					remainingSteps *= nextStepSize / nextStepSize2;
					nextStepSize = nextStepSize2;
				}
				remainingSteps = Math.Min(remainingSteps, nearestPointIndex2AboveNearestPointIndex);
			}
		}

		public double GetFollowingSegmentScale(DiscNodeState nodeState, out float rate)
		{
			return side.SegmentScaleFollowing(nodeState.pop, out rate, nodeState.precedingPieceIdx);
		}

		public double GetFollowingSegmentScale(PointOnPolyline pop, out float rate)
		{
			return side.SegmentScaleFollowing(pop, out rate);
		}

		public MatchStatus SnapNode(DiscNodeState result, DiscNodeState prevOutput, DiscNodeState nextInput)
		{
			result.nodeState = ConstraintState.FIXED;
			return SnapPoint(ref result.pop, prevOutput.pop, nextInput);
		}

		public MatchStatus SnapPoint(ref PointOnPolyline pop, PointOnPolyline prevOutput, DiscNodeState nextInput)
		{
			Polyline polyline = side.PieceNodes(nextInput.precedingPieceIdx);
			PointOnPolyline pointOnPolyline = polyline.NearestPointOnPolyline(pop.Point);
			if (pointOnPolyline.Fraction == 0.0)
			{
				pop.Point = polyline[pointOnPolyline.SegmentIndex];
			}
			else if (pointOnPolyline.Fraction == 1.0)
			{
				pop.Point = polyline[pointOnPolyline.SegmentIndex + 1];
			}
			else
			{
				double distanceSquaredTo = pop.Point.DistanceSquaredTo(polyline[pointOnPolyline.SegmentIndex]);
				double distanceSquaredTo2 = pop.Point.DistanceSquaredTo(polyline[pointOnPolyline.SegmentIndex + 1]);
				Point point = ((!(distanceSquaredTo < distanceSquaredTo2)) ? polyline[pointOnPolyline.SegmentIndex + 1] : polyline[pointOnPolyline.SegmentIndex]);
				pop = src.NearestPointOnPolyline(point);
				pop.Point = point;
			}
			MatchStatus result;
			if (PointOnPolyline.Compare(pop, prevOutput) > 0)
			{
				result = ((PointOnPolyline.Compare(pop, nextInput.pop) >= 0) ? MatchStatus.MATCH_NEXT : MatchStatus.MATCH_OK);
			}
			else
			{
				result = MatchStatus.MATCH_PREV;
				Point point2 = polyline[pointOnPolyline.SegmentIndex + 1];
				pop = src.NearestPointOnPolyline(point2);
				pop.Point = point2;
			}
			return result;
		}
	}

	public static void Apply(ConceptualMesh cmesh, int cIdx, ReplaceMode replaceMode = ReplaceMode.REFINE_EXISTING)
	{
		QuadTransformLaplacian quadTransform = cmesh.GetQuadTransform(cIdx);
		Apply(cmesh, cIdx, quadTransform, replaceMode);
	}

	public static void Apply(ConceptualMesh cmesh, int cIdx, ISpatialTransformer transform, ReplaceMode replaceMode = ReplaceMode.REFINE_EXISTING)
	{
		PatchCommon patchCommon = new PatchCommon(cmesh, cIdx);
		patchCommon.result = new SlidingMesh();
		patchCommon.SetSides();
		if (!PatchSide.CheckRotation(patchCommon.Sides, transform))
		{
			throw new DynamicMeshException($"Patch rotation is corrupt for region index {cIdx}");
		}
		for (int i = 0; i < 2; i++)
		{
			PatchSide patchSide = patchCommon.Sides[i];
			PatchSide patchSide2 = patchCommon.Sides[i + 2];
			bool isXAligned = i == 0;
			_ = (i + 3) % 4;
			List<Point> nodesA = null;
			List<Point> nodesB = null;
			FaceDiscretizationState newFaceDiscretizationState = FaceDiscretizationState.DISCRETIZED;
			if (cmesh.CellRegionMetadata[cIdx].PatchType == PatchType.OneD)
			{
				if (i == 1)
				{
					continue;
				}
				newFaceDiscretizationState = FaceDiscretizationState.DISCRETIZED_SUBSET;
				DiscretizeParallel(patchSide, patchSide2, isXAligned, transform, out nodesA, out nodesB, subsetFixedNodes: true);
			}
			else
			{
				DiscretizeParallel(patchSide, patchSide2, isXAligned, transform, out nodesA, out nodesB, subsetFixedNodes: false);
			}
			patchSide.UpdateNodes(cmesh, nodesA, replaceMode, newFaceDiscretizationState);
			patchSide2.UpdateNodes(cmesh, nodesB, replaceMode, newFaceDiscretizationState);
		}
	}

	public static void DiscretizeParallel(PatchSide sideA, PatchSide sideB, bool isXAligned, ISpatialTransformer transform, out List<Point> nodesA, out List<Point> nodesB, bool subsetFixedNodes)
	{
		DiscretizeParallel(new List<PatchSide>
		{
			sideA,
			sideB.CloneReversed()
		}, isXAligned, transform, out var nodes, subsetFixedNodes);
		nodesA = nodes[0];
		nodesB = nodes[1];
		nodesB.Reverse();
	}

	public static void DiscretizeParallel(List<PatchSide> sides, bool isXAligned, ISpatialTransformer transform, out List<List<Point>> nodes, bool subsetFixedNodes)
	{
		double nodeRepulsion = 0.3;
		List<DiscretizeState> matchParallelNodes = MatchParallelNodes(sides, isXAligned, transform, nodeRepulsion, subsetFixedNodes);
		FillViaContours(matchParallelNodes, isXAligned, transform);
		nodes = new List<List<Point>>();
		for (int i = 0; i < matchParallelNodes.Count; i++)
		{
			nodes.Add(matchParallelNodes[i].RecoverNodes());
		}
	}

	private static List<DiscretizeState> MatchParallelNodes(List<PatchSide> sides, bool isXAligned, ISpatialTransformer transform, double nodeRepulsion, bool subsetFixedNodes)
	{
		double computed = 0.33;
		_ = isXAligned;
		_ = isXAligned;
		if (sides.Count != 2)
		{
			throw new Exception("Not generalized beyond a pair of sides");
		}
		List<DiscretizeState> states = new List<DiscretizeState>
		{
			new DiscretizeState(sides[0], PointToUnitPerp, 0, subsetFixedNodes),
			new DiscretizeState(sides[1], PointToUnitPerp, 1, subsetFixedNodes)
		};
		DiscretizeState discretizeState = states[0];
		DiscretizeState discretizeState2 = states[1];
		List<DiscNodeState> southInput = discretizeState.nodes;
		List<DiscNodeState> northInput = discretizeState2.nodes;
		List<DiscNodeState> southNodes = new List<DiscNodeState>();
		List<DiscNodeState> northNodes = new List<DiscNodeState>();
		southNodes.Add(southInput.First());
		northNodes.Add(northInput.First());
		int southIdx = 1;
		int northIdx = 1;
		while (southIdx < southInput.Count && northIdx < northInput.Count)
		{
			if (southIdx + 1 == southInput.Count && northIdx + 1 == northInput.Count)
			{
				IncrSouth();
				IncrNorth();
				break;
			}
			if (southIdx + 1 == southInput.Count)
			{
				IncrNorth();
				DiscNodeState matchSouth = MatchSouth(out var matchStatus);
				switch (matchStatus)
				{
				case MatchStatus.MATCH_OK:
					southNodes.Add(matchSouth);
					break;
				case MatchStatus.MATCH_NEXT:
					IncrSouth();
					break;
				default:
					southNodes.Add(southNodes.Last());
					break;
				}
				continue;
			}
			if (northIdx + 1 == northInput.Count)
			{
				IncrSouth();
				DiscNodeState matchNorth = MatchNorth(out var matchStatus2);
				switch (matchStatus2)
				{
				case MatchStatus.MATCH_OK:
					northNodes.Add(matchNorth);
					break;
				case MatchStatus.MATCH_NEXT:
					IncrNorth();
					break;
				default:
					northNodes.Add(northNodes.Last());
					break;
				}
				continue;
			}
			float rate;
			if (southInput[southIdx].unitCoord < northInput[northIdx].unitCoord)
			{
				IncrSouth();
				DiscNodeState discNodeState = MatchNorth(out var matchStatus3);
				switch (matchStatus3)
				{
				case MatchStatus.MATCH_OK:
				{
					if (southInput[southIdx].unitCoord <= northInput[northIdx].unitCoord)
					{
						northNodes.Add(discNodeState);
						break;
					}
					if (northInput[northIdx].precedingState == FaceDiscretizationState.FIXED)
					{
						IncrNorth();
						break;
					}
					double distanceTo = northInput[northIdx].pop.Point.DistanceTo(discNodeState.pop.Point);
					double precedingSegmentScale = discretizeState2.GetPrecedingSegmentScale(northInput[northIdx], out rate);
					if (distanceTo < computed * precedingSegmentScale)
					{
						IncrNorth();
					}
					else
					{
						northNodes.Add(discNodeState);
					}
					break;
				}
				case MatchStatus.MATCH_NEXT:
					IncrNorth();
					break;
				default:
					northNodes.Add(northNodes.Last());
					break;
				}
				continue;
			}
			IncrNorth();
			DiscNodeState discNodeState2 = MatchSouth(out var matchStatus4);
			switch (matchStatus4)
			{
			case MatchStatus.MATCH_OK:
			{
				if (northInput[northIdx].unitCoord <= southInput[southIdx].unitCoord)
				{
					southNodes.Add(discNodeState2);
					break;
				}
				double distanceTo2 = southInput[southIdx].pop.Point.DistanceTo(discNodeState2.pop.Point);
				double precedingSegmentScale2 = discretizeState.GetPrecedingSegmentScale(southInput[southIdx], out rate);
				if (southInput[southIdx].precedingState == FaceDiscretizationState.FIXED || distanceTo2 < computed * precedingSegmentScale2)
				{
					IncrSouth();
				}
				else
				{
					southNodes.Add(discNodeState2);
				}
				break;
			}
			case MatchStatus.MATCH_NEXT:
				IncrSouth();
				break;
			default:
				southNodes.Add(southNodes.Last());
				break;
			}
		}
		states[0].nodes = southNodes;
		states[1].nodes = northNodes;
		SmoothNodes(states, nodeRepulsion);
		return states;
		void IncrNorth()
		{
			northNodes.Add(northInput[northIdx]);
			northIdx++;
		}
		void IncrSouth()
		{
			southNodes.Add(southInput[southIdx]);
			southIdx++;
		}
		DiscNodeState MatchNorth(out MatchStatus reference)
		{
			double unitCoord = southNodes.Last().unitCoord;
			double freeIds = states[1].InterpPerpendicularCoord(unitCoord);
			Point pt = ISpatialTransformerExtensions.Transform(pt: (!isXAligned) ? new Point(freeIds, unitCoord) : new Point(unitCoord, freeIds), xf: transform);
			PointOnPolyline pop = states[1].src.NearestPointOnPolyline(pt);
			DiscNodeState result = new DiscNodeState(unitCoord, freeIds, pop, northInput[northIdx].precedingState, ConstraintState.FREE, northInput[northIdx].precedingPieceIdx);
			if (northInput[northIdx].precedingState == FaceDiscretizationState.DISCRETIZED_SUPERSET)
			{
				reference = states[1].SnapNode(result, northNodes.Last(), northInput[northIdx]);
			}
			else
			{
				reference = MatchStatus.MATCH_OK;
			}
			return result;
		}
		DiscNodeState MatchSouth(out MatchStatus reference)
		{
			double unitCoord = northNodes.Last().unitCoord;
			double freeIds2 = states[0].InterpPerpendicularCoord(unitCoord);
			Point pt = ISpatialTransformerExtensions.Transform(pt: (!isXAligned) ? new Point(freeIds2, unitCoord) : new Point(unitCoord, freeIds2), xf: transform);
			PointOnPolyline pop = states[0].src.NearestPointOnPolyline(pt);
			DiscNodeState result = new DiscNodeState(unitCoord, freeIds2, pop, southInput[southIdx].precedingState, ConstraintState.FREE, southInput[southIdx].precedingPieceIdx);
			if (southInput[southIdx].precedingState == FaceDiscretizationState.DISCRETIZED_SUPERSET)
			{
				reference = states[0].SnapNode(result, southNodes.Last(), southInput[southIdx]);
			}
			else
			{
				reference = MatchStatus.MATCH_OK;
			}
			return result;
		}
		Point PointToUnitPerp(Point p)
		{
			Point result = transform.ReverseTransform(p);
			if (isXAligned)
			{
				return result;
			}
			return new Point(result.Y, result.X);
		}
	}

	private static void SmoothNodes(List<DiscretizeState> states, double nodeRepulsion)
	{
		for (int i = 0; i < 2; i++)
		{
			DiscretizeState discretizeState = states[i];
			double[] cumulativeSegmentLengths = null;
			for (int j = 0; j < 5; j++)
			{
				bool condition = false;
				for (int k = 1; k < states[0].nodes.Count - 1; k++)
				{
					if (!Nudgeable(discretizeState, k))
					{
						continue;
					}
					double precedingSegmentScale = discretizeState.GetPrecedingSegmentScale(discretizeState.nodes[k], out var rate);
					double followingSegmentScale = discretizeState.GetFollowingSegmentScale(discretizeState.nodes[k], out rate);
					double distanceAlongFast = discretizeState.src.DistanceAlongFast(discretizeState.nodes[k - 1].pop, ref cumulativeSegmentLengths);
					double distanceAlongFast2 = discretizeState.src.DistanceAlongFast(discretizeState.nodes[k].pop, ref cumulativeSegmentLengths);
					double distanceAlongFast3 = discretizeState.src.DistanceAlongFast(discretizeState.nodes[k + 1].pop, ref cumulativeSegmentLengths);
					double distanceAlongFast2AboveDistanceAlongFast = distanceAlongFast2 - distanceAlongFast;
					double distanceAlongFast3AboveDistanceAlongFast2 = distanceAlongFast3 - distanceAlongFast2;
					if (!(distanceAlongFast2AboveDistanceAlongFast > precedingSegmentScale * nodeRepulsion) || !(distanceAlongFast3AboveDistanceAlongFast2 > followingSegmentScale * nodeRepulsion))
					{
						double precedingSegmentScaleTerm;
						if (distanceAlongFast2AboveDistanceAlongFast + distanceAlongFast3AboveDistanceAlongFast2 > nodeRepulsion * (precedingSegmentScale + followingSegmentScale))
						{
							precedingSegmentScaleTerm = ((!(distanceAlongFast2AboveDistanceAlongFast / precedingSegmentScale > distanceAlongFast3AboveDistanceAlongFast2 / followingSegmentScale)) ? (precedingSegmentScale * nodeRepulsion - distanceAlongFast2AboveDistanceAlongFast) : (0.0 - (followingSegmentScale * nodeRepulsion - distanceAlongFast3AboveDistanceAlongFast2)));
						}
						else
						{
							precedingSegmentScaleTerm = distanceAlongFast3AboveDistanceAlongFast2 - (distanceAlongFast2AboveDistanceAlongFast + distanceAlongFast3AboveDistanceAlongFast2) / 2.0;
						}
						if (!(Math.Abs(precedingSegmentScaleTerm) < 1E-10))
						{
							discretizeState.nodes[k].UpdatePop(discretizeState.src.PointAtDistance(distanceAlongFast2 + precedingSegmentScaleTerm));
							condition = true;
						}
					}
				}
				if (!condition)
				{
					break;
				}
			}
			foreach (DiscNodeState node in discretizeState.nodes)
			{
				if (double.IsNaN(node.unitCoord))
				{
					node.UpdateUnitPerp(discretizeState.ToUnitPerp(node.pop.Point));
				}
			}
		}
		static bool Nudgeable(DiscretizeState state, int nodeIdx)
		{
			return state.nodes[nodeIdx].nodeState == ConstraintState.FREE;
		}
	}

	private static void FillViaContours(List<DiscretizeState> states, bool isXAligned, ISpatialTransformer transform)
	{
		int unity = 1;
		int count = states[0].nodes.Count;
		Span<bool> boolSpan = stackalloc bool[2];
		Span<MatchStatus> matchStatus = stackalloc MatchStatus[2];
		while (unity < count)
		{
			if (states[0].nodes[unity].pop.Point == states[0].nodes[unity - 1].pop.Point)
			{
				states[1].dstPoints.Add(states[1].nodes[unity].pop);
				unity++;
				continue;
			}
			if (states[1].nodes[unity].pop.Point == states[1].nodes[unity - 1].pop.Point)
			{
				states[0].dstPoints.Add(states[0].nodes[unity].pop);
				unity++;
				continue;
			}
			bool precedingStateNodesTerm = false;
			for (int i = 0; i < states.Count; i++)
			{
				DiscretizeState discretizeState = states[i];
				_ = discretizeState.PrevPoP;
				discretizeState.nextNode = discretizeState.nodes[unity].pop;
				discretizeState.nextUnitCoord = discretizeState.nodes[unity].unitCoord;
				precedingStateNodesTerm = precedingStateNodesTerm || discretizeState.nodes[unity].precedingState == FaceDiscretizationState.FIXED;
				double[] cumulativeSegmentLengths = discretizeState.side.smoothDistances;
				double distanceAlongFast = discretizeState.src.DistanceAlongFast(discretizeState.PrevPoP, ref cumulativeSegmentLengths);
				double distanceAlongFast2 = discretizeState.src.DistanceAlongFast(discretizeState.nextNode, ref cumulativeSegmentLengths);
				discretizeState.remainingDistance = distanceAlongFast2 - distanceAlongFast;
				discretizeState.UpdateFollowingSegment(discretizeState.PrevPoP, unity);
			}
			double remainingSteps;
			if (double.IsNaN(states[0].remainingSteps))
			{
				remainingSteps = states[1].remainingSteps;
				states[0].remainingSteps = remainingSteps;
				states[0].nextStepSize = states[0].remainingDistance / remainingSteps;
			}
			else if (double.IsNaN(states[1].remainingSteps))
			{
				remainingSteps = states[0].remainingSteps;
				states[1].remainingSteps = remainingSteps;
				states[1].nextStepSize = states[1].remainingDistance / remainingSteps;
			}
			else
			{
				remainingSteps = Math.Round(2.0 / (1.0 / states[0].remainingSteps + 1.0 / states[1].remainingSteps));
			}
			if ((remainingSteps <= 1.0) | precedingStateNodesTerm)
			{
				for (int j = 0; j < states.Count; j++)
				{
					PointOnPolyline pop = states[j].nodes[unity].pop;
					states[j].dstPoints.Add(pop);
				}
				unity++;
				continue;
			}
			for (int k = 0; k < states.Count; k++)
			{
				matchStatus[k] = MatchStatus.MATCH_OK;
				_ = states[k].nodes[unity].pop;
				states[k].nextStepSize *= states[k].remainingSteps / remainingSteps;
				double[] cumulativeSegmentLengths2 = states[k].side.smoothDistances;
				double distanceAlongFast3 = states[k].src.DistanceAlongFast(states[k].PrevPoP, ref cumulativeSegmentLengths2);
				PointOnPolyline pop2 = states[k].src.PointAtDistance(distanceAlongFast3 + states[k].nextStepSize);
				if (states[k].nodes[unity].precedingState == FaceDiscretizationState.DISCRETIZED_SUPERSET)
				{
					matchStatus[k] = states[k].SnapPoint(ref pop2, states[k].dstPoints.Last(), states[k].nodes[unity]);
				}
				states[k].dstPoints.Add(pop2);
			}
			if ((matchStatus[0] != MatchStatus.MATCH_OK || matchStatus[1] != MatchStatus.MATCH_OK) && matchStatus[0] != MatchStatus.MATCH_NEXT && matchStatus[1] != MatchStatus.MATCH_NEXT)
			{
				if (matchStatus[0] != MatchStatus.MATCH_PREV || matchStatus[1] != MatchStatus.MATCH_PREV)
				{
					if (matchStatus[0] == MatchStatus.MATCH_PREV && matchStatus[1] == MatchStatus.MATCH_OK)
					{
						states[0].dstPoints.RemoveAt(states[0].dstPoints.Count - 1);
						continue;
					}
					if (matchStatus[0] == MatchStatus.MATCH_OK && matchStatus[1] == MatchStatus.MATCH_PREV)
					{
						states[1].dstPoints.RemoveAt(states[1].dstPoints.Count - 1);
						continue;
					}
					throw new Exception("Logic failure");
				}
				Point point = states[0].dstPoints[states[0].dstPoints.Count - 2].Point;
				Point point2 = states[0].dstPoints[states[0].dstPoints.Count - 1].Point;
				Point point3 = states[1].dstPoints[states[1].dstPoints.Count - 2].Point;
				Point point4 = states[1].dstPoints[states[1].dstPoints.Count - 1].Point;
				double distanceTo = point.DistanceTo(point2);
				double distanceTo2 = point3.DistanceTo(point4);
				int distanceToDistanceTo2Term = ((!(distanceTo < distanceTo2)) ? 1 : 0);
				if (!(states[distanceToDistanceTo2Term].dstPoints.Last().Point == states[distanceToDistanceTo2Term].nodes[unity].pop.Point))
				{
					states[1 - distanceToDistanceTo2Term].dstPoints.RemoveAt(states[1 - distanceToDistanceTo2Term].dstPoints.Count - 1);
					continue;
				}
				matchStatus[0] = (matchStatus[1] = MatchStatus.MATCH_NEXT);
			}
			if (matchStatus[0] == MatchStatus.MATCH_NEXT || matchStatus[1] == MatchStatus.MATCH_NEXT)
			{
				for (int l = 0; l < states.Count; l++)
				{
					PointOnPolyline pop3 = states[l].nodes[unity].pop;
					states[l].dstPoints[states[l].dstPoints.Count - 1] = pop3;
				}
				unity++;
				continue;
			}
			List<Point> points = states.Select((DiscretizeState state) => transform.ReverseTransform(state.PrevPoint)).ToList();
			boolSpan[0] = states[0].nodes[unity].precedingState != FaceDiscretizationState.DISCRETIZED_SUPERSET;
			boolSpan[1] = states[1].nodes[unity].precedingState != FaceDiscretizationState.DISCRETIZED_SUPERSET;
			if (!boolSpan[0] && !boolSpan[1])
			{
				NudgeDiscreteByContours(states, unity, isXAligned, transform);
			}
			else if (boolSpan[0] && boolSpan[1])
			{
				Point matchUnit = new Point(0.5 * (Clamp(points[0]) + Clamp(points[1])));
				NudgeByContours(states[0], points[0], matchUnit, isXAligned, transform);
				NudgeByContours(states[1], points[1], matchUnit, isXAligned, transform);
			}
			else if (!boolSpan[0])
			{
				NudgeByContours(matchUnit: new Point(Clamp(points[0])), state: states[1], myUnit: points[1], isXAligned: isXAligned, transform: transform);
			}
			else
			{
				NudgeByContours(matchUnit: new Point(Clamp(points[1])), state: states[0], myUnit: points[0], isXAligned: isXAligned, transform: transform);
			}
			for (int intervalIndex = 0; intervalIndex < 2; intervalIndex++)
			{
				double[] cumulativeSegmentLengths3 = states[intervalIndex].side.smoothDistances;
				double distanceAlongFast4 = states[intervalIndex].src.DistanceAlongFast(states[intervalIndex].dstPoints[states[intervalIndex].dstPoints.Count - 2], ref cumulativeSegmentLengths3);
				double distanceAlongFast5 = states[intervalIndex].src.DistanceAlongFast(states[intervalIndex].dstPoints[states[intervalIndex].dstPoints.Count - 1], ref cumulativeSegmentLengths3);
				if (distanceAlongFast4 >= distanceAlongFast5)
				{
					throw new DynamicMeshException("Quad region may be too skewed. Check corners");
				}
			}
		}
		static Point Clamp(Point input)
		{
			return new Point(Math.Min(1.0, Math.Max(0.0, input.X)), Math.Min(1.0, Math.Max(0.0, input.Y)));
		}
	}

	private static void NudgeDiscreteByContours(List<DiscretizeState> states, int nextNodeIdx, bool isXAligned, ISpatialTransformer transform)
	{
		Span<int> intSpan = stackalloc int[2];
		Span<int> intSpan2 = stackalloc int[2];
		Span<int> intSpan3 = stackalloc int[2];
		Polyline[] polylines = new Polyline[2];
		Span<double> doubleSpan = stackalloc double[2];
		for (int i = 0; i < 2; i++)
		{
			polylines[i] = states[i].side.PieceNodes(states[i].nodes[nextNodeIdx].precedingPieceIdx);
			Point point = states[i].dstPoints.Last().Point;
			intSpan[i] = polylines[i].NearestPointIndex(point, out var distance);
			if (distance > 1E-06)
			{
				throw new Exception("Discrete nudging did not recover index");
			}
			doubleSpan[i] = PointToUnit(point, transform, isXAligned);
			Point point2 = states[i].dstPoints[states[i].dstPoints.Count - 2].Point;
			int ordinal = intSpan[i] - 1;
			while (ordinal >= 0 && !(polylines[i][ordinal] == point2))
			{
				ordinal--;
			}
			if (ordinal < 0)
			{
				throw new Exception("Did not find preceding point in discrete nudging");
			}
			intSpan2[i] = ordinal + 1;
			point2 = states[i].nodes[nextNodeIdx].pop.Point;
			for (ordinal = intSpan[i] + 1; ordinal < polylines[i].Count && !(polylines[i][ordinal] == point2); ordinal++)
			{
			}
			if (ordinal == polylines[i].Count)
			{
				throw new Exception("Did not find following point in discrete nudging");
			}
			intSpan3[i] = ordinal;
		}
		double computed = Math.Abs(doubleSpan[0] - doubleSpan[1]);
		int ordinal2 = ((!(doubleSpan[0] < doubleSpan[1])) ? 1 : 0);
		int ordinal3 = 1 - ordinal2;
		for (int ordinal4 = intSpan3[0] - intSpan2[0] + intSpan3[1] - intSpan2[1]; ordinal4 > 0; ordinal4--)
		{
			double maxValue = double.MaxValue;
			double naN = double.NaN;
			if (intSpan[ordinal2] + 1 < intSpan3[ordinal2])
			{
				naN = PointToUnit(polylines[ordinal2][intSpan[ordinal2] + 1], transform, isXAligned);
				maxValue = Math.Abs(naN - doubleSpan[ordinal3]);
			}
			double maxValue2 = double.MaxValue;
			double naN2 = double.NaN;
			if (intSpan[ordinal3] > intSpan2[ordinal3])
			{
				naN2 = PointToUnit(polylines[ordinal3][intSpan[ordinal3] - 1], transform, isXAligned);
				maxValue2 = Math.Abs(naN2 - doubleSpan[ordinal2]);
			}
			if (maxValue < computed && maxValue < maxValue2)
			{
				doubleSpan[ordinal2] = naN;
				intSpan[ordinal2]++;
				computed = maxValue;
			}
			else
			{
				if (!(maxValue2 < computed))
				{
					break;
				}
				doubleSpan[ordinal3] = naN2;
				intSpan[ordinal3]--;
				computed = maxValue2;
			}
		}
		for (int j = 0; j < 2; j++)
		{
			Point point3 = polylines[j][intSpan[j]];
			PointOnPolyline dstPoints = states[j].src.NearestPointOnPolyline(point3);
			dstPoints.Point = point3;
			states[j].dstPoints[states[j].dstPoints.Count - 1] = dstPoints;
		}
		static double PointToUnit(Point p, ISpatialTransformer xform, bool condition)
		{
			if (condition)
			{
				return xform.ReverseTransform(p).X;
			}
			return xform.ReverseTransform(p).Y;
		}
	}

	private static void NudgeByContours(DiscretizeState state, Point myUnit, Point matchUnit, bool isXAligned, ISpatialTransformer transform)
	{
		if (isXAligned)
		{
			myUnit.X = matchUnit.X;
		}
		else
		{
			myUnit.Y = matchUnit.Y;
		}
		Point pt = transform.Transform(myUnit);
		PointOnPolyline pointOnPolyline = state.src.NearestPointOnPolyline(pt);
		double[] cumulativeSegmentLengths = state.side.smoothDistances;
		double distanceAlongFast = state.src.DistanceAlongFast(pointOnPolyline, ref cumulativeSegmentLengths);
		if (!((state.src.DistanceAlongFast(state.nextNode, ref cumulativeSegmentLengths) - distanceAlongFast) / state.nextStepSize < 0.5))
		{
			state.dstPoints[state.dstPoints.Count - 1] = pointOnPolyline;
		}
	}
}
