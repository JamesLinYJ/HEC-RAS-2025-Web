using System;
using System.Collections.Generic;
using System.Data;
using Geospatial.Vectors;
using OSGeo.OGR;
using OSGeo.OSR;
using Utility.Memory;
using Utility.Progress;
using Utility.Units;

namespace Geospatial.GDALAssist.Vectors;

public sealed class VectorLayer : IDisposable
{
	private readonly Layer _lyr;

	private readonly DataSource _srcDs;

	private readonly FeatureDefn _lyrDefinition;

	private readonly bool _isSqlQuery;

	private readonly Dictionary<int, FieldDefn> _fields = new Dictionary<int, FieldDefn>();

	private static readonly Dictionary<string, Type> TypeMap = new Dictionary<string, Type>
	{
		{
			"String",
			typeof(string)
		},
		{
			"Int",
			typeof(int)
		},
		{
			"Integer",
			typeof(int)
		},
		{
			"Integer64",
			typeof(long)
		},
		{
			"Long",
			typeof(long)
		},
		{
			"Real",
			typeof(double)
		},
		{
			"DateTime",
			typeof(DateTime)
		}
	};

	private Projection _nativeProjection;

	public Layer OgrLayer => _lyr;

	internal VectorLayer(Layer lyr)
	{
		_lyr = lyr;
		_lyrDefinition = _lyr.GetLayerDefn();
		_isSqlQuery = false;
	}

	internal VectorLayer(Layer lyr, DataSource ds, bool isSqlQuery)
		: this(lyr)
	{
		_isSqlQuery = isSqlQuery;
		_srcDs = ds;
	}

	public string GetName()
	{
		return _lyr.GetName();
	}

	public long GetFeatureCount()
	{
		return _lyr.GetFeatureCount(1);
	}

	public int GetFieldCount()
	{
		return _lyrDefinition.GetFieldCount();
	}

	public string GetFieldName(int idx)
	{
		return EnsureField(idx).GetName();
	}

	public string GetFieldTypeName(int idx)
	{
		return EnsureField(idx)?.GetTypeName();
	}

	public Type GetFieldType(int idx)
	{
		string fieldTypeName = GetFieldTypeName(idx);
		TypeMap.TryGetValue(fieldTypeName, out var fieldType);
		return fieldType;
	}

	public List<KeyValuePair<string, Type>> GetAllFields()
	{
		int fieldCount = GetFieldCount();
		List<KeyValuePair<string, Type>> allFields = new List<KeyValuePair<string, Type>>(fieldCount);
		for (int i = 0; i < fieldCount; i++)
		{
			allFields.Add(new KeyValuePair<string, Type>(GetFieldName(i), GetFieldType(i)));
		}
		return allFields;
	}

	public Type[] GetFieldIntKeys()
	{
		int fieldCount = GetFieldCount();
		Type[] fieldIntKeys = new Type[fieldCount];
		for (int i = 0; i < fieldCount; i++)
		{
			fieldIntKeys[i] = GetFieldType(i);
		}
		return fieldIntKeys;
	}

	private FieldDefn EnsureField(int idx)
	{
		lock (_fields)
		{
			if (_fields.TryGetValue(idx, out var localValue))
			{
				return localValue;
			}
			FieldDefn fieldDefn = _lyrDefinition.GetFieldDefn(idx);
			_fields.Add(idx, fieldDefn);
			return fieldDefn;
		}
	}

	public VectorType GetGeometryType()
	{
		return VectorTypeHelpers.Parse(_lyrDefinition.GetGeomType());
	}

	public List<ComplexPolygonCollection> ReadFeaturesAsPolygonCollections(Extent filterExtent, DataTable dtStructure = null, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		VectorType geometryType = GetGeometryType();
		if (geometryType != VectorType.PolygonCollection && geometryType != VectorType.Polygon)
		{
			return null;
		}
		_lyr.SetSpatialFilterRect(filterExtent.MinX, filterExtent.MinY, filterExtent.MaxX, filterExtent.MaxY);
		int featureCount = (int)GetFeatureCount();
		List<ComplexPolygonCollection> complexPolygonCollections = new List<ComplexPolygonCollection>();
		int ordinal = 0;
		int getFID = 0;
		while (true)
		{
			prog.ReportProgress(getFID * 100 / featureCount);
			try
			{
				using Feature feature = _lyr.GetNextFeature();
				if (feature == null)
				{
					break;
				}
				getFID = (int)feature.GetFID();
				using (Geometry geometry = feature.GetGeometryRef())
				{
					if (geometry == null)
					{
						continue;
					}
					ComplexPolygonCollection complexPolygonCollection = BuildPolygonCollection(geometry, prog);
					if (complexPolygonCollection == null || complexPolygonCollection.Count == 0)
					{
						ordinal++;
					}
					complexPolygonCollections.Add(complexPolygonCollection);
					goto IL_00bf;
				}
				IL_00bf:
				if (dtStructure != null)
				{
					DataRow dataRow = dtStructure.NewRow();
					for (int i = 0; i < dtStructure.Columns.Count; i++)
					{
						dataRow[i] = SwitchType(feature, feature.GetFieldIndex(dtStructure.Columns[i].ColumnName), dtStructure.Columns[i].DataType);
					}
					dtStructure.Rows.Add(dataRow);
				}
				continue;
			}
			catch (Exception ex)
			{
				prog.ReportError("Feature could not be read. " + ex.Message);
				continue;
			}
		}
		_lyr.ResetReading();
		_lyr.SetSpatialFilter(null);
		if (ordinal > 0)
		{
			prog.ReportMessage(ordinal + "/" + featureCount + " rejected due to geometry issues.");
		}
		return complexPolygonCollections;
	}

