using System;
using System.Collections.Generic;
using Geospatial.GDALAssist;
using Geospatial.GDALAssist.Vectors;
using Geospatial.Vectors;
using Utility.Logging;
using Utility.Memory;
using Utility.Unsafe;

namespace Geospatial.IO.Network.ArcGisRest;

public static class ArcGisRestConversions
{
	private readonly struct FlattenedParts
	{
		public Point[] Points { get; init; }

		public double[] Z { get; init; }

		public List<int> PartLengths { get; init; }

		public List<FeaturePartSpan> Features { get; init; }
	}

	private readonly struct FeaturePartSpan(ArcGisFeature feature, int firstPartIndex, int partCount)
	{
		public ArcGisFeature Feature { get; } = feature;

		public int FirstPartIndex { get; } = firstPartIndex;

		public int PartCount { get; } = partCount;
	}

	private static readonly MessageClass UnresolvedProjection = new MessageClass("ArcGis_UnresolvedProjection", MessageSeverity.Warning, "ArcGIS response reported spatial reference wkid {Wkid} (latestWkid {LatestWkid}), which did not resolve to a valid EPSG projection (likely an ESRI-proprietary code). Falling back to WGS84; geometry in a different CRS would be misinterpreted.");

	private static readonly MessageClass UnresolvedProjectionError = new MessageClass("ArcGis_UnresolvedProjectionError", MessageSeverity.Error, "ArcGIS response reported spatial reference wkid {Wkid} (latestWkid {LatestWkid}), which did not resolve to a valid EPSG projection (likely an ESRI-proprietary code). Cannot interpret the geometry's CRS.");

	private static readonly MessageClass DegradedRingTopology = new MessageClass("ArcGis_DegradedRingTopology", MessageSeverity.Warning, "{Count} feature(s) had ring topology that could not be resolved into hole-aware polygons; degraded to independent simple rings (holes/islands lost).");

	public static OperationResult TryConvertToPolylines(ArcGisQueryResponse response, Projection sourceProjection, Projection targetProjection, out PolylineCollection polylines, out Table attributeTable)
	{
		OperationResult result = new OperationResult(success: true);
		polylines = new PolylineCollection();
		attributeTable = new Table();
		if (response?.Features == null)
		{
			return result;
		}
		Type[] columnTypes = ArcGisRestAttributeTableHelper.BuildColumns(attributeTable, response.Fields);
		if (!TryFlattenParts(response, wantPolygons: false, sourceProjection, targetProjection, out var flattened))
		{
			return result;
		}
		int partLengthSum = 0;
		foreach (FeaturePartSpan feature in flattened.Features)
		{
			object[] row = ArcGisRestAttributeTableHelper.BuildRow(feature.Feature, response.Fields, columnTypes);
			for (int i = 0; i < feature.PartCount; i++)
			{
				int partLength = flattened.PartLengths[feature.FirstPartIndex + i];
				Point[] points = flattened.Points.AsSpan(partLengthSum, partLength).ToArray();
				double[] z = flattened.Z?.AsSpan(partLengthSum, partLength).ToArray();
				polylines.Add(Polyline.CreateWithBuffer(points, z));
				attributeTable.AddNewRow(ArcGisRestAttributeTableHelper.CopyRow(row));
				partLengthSum += partLength;
			}
		}
		return result;
	}

	public static OperationResult TryConvertToPolygons(ArcGisQueryResponse response, Projection sourceProjection, Projection targetProjection, out PolygonCollection polygons, out Table attributeTable)
	{
		OperationResult result = new OperationResult(success: true);
		polygons = new PolygonCollection();
		attributeTable = new Table();
		if (response?.Features == null)
		{
			return result;
		}
		Type[] columnTypes = ArcGisRestAttributeTableHelper.BuildColumns(attributeTable, response.Fields);
		if (!TryFlattenParts(response, wantPolygons: true, sourceProjection, targetProjection, out var flattened))
		{
			return result;
		}
		int partLengthSum = 0;
		foreach (FeaturePartSpan feature in flattened.Features)
		{
			object[] row = ArcGisRestAttributeTableHelper.BuildRow(feature.Feature, response.Fields, columnTypes);
			for (int i = 0; i < feature.PartCount; i++)
			{
				int partLength = flattened.PartLengths[feature.FirstPartIndex + i];
				Point[] points = flattened.Points.AsSpan(partLengthSum, partLength).ToArray();
				double[] z = flattened.Z?.AsSpan(partLengthSum, partLength).ToArray();
				polygons.Add(Polygon.CreateWithBuffer(points, z));
				attributeTable.AddNewRow(ArcGisRestAttributeTableHelper.CopyRow(row));
				partLengthSum += partLength;
			}
		}
		return result;
	}

