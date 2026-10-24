using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geospatial.Rasters;
using Utility;
using Utility.Extensions;
using Utility.Logging;

namespace Geospatial.Vectors.MeshTopology;

public class TinSizeFunction : ISizeFunction
{
	public enum NodeState
	{
		FREE,
		FIXED
	}

	public class MessageCodes
	{
		public static readonly MessageClass InsufficientSizeInformation = new MessageClass("TinSizeFunction_InvalidRegion", MessageSeverity.Error, "Insufficient size information was supplied. Add cell size to more arcs or regions.");

		public static Message GetInsufficientSizeInformation()
		{
			return InsufficientSizeInformation.Create();
		}
	}

	private double noDataValue = -9999.0;

	private Tin _tin;

	private int _tri;

	public Func<Point, SizeParams> sizeParamFunction;

	private bool useDiffusion;

	public Tin Tin => _tin;

	public TinSizeFunction(SizeMesh mesh, double fillBias = 0.0)
	{
		useDiffusion = true;
		_tin = mesh.ComputeTin(fillBias, double.NaN);
		FillSizes();
	}

	public TinSizeFunction(Tin tin, double telescopeRate = 0.1, NodeState[] nodeState = null)
	{
		if (telescopeRate == 0.0)
		{
			useDiffusion = true;
		}
		else
		{
			sizeParamFunction = PointToSizeParams;
		}
		_tin = tin;
		FillSizes(nodeState);
		SizeParams PointToSizeParams(Point point)
		{
			return new SizeParams(double.NaN, telescopeRate, -1);
		}
	}

	public TinSizeFunction(Tin tin, Func<Point, SizeParams> PointToSizeParams, NodeState[] nodeState = null)
	{
		sizeParamFunction = PointToSizeParams;
		_tin = tin;
		FillSizes(nodeState);
	}