	private ComplexPolygonCollection BuildPolygonCollection(Geometry geom, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		ComplexPolygonCollection complexPolygonCollection = new ComplexPolygonCollection();
		wkbGeometryType geometryType = geom.GetGeometryType();
		if (VectorTypeHelpers.Parse(geometryType) == VectorType.PolygonCollection)
		{
			int geometryCount = geom.GetGeometryCount();
			for (int i = 0; i < geometryCount; i++)
			{
				using Geometry geom2 = geom.GetGeometryRef(i);
				ComplexPolygon complexPolygon = BuildPolygon(geom2, reporter);
				if (complexPolygon != null)
				{
					complexPolygonCollection.Add(complexPolygon);
				}
			}
			return complexPolygonCollection;
		}
		if (VectorTypeHelpers.Parse(geometryType) == VectorType.Polygon)
		{
			ComplexPolygon complexPolygon2 = BuildPolygon(geom, reporter);
			if (complexPolygon2 != null)
			{
				complexPolygonCollection.Add(complexPolygon2);
			}
			return complexPolygonCollection;
		}
		return complexPolygonCollection;
	}

	private ComplexPolygon BuildPolygon(Geometry geom, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		if (VectorTypeHelpers.Parse(geom.GetGeometryType()) == VectorType.Polygon)
		{
			int geometryCount = geom.GetGeometryCount();
			if (geometryCount == 0)
			{
				reporter.ReportError("Polygon contains no sub-geometries. Expected 1+ linear ring(s).");
				return null;
			}
			double[] doubleValues = new double[2];
			List<Polygon> polygons = new List<Polygon>();
			for (int i = 0; i < geometryCount; i++)
			{
				using Geometry geometry = geom.GetGeometryRef(i);
				wkbGeometryType geometryType = geometry.GetGeometryType();
				if (geometryType != wkbGeometryType.wkbLinearRing && geometryType != wkbGeometryType.wkbLineString)
				{
					reporter.ReportError("Polygon subfeature didn't contain a linear ring or line-string: " + geometryType);
					continue;
				}
				int pointCount = geometry.GetPointCount();
				if (pointCount == 0)
				{
					reporter.ReportError("Warning: Linear Ring has 0 points.");
					continue;
				}
				Polygon polygon = new Polygon(pointCount - 1);
				for (int j = 0; j < pointCount - 1; j++)
				{
					geometry.GetPoint_2D(j, doubleValues);
					polygon.Add(doubleValues[0], doubleValues[1]);
				}
				polygons.Add(polygon);
			}
			if ((bool)ComplexPolygon.TryCreateComplexPolygon(polygons, out var poly))
			{
				return poly;
			}
			reporter.ReportError("Invalid Complex Polygon");
			return null;
		}
		reporter.ReportError("Geometry was not a polygon type");
		return null;
	}

	public PointCollection ReadFeaturesAsPoints(Extent? filterExtent = null)
	{
		VectorType geometryType = GetGeometryType();
		if (geometryType != VectorType.Point && geometryType != VectorType.PointCollection)
		{
			return null;
		}
		if (filterExtent.HasValue)
		{
			Extent extent = filterExtent.Value;
			_lyr.SetSpatialFilterRect(extent.MinX, extent.MinY, extent.MaxX, extent.MaxY);
		}
		PointCollection pointCollection = new PointCollection();
		double[] doubleValues = new double[2];
		while (true)
		{
			using Feature feature = _lyr.GetNextFeature();
			if (feature == null)
			{
				break;
			}
			using (Geometry geometry = feature.GetGeometryRef())
			{
				if (geometry == null)
				{
					continue;
				}
				switch (VectorTypeHelpers.Parse(geometry.GetGeometryType()))
				{
				case VectorType.Point:
					geometry.GetPoint_2D(0, doubleValues);
					pointCollection.Add(doubleValues[0], doubleValues[1]);
					break;
				case VectorType.PointCollection:
				{
					int geometryCount = geometry.GetGeometryCount();
					for (int i = 0; i < geometryCount; i++)
					{
						using Geometry geometry2 = geometry.GetGeometryRef(i);
						geometry2.GetPoint_2D(0, doubleValues);
						pointCollection.Add(doubleValues[0], doubleValues[1]);
					}
					break;
				}
				}
			}
			continue;
		}
		_lyr.ResetReading();
		_lyr.SetSpatialFilter(null);
		return pointCollection;
	}

