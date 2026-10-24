using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geospatial.DataStructures;
using Utility.Logging;

namespace Geospatial.Vectors.MeshTopology;

public class MeshIntersection
{
	public static class MessageCodes
	{
		public static readonly MessageClass InvalidGeometry = new MessageClass("MeshIntersection_InvalidGeoemtry", MessageSeverity.Error, "Segment start on wrong side of face while traversing cell. Likely concave or self-intersecting cells. Near {Coordinate} to {Coordinate}");

		public static Message GetInvalidGeometry(Point ptA, Point ptB)
		{
			return InvalidGeometry.Create(ptA, ptB);
		}
	}

	public HalfEdge halfEdge;

	public bool finiteEdgeIntersection;

	public double faceFraction;

	public int cell;

	public int node;

	public PointOnPolyline pop;

	public int newNode;

	public MeshIntersection()
	{
		halfEdge = HalfEdge.NullHalfEdge;
		cell = -1;
		node = -1;
		pop = PointOnPolyline.NullPointOnPolyline;
		faceFraction = double.NaN;
		newNode = -1;
	}

	private MeshIntersection Copy()
	{
		return new MeshIntersection
		{
			halfEdge = halfEdge,
			cell = cell,
			node = node,
			pop = pop,
			faceFraction = faceFraction,
			newNode = newNode
		};
	}

	public override string ToString()
	{
		string messageSum = "";
		if (cell >= 0)
		{
			messageSum += $"\tcell={cell}";
		}
		if (node >= 0)
		{
			messageSum += $"\tnode={node}";
		}
		if (!halfEdge.IsNull())
		{
			char finiteEdgeIntersectionTerm = (finiteEdgeIntersection ? '_' : '.');
			messageSum += $"\the{finiteEdgeIntersectionTerm}={halfEdge}";
		}
		string messageSum2 = messageSum;
		PointOnPolyline pointOnPolyline = pop;
		return messageSum2 + "\t" + pointOnPolyline.ToString();
	}

	public static MeshIntersection PopToIntersection(SlidingMesh mesh, PointOnPolyline pop, SpatialIndex<int> cellIndex)
	{
		MeshIntersection meshIntersection = new MeshIntersection();
		meshIntersection.cell = mesh.FindCellContaining(pop.Point, cellIndex);
		if (meshIntersection.cell < 0)
		{
			throw new ArgumentOutOfRangeException("Ring did not start within mesh");
		}
		List<HalfEdge> cellFace = mesh.CellFaces[meshIntersection.cell];
		int index = mesh.HalfEdgeNodeBack(cellFace[0]);
		if (pop.Point == mesh.NodePoints[index])
		{
			meshIntersection.node = index;
			meshIntersection.cell = -1;
		}
		else
		{
			foreach (HalfEdge cellFace2 in cellFace)
			{
				int halfEdgeNodeForward = mesh.HalfEdgeNodeForward(cellFace2);
				if (pop.Point == mesh.NodePoints[halfEdgeNodeForward])
				{
					meshIntersection.node = halfEdgeNodeForward;
					meshIntersection.cell = -1;
					break;
				}
				double counterClockwise = NativePredicates.CounterClockwise(pop.Point, mesh.NodePoints[index], mesh.NodePoints[halfEdgeNodeForward]);
				if (counterClockwise == 0.0)
				{
					meshIntersection.cell = -1;
					meshIntersection.halfEdge = new HalfEdge(cellFace2.fIdx, 0);
					meshIntersection.finiteEdgeIntersection = false;
					break;
				}
				if (counterClockwise < 0.0)
				{
					meshIntersection.cell = mesh.HalfEdgeToOppositeCell(cellFace2);
					break;
				}
				index = halfEdgeNodeForward;
			}
		}
		meshIntersection.pop = pop;
		return meshIntersection;
	}

