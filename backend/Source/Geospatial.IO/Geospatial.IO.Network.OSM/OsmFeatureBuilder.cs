using System;
using System.Collections.Generic;
using Geospatial.Features;
using Geospatial.GDALAssist;
using Geospatial.GDALAssist.Vectors;
using Geospatial.Vectors;
using Utility.Logging;

namespace Geospatial.IO.Network.OSM;

public static class OsmFeatureBuilder
{
	private class ConversionTally
	{
		public int DegenerateCount { get; set; }

		public int UnclosedCount { get; set; }

		public int EmptyRelationCount { get; set; }

		public int DegradedTopologyCount { get; set; }
	}

	private readonly struct PendingPoint(OsmElement element, int index)
	{
		public OsmElement Element { get; } = element;

		public int Index { get; } = index;
	}

	private readonly struct PendingLine(OsmElement element, int start, int length)
	{
		public OsmElement Element { get; } = element;

		public int Start { get; } = start;

		public int Length { get; } = length;
	}

	private readonly struct RingSpan(int start, int length)
	{
		public int Start { get; } = start;

		public int Length { get; } = length;
	}

	private class PendingPolygon(OsmElement element)
	{
		public OsmElement Element { get; } = element;

		public List<RingSpan> OuterRings { get; } = new List<RingSpan>();

		public List<RingSpan> InnerRings { get; } = new List<RingSpan>();
	}

	private static readonly MessageClass DegenerateGeometry = new MessageClass("Osm_DegenerateGeometry", MessageSeverity.Warning, "{Count} OSM element(s) carried too few coordinates to form a line or a ring and were skipped.");

	private static readonly MessageClass UnclosedRings = new MessageClass("Osm_UnclosedRings", MessageSeverity.Warning, "{Count} relation member chain(s) never closed into a ring and were dropped; the affected areas may be incomplete.");

	private static readonly MessageClass EmptyAreaRelations = new MessageClass("Osm_EmptyAreaRelations", MessageSeverity.Warning, "{Count} area relation(s) produced no usable outer ring and were skipped.");

	private static readonly MessageClass DegradedRingTopology = new MessageClass("Osm_DegradedRingTopology", MessageSeverity.Warning, "{Count} relation(s) had ring topology that could not be resolved into hole-aware polygons; degraded to independent outer rings (holes lost).");

	private static readonly MessageClass MissingTargetProjection = new MessageClass("Osm_MissingTargetProjection", MessageSeverity.Warning, "No target projection was supplied, so OSM geometry is being returned in WGS84 (EPSG:4326).");

	public static bool TryBuildFeatureSets(OverpassResponse response, Projection target, out OsmFeatureSets sets, out OperationResult result)
	{
		result = new OperationResult(success: true);
		sets = new OsmFeatureSets();
		try
		{
			BuildFeatureSets(response, target, sets, result);
			return true;
		}
		catch (Exception ex)
		{
			result.AddFailure(GeneralMessageCodes.GetExceptionOccurred("Convert OSM response to features", ex));
			sets = new OsmFeatureSets();
			return false;
		}
	}

	private static void BuildFeatureSets(OverpassResponse response, Projection target, OsmFeatureSets sets, OperationResult result)
	{
		Projection wGS = WellKnownProjections.WGS84;
		if (target == null)
		{
			result.AddMessage(MissingTargetProjection.Create());
			target = wGS;
		}
		List<Point> points = new List<Point>();
		List<PendingPoint> pendingPoints = new List<PendingPoint>();
		List<PendingLine> pendingLines = new List<PendingLine>();
		List<PendingPolygon> pendingPolygons = new List<PendingPolygon>();
		ConversionTally tally = new ConversionTally();
		if (response?.Elements != null)
		{
			foreach (OsmElement element in response.Elements)
			{
				CollectElement(element, points, pendingPoints, pendingLines, pendingPolygons, tally);
			}
		}
		Point[] pointsArray = points.ToArray();
		if (!wGS.IsEqual(target))
		{
			pointsArray.AsSpan().ReprojectAll(wGS, target);
		}
		sets.Points = BuildPoints(pointsArray, pendingPoints);
		sets.Lines = BuildLines(pointsArray, pendingLines);
		sets.Polygons = BuildPolygons(pointsArray, pendingPolygons, tally);
		ReportTally(result, tally);
	}