	public PolylineCollection ReadFeaturesAsPolylines(Extent? filterExtent = null)
	{
		VectorType geometryType = GetGeometryType();
		if (geometryType != VectorType.Line && geometryType != VectorType.LineCollection)
		{
			return null;
		}
		if (filterExtent.HasValue)
		{
			Extent extent = filterExtent.Value;
			_lyr.SetSpatialFilterRect(extent.MinX, extent.MinY, extent.MaxX, extent.MaxY);
		}
		PolylineCollection polylineCollection = new PolylineCollection();
		double[] xy = new double[2];
		while (true)
		{
			using Feature feature = _lyr.GetNextFeature();
			if (feature == null)
			{
				break;
			}
			using (Geometry geometry = feature.GetGeometryRef())
			{
				if (geometry == null)
				{
					continue;
				}
				switch (VectorTypeHelpers.Parse(geometry.GetGeometryType()))
				{
				case VectorType.Line:
				{
					Polyline polyline2 = ExtractPolyline(geometry, xy);
					if (polyline2 != null)
					{
						polylineCollection.Add(polyline2);
					}
					break;
				}
				case VectorType.LineCollection:
				{
					int geometryCount = geometry.GetGeometryCount();
					for (int i = 0; i < geometryCount; i++)
					{
						using Geometry geom = geometry.GetGeometryRef(i);
						Polyline polyline = ExtractPolyline(geom, xy);
						if (polyline != null)
						{
							polylineCollection.Add(polyline);
						}
					}
					break;
				}
				}
			}
			continue;
		}
		_lyr.ResetReading();
		_lyr.SetSpatialFilter(null);
		return polylineCollection;
	}

	private static Polyline ExtractPolyline(Geometry geom, double[] xy)
	{
		int pointCount = geom.GetPointCount();
		if (pointCount < 2)
		{
			return null;
		}
		Polyline polyline = new Polyline(pointCount);
		for (int i = 0; i < pointCount; i++)
		{
			geom.GetPoint_2D(i, xy);
			polyline.Add(xy[0], xy[1]);
		}
		return polyline;
	}

	public Table ReadAttributeTable()
	{
		Table table = new Table();
		foreach (KeyValuePair<string, Type> allField in GetAllFields())
		{
			if (allField.Value != null)
			{
				table.AddColumn(allField.Key, allField.Value);
			}
		}
		_lyr.ResetReading();
		while (true)
		{
			using Feature feature = _lyr.GetNextFeature();
			if (feature == null)
			{
				break;
			}
			object[] objectValues = new object[table.Columns.Count];
			for (int i = 0; i < table.Columns.Count; i++)
			{
				int fieldIndex = feature.GetFieldIndex(table.Columns[i].Name);
				if (fieldIndex >= 0)
				{
					objectValues[i] = SwitchType(feature, fieldIndex, table.Columns[i].Type);
				}
			}
			table.AddNewRow(objectValues);
			continue;
		}
		_lyr.ResetReading();
		return table;
	}

	public DataTable GetTable(bool instantiateGeometry, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		DataTable dataTable = new DataTable(GetName());
		Type[] fieldIntKeys = GetFieldIntKeys();
		string geometryColumn = _lyr.GetGeometryColumn();
		VectorType geometryType = GetGeometryType();
		bool vectorReadIsSupported = VectorReadIsSupported(geometryType);
		bool notIsNullOrEmpty = (instantiateGeometry & vectorReadIsSupported) && !string.IsNullOrEmpty(geometryColumn);
		int ordinal = 0;
		if (notIsNullOrEmpty)
		{
			ordinal++;
			dataTable.Columns.Add("Feature", typeof(object));
		}
		List<KeyValuePair<string, Type>> allFields = GetAllFields();
		foreach (KeyValuePair<string, Type> allField in allFields)
		{
			dataTable.Columns.Add(allField.Key, allField.Value);
		}
		long featureCount = _lyr.GetFeatureCount(0);
		long ordinal2 = 0L;
		Feature nextFeature = _lyr.GetNextFeature();
		while (nextFeature != null)
		{
			DataRow dataRow = dataTable.NewRow();
			for (int i = 0; i < fieldIntKeys.Length; i++)
			{
				if (!(allFields[i].Key == geometryColumn))
				{
					dataRow[i + ordinal] = SwitchType(nextFeature, i, allFields[i].Value);
				}
			}
			if (notIsNullOrEmpty)
			{
				dataRow["Feature"] = TryReadGeometry(nextFeature, geometryType, prog);
			}
			dataTable.Rows.Add(dataRow);
			nextFeature.Dispose();
			nextFeature = _lyr.GetNextFeature();
			ordinal2++;
			if (featureCount > 0 && ordinal2 % 100 == 0L)
			{
				prog.ReportProgress((int)(100 * ordinal2 / featureCount));
			}
		}
		_lyr.ResetReading();
		prog.ReportProgress(100);
		return dataTable;
	}

