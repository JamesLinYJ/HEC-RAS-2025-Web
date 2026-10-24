using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.PairedData;
using Geospatial.Vectors;
using RasMapperLib;
using RasMapperLib.Mesh;
using RasMapperLib.TinHelpers;

namespace Ras.Migration;

public static class GeoConverter
{
	public static Geospatial.Vectors.Polyline Convert(RasMapperLib.Polyline p6)
	{
		Geospatial.Vectors.Polyline polyline = new Geospatial.Vectors.Polyline(p6.Count);
		for (int i = 0; i < p6.Count; i++)
		{
			polyline.Add(Convert(p6[i]));
		}
		return polyline;
	}

	public static Geospatial.Vectors.Polygon Convert(RasMapperLib.Polygon p6)
	{
		Geospatial.Vectors.Polygon polygon = new Geospatial.Vectors.Polygon(p6.Count);
		for (int i = 0; i < p6.Count - 1; i++)
		{
			polygon.Add(Convert(p6[i]));
		}
		if (!p6.Last().EqualXY(p6.First()))
		{
			polygon.Add(Convert(p6.Last()));
		}
		return polygon;
	}

	public static RasMapperLib.Polygon Convert(Geospatial.Vectors.Polygon p)
	{
		PointMs pointMs = new PointMs();
		for (int i = 0; i < p.Count; i++)
		{
			pointMs.Add(Convert(p.Points[i]));
		}
		pointMs.Add(Convert(p.Points[0]));
		return new RasMapperLib.Polygon(pointMs);
	}

	public static StationElevationProfile Convert(RasMapperLib.Profile profile)
	{
		StationElevationProfile stationElevationProfile = new StationElevationProfile(profile.Count);
		for (int i = 0; i < profile.Count; i++)
		{
			stationElevationProfile.Add(profile[i].X, profile[i].Y);
		}
		return stationElevationProfile;
	}

	public static PointM Convert(Geospatial.Vectors.Point p)
	{
		return new PointM(p.X, p.Y);
	}

	public static PointM Convert(Geospatial.Vectors.Point3D p)
	{
		return new PointM(p.X, p.Y, p.Z);
	}

	public static PointM Convert(Geospatial.Vectors.Point4D p)
	{
		return new PointM(p.X, p.Y, p.Z, p.M);
	}

	public static Point2Double ConvertP2D(Geospatial.Vectors.Point p)
	{
		return new Point2Double(p.X, p.Y);
	}

	public static Geospatial.Vectors.Point Convert(PointM p6)
	{
		return new Geospatial.Vectors.Point(p6.X, p6.Y);
	}

	public static Geospatial.Vectors.Point3D ConvertZ(PointM p6)
	{
		return new Geospatial.Vectors.Point3D(p6.X, p6.Y, p6.Z);
	}

	public static Geospatial.Vectors.Point4D ConvertZM(PointM p6)
	{
		return new Geospatial.Vectors.Point4D(p6.X, p6.Y, p6.Z, p6.M);
	}

	public static List<Geospatial.Vectors.Point> Convert(IList<PointM> p6s)
	{
		return p6s.Select((PointM p6) => Convert(p6)).ToList();
	}

	public static List<Geospatial.Vectors.Point3D> ConvertZ(IList<PointM> p6s)
	{
		return p6s.Select((PointM p6) => ConvertZ(p6)).ToList();
	}

	public static List<Geospatial.Vectors.Point4D> ConvertZM(IList<PointM> p6s)
	{
		return p6s.Select((PointM p6) => ConvertZM(p6)).ToList();
	}

	public static List<Geospatial.Vectors.Point> Convert(PointMs p6s)
	{
		return p6s.Select((PointM p6) => Convert(p6)).ToList();
	}

	public static List<Geospatial.Vectors.Point3D> ConvertZ(PointMs p6s)
	{
		return p6s.Select((PointM p6) => ConvertZ(p6)).ToList();
	}

	public static List<Geospatial.Vectors.Point4D> ConvertZM(PointMs p6s)
	{
		return p6s.Select((PointM p6) => ConvertZM(p6)).ToList();
	}

	public static PointMs ConvertPMS(List<Geospatial.Vectors.Point> points)
	{
		return new PointMs(points.Select((Geospatial.Vectors.Point p7) => Convert(p7)).ToList());
	}

	public static MultiPoint ConvertMultipoint(List<Geospatial.Vectors.Point> points)
	{
		return new MultiPoint(ConvertPMS(points));
	}

	public static Geospatial.Vectors.Point Convert(Point2Double p6)
	{
		return new Geospatial.Vectors.Point(p6.X, p6.Y);
	}

	public static List<Geospatial.Vectors.Point> Convert(IList<Point2Double> p6s)
	{
		return p6s.Select((Point2Double p6) => Convert(p6)).ToList();
	}