	private static void CollectElement(OsmElement element, List<Point> coordinates, List<PendingPoint> points, List<PendingLine> lines, List<PendingPolygon> polygons, ConversionTally tally)
	{
		if (element == null)
		{
			return;
		}
		switch (OsmGeometryClassifier.Classify(element))
		{
		case OsmGeometryKind.Point:
			CollectPoint(element, coordinates, points);
			break;
		case OsmGeometryKind.Line:
			if (element.IsRelation)
			{
				CollectRelationMemberLines(element, coordinates, lines, tally);
			}
			else
			{
				CollectLine(element, element.Geometry, coordinates, lines, tally);
			}
			break;
		case OsmGeometryKind.Polygon:
			CollectPolygon(element, coordinates, polygons, tally);
			break;
		}
	}

	private static void CollectPoint(OsmElement element, List<Point> coordinates, List<PendingPoint> points)
	{
		if (element.Tags != null && element.Tags.Count != 0 && element.Lat.HasValue && element.Lon.HasValue)
		{
			points.Add(new PendingPoint(element, coordinates.Count));
			coordinates.Add(new Point(element.Lon.Value, element.Lat.Value));
		}
	}

	private static void CollectRelationMemberLines(OsmElement relation, List<Point> coordinates, List<PendingLine> lines, ConversionTally tally)
	{
		if (relation.Members == null)
		{
			return;
		}
		foreach (OsmMember member in relation.Members)
		{
			if (member != null && member.Geometry != null)
			{
				CollectLine(relation, member.Geometry, coordinates, lines, tally);
			}
		}
	}

	private static void CollectLine(OsmElement element, List<OsmCoordinate> geometry, List<Point> coordinates, List<PendingLine> lines, ConversionTally tally)
	{
		int count = coordinates.Count;
		AppendCoordinates(geometry, coordinates);
		int countDifference = coordinates.Count - count;
		if (countDifference < 2)
		{
			coordinates.RemoveRange(count, countDifference);
			tally.DegenerateCount++;
		}
		else
		{
			lines.Add(new PendingLine(element, count, countDifference));
		}
	}

	private static void CollectPolygon(OsmElement element, List<Point> coordinates, List<PendingPolygon> polygons, ConversionTally tally)
	{
		PendingPolygon pendingPolygon = new PendingPolygon(element);
		if (element.IsWay)
		{
			if (!TryAppendClosedWayRing(element.Geometry, coordinates, out var ringSpan))
			{
				tally.DegenerateCount++;
				return;
			}
			pendingPolygon.OuterRings.Add(ringSpan);
			polygons.Add(pendingPolygon);
			return;
		}
		OsmRingAssembler.TryChainRings(element, out var rings);
		tally.UnclosedCount += rings.UnclosedCount;
		tally.DegenerateCount += rings.DegenerateCount;
		foreach (List<Point> outerRing in rings.OuterRings)
		{
			pendingPolygon.OuterRings.Add(AppendRing(outerRing, coordinates));
		}
		foreach (List<Point> innerRing in rings.InnerRings)
		{
			pendingPolygon.InnerRings.Add(AppendRing(innerRing, coordinates));
		}
		if (pendingPolygon.OuterRings.Count == 0)
		{
			tally.EmptyRelationCount++;
		}
		else
		{
			polygons.Add(pendingPolygon);
		}
	}

	private static void AppendCoordinates(List<OsmCoordinate> geometry, List<Point> coordinates)
	{
		if (geometry == null)
		{
			return;
		}
		int count = coordinates.Count;
		foreach (OsmCoordinate geometry2 in geometry)
		{
			if (geometry2 == null)
			{
				continue;
			}
			Point point = new Point(geometry2.Lon, geometry2.Lat);
			if (coordinates.Count > count)
			{
				if (SamePoint(coordinates[coordinates.Count - 1], point))
				{
					continue;
				}
			}
			coordinates.Add(point);
		}
	}

