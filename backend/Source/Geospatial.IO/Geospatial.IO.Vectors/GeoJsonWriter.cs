using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Geospatial.GDALAssist;
using Geospatial.GDALAssist.Vectors;
using Geospatial.Vectors;
using Utility.Logging;
using Utility.Memory;

namespace Geospatial.IO.Vectors;

public static class GeoJsonWriter
{
	public static class MessageCodes
	{
		public static readonly MessageClass FailedToWriteGeoJson = new MessageClass("GeoJsonWriter_FailedToWrite", MessageSeverity.Error, "Failed to write GeoJSON file: \"{Filename}\".");

		public static readonly MessageClass EmbeddedIslandConvention = new MessageClass("GeoJsonWriter_EmbeddedIslandConvention", MessageSeverity.Warning, "One or more complex polygons contain an embedded island and were written as GeoJSON Polygons with all rings in order. RAS distinguishes islands from holes by ring orientation (clockwise = fill, counter-clockwise = hole, the ESRI convention), but RFC 7946 consumers treat every ring after the first as a hole — so embedded islands will render as holes in other GIS software.");

		public static Message GetFailedToWriteGeoJson(string filename)
		{
			return FailedToWriteGeoJson.Create(filename);
		}

		public static Message GetEmbeddedIslandConvention()
		{
			return EmbeddedIslandConvention.Create();
		}
	}

	private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	public static string Serialize(GeoJsonFeatureCollection geojson)
	{
		return JsonSerializer.Serialize(geojson, WriteOptions);
	}

	public static OperationResult Write(string filePath, IReadOnlyList<Point> points, Projection? sourceProjection, Table attrTable = null)
	{
		if (sourceProjection == null || sourceProjection.IsEqual(WellKnownProjections.WGS84))
		{
			return WriteWgs84(filePath, points, attrTable);
		}
		Point[] pointsArray = points.ToArray();
		pointsArray.ReprojectAll(sourceProjection, WellKnownProjections.WGS84);
		return WriteWgs84(filePath, pointsArray, attrTable);
	}

	public static OperationResult Write(string filePath, PointCollection points, Projection? sourceProjection, Table attrTable = null)
	{
		if (sourceProjection == null || sourceProjection.IsEqual(WellKnownProjections.WGS84))
		{
			return WriteWgs84(filePath, points, attrTable);
		}
		Point[] pointsArray = points.ToArray();
		pointsArray.AsSpan().ReprojectAll(sourceProjection, WellKnownProjections.WGS84);
		PointCollection points2 = (points.HasZ ? new PointCollection(pointsArray.ToList(), points.Z.ToList()) : new PointCollection(pointsArray));
		return WriteWgs84(filePath, points2, attrTable);
	}

	public static OperationResult Write(string filePath, IReadOnlyList<Polyline> polylines, Projection? sourceProjection, Table attrTable = null)
	{
		if (sourceProjection == null || sourceProjection.IsEqual(WellKnownProjections.WGS84))
		{
			return WriteWgs84(filePath, polylines, attrTable);
		}
		PolylineCollection polylines2 = new PolylineCollection(polylines).ReprojectAll(sourceProjection, WellKnownProjections.WGS84);
		return WriteWgs84(filePath, polylines2, attrTable);
	}

	public static OperationResult Write(string filePath, IReadOnlyList<Polygon> polygons, Projection? sourceProjection, Table attrTable = null)
	{
		if (sourceProjection == null || sourceProjection.IsEqual(WellKnownProjections.WGS84))
		{
			return WriteWgs84(filePath, polygons, attrTable);
		}
		PolygonCollection polygons2 = new PolygonCollection(polygons).ReprojectAll(sourceProjection, WellKnownProjections.WGS84);
		return WriteWgs84(filePath, polygons2, attrTable);
	}

	public static OperationResult Write(string filePath, IReadOnlyList<ComplexPolygon> polygons, Projection? sourceProjection, Table attrTable = null)
	{
		OperationResult operationResult;
		if (sourceProjection == null || sourceProjection.IsEqual(WellKnownProjections.WGS84))
		{
			operationResult = WriteWgs84(filePath, polygons, attrTable);
		}
		else
		{
			ComplexPolygonCollection polygons2 = new ComplexPolygonCollection(polygons).ReprojectAll(sourceProjection, WellKnownProjections.WGS84);
			operationResult = WriteWgs84(filePath, polygons2, attrTable);
		}
		if (operationResult.Result && HasEmbeddedIsland(polygons))
		{
			operationResult.AddMessage(MessageCodes.GetEmbeddedIslandConvention());
		}
		return operationResult;
	}