	public static bool AdvanceInCell(SlidingMesh mesh, Polyline pline, MeshIntersection X, double tolerance = 1E-06)
	{
		int segmentCount = pline.SegmentCount;
		List<HalfEdge> cellFace = mesh.CellFaces[X.cell];
		for (int i = X.pop.SegmentIndex; i < segmentCount; i++)
		{
			Segment segment = pline.Segments[i];
			double computed = 1000000.0;
			double x = 0.0;
			double y = 0.0;
			foreach (HalfEdge cellFace2 in cellFace)
			{
				int index = mesh.HalfEdgeNodeBack(cellFace2);
				int index2 = mesh.HalfEdgeNodeForward(cellFace2);
				Point point = mesh.NodePoints[index];
				Point point2 = mesh.NodePoints[index2];
				if (NativePredicates.CounterClockwise(point, point2, segment.B) > 0.0)
				{
					continue;
				}
				Segment seg = new Segment(point, point2);
				segment.IntersectPointCheckNodes(seg, out var x2, out var y2, out var t, out var t2);
				if (double.IsNaN(t))
				{
					continue;
				}
				if (t < 0.0 - tolerance)
				{
					throw new StructuredException(MessageCodes.GetInvalidGeometry(point, point2));
				}
				if (!(t2 < 0.0) && !(t2 > 1.0) && !(t >= computed))
				{
					computed = t;
					if (t2 == 0.0)
					{
						X.node = index;
						X.halfEdge = HalfEdge.NullHalfEdge;
						x = point.X;
						y = point.Y;
					}
					else if (t2 == 1.0)
					{
						X.node = index2;
						X.halfEdge = HalfEdge.NullHalfEdge;
						x = point2.X;
						y = point2.Y;
					}
					else
					{
						X.halfEdge = cellFace2;
						X.node = -1;
						X.faceFraction = ((cellFace2.orient == 0) ? t2 : (1.0 - t2));
						X.finiteEdgeIntersection = false;
						x = x2;
						y = y2;
					}
				}
			}
			if (computed < 1.0)
			{
				X.pop = new PointOnPolyline(x, y, i, computed);
				X.cell = -1;
				return true;
			}
			if (computed <= 1.0)
			{
				if (i + 1 < pline.SegmentCount)
				{
					X.pop = new PointOnPolyline(x, y, i + 1, 0.0);
				}
				else
				{
					X.pop = new PointOnPolyline(x, y, i, 1.0);
				}
				X.cell = -1;
				return true;
			}
		}
		return false;
	}

	public static bool AdvanceAtNode(SlidingMesh mesh, Polyline pline, MeshIntersection trav)
	{
		int segmentIndexPopTerm = trav.pop.SegmentIndex + (int)trav.pop.Fraction + 1;
		if (segmentIndexPopTerm > pline.SegmentCount)
		{
			return false;
		}
		Point b = pline.Segments[segmentIndexPopTerm - 1].B;
		List<HalfEdge> nodeToSortedHalfEdges = mesh.NodeToSortedHalfEdges(trav.node);
		double counterClockwise = 0.0;
		double counterClockwise2 = 0.0;
		for (int intervalIndex = 0; intervalIndex < nodeToSortedHalfEdges.Count; intervalIndex++)
		{
			HalfEdge he = nodeToSortedHalfEdges[intervalIndex];
			HalfEdge he2 = nodeToSortedHalfEdges[(intervalIndex + 1) % nodeToSortedHalfEdges.Count];
			Point point = HalfEdgeNextPoint(he);
			Point point2 = HalfEdgeNextPoint(he2);
			double counterClockwise3 = NativePredicates.CounterClockwise(trav.pop.Point, point, point2);
			counterClockwise = ((intervalIndex != 0) ? counterClockwise2 : NativePredicates.CounterClockwise(trav.pop.Point, point, b));
			counterClockwise2 = NativePredicates.CounterClockwise(trav.pop.Point, point2, b);
			bool counterClockwiseTerm;
			if (!(counterClockwise3 >= 0.0))
			{
				if (!(counterClockwise2 >= 0.0))
				{
					goto IL_0138;
				}
				counterClockwiseTerm = !(counterClockwise < 0.0);
			}
			else
			{
				if (!(counterClockwise >= 0.0))
				{
					goto IL_01c0;
				}
				counterClockwiseTerm = counterClockwise2 < 0.0;
			}
			if (counterClockwiseTerm)
			{
				goto IL_0138;
			}
			goto IL_01c0;
			IL_0138:
			if (counterClockwise == 0.0 && (b - trav.pop.Point).Dot(point - trav.pop.Point) > 0.0)
			{
				trav.halfEdge = he;
				trav.finiteEdgeIntersection = true;
			}
			else
			{
				trav.cell = mesh.HalfEdgeToCell(he);
				if (trav.cell < 0)
				{
					throw new ArgumentOutOfRangeException("Polyline exits mesh - not supported");
				}
			}
			trav.node = -1;
			break;
			IL_01c0:
			if (counterClockwise == 0.0 && counterClockwise2 == 0.0)
			{
				Vector vector = b - trav.pop.Point;
				double dot = vector.Dot(point - trav.pop.Point);
				double dot2 = vector.Dot(point2 - trav.pop.Point);
				if (dot > 0.0)
				{
					if (dot2 >= 0.0)
					{
						throw new Exception("Mesh traversal by polyline failed, may be invalid");
					}
					trav.halfEdge = he;
					trav.finiteEdgeIntersection = true;
					trav.node = -1;
					break;
				}
			}
		}
		if (trav.node >= 0 || (trav.halfEdge.IsNull() && trav.cell < 0))
		{
			throw new Exception("Mesh traversal by polyline failed to leave node");
		}
		return true;
		Point HalfEdgeNextPoint(HalfEdge he3)
		{
			return mesh.NodePoints[mesh.HalfEdgeNodeForward(he3)];
		}
	}

