using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Utility.Logging;

namespace Geospatial.Vectors.MeshTopology;

public class ForceDisplacement
{
	public static class MessageCodes
	{
		public static readonly MessageClass TinConstraintError = new MessageClass("ForceDisplacement_TinConstraintError", MessageSeverity.Error, "Internal constraint error near {Coordinate} during ForceDisplacement.");

		public static Message GetTinConstraintError(Point pt)
		{
			return TinConstraintError.Create(pt);
		}
	}

	private int _iterations = 10;

	private double _minRelativeMove = 0.001;

	private bool _cullNodes = true;

	private bool _insertNodes = true;

	private Vector[] _forces;

	private float[] _localSizes;

	private int[] _meshToTin;

	private double _compressionFactor = 1.2;

	private double _scaleFactor = 1.0;

	private readonly List<object> _history;

	private DynamicTINFactory<NativePredicates> _dTin;

	private SlidingMesh _mesh;

	private ISizeFunction _sizeFunction;

	public static OperationResult ForceDisplacementOptimizer(SlidingMesh mesh, ISizeFunction sizeFunction, int iterations = 10, bool cullNodes = false, bool insertNodes = false, string diagnosticPath = null)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		ForceDisplacement optimizer = new ForceDisplacement(mesh, sizeFunction, captureHistory: diagnosticPath != null)
		{
			_iterations = iterations,
			_cullNodes = cullNodes,
			_insertNodes = insertNodes
		};
		OperationResult result = optimizer.Apply();
		if (diagnosticPath != null)
		{
			optimizer.DumpToJson(diagnosticPath);
		}
		stopwatch.Stop();
		return result;
	}

	private ForceDisplacement(SlidingMesh mesh, ISizeFunction sizeFunction, bool captureHistory)
	{
		_history = captureHistory ? new List<object>() : null;
		_mesh = mesh;
		_sizeFunction = sizeFunction;
		_forces = new Vector[_mesh.NodeCount];
		_localSizes = new float[_mesh.NodeCount];
		_meshToTin = new int[_mesh.NodeCount];
	}

	private OperationResult Apply()
	{
		OperationResult result = new OperationResult();
		Stopwatch stopwatch = Stopwatch.StartNew();
		PrepareMesh();
		_dTin = InitializeTriangulation();
		if (_history != null)
		{
			_history.Add(new List<object> { "PYTHON", "tins=[]" });
		}
		if (_cullNodes || _insertNodes)
		{
			AddRemoveToEstimatedCellCount();
		}
		for (int i = 0; i < _iterations; i++)
		{
			if (_history != null)
			{
				stopwatch.Stop();
				_history.Add(new List<object>
				{
					"SET",
					"dt_raw",
					_dTin.Tin.Clone()
				});
				_history.Add(new List<object> { "@PYTHON", "\r\ndt=json_to_mesh(dt_raw)\r\n# clean out deleted/external triangles\r\nfor c in np.nonzero(dt.cells['state'] == 1)[0]:\r\n    dt.delete_cell(c)\r\ndt.delete_naked_edges()\r\ndt.delete_naked_nodes()\r\ntins.append(dt)" });
				stopwatch.Start();
			}
			ApplyOnce();
		}
		UpdateMesh();
		stopwatch.Stop();
		return result;
	}

	private void ApplyOnce()
	{
		UpdateSizes();
		UpdateForces();
		UpdatePositions();
	}

	public void AddRemoveToEstimatedCellCount()
	{
		UpdateSizes();
		double integrateSizeFunctionSum = 0.0;
		int ordinal = 0;
		for (int i = 0; i < _dTin.Tin.TriangleCount; i++)
		{
			TriangleDefinition triangleDefinition = _dTin.Tin.TriangleDefinition[i];
			if (!triangleDefinition.IsDeleted)
			{
				ordinal++;
				float la = _localSizes[(int)_dTin.Tin.Z[triangleDefinition.p0]];
				float lb = _localSizes[(int)_dTin.Tin.Z[triangleDefinition.p1]];
				float lc = _localSizes[(int)_dTin.Tin.Z[triangleDefinition.p2]];
				double area = _dTin.Tin.Triangles[i].Area;
				integrateSizeFunctionSum += IntegrateSizeFunction(la, lb, lc, area, Math.Sqrt(3.0) / 4.0);
			}
		}
		int integrateSizeFunctionSumTerm = (int)(integrateSizeFunctionSum - (double)ordinal) / 2;
		if (_insertNodes && integrateSizeFunctionSumTerm > 0)
		{
			PriorityQueue<int, float> priorityQueue = new PriorityQueue<int, float>();
			List<TriangleDefinition> triangleDefinition2 = _dTin.Tin.TriangleDefinition;
			for (int j = 0; j < _dTin.Tin.TriangleCount; j++)
			{
				TriangleDefinition triangleDefinition3 = triangleDefinition2[j];
				if (!triangleDefinition3.IsDeleted)
				{
					double localSizesTinTerm = 1.0 / 3.0 * (double)(_localSizes[(int)_dTin.Tin.Z[triangleDefinition3.p0]] + _localSizes[(int)_dTin.Tin.Z[triangleDefinition3.p1]] + _localSizes[(int)_dTin.Tin.Z[triangleDefinition3.p2]]);
					double computed = Math.Sqrt(3.0) / 4.0 * localSizesTinTerm;
					double area2 = _dTin.Tin.Triangles[j].Area;
					priorityQueue.Enqueue(j, (float)(area2 / computed));
					if (priorityQueue.Count > integrateSizeFunctionSumTerm)
					{
						priorityQueue.Dequeue();
					}
				}
			}
			List<int> meshToTin = _meshToTin.ToList();
			while (priorityQueue.Count > 0)
			{
				int t = priorityQueue.Dequeue();
				Point point = _dTin.Tin.Triangles[t].Centroid();
				_dTin.DynInsertPoint(point, ref t, translatePoint: false, out var p);
				int addNode = _mesh.AddNode(point);
				if (addNode != meshToTin.Count)
				{
					throw new Exception("Internal error during ForceDisplacement");
				}
				meshToTin.Add(p);
				_dTin.Tin.Z[p] = addNode;
			}
			_meshToTin = meshToTin.ToArray();
			_localSizes = new float[_mesh.NodeCount];
			_forces = new Vector[_mesh.NodeCount];
		}
		else
		{
			if (!_cullNodes || integrateSizeFunctionSumTerm >= 0)
			{
				return;
			}
			float[] floatValues = new float[_mesh.NodeCount];
			UpdateForces(floatValues);
			PriorityQueue<int, float> priorityQueue2 = new PriorityQueue<int, float>();
			for (int k = 0; k < _mesh.NodeCount; k++)
			{
				if (!_mesh.NodeDeleted[k] && _mesh.NodeDegree(k) <= 0 && !(floatValues[k] <= 0f))
				{
					priorityQueue2.Enqueue(k, floatValues[k]);
					if (priorityQueue2.Count > -integrateSizeFunctionSumTerm)
					{
						priorityQueue2.Dequeue();
					}
				}
			}
			int t2 = -1;
			while (priorityQueue2.Count > 0)
			{
				int dequeue = priorityQueue2.Dequeue();
				int pointIdx = _meshToTin[dequeue];
				_dTin.DynDeletePoint(pointIdx, ref t2);
				_mesh.DeleteNode(dequeue);
				_meshToTin[dequeue] = -1;
			}
		}
	}

	private double IntegrateSizeFunction(float La, float Lb, float Lc, double area, double unitCellArea)
	{
		double tolerance = 0.0001;
		bool laLbTerm = Math.Abs((double)(La / Lb) - 1.0) > tolerance;
		bool lbLcTerm = Math.Abs((double)(Lb / Lc) - 1.0) > tolerance;
		bool lcLaTerm = (laLbTerm | lbLcTerm) && Math.Abs((double)(Lc / La) - 1.0) > tolerance;
		double unitCellAreaLcTerm;
		if (laLbTerm & lbLcTerm & lcLaTerm)
		{
			unitCellAreaLcTerm = -1.0 / unitCellArea * 1.0 / (double)(Lc - La) * (Math.Log(Lb / Lc) / (double)(Lb - Lc) - Math.Log(Lb / La) / (double)(Lb - La));
		}
		else if (!laLbTerm && !lbLcTerm)
		{
			unitCellAreaLcTerm = 0.5 / (unitCellArea * Math.Pow(La, 2.0));
		}
		else
		{
			double laTerm;
			double lcTerm;
			if (!laLbTerm & lbLcTerm)
			{
				laTerm = La;
				lcTerm = Lc;
			}
			else if (!lbLcTerm & lcLaTerm)
			{
				laTerm = Lb;
				lcTerm = La;
			}
			else
			{
				if (!(!lcLaTerm & laLbTerm))
				{
					throw new Exception("Logic fell through");
				}
				laTerm = Lc;
				lcTerm = Lb;
			}
			unitCellAreaLcTerm = 1.0 / (unitCellArea * (lcTerm - laTerm)) * (1.0 / laTerm - Math.Log(laTerm / lcTerm) / (laTerm - lcTerm));
		}
		return unitCellAreaLcTerm * area / 0.5;
	}

	private void UpdateSizes()
	{
		foreach (int nodeIterator in _mesh.NodeIterator())
		{
			if (!(_localSizes[nodeIterator] > 0f))
			{
				_localSizes[nodeIterator] = (float)_sizeFunction.Query(_mesh.NodePoints[nodeIterator]);
			}
		}
	}

	private void UpdateForces(float[] forceMagnitudes = null)
	{
		for (int i = 0; i < _mesh.NodeCount; i++)
		{
			_forces[i] = Vector.Zero;
		}
		double computed = 0.0;
		double distanceSquaredToSum = 0.0;
		List<TriangleDefinition> triangleDefinition = _dTin.Tin.TriangleDefinition;
		for (int j = 0; j < _dTin.Tin.TriangleCount; j++)
		{
			if (triangleDefinition[j].IsDeleted)
			{
				continue;
			}
			TriangleDefinition triangleDefinition2 = triangleDefinition[j];
			for (int k = 0; k < 3; k++)
			{
				triangleDefinition2.EdgePoints(k, out var ptA, out var ptB);
				bool isConstrained = _dTin.IsConstrained(ptA, ptB);
				bool isDeleted = ptA < ptB || triangleDefinition[triangleDefinition2.AdjacentTriangleIdx(k)].IsDeleted;
				if (!isConstrained && !isDeleted)
				{
					continue;
				}
				int tinDTinTerm = (int)_dTin.Tin.Z[ptA];
				int tinDTinTerm2 = (int)_dTin.Tin.Z[ptB];
				double compressionFactorLocalSizesTerm = _compressionFactor * 0.5 * (double)(_localSizes[tinDTinTerm] + _localSizes[tinDTinTerm2]);
				double computed2 = Math.Pow(compressionFactorLocalSizesTerm, 2.0);
				compressionFactorLocalSizesTerm *= _scaleFactor;
				if (isDeleted)
				{
					double distanceSquaredTo = _dTin.Tin[ptA].DistanceSquaredTo(_dTin.Tin[ptB]);
					distanceSquaredToSum += distanceSquaredTo;
					computed += computed2;
					if (distanceSquaredTo < compressionFactorLocalSizesTerm * compressionFactorLocalSizesTerm)
					{
						double sqrtDistanceSquaredTo = Math.Sqrt(distanceSquaredTo);
						double computed3 = compressionFactorLocalSizesTerm - sqrtDistanceSquaredTo;
						Vector vector = computed3 * (_dTin.Tin[ptB] - _dTin.Tin[ptA]).UnitVector();
						_forces[tinDTinTerm] -= vector;
						_forces[tinDTinTerm2] += vector;
						if (forceMagnitudes != null)
						{
							forceMagnitudes[tinDTinTerm] += (float)(computed3 / compressionFactorLocalSizesTerm);
							forceMagnitudes[tinDTinTerm2] += (float)(computed3 / compressionFactorLocalSizesTerm);
						}
					}
				}
				if (!isConstrained)
				{
					continue;
				}
				int edgePointOpposite = triangleDefinition2.EdgePointOpposite(k);
				AddMultipointForce(ptA, ptB, edgePointOpposite, compressionFactorLocalSizesTerm);
				Segment segment = new Segment(_dTin.Tin[ptA], _dTin.Tin[ptB]);
				double perpendicularSignedDistanceSegmentTerm = 1.155 * segment.PerpendicularSignedDistance(_dTin.Tin[edgePointOpposite]);
				double segmentTerm = Math.Min(compressionFactorLocalSizesTerm, segment.Length);
				Vector vector2 = segment.AsVector().Perpendicular(clockwise: false);
				if (perpendicularSignedDistanceSegmentTerm < segmentTerm)
				{
					int tinDTinTerm3 = (int)_dTin.Tin.Z[edgePointOpposite];
					double computed4 = segmentTerm - perpendicularSignedDistanceSegmentTerm;
					_forces[tinDTinTerm3] += computed4 * vector2;
					if (forceMagnitudes != null)
					{
						forceMagnitudes[tinDTinTerm3] += (float)(computed4 / segmentTerm);
					}
				}
			}
		}
		_scaleFactor = Math.Sqrt(distanceSquaredToSum / computed);
	}

	private void AddMultipointForce(int pA, int pB, int pC, double targetLength)
	{
		int tinDTinTerm = (int)_dTin.Tin.Z[pA];
		int tinDTinTerm2 = (int)_dTin.Tin.Z[pB];
		int tinDTinTerm3 = (int)_dTin.Tin.Z[pC];
		HalfEdge halfEdge = _mesh.NodesToHalfEdge(tinDTinTerm, tinDTinTerm2);
		if (halfEdge.IsNull())
		{
			throw new StructuredException(MessageCodes.GetTinConstraintError(_dTin.Tin[pA]));
		}
		Polyline polyline = _mesh.FaceSmoothLine[halfEdge.fIdx] ?? _mesh.ComputeSubFaceGeometry(halfEdge.fIdx, constrained: true, interior: false);
		if (polyline.PointCount <= 2)
		{
			return;
		}
		Polygon polygon = new Polygon();
		if (halfEdge.orient == 0)
		{
			polyline = polyline.Clone();
			polyline.Reverse();
		}
		for (int i = 0; i < 2; i++)
		{
			HalfEdge halfEdge2 = ((i == 0) ? _mesh.NodesToHalfEdge(tinDTinTerm2, tinDTinTerm3) : _mesh.NodesToHalfEdge(tinDTinTerm3, tinDTinTerm));
			if (!halfEdge2.IsNull() && _mesh.FaceConstraints[halfEdge2.fIdx] != ConstraintState.FREE)
			{
				continue;
			}
			Segment segment = ((i != 0) ? new Segment(_dTin.Tin[pC], _dTin.Tin[pA]) : new Segment(_dTin.Tin[pB], _dTin.Tin[pC]));
			if (i == 0)
			{
				if (segment.A != polyline.First())
				{
					throw new Exception("Smooth does not match triangles face");
				}
			}
			else if (segment.B != polyline.Last())
			{
				throw new Exception("Smooth does not match triangles face");
			}
			polygon.Clear();
			polygon.Add(segment.A);
			double counterClockwiseTerm = ((i == 0) ? 0.0 : 1.0);
			for (int j = 0; j < polyline.SegmentCount; j++)
			{
				Segment segment2 = polyline.Segments[j];
				double counterClockwise = NativePredicates.CounterClockwise(segment.A, segment.B, segment2.B);
				if (!(counterClockwiseTerm >= 0.0) || !(counterClockwise > 0.0))
				{
					if (counterClockwiseTerm * counterClockwise < 0.0)
					{
						Segment.IntersectsLineSegment(segment.A, segment.B, segment2.A, segment2.B, out var intersectingPoint);
						if (intersectingPoint.IsNull())
						{
							throw new Exception("Unexpected parallel lines");
						}
						polygon.Add(intersectingPoint);
						if (counterClockwise < 0.0)
						{
							polygon.Add(segment2.B);
						}
					}
					else
					{
						if ((!(counterClockwiseTerm <= 0.0) || !(counterClockwise < 0.0)) && counterClockwise != 0.0)
						{
							throw new Exception("Logic failure");
						}
						polygon.Add(segment2.B);
					}
				}
				counterClockwiseTerm = counterClockwise;
			}
			if (i == 0)
			{
				polygon.Add(segment.B);
			}
			if (polygon.Count > 2)
			{
				_ = polygon.Area;
				Point pt = polygon.ComputeCentroid();
				segment.NearestPoint(pt, out var t);
				Vector vector = -0.5 * targetLength * segment.PerpendicularUnitVector();
				_forces[tinDTinTerm] += (1.0 - t) * vector;
				_forces[tinDTinTerm2] += t * vector;
			}
		}
	}

	private void UpdatePositions()
	{
		double computed = 0.2;
		int ordinal = 0;
		float computed2 = 0f;
		int t = -1;
		foreach (int movableNode in _mesh.MovableNodes())
		{
			Vector vector = computed * _forces[movableNode];
			Point point = _mesh.NodePoints[movableNode];
			Point p = _mesh.NodePoints[movableNode] + vector;
			if (!(vector.LengthSquared < Math.Pow(_minRelativeMove * (double)_localSizes[movableNode], 2.0)) && _mesh.MoveOrSlideNode(movableNode, p))
			{
				if (!_dTin.DynMovePoint(_meshToTin[movableNode], _mesh.NodePoints[movableNode], ref t, checkInside: true))
				{
					_mesh.MoveOrSlideNode(movableNode, point, check: false);
					continue;
				}
				ordinal++;
				computed2 += (float)_mesh.NodePoints[movableNode].DistanceSquaredTo(point);
				_localSizes[movableNode] = -1f;
			}
		}
		computed2 = MathF.Sqrt(computed2);
	}

	private void PrepareMesh()
	{
		foreach (int cellIterator in _mesh.CellIterator())
		{
			_mesh.DeleteCell(cellIterator);
		}
		double faceLengthSquaredSum = 0.0;
		double querySquaredSum = 0.0;
		foreach (int faceIterator in _mesh.FaceIterator())
		{
			faceLengthSquaredSum += _mesh.FaceLengthSquared(faceIterator);
			querySquaredSum += Math.Pow(_sizeFunction.Query(_mesh.FaceMidPoint(faceIterator)), 2.0);
			if (_mesh.FaceConstraints[faceIterator] == ConstraintState.FREE)
			{
				_mesh.DeleteFaceCascade(faceIterator);
			}
		}
		_scaleFactor = Math.Sqrt(faceLengthSquaredSum / querySquaredSum);
	}

	private void UpdateMesh()
	{
		List<TriangleDefinition> triangleDefinition = _dTin.Tin.TriangleDefinition;
		for (int i = 0; i < _dTin.Tin.TriangleCount; i++)
		{
			if (!triangleDefinition[i].IsDeleted)
			{
				int tinDTinTerm = (int)_dTin.Tin.Z[triangleDefinition[i].p0];
				int tinDTinTerm2 = (int)_dTin.Tin.Z[triangleDefinition[i].p1];
				int tinDTinTerm3 = (int)_dTin.Tin.Z[triangleDefinition[i].p2];
				_mesh.AddCellFromNodes(new List<int> { tinDTinTerm, tinDTinTerm2, tinDTinTerm3 }, ElementMode.CREATE_OR_EXISTING);
			}
		}
	}

	private DynamicTINFactory<NativePredicates> InitializeTriangulation()
	{
		DynamicTINFactory<NativePredicates> DTin = new DynamicTINFactory<NativePredicates>();
		DTin.OnEdgeOverConstrained = (ConstrainedEdge a, ConstrainedEdge b) =>
		{
			Point location = DTin.Tin.Points[a.PointA];
			throw new MeshTriangulationError("Mesh could not be triangulated", location);
		};
		List<Point> points = new List<Point>();
		List<double> doubleList = new List<double>();
		foreach (int nodeIterator in _mesh.NodeIterator())
		{
			if (_mesh.NodeDegree(nodeIterator) <= 0)
			{
				points.Add(_mesh.NodePoints[nodeIterator]);
				doubleList.Add(nodeIterator);
			}
		}
		if (points.Count > 0)
		{
			DTin.AddMassPoints(new PointCollection(points, doubleList));
		}
		List<List<int>> extractNodeSequences = _mesh.ExtractNodeSequences(extractPerimeter: true);
		for (int intervalIndex = 0; intervalIndex < extractNodeSequences.Count; intervalIndex++)
		{
			List<int> extractNodeSequence = extractNodeSequences[intervalIndex];
			points.Clear();
			doubleList.Clear();
			bool extractNodeSequenceTerm = extractNodeSequence.First() == extractNodeSequence.Last() && extractNodeSequence.Count() > 3;
			if (extractNodeSequenceTerm)
			{
				extractNodeSequence.RemoveAt(0);
			}
			foreach (int extractNodeSequence2 in extractNodeSequence)
			{
				points.Add(_mesh.NodePoints[extractNodeSequence2]);
				doubleList.Add(extractNodeSequence2);
			}
			if (extractNodeSequenceTerm)
			{
				Polygon polygon = new Polygon(points, doubleList);
				if (intervalIndex == 0)
				{
					DTin.AddPerimeter(polygon);
				}
				else
				{
					DTin.AddBreakline(polygon);
				}
			}
			else
			{
				Polyline breakline = new Polyline(points, doubleList);
				DTin.AddBreakline(breakline);
			}
		}
		DTin.ComputeTIN();
		for (int meshToTin = 0; meshToTin < DTin.Tin.PointCount; meshToTin++)
		{
			int tinDTinTerm = (int)DTin.Tin.Z[meshToTin];
			_meshToTin[tinDTinTerm] = meshToTin;
		}
		return DTin;
	}

	public void DumpToJson(string filename = null)
	{
		using FileStream utf8Json = File.Create(filename ?? Path.Combine(Path.GetTempPath(), "hecras-force-displacement.json"));
		JsonSerializerOptions options = new JsonSerializerOptions
		{
			WriteIndented = true,
			NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
			Converters = { (JsonConverter)new TinJsonConverter() }
		};
		List<double[]> forcesVectorTerm = _forces.Select((Vector v) => new double[2] { v.X, v.Y }).ToList();
		List<object> objectList = new List<object>();
		if (_history != null)
		{
			objectList.AddRange(_history);
		}
		else
		{
			objectList.Add(new List<object> { "PYTHON", "tins=[]" });
		}
		objectList.Add(new List<object> { "PYTHON", "arcs=[]" });
		for (int intervalIndex = 0; intervalIndex < _mesh.constraints.Count; intervalIndex++)
		{
			objectList.Add(new List<object>
			{
				"SET",
				"tmp_arc",
				DynamicMesh.SerializePolyline(_mesh.constraints[intervalIndex].Polyline)
			});
			objectList.Add(new List<object> { "@PYTHON", "arcs.append(np.array(tmp_arc))" });
		}
		objectList.Add(new List<object> { "SET", "g_raw", _mesh });
		objectList.Add(new List<object> { "SET", "tin_raw", _dTin.Tin });
		objectList.Add(new List<object> { "SET", "forces_raw", forcesVectorTerm });
		objectList.Add(new List<object> { "PYTHON", "\r\nimport matplotlib.widgets as mw\r\ng=json_to_mesh(g_raw)\r\nfig,ax=plt.subplots(num=1,clear=1)\r\nax.set_adjustable('datalim')\r\n#g.plot_nodes(ax = ax)\r\n#g.plot_edges(ax = ax, color = 'm', subedges='smoothStringXY')\r\nfor arc in arcs:\r\n    ax.plot(arc[:,0],arc[:,1],color='0.5',lw=0.5)\r\nbounds = utils.expand_xxyy(g.bounds(),0.05)\r\n#g.plot_cells(ax = ax, color = 'orange', subedges='smoothStringXY', alpha = 0.4, zorder = -1)\r\ndt=json_to_mesh(tin_raw)\r\n#for c in np.nonzero(dt.cells['state'] == 1)[0]:\r\n#    dt.delete_cell(c)\r\n#dt.delete_naked_edges()\r\n#dt.delete_naked_nodes()\r\ndt.plot_edges(ax=ax, color='r', lw=0.5, alpha=0.4)\r\ndt.plot_cells(ax=ax, values=dt.cells['state'],cmap='rainbow',alpha=0.4,\r\n              zorder=-1)\r\n# animate the process:\r\nif len(tins):\r\n    #ax.collections[1].set_visible(0)\r\n    slide_ax = fig.add_axes([0.1, 0.05, 0.8, 0.03])\r\n    slider = mw.Slider(slide_ax, 'Iteration', valmin=0, valmax=len(tins)-1,\r\n                       valinit=0,valstep=1.0)\r\n    def update_iteration(step):\r\n        step=int(step)\r\n        ax.collections[len(ax.collections)-1].remove()\r\n        tins[step].plot_edges(color='r',lw=0.5,alpha=0.8,ax=ax)    \r\n        ax.axis(bounds)\r\n    update_iteration(0)\r\n    slider.on_changed(update_iteration)\r\n" });
		JsonSerializer.Serialize(utf8Json, objectList, options);
	}
}
