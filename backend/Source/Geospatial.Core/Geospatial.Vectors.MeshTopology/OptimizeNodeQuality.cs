using System;
using System.Collections.Generic;

namespace Geospatial.Vectors.MeshTopology;

public class OptimizeNodeQuality : OptimizeNodeMetric
{
	protected int nIdxCached = -1;

	private List<Polygon> CacheCellPolygons;

	private double CacheCCScale;

	public readonly double epsLength = 1E-10;

	public double scaleLossFactor = 30.0;

	public double circumcenterLossFactor = 30.0;

	public double oppositeAngleLossFactor = 100.0;

	public OptimizeNodeQuality(SlidingMesh mesh, IList<int> nodes = null)
		: base(mesh, nodes)
	{
		CacheCellPolygons = new List<Polygon>();
	}

	public override double NodeLoss(int nIdx, Point p)
	{
		CacheNode(nIdx);
		double computed = 0.0;
		if (circumcenterLossFactor > 0.0)
		{
			computed += circumcenterLossFactor * CircumcenterClearanceLoss(nIdx, p);
		}
		if (oppositeAngleLossFactor > 0.0)
		{
			computed += oppositeAngleLossFactor * OrthoQuadLoss(nIdx, p);
		}
		if (scaleLossFactor > 0.0)
		{
			computed += scaleLossFactor * ScaleLoss(nIdx, p);
		}
		return computed;
	}

	private Point ApproxCircumcenter(Polygon poly)
	{
		Point point = new Point(0.0, 0.0);
		int count = poly.Count;
		int count2 = count;
		if (count == 3)
		{
			count2 = 1;
		}
		for (int i = 0; i < count2; i++)
		{
			point += new Triangle(poly.Points[i], poly.Points[(i + 1) % count], poly.Points[(i + 2) % count]).CircumCircleCenter();
		}
		return point * (1.0 / (double)count2);
	}

	private double CircumcenterClearanceLoss(int nIdx, Point point)
	{
		double computed = 10.0;
		double computed2 = 0.0;
		CacheNode(nIdx);
		foreach (Polygon cacheCellPolygon in CacheCellPolygons)
		{
			cacheCellPolygon.Points[0] = point;
			Point p = ApproxCircumcenter(cacheCellPolygon);
			foreach (Segment segment in cacheCellPolygon.Segments)
			{
				double perpendicularSignedDistance = segment.PerpendicularSignedDistance(p);
				computed2 += Math.Exp(0.0 - Math.Max(-10.0, computed * perpendicularSignedDistance / CacheCCScale));
			}
		}
		return computed2;
	}

	private double OrthoQuadLoss(int nIdx, Point point)
	{
		CacheNode(nIdx);
		double computed = 0.0;
		foreach (Polygon cacheCellPolygon in CacheCellPolygons)
		{
			if (cacheCellPolygon.Count == 4)
			{
				cacheCellPolygon.Points[0] = point;
				IReadOnlyList<Segment> segments = cacheCellPolygon.Segments;
				_ = new double[4]
				{
					segments[0].Bearing,
					segments[1].Bearing,
					segments[2].Bearing,
					segments[3].Bearing
				};
				double scaledPi = Math.PI * 2.0;
				double[] doubleValues = new double[4]
				{
					(scaledPi + segments[1].Bearing - segments[0].Bearing) % scaledPi,
					(scaledPi + segments[2].Bearing - segments[1].Bearing) % scaledPi,
					(scaledPi + segments[3].Bearing - segments[2].Bearing) % scaledPi,
					(scaledPi + segments[0].Bearing - segments[3].Bearing) % scaledPi
				};
				computed += Math.Abs(doubleValues[0] + doubleValues[2] - Math.PI) + Math.Abs(doubleValues[1] + doubleValues[3] - Math.PI);
			}
		}
		return computed;
	}

	private double ScaleLoss(int nIdx, Point point)
	{
		double computed = 0.0;
		foreach (HalfEdge nodeToHalfEdge in mesh.NodeToHalfEdges(nIdx))
		{
			double distanceTo = mesh.NodePoints[mesh.HalfEdgeNodeForward(nodeToHalfEdge)].DistanceTo(point);
			double faceTargetLength = mesh.FaceTargetLength[nodeToHalfEdge.fIdx];
			if (!(faceTargetLength < 0.0))
			{
				double faceTargetLengthOverDistanceTo = ((!(distanceTo > faceTargetLength)) ? (faceTargetLength / distanceTo) : (distanceTo / faceTargetLength));
				computed += Math.Pow(faceTargetLengthOverDistanceTo - 1.0, 4.0);
			}
		}
		return computed;
	}

	protected void CacheNode(int nIdx)
	{
		if (nIdxCached == nIdx)
		{
			return;
		}
		nIdxCached = nIdx;
		CacheCellPolygons.Clear();
		CacheCCScale = 0.0;
		foreach (int nodeToCellIndex in mesh.NodeToCells(nIdx))
		{
			List<Point> collectionList = new List<Point>();
			List<Point> collection = null;
			List<HalfEdge> cellFace = mesh.CellFaces[nodeToCellIndex];
			for (int i = 0; i < cellFace.Count; i++)
			{
				int halfEdgeNodeBack = mesh.HalfEdgeNodeBack(cellFace[i]);
				if (halfEdgeNodeBack == nIdx)
				{
					collection = collectionList;
					collectionList = new List<Point>();
				}
				collectionList.Add(mesh.NodePoints[halfEdgeNodeBack]);
			}
			collectionList.AddRange(collection);
			if (mesh.CellTypes[nodeToCellIndex] == SlidingMesh.CellType.CELL_2D)
			{
				Polygon polygon = new Polygon(collectionList);
				CacheCellPolygons.Add(polygon);
				CacheCCScale += Math.Max(0.0, 0.0 - polygon.Area);
			}
		}
		CacheCCScale = Math.Max(1E-08, Math.Sqrt(CacheCCScale));
	}
}