	private object TryReadGeometry(Feature ft, VectorType vecType, ProgressReporter prog)
	{
		switch (vecType)
		{
		case VectorType.PolygonCollection:
		{
			using Geometry geom2 = ft.GetGeometryRef();
			return BuildPolygonCollection(geom2, prog);
		}
		case VectorType.Polygon:
		{
			using Geometry geom = ft.GetGeometryRef();
			return BuildPolygon(geom, prog);
		}
		case VectorType.Point:
		case VectorType.PointCollection:
		{
			using Geometry geometry2 = ft.GetGeometryRef();
			if (geometry2 == null)
			{
				return null;
			}
			double[] doubleValues = new double[2];
			geometry2.GetPoint_2D(0, doubleValues);
			return new Point(doubleValues[0], doubleValues[1]);
		}
		case VectorType.Line:
		case VectorType.LineCollection:
		{
			using Geometry geometry = ft.GetGeometryRef();
			if (geometry == null)
			{
				return null;
			}
			double[] xy = new double[2];
			return ExtractPolyline(geometry, xy);
		}
		default:
			prog.ReportError("Vector Read not supported for " + vecType);
			return null;
		}
	}

	private bool VectorReadIsSupported(VectorType vecType)
	{
		return vecType switch
		{
			VectorType.PolygonCollection => true, 
			VectorType.Polygon => true, 
			VectorType.Point => true, 
			VectorType.PointCollection => true, 
			VectorType.Line => true, 
			VectorType.LineCollection => true, 
			_ => false, 
		};
	}

	private object SwitchType(Feature ft, int colID, Type t)
	{
		if (t == typeof(int))
		{
			return ft.GetFieldAsInteger(colID);
		}
		if (t == typeof(long))
		{
			return ft.GetFieldAsInteger64(colID);
		}
		int count;
		if (t == typeof(DateTime))
		{
			ft.GetFieldAsDateTime(colID, out var pnYear, out var pnMonth, out var pnDay, out var pnHour, out var pnMinute, out var pfSecond, out count);
			Time.SplitSeconds(pfSecond, out var wholeSec, out var ms);
			if (pnYear == 0 && pnMonth == 0 && pnDay == 0 && pnHour == 0 && pnMinute == 0 && pfSecond == 0f)
			{
				return DBNull.Value;
			}
			return new DateTime(pnYear, pnMonth, pnDay, pnHour, pnMinute, wholeSec, ms);
		}
		if (t == typeof(float))
		{
			return ft.GetFieldAsDouble(colID);
		}
		if (t == typeof(double))
		{
			return ft.GetFieldAsDouble(colID);
		}
		if (t == typeof(double[]))
		{
			return ft.GetFieldAsDoubleList(colID, out count);
		}
		if (t == typeof(int[]))
		{
			return ft.GetFieldAsIntegerList(colID, out count);
		}
		if (t == typeof(string))
		{
			return ft.GetFieldAsString(colID);
		}
		if (t == typeof(string[]))
		{
			return ft.GetFieldAsStringList(colID);
		}
		return null;
	}

	public Projection GetProjection()
	{
		if (_nativeProjection != null)
		{
			return _nativeProjection;
		}
		SpatialReference spatialRef = _lyr.GetSpatialRef();
		if (spatialRef == null)
		{
			return null;
		}
		_nativeProjection = new Projection(spatialRef);
		spatialRef.Dispose();
		return _nativeProjection;
	}

	public void Dispose()
	{
		foreach (KeyValuePair<int, FieldDefn> field in _fields)
		{
			field.Value.Dispose();
		}
		if (_isSqlQuery)
		{
			_srcDs?.ReleaseResultSet(_lyr);
		}
		_lyrDefinition.Dispose();
		_lyr.Dispose();
	}
}