	public static void AdvanceInExterior(SlidingMesh mesh, Polyline pline, MeshIntersection trav)
	{
	}

	public static bool AdvanceAlongFace(SlidingMesh mesh, Polyline pline, MeshIntersection trav, double tolerance = 1E-06)
	{
		int segmentIndex = trav.pop.SegmentIndex + (int)trav.pop.Fraction + 1;
		Segment segment = pline.Segments[segmentIndex - 1];
		segment.A = trav.pop.Point;
		Segment segment2 = mesh.FaceSegment(trav.halfEdge);
		Math.Abs((segment.Bearing - segment2.Bearing + Math.PI) % (Math.PI * 2.0) - Math.PI);
		segment2.PerpendicularSignedDistance(trav.pop.Point);
		int index = mesh.HalfEdgeNodeForward(trav.halfEdge);
		Point point = mesh.NodePoints[index];
		double distanceSquaredTo = trav.pop.Point.DistanceSquaredTo(point);
		double distanceSquaredTo2 = trav.pop.Point.DistanceSquaredTo(segment.B);
		if (distanceSquaredTo <= distanceSquaredTo2 + tolerance)
		{
			PointOnPolyline pointOnPolyline = new PointOnPolyline
			{
				Point = point
			};
			if (distanceSquaredTo >= distanceSquaredTo2 - tolerance)
			{
				pointOnPolyline.Fraction = 0.0;
				pointOnPolyline.SegmentIndex = segmentIndex;
			}
			else
			{
				pointOnPolyline.SegmentIndex = trav.pop.SegmentIndex + (int)trav.pop.Fraction;
				pointOnPolyline.Fraction = Math.Sqrt(distanceSquaredTo) / pline.Segments[trav.pop.SegmentIndex].Length;
			}
			trav.pop = pointOnPolyline;
			trav.faceFraction = 1.0 - (double)(int)trav.halfEdge.orient;
			trav.halfEdge = HalfEdge.NullHalfEdge;
			trav.node = index;
			return true;
		}
		trav.pop = new PointOnPolyline(segment.B, segmentIndex - 1, 1.0);
		trav.finiteEdgeIntersection = false;
		return true;
	}