	public void FillSizes(NodeState[] nodeState = null)
	{
		int intervalIndex = 0;
		double[] pathDistance3;
		if (!useDiffusion)
		{
			double[] fillByTelescoping = FillByTelescoping(_tin, 1.0, nodeState, sizeParamFunction, out var pathDistance, boundByFree: false);
			double[] fillByTelescopingArray = FillByTelescoping(_tin, -1.0, nodeState, sizeParamFunction, out var pathDistance2, boundByFree: false);
			for (intervalIndex = 0; intervalIndex < _tin.Count; intervalIndex++)
			{
				double tinTerm = _tin.Z[intervalIndex];
				if (double.IsFinite(tinTerm))
				{
					if (tinTerm < fillByTelescopingArray[intervalIndex])
					{
						_tin.Z[intervalIndex] = fillByTelescopingArray[intervalIndex];
					}
					else if (tinTerm > fillByTelescoping[intervalIndex])
					{
						_tin.Z[intervalIndex] = fillByTelescoping[intervalIndex];
					}
				}
			}
			double[] fillByTelescoping2 = FillByTelescoping(_tin, 1.0, nodeState, sizeParamFunction, out pathDistance3);
			double[] fillByTelescoping3 = FillByTelescoping(_tin, -1.0, nodeState, sizeParamFunction, out pathDistance3);
			if (!_tin.HasM)
			{
				_tin.TrackM();
			}
			for (intervalIndex = 0; intervalIndex < _tin.Count; intervalIndex++)
			{
				if (fillByTelescoping[intervalIndex] < fillByTelescopingArray[intervalIndex])
				{
					if (intervalIndex >= nodeState.Length || nodeState[intervalIndex] == NodeState.FREE)
					{
						double pathDistancePathDistance2Term = pathDistance[intervalIndex] + pathDistance2[intervalIndex];
						double pathDistance2Term = ((pathDistancePathDistance2Term == 0.0) ? 0.5 : (pathDistance2[intervalIndex] / pathDistancePathDistance2Term));
						fillByTelescopingArray[intervalIndex] = (fillByTelescoping[intervalIndex] = pathDistance2Term * fillByTelescoping[intervalIndex] + (1.0 - pathDistance2Term) * fillByTelescopingArray[intervalIndex]);
						_tin.Z[intervalIndex] = fillByTelescoping[intervalIndex];
					}
				}
				else
				{
					double fillByTelescopingFillByTelescoping2Term = Math.Min(fillByTelescoping[intervalIndex], Math.Max(fillByTelescoping2[intervalIndex], fillByTelescopingArray[intervalIndex]));
					double fillByTelescopingFillByTelescoping3Term = Math.Min(fillByTelescoping[intervalIndex], Math.Max(fillByTelescoping3[intervalIndex], fillByTelescopingArray[intervalIndex]));
					if (fillByTelescopingFillByTelescoping2Term < fillByTelescopingFillByTelescoping3Term)
					{
						IList<double> z = _tin.Z;
						int index = intervalIndex;
						double tinTerm2 = (_tin.M[intervalIndex] = 0.5 * (fillByTelescopingFillByTelescoping2Term + fillByTelescopingFillByTelescoping3Term));
						z[index] = tinTerm2;
					}
					else
					{
						SizeParams sizeParams = sizeParamFunction(_tin[intervalIndex]);
						if (double.IsFinite(sizeParams.target))
						{
							if (sizeParams.target > fillByTelescopingFillByTelescoping2Term)
							{
								_tin.Z[intervalIndex] = fillByTelescopingFillByTelescoping2Term;
							}
							else if (sizeParams.target < fillByTelescopingFillByTelescoping3Term)
							{
								_tin.Z[intervalIndex] = fillByTelescopingFillByTelescoping3Term;
							}
							else
							{
								_tin.Z[intervalIndex] = sizeParams.target;
							}
						}
						else
						{
							_tin.Z[intervalIndex] = 0.5 * (fillByTelescopingFillByTelescoping2Term + fillByTelescopingFillByTelescoping3Term);
						}
					}
				}
				_tin.M[intervalIndex] = _tin.Z[intervalIndex];
			}
		}
		else
		{
			FillByDiffusion(_tin);
			double[] fillByTelescoping4 = FillByTelescoping(_tin, 1.0, nodeState, DefaultSizeParams, out pathDistance3);
			for (intervalIndex = 0; intervalIndex < _tin.Count; intervalIndex++)
			{
				_tin.Z[intervalIndex] = fillByTelescoping4[intervalIndex];
			}
		}
		static SizeParams DefaultSizeParams(Point p)
		{
			return new SizeParams(10000000000.0, 0.1, -1);
		}
	}

	private static void FillByDiffusion(Tin tin)
	{
		TinLaplacian tinLaplacian = new TinLaplacian(tin);
		double[] doubleValues = tin.Z.ToArray();
		tinLaplacian.SolveLaplacian(doubleValues);
		for (int i = 0; i < tin.Count; i++)
		{
			tin.Z[i] = doubleValues[i];
		}
	}