	public static OperationResult TryConvertToComplexPolygons(ArcGisQueryResponse response, Projection sourceProjection, Projection targetProjection, out ComplexPolygonCollection complexPolygons, out Table attributeTable, bool useOrientationFastPath = true)
	{
		OperationResult operationResult = new OperationResult(success: true);
		complexPolygons = new ComplexPolygonCollection();
		attributeTable = new Table();
		if (response?.Features == null)
		{
			return operationResult;
		}
		Type[] columnTypes = ArcGisRestAttributeTableHelper.BuildColumns(attributeTable, response.Fields);
		if (!TryFlattenParts(response, wantPolygons: true, sourceProjection, targetProjection, out var flattened))
		{
			return operationResult;
		}
		int[] intValues = new int[flattened.PartLengths.Count];
		int partLengthSum = 0;
		for (int i = 0; i < flattened.PartLengths.Count; i++)
		{
			intValues[i] = partLengthSum;
			partLengthSum += flattened.PartLengths[i];
		}
		int ordinal = 0;
		foreach (FeaturePartSpan feature in flattened.Features)
		{
			List<Polygon> polygons = new List<Polygon>(feature.PartCount);
			for (int j = 0; j < feature.PartCount; j++)
			{
				int firstPartIndexFeatureTerm = feature.FirstPartIndex + j;
				int start = intValues[firstPartIndexFeatureTerm];
				int length = flattened.PartLengths[firstPartIndexFeatureTerm];
				Point[] points = flattened.Points.AsSpan(start, length).ToArray();
				double[] z = flattened.Z?.AsSpan(start, length).ToArray();
				polygons.Add(Polygon.CreateWithBuffer(points, z));
			}
			object[] row = ArcGisRestAttributeTableHelper.BuildRow(feature.Feature, response.Fields, columnTypes);
			if (useOrientationFastPath && ComplexPolygon.TryCreateComplexPolygonByOrientation_OnlyLakesHotpath(polygons, out var poly))
			{
				complexPolygons.Add(poly);
				attributeTable.AddNewRow(ArcGisRestAttributeTableHelper.CopyRow(row));
				continue;
			}
			if (ComplexPolygon.TryCreateComplexPolygons(polygons, out var complexPolys))
			{
				foreach (ComplexPolygon complexPoly in complexPolys)
				{
					complexPolygons.Add(complexPoly);
					attributeTable.AddNewRow(ArcGisRestAttributeTableHelper.CopyRow(row));
				}
				continue;
			}
			ordinal++;
			foreach (Polygon polygon in polygons)
			{
				complexPolygons.Add(ComplexPolygon.FromOuterRing(polygon));
				attributeTable.AddNewRow(ArcGisRestAttributeTableHelper.CopyRow(row));
			}
		}
		if (ordinal > 0)
		{
			operationResult.AddMessage(DegradedRingTopology.Create(ordinal));
		}
		return operationResult;
	}

	public static OperationResult TryGetResponseProjection(ArcGisQueryResponse response, out Projection projection)
	{
		if (TryResolveResponseProjection(response, out projection))
		{
			return new OperationResult(success: true);
		}
		return OperationResult.Fail(UnresolvedProjectionError.Create(response.SpatialReference.Wkid, response.SpatialReference.LatestWkid));
	}