	public static Geospatial.Vectors.Extent Convert(RasMapperLib.Extent e6)
	{
		return new Geospatial.Vectors.Extent(e6.Left, e6.Right, e6.Bottom, e6.Top);
	}

	public static Mesh Convert(MeshFV2D m6)
	{
		Geospatial.Vectors.Polygon perim = Convert(m6.Perimeter);
		Geospatial.Vectors.Cell[] cells = Convert((from cIdx in Enumerable.Range(0, m6.NonVirtualCellCount)
			select m6.Cell(cIdx)).ToArray(), out var centers);
		Geospatial.Vectors.Face[] faces = Convert((from fIdx in Enumerable.Range(0, m6.FaceCount)
			select m6.Faces[fIdx]).ToArray(), out var fInternalPts);
		Geospatial.Vectors.FacePoint[] facePoints = Convert((from fpIdx in Enumerable.Range(0, m6.FacePointCount)
			select m6.FacePoints[fpIdx]).ToArray());
		for (int intervalIndex = 0; intervalIndex < faces.Length; intervalIndex++)
		{
			Geospatial.Vectors.Face face = faces[intervalIndex];
			if (face.cellB >= m6.NonVirtualCellCount)
			{
				face.cellB = -1;
			}
			faces[intervalIndex] = face;
		}
		return new Mesh(perim, cells, faces, facePoints, centers, fInternalPts);
	}

	public static MeshFV2D Convert(Mesh m7)
	{
		RasMapperLib.Polygon perimeter = Convert(m7.GetSingleRingPerimeter());
		RasMapperLib.Mesh.Cell[] cells = Convert((from cIdx in Enumerable.Range(0, m7.CellCount)
			select (m7.Cells[cIdx], m7.CellCenters[cIdx])).ToArray());
		RasMapperLib.Mesh.Face[] faces = Convert((from fIdx in Enumerable.Range(0, m7.FaceCount)
			select (m7.Faces[fIdx], m7.FaceInternalPoints[fIdx].Span.ToArray().ToList())).ToArray());
		RasMapperLib.Mesh.FacePoint[] facePoints = Convert((from fpIdx in Enumerable.Range(0, m7.FacePointCount)
			select m7.FacePoints[fpIdx]).ToArray());
		return new MeshFV2D(perimeter, cells, faces, facePoints);
	}

	public static Geospatial.Vectors.Cell Convert(RasMapperLib.Mesh.Cell c6, out Geospatial.Vectors.Point pt)
	{
		pt = Convert(c6.Point);
		Geospatial.Vectors.Cell result = new Geospatial.Vectors.Cell();
		result.Faces = c6.Faces.ToArray();
		return result;
	}

	public static RasMapperLib.Mesh.Cell Convert(Geospatial.Vectors.Cell c7, Geospatial.Vectors.Point pt)
	{
		return new RasMapperLib.Mesh.Cell
		{
			Faces = c7.Faces.ToArray(),
			Point = ConvertP2D(pt)
		};
	}

	public static Geospatial.Vectors.Face Convert(RasMapperLib.Mesh.Face f6)
	{
		Geospatial.Vectors.Face result = new Geospatial.Vectors.Face(f6.cellA, f6.cellB);
		result.fpA = f6.fpA;
		result.fpB = f6.fpB;
		return result;
	}

	public static RasMapperLib.Mesh.Face Convert(Geospatial.Vectors.Face f7, IList<Geospatial.Vectors.Point> internalPts)
	{
		return new RasMapperLib.Mesh.Face
		{
			fpA = f7.fpA,
			fpB = f7.fpB,
			cellA = f7.cellA,
			cellB = f7.cellB,
			InternalPoints = ((internalPts == null) ? null : ConvertPMS(internalPts.ToList()))
		};
	}

	public static Geospatial.Vectors.FacePoint Convert(RasMapperLib.Mesh.FacePoint f6)
	{
		Geospatial.Vectors.FacePoint result = new Geospatial.Vectors.FacePoint(Convert(f6.Point));
		result.Faces = f6.Faces.ToArray();
		return result;
	}

	public static RasMapperLib.Mesh.FacePoint Convert(Geospatial.Vectors.FacePoint f7)
	{
		RasMapperLib.Mesh.FacePoint result = new RasMapperLib.Mesh.FacePoint(ConvertP2D(f7.Point));
		result.Faces = f7.Faces.ToArray();
		return result;
	}

	public static Geospatial.Vectors.Cell[] Convert(IList<RasMapperLib.Mesh.Cell> c6, out Geospatial.Vectors.Point[] centers)
	{
		Geospatial.Vectors.Cell[] cells = new Geospatial.Vectors.Cell[c6.Count];
		centers = new Geospatial.Vectors.Point[c6.Count];
		for (int i = 0; i < cells.Length; i++)
		{
			cells[i] = Convert(c6[i], out var pt);
			centers[i] = pt;
		}
		return cells;
	}