	private static double[] FillByTelescoping(Tin tin, double sign, NodeState[] nodeState, Func<Point, SizeParams> PointToSizeParams, out double[] pathDistance, bool boundByFree = true)
	{
		double[] newZ = tin.Z.ToArray();
		int tri = 0;
		HashSet<int> visited = new HashSet<int>();
		PriorityQueue<int, double> priorityQueue = new PriorityQueue<int, double>();
		double[] distances = new double[newZ.Length];
		pathDistance = distances;
		for (int i = 0; i < tin.Count; i++)
		{
			distances[i] = 0.0;
		}
		if (nodeState != null && !boundByFree)
		{
			for (int j = 0; j < tin.Count; j++)
			{
				if (GetNodeState(j) != NodeState.FIXED)
				{
					newZ[j] = double.NaN;
				}
			}
		}
		for (int k = 0; k < tin.Count; k++)
		{
			if (GetNodeState(k) == NodeState.FIXED)
			{
				visited.Add(k);
				Visit(k);
			}
			else if (double.IsFinite(newZ[k]))
			{
				priorityQueue.Enqueue(k, sign * newZ[k]);
			}
		}
		int element;
		double priority;
		while (priorityQueue.TryDequeue(out element, out priority))
		{
			if (!(priority > sign * newZ[element]))
			{
				visited.Add(element);
				Visit(element);
			}
		}
		double newZ2 = ((sign > 0.0) ? 100000000.0 : 0.0);
		for (int l = 0; l < tin.Count; l++)
		{
			if (double.IsNaN(newZ[l]))
			{
				newZ[l] = newZ2;
			}
		}
		return newZ;
		NodeState GetNodeState(int ordinal)
		{
			if (nodeState != null)
			{
				if (ordinal < nodeState.Length)
				{
					return nodeState[ordinal];
				}
			}
			else if (double.IsFinite(tin.Z[ordinal]))
			{
				return NodeState.FIXED;
			}
			return NodeState.FREE;
		}
		void Visit(int ordinal2)
		{
			if (ordinal2 < 8)
			{
				return;
			}
			Point point = tin.Points[ordinal2];
			tin.FindTriangleContainingPoint(point, ref tri);
			foreach (Tuple<int, bool> pointsAroundPointCCW in tin.PointsAroundPointCCW(ordinal2, tri))
			{
				int pointsAroundPointCCWTerm = pointsAroundPointCCW.Item1;
				bool pointsAroundPointCCWTerm2 = pointsAroundPointCCW.Item2;
				if (!visited.Contains(pointsAroundPointCCWTerm) && GetNodeState(pointsAroundPointCCWTerm) != NodeState.FIXED && !((pointsAroundPointCCWTerm >= 8) & pointsAroundPointCCWTerm2))
				{
					double maxValue = double.MaxValue;
					EdgeDefinition edgeDefinition = tin.FindEdge(ordinal2, pointsAroundPointCCWTerm, ref tri);
					if (edgeDefinition.ATriangleIndex >= 0)
					{
						Point centroid = tin.Triangles[edgeDefinition.ATriangleIndex].Centroid();
						SizeParams sizeParams = PointToSizeParams(centroid);
						if (double.IsFinite(sizeParams.stretchRate))
						{
							maxValue = sizeParams.stretchRate;
						}
					}
					if (edgeDefinition.BTriangleIndex >= 0)
					{
						Point centroid2 = tin.Triangles[edgeDefinition.BTriangleIndex].Centroid();
						SizeParams sizeParams2 = PointToSizeParams(centroid2);
						if (double.IsFinite(sizeParams2.stretchRate))
						{
							maxValue = Math.Min(maxValue, sizeParams2.stretchRate);
						}
					}
					if (maxValue == double.MaxValue)
					{
						maxValue = 0.1;
					}
					double signTimesMaxValue = sign * maxValue;
					double distanceTo = tin.Points[pointsAroundPointCCWTerm].DistanceTo(point);
					double newZ3 = newZ[ordinal2] + signTimesMaxValue * distanceTo;
					if (double.IsNaN(newZ[pointsAroundPointCCWTerm]) || sign * newZ3 < sign * newZ[pointsAroundPointCCWTerm])
					{
						newZ[pointsAroundPointCCWTerm] = newZ3;
						distances[pointsAroundPointCCWTerm] = distances[ordinal2] + distanceTo;
						priorityQueue.Enqueue(pointsAroundPointCCWTerm, sign * newZ[pointsAroundPointCCWTerm]);
					}
				}
			}
		}
	}

	public double Query(Point point, bool checkInside = false)
	{
		return Query(point, checkInside, minimum: false);
	}

	public double Query(Point point, bool checkInside, bool minimum)
	{
		int t = _tri;
		_tin.FindTriangleContainingPoint(point, ref t);
		if (t == -1)
		{
			return noDataValue;
		}
		_tri = t;
		if (checkInside && _tin.TriangleDefinition[t].IsDeleted)
		{
			return noDataValue;
		}
		double m;
		if (!minimum)
		{
			_tin.Triangle3D[t].ComputeZ(point, out m);
		}
		else
		{
			_tin.Triangle4D[t].ComputeM(point, out m);
		}
		if (!double.IsFinite(m))
		{
			throw new Exception($"Size function had invalid value {m}");
		}
		return m;
	}

