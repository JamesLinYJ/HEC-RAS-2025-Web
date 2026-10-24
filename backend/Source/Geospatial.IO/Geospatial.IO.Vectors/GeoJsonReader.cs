using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Geospatial.Features;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.Extensions;
using Utility.Logging;
using Utility.Memory;

namespace Geospatial.IO.Vectors;

public static class GeoJsonReader
{
	public static class MessageCodes
	{
		public static readonly MessageClass NoFeaturesDetected = new MessageClass("GeoJsonReader_NoFeaturesDetected", MessageSeverity.Error, "No features detected in \"{Filename}\".");

		public static readonly MessageClass GeometryTypeDetectionFailed = new MessageClass("GeoJsonReader_GeometryTypeDetectionFailed", MessageSeverity.Error, "Failed to detect geometry type in GeoJSON: \"{Filename}\".");

		public static readonly MessageClass FailedToReadGeoJson = new MessageClass("GeoJsonReader_FailedToRead", MessageSeverity.Error, "Failed to read GeoJSON file: \"{Filename}\"");

		public static readonly MessageClass RightHandRuleDeviation = new MessageClass("GeoJsonReader_RightHandRuleDeviation", MessageSeverity.Information, "GeoJSON deviates from the RFC 7946 right-hand rule (exterior rings should be counter-clockwise). The winding convention was inferred from each polygon's exterior ring and read accordingly.");

		public static readonly MessageClass UnlocatedFeaturesSkipped = new MessageClass("GeoJsonReader_UnlocatedFeaturesSkipped", MessageSeverity.Information, "GeoJSON contains {UnlocatedCount} unlocated feature(s) (null geometry, RFC 7946 §3.2); each is skipped along with its attribute row.");

		public static readonly MessageClass ComplexPolygonValidationFailed = new MessageClass("GeoJsonReader_ComplexPolygonValidationFailed", MessageSeverity.Warning, "A GeoJSON polygon failed hole/multipart validation ({ValidationReason}); it was read as its outer ring only.");

		public static readonly MessageClass ComplexPropertyStoredAsText = new MessageClass("GeoJsonReader_ComplexPropertyStoredAsText", MessageSeverity.Information, "GeoJSON property/properties {PropertyNames} have nested object or array values; each was stored as raw JSON text in a string column (not flattened).");

		public static Message GetNoFeaturesDetected(string filename)
		{
			return NoFeaturesDetected.Create(filename);
		}

		public static Message GetGeometryTypeDetectionFailed(string filename)
		{
			return GeometryTypeDetectionFailed.Create(filename);
		}

		public static Message GetFailedToReadGeoJson(string filename)
		{
			return FailedToReadGeoJson.Create(filename);
		}

		public static Message GetRightHandRuleDeviation()
		{
			return RightHandRuleDeviation.Create();
		}

		public static Message GetUnlocatedFeaturesSkipped(int count)
		{
			return UnlocatedFeaturesSkipped.Create(count.ToString());
		}

		public static Message GetComplexPolygonValidationFailed(string reason)
		{
			return ComplexPolygonValidationFailed.Create(reason);
		}

		public static Message GetComplexPropertyStoredAsText(IEnumerable<string> propertyNames)
		{
			return ComplexPropertyStoredAsText.Create(string.Join(", ", propertyNames));
		}
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("GeoJsonReader");

	public static GeoJsonFeatureCollection Deserialize(string json)
	{
		return NormalizeToFeatureCollection(json);
	}

	public static OperationResult TryRead(string filePath, out PointFeatureCollection collection)
	{
		collection = null;
		OperationResult operationResult = ValidateFile(filePath);
		if (!operationResult.Result)
		{
			return operationResult;
		}
		try
		{
			GeoJsonFeatureCollection geojson = DeserializeFile(filePath);
			operationResult.MergeIn(ToPointFeatureCollection(geojson, out collection));
			return operationResult;
		}
		catch (Exception ex)
		{
			return Fail(filePath, ex);
		}
	}

	public static OperationResult TryRead(string filePath, out PolylineFeatureCollection collection)
	{
		collection = null;
		OperationResult operationResult = ValidateFile(filePath);
		if (!operationResult.Result)
		{
			return operationResult;
		}
		try
		{
			GeoJsonFeatureCollection geojson = DeserializeFile(filePath);
			operationResult.MergeIn(ToPolylineFeatureCollection(geojson, out collection));
			return operationResult;
		}
		catch (Exception ex)
		{
			return Fail(filePath, ex);
		}
	}