	public static RasMapperLib.Mesh.Cell[] Convert(IList<(Geospatial.Vectors.Cell, Geospatial.Vectors.Point)> c7)
	{
		RasMapperLib.Mesh.Cell[] cells = new RasMapperLib.Mesh.Cell[c7.Count];
		for (int i = 0; i < cells.Length; i++)
		{
			cells[i] = Convert(c7[i].Item1, c7[i].Item2);
		}
		return cells;
	}

	public static Geospatial.Vectors.Face[] Convert(IList<RasMapperLib.Mesh.Face> f6, out Memory<Geospatial.Vectors.Point>[] fInternalPts)
	{
		Geospatial.Vectors.Face[] faces = new Geospatial.Vectors.Face[f6.Count];
		fInternalPts = new Memory<Geospatial.Vectors.Point>[f6.Count];
		int sum = f6.Sum((RasMapperLib.Mesh.Face ff) => ff.InternalPoints?.Count ?? 0);
		Memory<Geospatial.Vectors.Point> points = Memory<Geospatial.Vectors.Point>.Empty;
		if (sum > 0)
		{
			points = new Geospatial.Vectors.Point[sum];
		}
		for (int intervalIndex = 0; intervalIndex < faces.Length; intervalIndex++)
		{
			faces[intervalIndex] = Convert(f6[intervalIndex]);
			PointMs internalPoints = f6[intervalIndex].InternalPoints;
			if (internalPoints != null && internalPoints.Count > 0)
			{
				Memory<Geospatial.Vectors.Point> slice = points.Slice(0, internalPoints.Count);
				points = points.Slice(internalPoints.Count);
				Span<Geospatial.Vectors.Point> sliceSpan = slice.Span;
				for (int intervalIndex2 = 0; intervalIndex2 < internalPoints.Count; intervalIndex2++)
				{
					sliceSpan[intervalIndex2] = Convert(internalPoints[intervalIndex2]);
				}
			}
		}
		return faces;
	}

	public static RasMapperLib.Mesh.Face[] Convert(IList<(Geospatial.Vectors.Face, List<Geospatial.Vectors.Point>)> f7s)
	{
		RasMapperLib.Mesh.Face[] faces = new RasMapperLib.Mesh.Face[f7s.Count];
		for (int i = 0; i < faces.Length; i++)
		{
			(Geospatial.Vectors.Face, List<Geospatial.Vectors.Point>) tuple = f7s[i];
			faces[i] = Convert(tuple.Item1, tuple.Item2);
		}
		return faces;
	}

	public static Geospatial.Vectors.FacePoint[] Convert(IList<RasMapperLib.Mesh.FacePoint> f6)
	{
		Geospatial.Vectors.FacePoint[] facePoints = new Geospatial.Vectors.FacePoint[f6.Count];
		for (int i = 0; i < facePoints.Length; i++)
		{
			facePoints[i] = Convert(f6[i]);
		}
		return facePoints;
	}

	public static RasMapperLib.Mesh.FacePoint[] Convert(IList<Geospatial.Vectors.FacePoint> f7)
	{
		RasMapperLib.Mesh.FacePoint[] facePoints = new RasMapperLib.Mesh.FacePoint[f7.Count];
		for (int i = 0; i < facePoints.Length; i++)
		{
			facePoints[i] = Convert(f7[i]);
		}
		return facePoints;
	}

	public static Geospatial.Vectors.Tin Convert(RasMapperLib.Tin t6)
	{
		PointMs pointMs = t6.CopyPoints();
		Geospatial.Vectors.Tin tin = Geospatial.Vectors.Tin.RebuildAdjacencyInformation(Convert(pointMs), Convert(t6.Triangles));
		if (pointMs.HasZ())
		{
			tin.TrackZ();
			for (int i = 0; i < pointMs.Count; i++)
			{
				tin.Z[i] = pointMs[i].Z;
			}
		}
		if (pointMs.HasM())
		{
			tin.TrackM();
			for (int j = 0; j < pointMs.Count; j++)
			{
				tin.M[j] = pointMs[j].M;
			}
		}
		return tin;
	}

	public static List<Geospatial.Vectors.Tin> Convert(IEnumerable<RasMapperLib.Tin> t6s)
	{
		return t6s.Select((RasMapperLib.Tin t6) => Convert(t6)).ToList();
	}

	public static TriangleDefinition Convert(TriangleStruct td6)
	{
		return new TriangleDefinition(td6.p0, td6.p1, td6.p2, td6.at0, td6.at1, td6.at2);
	}

	public static List<TriangleDefinition> Convert(IEnumerable<TriangleStruct> td6s)
	{
		return td6s.Select((TriangleStruct td6) => Convert(td6)).ToList();
	}
}