	public static OperationResult Write(string filePath, IReadOnlyList<ComplexPolygonCollection> multiPolygons, Projection? sourceProjection, Table attrTable = null)
	{
		OperationResult operationResult;
		if (sourceProjection == null || sourceProjection.IsEqual(WellKnownProjections.WGS84))
		{
			operationResult = WriteWgs84(filePath, multiPolygons, attrTable);
		}
		else
		{
			List<ComplexPolygonCollection> complexPolygonCollections = new List<ComplexPolygonCollection>(multiPolygons.Count);
			for (int i = 0; i < multiPolygons.Count; i++)
			{
				complexPolygonCollections.Add(multiPolygons[i].ReprojectAll(sourceProjection, WellKnownProjections.WGS84));
			}
			operationResult = WriteWgs84(filePath, complexPolygonCollections, attrTable);
		}
		if (operationResult.Result && AnyPartHasEmbeddedIsland(multiPolygons))
		{
			operationResult.AddMessage(MessageCodes.GetEmbeddedIslandConvention());
		}
		return operationResult;
	}

	private static bool HasEmbeddedIsland(IReadOnlyList<ComplexPolygon> polygons)
	{
		for (int i = 0; i < polygons.Count; i++)
		{
			int ordinal = 0;
			IReadOnlyList<Polygon> rings = polygons[i].Rings;
			for (int j = 0; j < rings.Count; j++)
			{
				if (rings[j].Area > 0.0)
				{
					ordinal++;
				}
				if (ordinal > 1)
				{
					return true;
				}
			}
		}
		return false;
	}

	private static bool AnyPartHasEmbeddedIsland(IReadOnlyList<ComplexPolygonCollection> multiPolygons)
	{
		for (int i = 0; i < multiPolygons.Count; i++)
		{
			if (HasEmbeddedIsland(multiPolygons[i]))
			{
				return true;
			}
		}
		return false;
	}

	private static OperationResult WriteWgs84(string filePath, IReadOnlyList<Point> points, Table attrTable)
	{
		return WriteFeatureCollection(filePath, () => ToGeoJsonFeatureCollection(points, attrTable));
	}

	private static OperationResult WriteWgs84(string filePath, PointCollection points, Table attrTable)
	{
		return WriteFeatureCollection(filePath, () => ToGeoJsonFeatureCollection(points, attrTable));
	}

	private static OperationResult WriteWgs84(string filePath, IReadOnlyList<Polyline> polylines, Table attrTable)
	{
		return WriteFeatureCollection(filePath, () => ToGeoJsonFeatureCollection(polylines, attrTable));
	}

	private static OperationResult WriteWgs84(string filePath, IReadOnlyList<Polygon> polygons, Table attrTable)
	{
		return WriteFeatureCollection(filePath, () => ToGeoJsonFeatureCollection(polygons, attrTable));
	}

	private static OperationResult WriteWgs84(string filePath, IReadOnlyList<ComplexPolygon> polygons, Table attrTable)
	{
		return WriteFeatureCollection(filePath, () => ToGeoJsonFeatureCollection(polygons, attrTable));
	}

	private static OperationResult WriteWgs84(string filePath, IReadOnlyList<ComplexPolygonCollection> multiPolygons, Table attrTable)
	{
		return WriteFeatureCollection(filePath, () => ToGeoJsonFeatureCollection(multiPolygons, attrTable));
	}

	private static OperationResult WriteFeatureCollection(string filePath, Func<GeoJsonFeatureCollection> build)
	{
		OperationResult operationResult = ValidateOutputPath(filePath);
		if (!operationResult.Result)
		{
			return operationResult;
		}
		try
		{
			File.WriteAllText(filePath, Serialize(build()));
			return operationResult;
		}
		catch (Exception ex)
		{
			return Fail(filePath, ex);
		}
	}