	public static OperationResult TryRead(string filePath, out PolygonFeatureCollection collection)
	{
		collection = null;
		OperationResult operationResult = ValidateFile(filePath);
		if (!operationResult.Result)
		{
			return operationResult;
		}
		try
		{
			GeoJsonFeatureCollection geojson = DeserializeFile(filePath);
			operationResult.MergeIn(ToPolygonFeatureCollection(geojson, out collection));
			return operationResult;
		}
		catch (Exception ex)
		{
			return Fail(filePath, ex);
		}
	}

	public static OperationResult TryRead(string filePath, out ComplexPolygonFeatureCollection collection)
	{
		collection = null;
		OperationResult operationResult = ValidateFile(filePath);
		if (!operationResult.Result)
		{
			return operationResult;
		}
		try
		{
			GeoJsonFeatureCollection geojson = DeserializeFile(filePath);
			operationResult.MergeIn(ToComplexPolygonFeatureCollection(geojson, out collection));
			return operationResult;
		}
		catch (Exception ex)
		{
			return Fail(filePath, ex);
		}
	}

	public static OperationResult TryRead(string filePath, out ComplexPolygonCollectionFeatureCollection collection)
	{
		collection = null;
		OperationResult operationResult = ValidateFile(filePath);
		if (!operationResult.Result)
		{
			return operationResult;
		}
		try
		{
			GeoJsonFeatureCollection geojson = DeserializeFile(filePath);
			operationResult.MergeIn(ToComplexPolygonCollectionFeatureCollection(geojson, out collection));
			return operationResult;
		}
		catch (Exception ex)
		{
			return Fail(filePath, ex);
		}
	}

	public static OperationResult TrySort(string filePath, out GeoJsonFeatureBuckets buckets)
	{
		buckets = null;
		OperationResult operationResult = ValidateFile(filePath);
		if (!operationResult.Result)
		{
			return operationResult;
		}
		try
		{
			return TrySort(DeserializeFile(filePath), out buckets);
		}
		catch (Exception ex)
		{
			return Fail(filePath, ex);
		}
	}