	public static bool AdvanceAtFace(SlidingMesh mesh, Polyline pline, MeshIntersection trav)
	{
		int segmentIndexPopTerm = trav.pop.SegmentIndex + (int)trav.pop.Fraction + 1;
		if (segmentIndexPopTerm - 1 >= pline.SegmentCount)
		{
			return false;
		}
		Segment segment = pline.Segments[segmentIndexPopTerm - 1];
		Segment segment2 = mesh.FaceSegment(trav.halfEdge);
		double counterClockwise = NativePredicates.CounterClockwise(segment2.A, segment2.B, segment.B);
		if (counterClockwise == 0.0)
		{
			trav.finiteEdgeIntersection = true;
			if (segment2.AsVector().Dot(segment.AsVector()) < 0.0)
			{
				trav.halfEdge = trav.halfEdge.Flipped();
			}
			return true;
		}
		if (counterClockwise > 0.0)
		{
			trav.cell = mesh.HalfEdgeToCell(trav.halfEdge);
		}
		else
		{
			trav.cell = mesh.HalfEdgeToOppositeCell(trav.halfEdge);
		}
		if (trav.cell < 0)
		{
			throw new ArgumentOutOfRangeException("Mesh traversal exited the mesh");
		}
		trav.halfEdge = HalfEdge.NullHalfEdge;
		return true;
	}

	public static void AdvanceAcrossFace(SlidingMesh mesh, Polyline pline, MeshIntersection trav)
	{
		trav.cell = mesh.HalfEdgeToOppositeCell(trav.halfEdge);
		trav.halfEdge = HalfEdge.NullHalfEdge;
	}

	public static List<MeshIntersection> RingToCrossingsRobust(SlidingMesh mesh, Polyline pline, SpatialIndex<int> cellIndex)
	{
		List<MeshIntersection> meshIntersections = new List<MeshIntersection>();
		PointOnPolyline pointOnPolyline = pline.FirstPointOnPolyline();
		MeshIntersection meshIntersection = PopToIntersection(mesh, pointOnPolyline, cellIndex);
		for (long faceCountMeshTerm = (long)(mesh.FaceCount + mesh.CellCount + mesh.NodeCount) * (long)pline.SegmentCount; faceCountMeshTerm > 0; faceCountMeshTerm--)
		{
			meshIntersections.Add(meshIntersection.Copy());
			if (meshIntersection.node >= 0)
			{
				if (!AdvanceAtNode(mesh, pline, meshIntersection))
				{
					break;
				}
			}
			else if (meshIntersection.cell >= 0)
			{
				if (!AdvanceInCell(mesh, pline, meshIntersection))
				{
					break;
				}
			}
			else
			{
				if (meshIntersection.halfEdge.IsNull())
				{
					throw new StructuredException(Message.GenericError("Polyline traversal failed"));
				}
				if (meshIntersection.finiteEdgeIntersection)
				{
					if (!AdvanceAlongFace(mesh, pline, meshIntersection))
					{
						break;
					}
				}
				else if (meshIntersection.pop.Fraction == 0.0 || meshIntersection.pop.Fraction == 1.0)
				{
					if (!AdvanceAtFace(mesh, pline, meshIntersection))
					{
						break;
					}
				}
				else
				{
					AdvanceAcrossFace(mesh, pline, meshIntersection);
				}
			}
		}
		if (pline is Polygon)
		{
			MeshIntersection meshIntersection2 = meshIntersections.First();
			MeshIntersection meshIntersection3 = meshIntersections.Last();
			if (meshIntersection2.node >= 0 && meshIntersection3.node != meshIntersection2.node)
			{
				throw new StructuredException(Message.GenericError("Polygon traversal started on node at {Coordinate} but ended elsewhere", pline.Points[0]));
			}
			if (meshIntersection2.cell >= 0 && meshIntersection3.cell != meshIntersection2.cell)
			{
				throw new StructuredException(Message.GenericError("Polygon traversal started on cell near {Coordinate} but ended elsewhere", pline.Points[0]));
			}
			if (!meshIntersection2.halfEdge.IsNull() && meshIntersection2.halfEdge.fIdx != meshIntersection3.halfEdge.fIdx)
			{
				throw new StructuredException(Message.GenericError("Polygon traversal started on face near {Coordinate} but ended elsewhere", pline.Points[0]));
			}
			meshIntersections.RemoveAt(meshIntersections.Count - 1);
		}
		return meshIntersections;
	}