	public static GeoJsonFeatureCollection ToGeoJsonFeatureCollection(IReadOnlyList<Point> points, Table attrTable = null)
	{
		List<GeoJsonFeature> features = new List<GeoJsonFeature>(points.Count);
		for (int i = 0; i < points.Count; i++)
		{
			Point point = points[i];
			features.Add(new GeoJsonFeature
			{
				Type = "Feature",
				Geometry = new GeoJsonGeometry
				{
					Type = "Point",
					Coordinates = JsonSerializer.SerializeToElement(new double[2] { point.X, point.Y })
				},
				Properties = BuildProperties(attrTable, i)
			});
		}
		return new GeoJsonFeatureCollection
		{
			Type = "FeatureCollection",
			Features = features
		};
	}

	public static GeoJsonFeatureCollection ToGeoJsonFeatureCollection(PointCollection points, Table attrTable = null)
	{
		bool hasZ = points.HasZ;
		List<GeoJsonFeature> features = new List<GeoJsonFeature>(points.Count);
		for (int i = 0; i < points.Count; i++)
		{
			Point point = points[i];
			features.Add(new GeoJsonFeature
			{
				Type = "Feature",
				Geometry = new GeoJsonGeometry
				{
					Type = "Point",
					Coordinates = JsonSerializer.SerializeToElement(hasZ ? Position(point.X, point.Y, points.Z[i]) : new double[2] { point.X, point.Y })
				},
				Properties = BuildProperties(attrTable, i)
			});
		}
		return new GeoJsonFeatureCollection
		{
			Type = "FeatureCollection",
			Features = features
		};
	}

	public static GeoJsonFeatureCollection ToGeoJsonFeatureCollection(IReadOnlyList<Polyline> polylines, Table attrTable = null)
	{
		List<GeoJsonFeature> features = new List<GeoJsonFeature>(polylines.Count);
		for (int i = 0; i < polylines.Count; i++)
		{
			features.Add(new GeoJsonFeature
			{
				Type = "Feature",
				Geometry = new GeoJsonGeometry
				{
					Type = "LineString",
					Coordinates = SerializeLineString(polylines[i])
				},
				Properties = BuildProperties(attrTable, i)
			});
		}
		return new GeoJsonFeatureCollection
		{
			Type = "FeatureCollection",
			Features = features
		};
	}

	public static GeoJsonFeatureCollection ToGeoJsonFeatureCollection(IReadOnlyList<Polygon> polygons, Table attrTable = null)
	{
		List<GeoJsonFeature> features = new List<GeoJsonFeature>(polygons.Count);
		for (int i = 0; i < polygons.Count; i++)
		{
			features.Add(new GeoJsonFeature
			{
				Type = "Feature",
				Geometry = new GeoJsonGeometry
				{
					Type = "Polygon",
					Coordinates = SerializePolygon(polygons[i])
				},
				Properties = BuildProperties(attrTable, i)
			});
		}
		return new GeoJsonFeatureCollection
		{
			Type = "FeatureCollection",
			Features = features
		};
	}

	public static GeoJsonFeatureCollection ToGeoJsonFeatureCollection(IReadOnlyList<ComplexPolygon> polygons, Table attrTable = null)
	{
		List<GeoJsonFeature> features = new List<GeoJsonFeature>(polygons.Count);
		for (int i = 0; i < polygons.Count; i++)
		{
			features.Add(new GeoJsonFeature
			{
				Type = "Feature",
				Geometry = new GeoJsonGeometry
				{
					Type = "Polygon",
					Coordinates = SerializeComplexPolygon(polygons[i])
				},
				Properties = BuildProperties(attrTable, i)
			});
		}
		return new GeoJsonFeatureCollection
		{
			Type = "FeatureCollection",
			Features = features
		};
	}

	public static GeoJsonFeatureCollection ToGeoJsonFeatureCollection(IReadOnlyList<ComplexPolygonCollection> multiPolygons, Table attrTable = null)
	{
		List<GeoJsonFeature> features = new List<GeoJsonFeature>(multiPolygons.Count);
		for (int i = 0; i < multiPolygons.Count; i++)
		{
			features.Add(new GeoJsonFeature
			{
				Type = "Feature",
				Geometry = new GeoJsonGeometry
				{
					Type = "MultiPolygon",
					Coordinates = SerializeMultiPolygon(multiPolygons[i])
				},
				Properties = BuildProperties(attrTable, i)
			});
		}
		return new GeoJsonFeatureCollection
		{
			Type = "FeatureCollection",
			Features = features
		};
	}

