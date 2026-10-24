using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.TinHelpers;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public abstract class TINFactory
{
	protected double _tolerance;

	protected PointMs _points;

	protected List<DevelopmentTriangle> _triangles;

	protected Stack<int> _deletedTrianglesStack;

	[SpecialName]
	private int _0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t;

	[SpecialName]
	private StaticLocalInitFlag _0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t_0024Init;

	[SpecialName]
	private double[] _0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd;

	[SpecialName]
	private StaticLocalInitFlag _0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd_0024Init;

	[SpecialName]
	private int _0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024t;

	[SpecialName]
	private StaticLocalInitFlag _0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024t_0024Init;

	[SpecialName]
	private double[] _0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024crossProd;

	[SpecialName]
	private StaticLocalInitFlag _0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024crossProd_0024Init;

	protected TINFactory(double nearPointTolerance)
	{
		_points = new PointMs();
		_tolerance = nearPointTolerance;
		_triangles = new List<DevelopmentTriangle>();
		_deletedTrianglesStack = new Stack<int>();
	}

	protected static bool Validate(PointMs points, List<DevelopmentTriangle> triangles)
	{
		checked
		{
			int count = triangles.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				DevelopmentTriangle developmentTriangle = triangles[i];
				if (points[developmentTriangle.p0].X == points[developmentTriangle.p1].X && points[developmentTriangle.p0].X == points[developmentTriangle.p2].X)
				{
					return false;
				}
				if (points[developmentTriangle.p0].Y == points[developmentTriangle.p1].Y && points[developmentTriangle.p0].Y == points[developmentTriangle.p2].Y)
				{
					return false;
				}
				if (developmentTriangle.at0 == -1 && developmentTriangle.at1 == -1 && developmentTriangle.at2 == -1)
				{
					return false;
				}
				if (developmentTriangle.at0 != -1)
				{
					DevelopmentTriangle developmentTriangle2 = triangles[developmentTriangle.at0];
					if (developmentTriangle2.at0 != i && developmentTriangle2.at1 != i && developmentTriangle2.at2 != i)
					{
						return false;
					}
					if (developmentTriangle.at0 == developmentTriangle.at1 || developmentTriangle.at0 == developmentTriangle.at2)
					{
						return false;
					}
				}
				if (developmentTriangle.at1 != -1)
				{
					DevelopmentTriangle developmentTriangle3 = triangles[developmentTriangle.at1];
					if (developmentTriangle3.at0 != i && developmentTriangle3.at1 != i && developmentTriangle3.at2 != i)
					{
						return false;
					}
					if (developmentTriangle.at1 == developmentTriangle.at0 || developmentTriangle.at1 == developmentTriangle.at2)
					{
						return false;
					}
				}
				if (developmentTriangle.at2 != -1)
				{
					DevelopmentTriangle developmentTriangle4 = triangles[developmentTriangle.at2];
					if (developmentTriangle4.at0 != i && developmentTriangle4.at1 != i && developmentTriangle4.at2 != i)
					{
						return false;
					}
					if (developmentTriangle.at2 == developmentTriangle.at0 || developmentTriangle.at2 == developmentTriangle.at1)
					{
						return false;
					}
				}
			}
			return true;
		}
	}

	protected int AddTriangle(int p0, int p1, int p2, int at0, int at1, int at2)
	{
		DevelopmentTriangle developmentTriangle = new DevelopmentTriangle(p0, p1, p2, at0, at1, at2);
		int popDeletedTrianglesStackTerm;
		if (_deletedTrianglesStack.Count != 0)
		{
			popDeletedTrianglesStackTerm = _deletedTrianglesStack.Pop();
			_triangles[popDeletedTrianglesStackTerm] = developmentTriangle;
		}
		else
		{
			popDeletedTrianglesStackTerm = _triangles.Count;
			_triangles.Add(developmentTriangle);
		}
		return popDeletedTrianglesStackTerm;
	}

	protected void FindTriangleAndPointIndexes(PointM point, ref int t, ref int p)
	{
		t = FindTriangleContainingPoint(point);
		if (t == -1)
		{
			p = -1;
			return;
		}
		DevelopmentTriangle developmentTriangle = _triangles[t];
		if (point.CloseTo(_points[developmentTriangle.p0], _tolerance) || point.CloseTo(_points[developmentTriangle.p1], _tolerance) || point.CloseTo(_points[developmentTriangle.p2], _tolerance))
		{
			p = ClosestPoint(point, developmentTriangle.p0, developmentTriangle.p1, developmentTriangle.p2);
		}
		else
		{
			p = -1;
		}
	}

	protected void FindTriangleAndNearestPointIndex(PointM point, ref int t, ref int p)
	{
		t = FindTriangleContainingPoint(point);
		if (t == -1)
		{
			p = -1;
			return;
		}
		DevelopmentTriangle developmentTriangle = _triangles[t];
		p = ClosestPoint(point, developmentTriangle.p0, developmentTriangle.p1, developmentTriangle.p2);
	}

	public int FindTriangleContainingPoint(PointM point)
	{
		if (_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t_0024Init == null)
		{
			Interlocked.CompareExchange(ref _0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t_0024Init, new StaticLocalInitFlag(), null);
		}
		bool lockTaken = false;
		try
		{
			Monitor.Enter(_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t_0024Init, ref lockTaken);
			if (_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t_0024Init.State == 0)
			{
				_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t_0024Init.State = 2;
				_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t = 0;
			}
			else if (_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t_0024Init.State == 2)
			{
				throw new IncompleteInitialization();
			}
		}
		finally
		{
			_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t_0024Init.State = 1;
			if (lockTaken)
			{
				Monitor.Exit(_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t_0024Init);
			}
		}
		if (_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t < 0)
		{
			_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t = 0;
		}
		checked
		{
			if (_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t > _triangles.Count - 1)
			{
				_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t = 0;
			}
			int ordinal = 0;
			int ordinal2 = -1;
			int min3IndexAt0Term = -1;
			if (_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd_0024Init == null)
			{
				Interlocked.CompareExchange(ref _0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd_0024Init, new StaticLocalInitFlag(), null);
			}
			bool lockTaken2 = false;
			try
			{
				Monitor.Enter(_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd_0024Init, ref lockTaken2);
				if (_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd_0024Init.State == 0)
				{
					_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd_0024Init.State = 2;
					_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd = new double[3];
				}
				else if (_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd_0024Init.State == 2)
				{
					throw new IncompleteInitialization();
				}
			}
			finally
			{
				_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd_0024Init.State = 1;
				if (lockTaken2)
				{
					Monitor.Exit(_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd_0024Init);
				}
			}
			do
			{
				DevelopmentTriangle developmentTriangle = _triangles[_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t];
				_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd[0] = CrossProduct(_points[developmentTriangle.p0], _points[developmentTriangle.p1], point);
				_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd[1] = CrossProduct(_points[developmentTriangle.p1], _points[developmentTriangle.p2], point);
				_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd[2] = CrossProduct(_points[developmentTriangle.p2], _points[developmentTriangle.p0], point);
				int min3Index = RasMapperLib.Utilities.Math.Min3Index(_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd[0], _0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd[1], _0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd[2]);
				if (_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd[min3Index] >= 0.0)
				{
					return _0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t;
				}
				min3IndexAt0Term = min3Index switch
				{
					0 => developmentTriangle.at0, 
					1 => developmentTriangle.at1, 
					_ => developmentTriangle.at2, 
				};
				if (ordinal2 == min3IndexAt0Term)
				{
					return _0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t;
				}
				ordinal2 = _0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t;
				_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t = min3IndexAt0Term;
				ordinal++;
			}
			while (!((_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t == -1) | (ordinal == _triangles.Count)));
			return -1;
		}
	}

	public int FindTriangleContainingPoint2(PointM point)
	{
		if (_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024t_0024Init == null)
		{
			Interlocked.CompareExchange(ref _0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024t_0024Init, new StaticLocalInitFlag(), null);
		}
		bool lockTaken = false;
		try
		{
			Monitor.Enter(_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024t_0024Init, ref lockTaken);
			if (_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024t_0024Init.State == 0)
			{
				_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024t_0024Init.State = 2;
				_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024t = 0;
			}
			else if (_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024t_0024Init.State == 2)
			{
				throw new IncompleteInitialization();
			}
		}
		finally
		{
			_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024t_0024Init.State = 1;
			if (lockTaken)
			{
				Monitor.Exit(_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024t_0024Init);
			}
		}
		if (_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024t < 0)
		{
			_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024t = 0;
		}
		checked
		{
			if (_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024t > _triangles.Count - 1)
			{
				_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024t = 0;
			}
			int ordinal = 0;
			if (_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024crossProd_0024Init == null)
			{
				Interlocked.CompareExchange(ref _0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024crossProd_0024Init, new StaticLocalInitFlag(), null);
			}
			bool lockTaken2 = false;
			try
			{
				Monitor.Enter(_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024crossProd_0024Init, ref lockTaken2);
				if (_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024crossProd_0024Init.State == 0)
				{
					_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024crossProd_0024Init.State = 2;
					_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024crossProd = new double[3];
				}
				else if (_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024crossProd_0024Init.State == 2)
				{
					throw new IncompleteInitialization();
				}
			}
			finally
			{
				_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024crossProd_0024Init.State = 1;
				if (lockTaken2)
				{
					Monitor.Exit(_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024crossProd_0024Init);
				}
			}
			do
			{
				DevelopmentTriangle developmentTriangle = _triangles[_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024t];
				_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024crossProd[0] = CrossProduct(_points[developmentTriangle.p0], _points[developmentTriangle.p1], point);
				_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024crossProd[1] = CrossProduct(_points[developmentTriangle.p1], _points[developmentTriangle.p2], point);
				_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024crossProd[2] = CrossProduct(_points[developmentTriangle.p2], _points[developmentTriangle.p0], point);
				int min3Index = RasMapperLib.Utilities.Math.Min3Index(_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024crossProd[0], _0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024crossProd[1], _0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024crossProd[2]);
				if (_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024crossProd[min3Index] >= 0.0)
				{
					return _0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024t;
				}
				switch (min3Index)
				{
				case 0:
					_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024t = developmentTriangle.at0;
					break;
				case 1:
					_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024t = developmentTriangle.at1;
					break;
				default:
					_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024t = developmentTriangle.at2;
					break;
				}
				ordinal++;
			}
			while (!((_0024STATIC_0024FindTriangleContainingPoint2_002420181180F0_0024t == -1) | (ordinal == _triangles.Count)));
			return -1;
		}
	}

	protected double CrossProduct(PointM a, PointM b, PointM c)
	{
		return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
	}

	protected void UpdatePointZM(int p, PointM point)
	{
		if (point.HasZ())
		{
			PointM point2 = _points[p];
			point2.Z = point.Z;
			_points[p] = point2;
		}
		if (point.HasM())
		{
			PointM point3 = _points[p];
			point3.M = point.M;
			_points[p] = point3;
		}
	}

	protected void SetEdgeType(int t, int pA, int pB, bool isPerimeter)
	{
		DevelopmentTriangle developmentTriangle = _triangles[t];
		int at0DevelopmentTriangleTerm = default;
		if ((developmentTriangle.p0 == pA && developmentTriangle.p1 == pB) || (developmentTriangle.p0 == pB && developmentTriangle.p1 == pA))
		{
			developmentTriangle.SetEdge(0, isPerimeter);
			at0DevelopmentTriangleTerm = developmentTriangle.at0;
		}
		else if ((developmentTriangle.p1 == pA && developmentTriangle.p2 == pB) || (developmentTriangle.p1 == pB && developmentTriangle.p2 == pA))
		{
			developmentTriangle.SetEdge(1, isPerimeter);
			at0DevelopmentTriangleTerm = developmentTriangle.at1;
		}
		else if ((developmentTriangle.p2 == pA && developmentTriangle.p0 == pB) || (developmentTriangle.p2 == pB && developmentTriangle.p0 == pA))
		{
			developmentTriangle.SetEdge(2, isPerimeter);
			at0DevelopmentTriangleTerm = developmentTriangle.at2;
		}
		if (at0DevelopmentTriangleTerm != -1)
		{
			developmentTriangle = _triangles[at0DevelopmentTriangleTerm];
			if ((developmentTriangle.p0 == pA && developmentTriangle.p1 == pB) || (developmentTriangle.p0 == pB && developmentTriangle.p1 == pA))
			{
				developmentTriangle.SetEdge(0, isPerimeter);
			}
			else if ((developmentTriangle.p1 == pA && developmentTriangle.p2 == pB) || (developmentTriangle.p1 == pB && developmentTriangle.p2 == pA))
			{
				developmentTriangle.SetEdge(1, isPerimeter);
			}
			else if ((developmentTriangle.p2 == pA && developmentTriangle.p0 == pB) || (developmentTriangle.p2 == pB && developmentTriangle.p0 == pA))
			{
				developmentTriangle.SetEdge(2, isPerimeter);
			}
		}
	}

	private int ClosestPoint(PointM point, int p0, int p1, int p2)
	{
		double distanceTo = point.DistanceTo(_points[p0]);
		double distanceTo2 = point.DistanceTo(_points[p1]);
		double distanceTo3 = point.DistanceTo(_points[p2]);
		if (distanceTo < distanceTo2)
		{
			if (distanceTo < distanceTo3)
			{
				return p0;
			}
			return p2;
		}
		if (distanceTo2 < distanceTo3)
		{
			return p1;
		}
		return p2;
	}

	protected SegmentM Edge(int t, int e012)
	{
		return e012 switch
		{
			0 => new SegmentM(_points[_triangles[t].p0], _points[_triangles[t].p1]), 
			1 => new SegmentM(_points[_triangles[t].p1], _points[_triangles[t].p2]), 
			2 => new SegmentM(_points[_triangles[t].p2], _points[_triangles[t].p0]), 
			_ => throw new ArgumentOutOfRangeException(), 
		};
	}

	protected int EdgeFirstPoint(int t, int e012)
	{
		return e012 switch
		{
			0 => _triangles[t].p0, 
			1 => _triangles[t].p1, 
			2 => _triangles[t].p2, 
			_ => throw new ArgumentOutOfRangeException(), 
		};
	}

	protected int EdgeLastPoint(int t, int e012)
	{
		return e012 switch
		{
			0 => _triangles[t].p1, 
			1 => _triangles[t].p2, 
			2 => _triangles[t].p0, 
			_ => throw new ArgumentOutOfRangeException(), 
		};
	}

	protected int e012Before(int e012)
	{
		return e012 switch
		{
			0 => 2, 
			1 => 0, 
			2 => 1, 
			_ => throw new ArgumentOutOfRangeException(), 
		};
	}

	protected int e012After(int e012)
	{
		return e012 switch
		{
			0 => 1, 
			1 => 2, 
			2 => 0, 
			_ => throw new ArgumentOutOfRangeException(), 
		};
	}

	protected int AdjacentTriangle(int t, int e012)
	{
		return e012 switch
		{
			0 => _triangles[t].at0, 
			1 => _triangles[t].at1, 
			2 => _triangles[t].at2, 
			_ => throw new ArgumentOutOfRangeException(), 
		};
	}

	protected void DebugExportShapefile(string filename)
	{
		PointFeatureLayer pointFeatureLayer = new PointFeatureLayer("pts");
		pointFeatureLayer.AddAttributeColumn("ID", typeof(int));
		checked
		{
			int count = _points.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				pointFeatureLayer.AddFeature(new Point(_points[i]))["ID"] = i;
			}
			pointFeatureLayer.SourceFilename = filename;
			pointFeatureLayer.Save();
		}
	}

	public FeatureLayer ExportToComputedPointLayer(string layername)
	{
		PointFeatureLayer pointFeatureLayer = new PointFeatureLayer(layername);
		checked
		{
			int count = _points.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				pointFeatureLayer.AddFeature(new Point(_points[i]));
			}
			return pointFeatureLayer;
		}
	}

	public FeatureLayer ExportToComputedTriLayer(string layername)
	{
		PolylineFeatureLayer polylineFeatureLayer = new PolylineFeatureLayer(layername);
		checked
		{
			int count = _triangles.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				PointMs pointMs = new PointMs();
				PointM point = _points[_triangles[i].p0];
				PointM point2 = _points[_triangles[i].p1];
				PointM point3 = _points[_triangles[i].p2];
				pointMs.Add(point);
				pointMs.Add(point2);
				pointMs.Add(point3);
				pointMs.Add(point);
				polylineFeatureLayer.AddFeature(new Polyline(pointMs));
			}
			return polylineFeatureLayer;
		}
	}
}