	public static OperationResult TrySort(GeoJsonFeatureCollection geojson, out GeoJsonFeatureBuckets buckets)
	{
		buckets = null;
		OperationResult operationResult = new OperationResult();
		try
		{
			List<GeoJsonFeature> geoJsonFeatures = new List<GeoJsonFeature>();
			List<GeoJsonFeature> geoJsonFeaturesList = new List<GeoJsonFeature>();
			List<GeoJsonFeature> geoJsonFeatures2 = new List<GeoJsonFeature>();
			int ordinal = 0;
			if (geojson?.Features != null)
			{
				foreach (GeoJsonFeature feature in geojson.Features)
				{
					if (feature.Geometry == null)
					{
						ordinal++;
						continue;
					}
					string type = feature.Geometry.Type;
					if (GeoJsonTypes.IsPointType(type))
					{
						geoJsonFeatures.Add(feature);
					}
					else if (GeoJsonTypes.IsLineType(type))
					{
						geoJsonFeaturesList.Add(feature);
					}
					else if (GeoJsonTypes.IsPolygonType(type))
					{
						geoJsonFeatures2.Add(feature);
					}
				}
			}
			operationResult.MergeIn(BuildPointBucket(geoJsonFeatures, out var bucket));
			operationResult.MergeIn(BuildPolylineBucket(geoJsonFeaturesList, out var bucket2));
			operationResult.MergeIn(BuildPolygonBucket(geoJsonFeatures2, out var bucket3));
			buckets = new GeoJsonFeatureBuckets
			{
				Points = bucket,
				Polylines = bucket2,
				Polygons = bucket3
			};
			if (ordinal > 0)
			{
				operationResult.AddMessage(MessageCodes.GetUnlocatedFeaturesSkipped(ordinal));
			}
			return operationResult;
		}
		catch (Exception ex)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetExceptionOccurred("Sort GeoJSON features", ex));
			return operationResult;
		}
	}

	public static OperationResult ToPointFeatureCollection(GeoJsonFeatureCollection geojson, out PointFeatureCollection collection)
	{
		OperationResult operationResult = FilterByGeometryTypes(geojson, out var matched, "Point");
		List<Point> points = new List<Point>(matched.Count);
		List<double> z = null;
		foreach (GeoJsonFeature matched2 in matched)
		{
			AppendPosition(matched2.Geometry.Coordinates, points, ref z);
		}
		PointCollection pc = ((z == null) ? new PointCollection(points) : new PointCollection(points, z));
		operationResult.MergeIn(TryBuildAttributeTable(matched, out var table));
		collection = new PointFeatureCollection(pc, table);
		return operationResult;
	}

	public static OperationResult ToPolylineFeatureCollection(GeoJsonFeatureCollection geojson, out PolylineFeatureCollection collection)
	{
		OperationResult operationResult = FilterByGeometryTypes(geojson, out var matched, "LineString");
		PolylineCollection polylineCollection = new PolylineCollection(matched.Count);
		foreach (GeoJsonFeature matched2 in matched)
		{
			polylineCollection.Add(ParseLineString(matched2.Geometry.Coordinates));
		}
		operationResult.MergeIn(TryBuildAttributeTable(matched, out var table));
		collection = new PolylineFeatureCollection(polylineCollection, table);
		return operationResult;
	}

	public static OperationResult ToPolygonFeatureCollection(GeoJsonFeatureCollection geojson, out PolygonFeatureCollection collection)
	{
		OperationResult operationResult = FilterByGeometryTypes(geojson, out var matched, "Polygon");
		PolygonCollection polygonCollection = new PolygonCollection(matched.Count);
		foreach (GeoJsonFeature matched2 in matched)
		{
			operationResult.MergeIn(ParsePolygonOuterRing(matched2.Geometry.Coordinates, out var ring));
			polygonCollection.Add(ring);
		}
		operationResult.MergeIn(TryBuildAttributeTable(matched, out var table));
		collection = new PolygonFeatureCollection(polygonCollection, table);
		return operationResult;
	}

	public static OperationResult ToComplexPolygonFeatureCollection(GeoJsonFeatureCollection geojson, out ComplexPolygonFeatureCollection collection)
	{
		OperationResult operationResult = FilterByGeometryTypes(geojson, out var matched, "Polygon");
		ComplexPolygonCollection complexPolygonCollection = new ComplexPolygonCollection(matched.Count);
		foreach (GeoJsonFeature matched2 in matched)
		{
			operationResult.MergeIn(ParseComplexPolygon(matched2.Geometry.Coordinates, out var poly));
			complexPolygonCollection.Add(poly);
		}
		operationResult.MergeIn(TryBuildAttributeTable(matched, out var table));
		collection = new ComplexPolygonFeatureCollection(complexPolygonCollection, table);
		return operationResult;
	}

	public static OperationResult ToComplexPolygonCollectionFeatureCollection(GeoJsonFeatureCollection geojson, out ComplexPolygonCollectionFeatureCollection collection)
	{
		OperationResult operationResult = FilterByPolygonalGeometry(geojson, out var matched);
		operationResult.MergeIn(BuildPolygonBucket(matched, out collection));
		return operationResult;
	}

	public static OperationResult ToPointCollectionFeatureCollection(GeoJsonFeatureCollection geojson, out PointCollectionFeatureCollection collection)
	{
		OperationResult operationResult = FilterByPointGeometry(geojson, out var matched);
		operationResult.MergeIn(BuildPointBucket(matched, out collection));
		return operationResult;
	}

	public static OperationResult ToPolylineCollectionFeatureCollection(GeoJsonFeatureCollection geojson, out PolylineCollectionFeatureCollection collection)
	{
		OperationResult operationResult = FilterByLineGeometry(geojson, out var matched);
		operationResult.MergeIn(BuildPolylineBucket(matched, out collection));
		return operationResult;
	}

	private static OperationResult BuildPointBucket(List<GeoJsonFeature> matched, out PointCollectionFeatureCollection bucket)
	{
		PointCollectionCollection pointCollectionCollection = new PointCollectionCollection();
		foreach (GeoJsonFeature matched2 in matched)
		{
			pointCollectionCollection.Add(ParseMultiPoint(matched2.Geometry));
		}
		OperationResult result = TryBuildAttributeTable(matched, out var table);
		bucket = new PointCollectionFeatureCollection(pointCollectionCollection, table);
		return result;
	}

	private static OperationResult BuildPolylineBucket(List<GeoJsonFeature> matched, out PolylineCollectionFeatureCollection bucket)
	{
		PolylineCollectionCollection polylineCollectionCollection = new PolylineCollectionCollection();
		foreach (GeoJsonFeature matched2 in matched)
		{
			polylineCollectionCollection.Add(ParseMultiLineString(matched2.Geometry));
		}
		OperationResult result = TryBuildAttributeTable(matched, out var table);
		bucket = new PolylineCollectionFeatureCollection(polylineCollectionCollection, table);
		return result;
	}

	private static OperationResult BuildPolygonBucket(List<GeoJsonFeature> matched, out ComplexPolygonCollectionFeatureCollection bucket)
	{
		OperationResult operationResult = new OperationResult();
		ComplexPolygonCollectionCollection complexPolygonCollectionCollection = new ComplexPolygonCollectionCollection();
		foreach (GeoJsonFeature matched2 in matched)
		{
			operationResult.MergeIn(ParseMultiPolygon(matched2.Geometry, out var collection));
			complexPolygonCollectionCollection.Add(collection);
		}
		operationResult.MergeIn(TryBuildAttributeTable(matched, out var table));
		bucket = new ComplexPolygonCollectionFeatureCollection(complexPolygonCollectionCollection, table);
		return operationResult;
	}

	private static Point ParsePoint(JsonElement coords)
	{
		if (coords.ValueKind != JsonValueKind.Array || coords.GetArrayLength() < 2)
		{
			throw new JsonException("GeoJSON position must have at least 2 coordinates (RFC 7946 §3.1.1).");
		}
		double x = coords[0].GetDouble();
		double y = coords[1].GetDouble();
		return new Point(x, y);
	}

	private static void ParsePositions(JsonElement coords, out List<Point> points, out List<double> z)
	{
		int arrayLength = coords.GetArrayLength();
		points = new List<Point>(arrayLength);
		z = null;
		foreach (JsonElement enumerateArray in coords.EnumerateArray())
		{
			AppendPosition(enumerateArray, points, ref z);
		}
	}

	private static void AppendPosition(JsonElement coord, List<Point> points, ref List<double> z)
	{
		points.Add(ParsePoint(coord));
		bool getArrayLengthCoordTerm = coord.GetArrayLength() >= 3;
		// A missing optional elevation remains no-data in the source model.
		// Map XY is still readable; the editor preserves the original JSON
		// null token rather than replacing it with a synthetic elevation.
		double getDouble = !getArrayLengthCoordTerm ? 0.0
			: coord[2].ValueKind == JsonValueKind.Null ? double.NaN : coord[2].GetDouble();
		if (getArrayLengthCoordTerm && z == null)
		{
			z = new List<double>(new double[points.Count - 1]);
		}
		if (z != null)
		{
			z.Add(getDouble);
		}
	}

	private static Polyline ParseLineString(JsonElement coords)
	{
		ParsePositions(coords, out var points, out var z);
		if (points.Count < 2)
		{
			throw new JsonException("GeoJSON LineString must have at least 2 positions (RFC 7946 §3.1.4).");
		}
		if (z != null)
		{
			return new Polyline(points, z);
		}
		return new Polyline(points);
	}

	private static OperationResult ParsePolygonOuterRing(JsonElement coords, out Polygon ring)
	{
		if (coords.ValueKind != JsonValueKind.Array || coords.GetArrayLength() < 1)
		{
			throw new JsonException("GeoJSON Polygon must have at least one ring (RFC 7946 §3.1.6).");
		}
		OperationResult operationResult = new OperationResult();
		ring = ParseRing(coords[0]);
		if (ring.Area > 0.0)
		{
			operationResult.AddMessage(MessageCodes.GetRightHandRuleDeviation());
		}
		else
		{
			ring.Reverse();
		}
		return operationResult;
	}

	private static OperationResult ParseComplexPolygon(JsonElement coords, out ComplexPolygon poly)
	{
		OperationResult operationResult = new OperationResult();
		int arrayLength = coords.GetArrayLength();
		List<Polygon> polygons = new List<Polygon>(arrayLength);
		for (int i = 0; i < arrayLength; i++)
		{
			polygons.Add(ParseRing(coords[i]));
		}
		if (polygons[0].Area > 0.0)
		{
			operationResult.AddMessage(MessageCodes.GetRightHandRuleDeviation());
		}
		else
		{
			foreach (Polygon polygon in polygons)
			{
				polygon.Reverse();
			}
		}
		if (polygons.Count == 1)
		{
			poly = ComplexPolygon.FromOuterRing(polygons[0]);
			return operationResult;
		}
		OperationResult operationResult2 = ComplexPolygon.TryCreateComplexPolygon(polygons, out var poly2, checkIntersections: false);
		if ((bool)operationResult2)
		{
			poly = poly2;
			return operationResult;
		}
		poly = ComplexPolygon.FromOuterRing(polygons[0]);
		operationResult.AddMessage(MessageCodes.GetComplexPolygonValidationFailed(operationResult2.GetConcatenatedMessages()));
		return operationResult;
	}

	private static PointCollection ParseMultiPoint(GeoJsonGeometry geometry)
	{
		if (GeoJsonTypes.IsMultiPoint(geometry.Type))
		{
			ParsePositions(geometry.Coordinates, out var points, out var z);
			if (z != null)
			{
				return new PointCollection(points, z);
			}
			return new PointCollection(points);
		}
		List<Point> points2 = new List<Point>(1);
		List<double> z2 = null;
		AppendPosition(geometry.Coordinates, points2, ref z2);
		if (z2 != null)
		{
			return new PointCollection(points2, z2);
		}
		return new PointCollection(points2);
	}

	private static PolylineCollection ParseMultiLineString(GeoJsonGeometry geometry)
	{
		if (GeoJsonTypes.IsMultiLineString(geometry.Type))
		{
			JsonElement coordinates = geometry.Coordinates;
			int arrayLength = coordinates.GetArrayLength();
			PolylineCollection polylineCollection = new PolylineCollection(arrayLength);
			for (int i = 0; i < arrayLength; i++)
			{
				polylineCollection.Add(ParseLineString(coordinates[i]));
			}
			return polylineCollection;
		}
		return new PolylineCollection(1) { ParseLineString(geometry.Coordinates) };
	}

	private static OperationResult ParseMultiPolygon(GeoJsonGeometry geometry, out ComplexPolygonCollection collection)
	{
		OperationResult operationResult = new OperationResult();
		if (GeoJsonTypes.IsMultiPolygon(geometry.Type))
		{
			JsonElement coordinates = geometry.Coordinates;
			int arrayLength = coordinates.GetArrayLength();
			collection = new ComplexPolygonCollection(arrayLength);
			for (int i = 0; i < arrayLength; i++)
			{
				operationResult.MergeIn(ParseComplexPolygon(coordinates[i], out var poly));
				collection.Add(poly);
			}
			return operationResult;
		}
		collection = new ComplexPolygonCollection(1);
		operationResult.MergeIn(ParseComplexPolygon(geometry.Coordinates, out var poly2));
		collection.Add(poly2);
		return operationResult;
	}

	private static Polygon ParseRing(JsonElement ringElement)
	{
		ParsePositions(ringElement, out var points, out var z);
		if (points.Count < 4)
		{
			throw new JsonException("GeoJSON linear ring must have at least 4 positions (RFC 7946 §3.1.6).");
		}
		Point point = points[0];
		List<Point> pointsList = points;
		Point point2 = pointsList[pointsList.Count - 1];
		if (point.X != point2.X || point.Y != point2.Y)
		{
			throw new JsonException("GeoJSON linear ring must be closed: its first and last positions must be identical (RFC 7946 §3.1.6).");
		}
		if (z != null)
		{
			return new Polygon(points, z);
		}
		return new Polygon(points);
	}

	private static OperationResult TryBuildAttributeTable(List<GeoJsonFeature> features, out Table table)
	{
		OperationResult operationResult = new OperationResult();
		table = new Table();
		if (features.Count == 0)
		{
			return operationResult;
		}
		try
		{
			Dictionary<string, Type> typeValueByString = new Dictionary<string, Type>();
			List<string> stringList = new List<string>();
			HashSet<string> stringSet = new HashSet<string>();
			foreach (GeoJsonFeature feature in features)
			{
				if (feature.Properties.ValueKind != JsonValueKind.Object)
				{
					continue;
				}
				foreach (JsonProperty enumerateObject in feature.Properties.EnumerateObject())
				{
					Type type = InferType(enumerateObject.Value);
					if (type == null)
					{
						continue;
					}
					JsonValueKind valueKind = enumerateObject.Value.ValueKind;
					if (valueKind - 1 <= JsonValueKind.Object)
					{
						stringSet.Add(enumerateObject.Name);
					}
					if (!typeValueByString.ContainsKey(enumerateObject.Name))
					{
						typeValueByString[enumerateObject.Name] = type;
						stringList.Add(enumerateObject.Name);
						continue;
					}
					Type type2 = typeValueByString[enumerateObject.Name];
					if (type2 == typeof(long) && type == typeof(double))
					{
						typeValueByString[enumerateObject.Name] = typeof(double);
					}
					else if (type2 != type && type2 != typeof(string))
					{
						typeValueByString[enumerateObject.Name] = typeof(string);
					}
				}
			}
			if (stringSet.Count > 0)
			{
				operationResult.AddMessage(MessageCodes.GetComplexPropertyStoredAsText(stringList.Where(stringSet.Contains)));
			}
			foreach (string textValue in stringList)
			{
				table.AddColumn(textValue, typeValueByString[textValue]);
			}
			foreach (GeoJsonFeature feature2 in features)
			{
				object[] objectValues = new object[stringList.Count];
				for (int i = 0; i < stringList.Count; i++)
				{
					string textValue2 = stringList[i];
					Type type3 = typeValueByString[textValue2];
					if (feature2.Properties.ValueKind == JsonValueKind.Object && feature2.Properties.TryGetProperty(textValue2, out var localValue))
					{
						objectValues[i] = JsonValueExtractor.ToTypedValue(localValue, type3);
					}
					else
					{
						objectValues[i] = JsonValueExtractor.DefaultFor(type3);
					}
				}
				table.AddNewRow(objectValues);
			}
		}
		catch (Exception ex)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetExceptionOccurred("Build GeoJSON attribute table", ex));
		}
		return operationResult;
	}

	private static Type InferType(JsonElement element)
	{
		long ordinal;
		return element.ValueKind switch
		{
			JsonValueKind.Number => element.TryGetInt64(out ordinal) ? typeof(long) : typeof(double), 
			JsonValueKind.String => typeof(string), 
			JsonValueKind.True => typeof(bool), 
			JsonValueKind.False => typeof(bool), 
			JsonValueKind.Object => typeof(string), 
			JsonValueKind.Array => typeof(string), 
			_ => null, 
		};
	}

	private static OperationResult FilterByGeometryTypes(GeoJsonFeatureCollection geojson, out List<GeoJsonFeature> matched, params string[] geometryTypes)
	{
		OperationResult operationResult = new OperationResult();
		matched = new List<GeoJsonFeature>();
		if (geojson?.Features == null)
		{
			return operationResult;
		}
		int ordinal = 0;
		foreach (GeoJsonFeature feature in geojson.Features)
		{
			if (feature.Geometry == null)
			{
				ordinal++;
				continue;
			}
			foreach (string expected in geometryTypes)
			{
				if (GeoJsonTypes.Matches(feature.Geometry.Type, expected))
				{
					matched.Add(feature);
					break;
				}
			}
		}
		if (ordinal > 0)
		{
			operationResult.AddMessage(MessageCodes.GetUnlocatedFeaturesSkipped(ordinal));
		}
		return operationResult;
	}

	private static OperationResult FilterByPointGeometry(GeoJsonFeatureCollection geojson, out List<GeoJsonFeature> matched)
	{
		return FilterByGeometryTypes(geojson, out matched, "Point", "MultiPoint");
	}

	private static OperationResult FilterByLineGeometry(GeoJsonFeatureCollection geojson, out List<GeoJsonFeature> matched)
	{
		return FilterByGeometryTypes(geojson, out matched, "LineString", "MultiLineString");
	}

	private static OperationResult FilterByPolygonalGeometry(GeoJsonFeatureCollection geojson, out List<GeoJsonFeature> matched)
	{
		return FilterByGeometryTypes(geojson, out matched, "Polygon", "MultiPolygon");
	}

	public static OperationResult TryDetectGeometryType(string filePath, out ShapeType geomtype)
	{
		geomtype = ShapeType.Unknown;
		OperationResult operationResult = ValidateFile(filePath);
		if (!operationResult.Result)
		{
			return operationResult;
		}
		try
		{
			GeoJsonFeatureCollection geoJsonFeatureCollection = DeserializeFile(filePath);
			if (geoJsonFeatureCollection?.Features == null)
			{
				operationResult.AddFailure(MessageCodes.GetNoFeaturesDetected(filePath));
				return operationResult;
			}
			foreach (GeoJsonFeature feature in geoJsonFeatureCollection.Features)
			{
				if (feature.Geometry != null)
				{
					string type = feature.Geometry.Type;
					if (GeoJsonTypes.IsPointType(type))
					{
						geomtype = ShapeType.Point;
					}
					if (GeoJsonTypes.IsLineType(type))
					{
						geomtype = ShapeType.Line;
					}
					if (GeoJsonTypes.IsPolygonType(type))
					{
						geomtype = ShapeType.Polygon;
					}
					if (geomtype != ShapeType.Unknown)
					{
						return operationResult;
					}
				}
			}
		}
		catch (Exception ex)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetExceptionOccurred("Detect GeoJSON geometry type", ex));
			operationResult.AddMessage(MessageCodes.GetGeometryTypeDetectionFailed(filePath));
			return operationResult;
		}
		operationResult.AddFailure(MessageCodes.GetGeometryTypeDetectionFailed(filePath));
		return operationResult;
	}

	public static bool HasJsonFileExtension(string path)
	{
		string extension = Path.GetExtension(path);
		if (!extension.RoughlyEquals(".geojson"))
		{
			return extension.RoughlyEquals(".json");
		}
		return true;
	}

	private static GeoJsonFeatureCollection DeserializeFile(string filePath)
	{
		return NormalizeToFeatureCollection(File.ReadAllText(filePath));
	}

	private static OperationResult ValidateFile(string filePath)
	{
		OperationResult operationResult = new OperationResult();
		if (!File.Exists(filePath))
		{
			operationResult.AddFailure(FileMessageCodes.GetFileNotFound(filePath));
		}
		return operationResult;
	}

	private static string PeekType(string json)
	{
		Utf8JsonReader utf8JsonReader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
		while (utf8JsonReader.Read())
		{
			if (utf8JsonReader.TokenType == JsonTokenType.PropertyName && utf8JsonReader.ValueTextEquals("type"))
			{
				utf8JsonReader.Read();
				return utf8JsonReader.GetString();
			}
		}
		return null;
	}

	private static GeoJsonFeatureCollection NormalizeToFeatureCollection(string json)
	{
		string peekType = PeekType(json);
		if (GeoJsonTypes.Matches(peekType, "FeatureCollection"))
		{
			return JsonSerializer.Deserialize<GeoJsonFeatureCollection>(json);
		}
		if (GeoJsonTypes.Matches(peekType, "Feature"))
		{
			GeoJsonFeature geoJsonFeature = JsonSerializer.Deserialize<GeoJsonFeature>(json);
			GeoJsonFeatureCollection geoJsonFeatureCollection = new GeoJsonFeatureCollection
			{
				Type = "FeatureCollection"
			};
			int unity = 1;
			List<GeoJsonFeature> features = new List<GeoJsonFeature>(unity);
			CollectionsMarshal.SetCount(features, unity);
			Span<GeoJsonFeature> geoJsonFeatures = CollectionsMarshal.AsSpan(features);
			int index = 0;
			geoJsonFeatures[index] = geoJsonFeature;
			geoJsonFeatureCollection.Features = features;
			return geoJsonFeatureCollection;
		}
		throw new JsonException($"GeoJSON expected as {"FeatureCollection"} or {"Feature"}, but found {peekType}.");
	}

	private static OperationResult Fail(string filePath, Exception ex)
	{
		OperationResult operationResult = new OperationResult(success: false);
		operationResult.AddMessage(GeneralMessageCodes.GetExceptionOccurred("Read GeoJSON file", ex));
		operationResult.AddMessage(MessageCodes.GetFailedToReadGeoJson(filePath));
		return operationResult;
	}
}