	private static double[] Position(double x, double y, double z)
	{
		if (double.IsNaN(z))
		{
			return new double[2] { x, y };
		}
		return new double[3] { x, y, z };
	}

	private static JsonElement SerializeLineString(Polyline polyline)
	{
		bool hasZ = polyline.HasZ;
		double[][] doubleValues = new double[polyline.Count][];
		for (int i = 0; i < polyline.Count; i++)
		{
			doubleValues[i] = (hasZ ? Position(polyline[i].X, polyline[i].Y, polyline.Z[i]) : new double[2]
			{
				polyline[i].X,
				polyline[i].Y
			});
		}
		return JsonSerializer.SerializeToElement(doubleValues);
	}

	private static JsonElement SerializePolygon(Polygon polygon)
	{
		return JsonSerializer.SerializeToElement(new double[1][][] { SerializeRing(polygon) });
	}

	private static JsonElement SerializeComplexPolygon(ComplexPolygon polygon)
	{
		return JsonSerializer.SerializeToElement(SerializeComplexPolygonRings(polygon));
	}

	private static JsonElement SerializeMultiPolygon(ComplexPolygonCollection multiPolygon)
	{
		double[][][][] doubleValues = new double[multiPolygon.Count][][][];
		for (int i = 0; i < multiPolygon.Count; i++)
		{
			doubleValues[i] = SerializeComplexPolygonRings(multiPolygon[i]);
		}
		return JsonSerializer.SerializeToElement(doubleValues);
	}

	private static double[][][] SerializeComplexPolygonRings(ComplexPolygon polygon)
	{
		IReadOnlyList<Polygon> rings = polygon.Rings;
		double[][][] doubleValues = new double[rings.Count][][];
		for (int i = 0; i < rings.Count; i++)
		{
			doubleValues[i] = SerializeRing(rings[i]);
		}
		return doubleValues;
	}

	private static double[][] SerializeRing(Polygon ring)
	{
		int count = ring.Count;
		bool hasZ = ring.HasZ;
		bool countRingTerm = count > 0 && ring[0] != ring[count - 1];
		double[][] doubleValues = new double[countRingTerm ? (count + 1) : count][];
		for (int i = 0; i < count; i++)
		{
			int index = count - 1 - i;
			Point point = ring[index];
			doubleValues[i] = (hasZ ? Position(point.X, point.Y, ring.Z[index]) : new double[2] { point.X, point.Y });
		}
		if (countRingTerm)
		{
			Point point2 = ring[count - 1];
			doubleValues[count] = (hasZ ? Position(point2.X, point2.Y, ring.Z[count - 1]) : new double[2] { point2.X, point2.Y });
		}
		return doubleValues;
	}

	private static JsonElement BuildProperties(Table table, int rowIndex)
	{
		if (table == null || table.Columns.Count == 0 || rowIndex >= table.Rows.Count)
		{
			return JsonSerializer.SerializeToElement(new Dictionary<string, object>());
		}
		Dictionary<string, object> objectByString = new Dictionary<string, object>(table.Columns.Count);
		TableRow tableRow = table[rowIndex];
		for (int i = 0; i < table.Columns.Count; i++)
		{
			string name = table.Columns[i].Name;
			object tableRow2 = tableRow[i];
			objectByString[name] = tableRow2 ?? "";
		}
		return JsonSerializer.SerializeToElement(objectByString);
	}

	private static OperationResult ValidateOutputPath(string filePath)
	{
		OperationResult operationResult = new OperationResult();
		string directoryName = Path.GetDirectoryName(filePath);
		if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
		{
			operationResult.AddFailure(FileMessageCodes.GetDirectoryNotFound(directoryName));
		}
		return operationResult;
	}

	private static OperationResult Fail(string filePath, Exception ex)
	{
		OperationResult operationResult = new OperationResult(success: false);
		operationResult.AddMessage(GeneralMessageCodes.GetExceptionOccurred("Write GeoJSON file", ex));
		operationResult.AddMessage(MessageCodes.GetFailedToWriteGeoJson(filePath));
		return operationResult;
	}
}