	public void QueryRange(Point point, bool checkInside, out double maximum, out double minimum)
	{
		int t = _tri;
		_tin.FindTriangleContainingPoint(point, ref t);
		maximum = (minimum = noDataValue);
		if (t != -1)
		{
			_tri = t;
			if (!checkInside || !_tin.TriangleDefinition[t].IsDeleted)
			{
				Triangle4D triangle4D = _tin.Triangle4D[t];
				triangle4D.ComputeZ(point, out maximum);
				triangle4D.ComputeM(point, out minimum);
			}
		}
	}

	public RasterData<float> Rasterize(double pixCount = 500000.0, bool omitDeleted = true)
	{
		RasterDefinition rasterDefinition = new RasterDefinition(_tin.Extent.Extend(0.1 * _tin.Extent.Width), Math.Sqrt(_tin.Extent.Area / pixCount));
		float[] floatValues = new float[rasterDefinition.CellCount];
		floatValues.Fill(float.NaN);
		float[] zValues = _tin.Z.Select((double z) => (float)z).ToArray();
		Rasterize(rasterDefinition, zValues, floatValues, omitDeleted);
		return new RasterData<float>(rasterDefinition, floatValues);
	}

	public void Rasterize(RasterDefinition raster, float[] zValues, float[] outBuffer, bool omitDeleted = true)
	{
		if (zValues == null)
		{
			throw new ArgumentNullException("zValues");
		}
		if (outBuffer == null)
		{
			throw new ArgumentNullException("outBuffer");
		}
		if (zValues.Length != _tin.PointCount)
		{
			throw new BufferSizeException("zValues", _tin.PointCount);
		}
		if (outBuffer.Length != raster.CellCount)
		{
			throw new BufferSizeException("outBuffer", raster.CellCount);
		}
		if (!_tin.Extent.Overlaps(raster.Extent))
		{
			return;
		}
		List<int> elements = _tin.TriangleSpatialIndex.GetElements(raster.Extent);
		for (int i = 0; i < elements.Count; i++)
		{
			int index = elements[i];
			Triangle triangle = _tin.Triangles[index];
			TriangleDefinition triDef = _tin.TriangleDefinition[index];
			if (!omitDeleted || !triDef.IsDeleted)
			{
				triangle.Rasterize(raster, (int px, BarycentricWeights wts) =>
				{
					outBuffer[px] = wts.Interpolate(zValues[triDef.p0], zValues[triDef.p1], zValues[triDef.p2]);
				});
			}
		}
	}

	public virtual void DumpToJson(List<object> preamble = null, string filename = "C:\\Temp\\cmd.json")
	{
		List<object> objectList = new List<object>();
		if (preamble != null)
		{
			objectList.AddRange(preamble);
		}
		objectList.AddRange(new List<object>
		{
			new List<object> { "SET", "dt_raw", Tin },
			new List<object> { "PYTHON", "\r\ndt=json_to_mesh(dt_raw)\r\n# Clean up external cells\r\nfor c in np.nonzero(dt.cells['state']==1)[0]:\r\n    dt.delete_cell(c)\r\ndt.delete_naked_edges()\r\ndt.delete_naked_nodes()\r\n\r\nfig,ax=plt.subplots(num=1,clear=1)\r\nax.set_adjustable('datalim')\r\ndt.plot_edges(ax=ax,color='k',lw=0.5)\r\nclim=[np.nanmin(dt.nodes['Z']), np.nanmax(dt.nodes['Z'])]\r\nif np.all(np.isfinite(dt.nodes['Z'])):\r\n    artist = dt.contourf_node_values(dt.nodes['Z'],cmap='turbo',clim=clim,extend='both')\r\nelse:\r\n    artist = dt.tripcolor_node_values(dt.nodes['Z'],cmap='turbo',clim=clim, ax=ax)\r\nplt.colorbar(artist,ax=ax,label='Z')\r\nax.axis('equal')\r\n" }
		});
		using FileStream utf8Json = File.Create(filename);
		JsonSerializer.Serialize(utf8Json, objectList, new JsonSerializerOptions
		{
			WriteIndented = true,
			NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
			Converters = { (JsonConverter)new TinJsonConverter() }
		});
	}
}