	public static OperationResult TryGetResponseProjectionOrDefault(ArcGisQueryResponse response, out Projection projection)
	{
		OperationResult operationResult = new OperationResult(success: true);
		if (TryResolveResponseProjection(response, out projection))
		{
			return operationResult;
		}
		operationResult.AddMessage(UnresolvedProjection.Create(response.SpatialReference.Wkid, response.SpatialReference.LatestWkid));
		return operationResult;
	}

	private static bool TryResolveResponseProjection(ArcGisQueryResponse response, out Projection projection)
	{
		projection = WellKnownProjections.WGS84;
		ArcGisSpatialReference arcGisSpatialReference = response?.SpatialReference;
		int latestWkid = ((arcGisSpatialReference != null) ? ((arcGisSpatialReference.LatestWkid != 0) ? arcGisSpatialReference.LatestWkid : arcGisSpatialReference.Wkid) : 0);
		if (latestWkid == 0)
		{
			return true;
		}
		try
		{
			projection = new EPSGProjection(latestWkid);
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static bool TryFlattenParts(ArcGisQueryResponse response, bool wantPolygons, Projection sourceProjection, Projection targetProjection, out FlattenedParts flattened)
	{
		flattened = default;
		if (response?.Features == null)
		{
			return false;
		}
		CountFeaturesPartsPoints(response, wantPolygons, out var totalParts, out var totalPoints);
		if (totalPoints == 0)
		{
			return false;
		}
		bool hasZ = response.HasZ;
		bool condition = false;
		List<Point> points = new List<Point>(totalPoints);
		List<double> doubleList = (hasZ ? new List<double>(totalPoints) : null);
		List<int> partLengths = new List<int>(totalParts);
		List<FeaturePartSpan> features = new List<FeaturePartSpan>(response.Features.Count);
		foreach (ArcGisFeature feature in response.Features)
		{
			List<List<Point3D>> parts = GetParts(feature.Geometry, wantPolygons);
			if (parts == null)
			{
				continue;
			}
			int count = partLengths.Count;
			int ordinal = 0;
			foreach (List<Point3D> part in parts)
			{
				if (part.Count == 0)
				{
					continue;
				}
				foreach (Point3D part2 in part)
				{
					points.Add(part2.AsPoint2D());
					if (hasZ)
					{
						doubleList.Add(part2.Z);
						if (!double.IsNaN(part2.Z))
						{
							condition = true;
						}
					}
				}
				partLengths.Add(part.Count);
				ordinal++;
			}
			if (ordinal > 0)
			{
				features.Add(new FeaturePartSpan(feature, count, ordinal));
			}
		}
		Point[] listRawBackingBuffer = UnsafeHelpers.GetListRawBackingBuffer(points);
		if (!sourceProjection.IsEqual(targetProjection))
		{
			listRawBackingBuffer.AsSpan().ReprojectAll(sourceProjection, targetProjection);
		}
		flattened = new FlattenedParts
		{
			Points = listRawBackingBuffer,
			Z = ((hasZ & condition) ? UnsafeHelpers.GetListRawBackingBuffer(doubleList) : null),
			PartLengths = partLengths,
			Features = features
		};
		return true;
	}

	private static void CountFeaturesPartsPoints(ArcGisQueryResponse response, bool wantPolygons, out int totalParts, out int totalPoints)
	{
		totalParts = 0;
		totalPoints = 0;
		foreach (ArcGisFeature feature in response.Features)
		{
			List<List<Point3D>> parts = GetParts(feature.Geometry, wantPolygons);
			if (parts == null)
			{
				continue;
			}
			totalParts += parts.Count;
			foreach (List<Point3D> part in parts)
			{
				totalPoints += part.Count;
			}
		}
	}

	private static List<List<Point3D>> GetParts(ArcGisGeometry geometry, bool wantPolygons)
	{
		if (geometry == null)
		{
			return null;
		}
		if (wantPolygons)
		{
			if (!geometry.IsPolygon)
			{
				return null;
			}
			return geometry.Rings;
		}
		if (!geometry.IsPolyline)
		{
			return null;
		}
		return geometry.Paths;
	}
}