	private static bool TryAppendClosedWayRing(List<OsmCoordinate> geometry, List<Point> coordinates, out RingSpan ringSpan)
	{
		ringSpan = default;
		int count = coordinates.Count;
		AppendCoordinates(geometry, coordinates);
		int countDifference = coordinates.Count - count;
		if (countDifference > 1 && SamePoint(coordinates[count], coordinates[count + countDifference - 1]))
		{
			coordinates.RemoveAt(count + countDifference - 1);
			countDifference--;
		}
		if (countDifference < 3)
		{
			coordinates.RemoveRange(count, countDifference);
			return false;
		}
		ringSpan = new RingSpan(count, countDifference);
		return true;
	}

	private static RingSpan AppendRing(List<Point> ring, List<Point> coordinates)
	{
		int count = coordinates.Count;
		coordinates.AddRange(ring);
		return new RingSpan(count, ring.Count);
	}

	private static PointFeatureCollection BuildPoints(Point[] buffer, List<PendingPoint> pending)
	{
		List<Point> points = new List<Point>(pending.Count);
		List<OsmElement> osmElements = new List<OsmElement>(pending.Count);
		foreach (PendingPoint pending2 in pending)
		{
			points.Add(buffer[pending2.Index]);
			osmElements.Add(pending2.Element);
		}
		return new PointFeatureCollection(new PointCollection(points), OsmAttributeTableBuilder.Build(osmElements));
	}

	private static PolylineFeatureCollection BuildLines(Point[] buffer, List<PendingLine> pending)
	{
		PolylineCollection polylineCollection = new PolylineCollection();
		List<OsmElement> osmElements = new List<OsmElement>(pending.Count);
		foreach (PendingLine pending2 in pending)
		{
			polylineCollection.Add(Polyline.CreateWithBuffer(buffer.AsSpan(pending2.Start, pending2.Length).ToArray()));
			osmElements.Add(pending2.Element);
		}
		return new PolylineFeatureCollection(polylineCollection, OsmAttributeTableBuilder.Build(osmElements));
	}

	private static ComplexPolygonFeatureCollection BuildPolygons(Point[] buffer, List<PendingPolygon> pending, ConversionTally tally)
	{
		ComplexPolygonCollection complexPolygonCollection = new ComplexPolygonCollection();
		List<OsmElement> osmElements = new List<OsmElement>(pending.Count);
		foreach (PendingPolygon pending2 in pending)
		{
			List<Polygon> materializeRings = MaterializeRings(buffer, pending2.OuterRings);
			if (materializeRings.Count == 0)
			{
				continue;
			}
			List<Polygon> innerRings = MaterializeRings(buffer, pending2.InnerRings);
			if (!OsmRingAssembler.TryGroupRings(materializeRings, innerRings, out var polygons))
			{
				tally.DegradedTopologyCount++;
			}
			foreach (ComplexPolygon polygon in polygons)
			{
				complexPolygonCollection.Add(polygon);
				osmElements.Add(pending2.Element);
			}
		}
		return new ComplexPolygonFeatureCollection(complexPolygonCollection, OsmAttributeTableBuilder.Build(osmElements));
	}

	private static List<Polygon> MaterializeRings(Point[] buffer, List<RingSpan> spans)
	{
		List<Polygon> polygons = new List<Polygon>(spans.Count);
		foreach (RingSpan ringSpan in spans)
		{
			polygons.Add(Polygon.CreateWithBuffer(buffer.AsSpan(ringSpan.Start, ringSpan.Length).ToArray()));
		}
		return polygons;
	}

	private static void ReportTally(OperationResult result, ConversionTally tally)
	{
		if (tally.DegenerateCount > 0)
		{
			result.AddMessage(DegenerateGeometry.Create(tally.DegenerateCount));
		}
		if (tally.UnclosedCount > 0)
		{
			result.AddMessage(UnclosedRings.Create(tally.UnclosedCount));
		}
		if (tally.EmptyRelationCount > 0)
		{
			result.AddMessage(EmptyAreaRelations.Create(tally.EmptyRelationCount));
		}
		if (tally.DegradedTopologyCount > 0)
		{
			result.AddMessage(DegradedRingTopology.Create(tally.DegradedTopologyCount));
		}
	}

	private static bool SamePoint(Point a, Point b)
	{
		if (a.X == b.X)
		{
			return a.Y == b.Y;
		}
		return false;
	}
}