	public static void DumpToJson(SlidingMesh mesh, Polyline pline, List<MeshIntersection> crossings, string cmdFile = null)
	{
		if (cmdFile == null)
		{
			cmdFile = "C:\\Temp\\cmd.json";
		}
		using FileStream utf8Json = File.Create(cmdFile);
		JsonSerializerOptions options = new JsonSerializerOptions
		{
			WriteIndented = true,
			NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
		};
		List<List<object>> localValue = new List<List<object>>
		{
			new List<object> { "SET", "g_raw", mesh },
			new List<object>
			{
				"SET",
				"pline_raw",
				DynamicMesh.SerializePolyline(pline)
			},
			new List<object>
			{
				"SET",
				"is_polygon",
				pline is Polygon
			},
			new List<object> { "PYTHON", "\r\ng=json_to_mesh(g_raw)\r\npline=np.array(pline_raw)\r\nfig,ax=plt.subplots(num=1,clear=1)\r\nax.set_adjustable('datalim')\r\ng.plot_nodes(ax = ax, alpha=0.2,color='k')\r\ng.plot_edges(ax = ax, color = 'k', alpha=0.5)\r\ng.plot_cells(ax = ax, color = 'orange', alpha = 0.4, zorder = -1)\r\nax.plot(pline[0,0],pline[0,1], 'ro') # label start \r\nif is_polygon:\r\n    ax.fill(pline[:,0], pline[:,1], fc='none', ec='r', lw=0.8)\r\nelse:\r\n    ax.plot(pline[:,0], pline[:,1], 'r', lw=0.8)\r\n" }
		};
		JsonSerializer.Serialize(utf8Json, localValue, options);
	}

	public static List<MeshIntersection> RingToCrossings(SlidingMesh mesh, Polyline ring, out List<int> crossedCells, SpatialIndex<int> cellIndex)
	{
		Point pt = ring.Points[0];
		int crossedCells2 = mesh.FindCellContaining(pt, cellIndex);
		List<MeshIntersection> meshIntersections = new List<MeshIntersection>();
		crossedCells = new List<int> { crossedCells2 };
		PointOnPolyline popStart = ring.FirstPointOnPolyline();
		int count = mesh.FaceCount + ring.SegmentCount;
		for (int i = 0; i < count; i++)
		{
			if (!mesh.NextFaceCrossing(ring, popStart, crossedCells2, out var crossing))
			{
				break;
			}
			crossedCells2 = mesh.HalfEdgeToOppositeCell(crossing.halfEdge);
			if (crossing.pop.SegmentIndex == 0 && crossing.pop.Fraction < 1E-06)
			{
				crossedCells[0] = crossedCells2;
				continue;
			}
			popStart = crossing.pop;
			meshIntersections.Add(crossing);
			crossedCells.Add(crossedCells2);
		}
		if (meshIntersections.Count > 0 && ring is Polygon)
		{
			int halfEdgeToCell = mesh.HalfEdgeToCell(meshIntersections[0].halfEdge);
			int halfEdgeToOppositeCell = mesh.HalfEdgeToOppositeCell(meshIntersections.Last().halfEdge);
			if (halfEdgeToCell != halfEdgeToOppositeCell)
			{
				bool condition = false;
				foreach (HalfEdge cellFace in mesh.CellFaces[halfEdgeToOppositeCell])
				{
					if (mesh.HalfEdgeToOppositeCell(cellFace) == halfEdgeToCell)
					{
						condition = true;
						Segment seg = mesh.FaceSegment(cellFace.fIdx);
						ring.Segments.Last().IntersectPointCheckNodes(seg, out var _, out var _, out var _, out var t2);
						meshIntersections.Add(new MeshIntersection
						{
							halfEdge = cellFace,
							pop = ring.LastPointOnPolyline(),
							faceFraction = t2
						});
						break;
					}
				}
				if (!condition)
				{
					throw new DynamicMeshException("Slicing by a ring that starts on a mesh node");
				}
			}
		}
		return meshIntersections;
	}
}
