using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Geospatial.PairedData;
using Geospatial.Rasters;
using Geospatial.Vectors;
using H5Assist;
using H5Assist.Identifiers;
using Microsoft.Extensions.Logging;
using Utility.Extensions;
using Utility.Logging;
using Utility.Unsafe;

namespace Geospatial.IO;

public static class HDF5VectorReader
{
	private static readonly ILogger _logger = LogHelper.GetLogger("HDF5VectorReader");

	private const int MaxRowsPerChunkInfoDataset = 256;

	private const int MaxRowsPerChunkPointDataset = 14;

	public static int RowsPerChunkInfoDataset(int featureCount)
	{
		return Math.Min(featureCount, 14);
	}

	public static int RowsPerChunkPointDataset(int pointCount)
	{
		return Math.Min(pointCount, 14);
	}

	public static Polygon TryReadPolygon(H5Reader hr, string dataset)
	{
		Point[] tryReadPoints = TryReadPoints(hr, dataset, out var z, out var m);
		if (tryReadPoints == null)
		{
			return null;
		}
		return Polygon.CreateWithBuffer(tryReadPoints, z, m);
	}

	public static PointCollection[] TryReadPointCollections(H5Reader hr, string datasetPrefix)
	{
		return TryReadPointCollectionSubtype(hr, datasetPrefix, (Memory<Point> pts, Memory<double> z, Memory<double> m) => new PointCollection(pts, z, m));
	}

	public static Polyline[] TryReadPolylines(H5Reader hr, string datasetPrefix)
	{
		return TryReadPointCollectionSubtype(hr, datasetPrefix, (Memory<Point> pts, Memory<double> z, Memory<double> m) => new Polyline(pts, z, m));
	}

	public static StationElevationProfile[] TryReadProfiles(H5Reader hr, string datasetPrefix)
	{
		return TryReadPointCollectionSubtype(hr, datasetPrefix, (Memory<Point> pts, Memory<double> _, Memory<double> _) => StationElevationProfile.FromPoints(pts));
	}

	public static Polygon[] TryReadPolygons(H5Reader hr, string datasetPrefix)
	{
		return TryReadPointCollectionSubtype(hr, datasetPrefix, (Memory<Point> pts, Memory<double> z, Memory<double> m) => new Polygon(pts, z, m));
	}

	public static T[] TryReadPointCollectionSubtype<T>(H5Reader hr, string datasetPrefix, Func<Memory<Point>, Memory<double>, Memory<double>, T> ConstructFrom)
	{
		try
		{
			if (hr.ExistsAsDataset(datasetPrefix))
			{
				if (!hr.ExistsAsAttribute(datasetPrefix, "Start") || !hr.ExistsAsAttribute(datasetPrefix, "Count"))
				{
					_logger.LogWarning("Error reading {Dataset}: Expected Attributes Start/Count", datasetPrefix);
					return null;
				}
				Point[] tryReadPoints = TryReadPoints(hr, datasetPrefix, out var z, out var m);
				if (tryReadPoints == null)
				{
					_logger.LogWarning("Could not read points from {Dataset}", datasetPrefix);
					return null;
				}
				hr.TryReadAttribute(datasetPrefix, "Start", out int[] values);
				hr.TryReadAttribute(datasetPrefix, "Count", out int[] values2);
				if (values.IsNullOrEmpty() || values2.IsNullOrEmpty() || values.Length != values2.Length)
				{
					_logger.LogWarning("Start/Count attribute disagreement. ({Dataset})", datasetPrefix);
					return null;
				}
				hr.TryReadAttribute(datasetPrefix, "Has Z", out int[] hasZ);
				hr.TryReadAttribute(datasetPrefix, "Has M", out int[] hasM);
				if (hasZ != null && hasZ.Length != values.Length || hasM != null && hasM.Length != values.Length) throw new InvalidDataException("Coordinate dimension flags do not match feature counts.");
				T[] valuesArray = new T[values.Length];
				Memory<Point> tryReadPointsSpan = tryReadPoints.AsMemory();
				for (int i = 0; i < values.Length; i++)
				{
					int start = values[i];
					int length = values2[i];
					valuesArray[i] = ConstructFrom(tryReadPointsSpan.Slice(start, length), hasZ != null && hasZ[i] == 0 ? Memory<double>.Empty : z?.AsMemory().Slice(start, length) ?? Memory<double>.Empty, hasM != null && hasM[i] == 0 ? Memory<double>.Empty : m?.AsMemory().Slice(start, length) ?? Memory<double>.Empty);
				}
				return valuesArray;
			}
			Point[] tryReadPointsArray = null;
			string datasetPrefixTerm = datasetPrefix + " Points";
			double[] z2;
			double[] m2;
			if (hr.ExistsAsDataset(datasetPrefixTerm))
			{
				tryReadPointsArray = TryReadPoints(hr, datasetPrefixTerm, out z2, out m2);
			}
			else
			{
				datasetPrefixTerm = datasetPrefix + " Values";
				if (hr.ExistsAsDataset(datasetPrefixTerm))
				{
					tryReadPointsArray = TryReadPoints(hr, datasetPrefixTerm, out z2, out m2);
				}
				else
				{
					z2 = null;
					m2 = null;
				}
			}
			if (tryReadPointsArray == null)
			{
				return null;
			}
			string datasetPrefixTerm2 = datasetPrefix + " Info";
			if (!hr.ExistsAsDataset(datasetPrefixTerm2))
			{
				_logger.LogWarning("Expected Info dataset doesn't exist {Dataset}", datasetPrefixTerm2);
				return null;
			}
			hr.ReadDataset(datasetPrefixTerm2, out int[,] values3);
			int length2 = values3.GetLength(0);
			if (values3.GetLength(1) < 2)
			{
				return null;
			}
			T[] values4 = new T[length2];
			Memory<Point> tryReadPoints2 = tryReadPointsArray.AsMemory();
			for (int j = 0; j < length2; j++)
			{
				int start2 = values3[j, 0];
				int length3 = values3[j, 1];
				values4[j] = ConstructFrom(tryReadPoints2.Slice(start2, length3), z2?.AsMemory().Slice(start2, length3) ?? Memory<double>.Empty, m2?.AsMemory().Slice(start2, length3) ?? Memory<double>.Empty);
			}
			return values4;
		}
		catch (Exception)
		{
			return null;
		}
	}

	public unsafe static Point[] TryReadPoints(H5Reader hr, string dataset, out double[] z, out double[] m)
	{
		using H5DatasetID h5DatasetID = hr.TryOpenDataset(dataset);
		if (h5DatasetID == null)
		{
			_logger.LogTrace("{Dataset} doesn't exist.", dataset);
			z = null;
			m = null;
			return null;
		}
		long[] datasetDimensions = hr.GetDatasetDimensions(h5DatasetID);
		if (datasetDimensions.Length != 2)
		{
			_logger.LogWarning("{Dataset} doesn't have expected #dimensions.", dataset);
			z = null;
			m = null;
			return null;
		}
		Type datasetType = hr.GetDatasetType(h5DatasetID);
		if (datasetType == typeof(double))
		{
			int datasetDimension = (int)datasetDimensions[0];
			switch ((int)datasetDimensions[1])
			{
			case 2:
			{
				z = null;
				m = null;
				Point[] points = new Point[datasetDimension];
				fixed (Point* points2 = points)
				{
					double* points3 = (double*)points2;
					hr.ReadDatasetUnsafe(h5DatasetID, points2);
				}
				return points;
			}
			case 3:
			{
				z = new double[datasetDimension];
				m = null;
				hr.ReadDataset(dataset, out double[,] values2);
				Point[] pointsArray = new Point[datasetDimension];
				for (int j = 0; j < datasetDimension; j++)
				{
					pointsArray[j] = new Point(values2[j, 0], values2[j, 1]);
					z[j] = values2[j, 2];
				}
				return pointsArray;
			}
			case 4:
			{
				z = new double[datasetDimension];
				m = new double[datasetDimension];
				hr.ReadDataset(dataset, out double[,] values);
				Point[] points4 = new Point[datasetDimension];
				for (int i = 0; i < datasetDimension; i++)
				{
					points4[i] = new Point(values[i, 0], values[i, 1]);
					z[i] = values[i, 2];
					m[i] = values[i, 3];
				}
				return points4;
			}
			default:
				z = null;
				m = null;
				_logger.LogWarning("Error - unexpected number of columns in Point dataset.");
				return null;
			}
		}
		if (datasetType == typeof(float))
		{
			hr.ReadDataset(dataset, out float[,] values3);
			int length = values3.GetLength(0);
			int length2 = values3.GetLength(1);
			switch (length2)
			{
			case 2:
				z = null;
				m = null;
				break;
			case 3:
				z = new double[length];
				m = null;
				break;
			case 4:
				z = new double[length];
				m = new double[length];
				break;
			default:
				z = null;
				m = null;
				_logger.LogWarning("Error - unexpected number of columns in Point dataset.");
				return null;
			}
			Point[] points5 = new Point[length];
			for (int k = 0; k < length; k++)
			{
				points5[k] = new Point(values3[k, 0], values3[k, 1]);
				if (length2 >= 3) z[k] = values3[k, 2];
				if (length2 == 4) m[k] = values3[k, 3];
			}
			return points5;
		}
		_logger.LogWarning("{Dataset} doesn't have expected type. Actual: {Type}.", dataset, datasetType.FullName);
		z = null;
		m = null;
		return null;
	}

	public static void Write(H5Writer hw, Polygon pgon, string dataset)
	{
		if (pgon.HasZ || pgon.HasM)
		{
			Write(hw, pgon.GetPointBufferUnsafe(), pgon.GetZBufferUnsafe(), pgon.GetMBufferUnsafe(), dataset);
		}
		else
		{
			Write(hw, pgon.GetPointBufferUnsafe(), dataset);
		}
	}

	public static void Write(H5Writer hw, List<Point> points, string dataset)
	{
		Write(hw, UnsafeHelpers.GetListBackingBuffer(points), dataset);
	}

	public unsafe static void Write(H5Writer hw, Memory<Point> points, string dataset)
	{
		using MemoryHandle memoryHandle = points.Pin();
		hw.WriteDatasetUnsafe(dataset, (double*)memoryHandle.Pointer, points.Length, 2);
	}

	public static void Write(H5Writer hw, Memory<Point> points, Memory<double> z, Memory<double> m, string dataset)
	{
		if (z.IsEmpty && m.IsEmpty)
		{
			Write(hw, points, dataset);
			return;
		}
		if (z.IsEmpty)
		{
			throw new ArgumentException("Cannot write XYM data without a Z vector.");
		}
		bool notIsEmpty = !m.IsEmpty;
		int count = ((!notIsEmpty) ? 3 : 4);
		double[,] doubleValues = new double[points.Length, count];
		Span<Point> pointsSpan = points.Span;
		Span<double> doubleSpan = z.Span;
		Span<double> doubleSpan2 = m.Span;
		for (int i = 0; i < points.Length; i++)
		{
			Point point = pointsSpan[i];
			doubleValues[i, 0] = point.X;
			doubleValues[i, 1] = point.Y;
			doubleValues[i, 2] = doubleSpan[i];
			if (notIsEmpty)
			{
				doubleValues[i, 3] = doubleSpan2[i];
			}
		}
		hw.WriteDataset(dataset, doubleValues);
	}

	public static void Write(H5Writer hw, IList<Polygon> polygons, string dataset)
	{
		WritePointCollections(hw, polygons.Cast<PointCollection>().ToArray(), dataset);
	}

	private static void WritePointCollections(H5Writer hw, IList<PointCollection> collections, string dataset)
	{
		bool hasM = collections.Any(item => item?.HasM == true);
		bool hasZ = collections.Any(item => item?.HasZ == true);
		int columns = hasM ? 4 : hasZ ? 3 : 2;
		hw.WriteJagged<PointCollection, double>(dataset, collections, columns, item => item?.PointCount ?? 0, (item, index, buffer) =>
		{
			Point point = item.Points[index]; buffer[0] = point.X; buffer[1] = point.Y;
			if (columns >= 3) buffer[2] = item.HasZ ? item.Z[index] : double.NaN;
			if (columns >= 4) buffer[3] = item.HasM ? item.M[index] : double.NaN;
		});
		if (collections.Count > 0 && hw.ExistsAsDataset(dataset))
		{
			// Existing readers keep their XY/XYZ/XYZM column layout. Optional
			// per-feature flags distinguish XYM and mixed-dimensional rows.
			hw.WriteAttribute(dataset, "Has Z", collections.Select(item => item?.HasZ == true ? 1 : 0).ToArray());
			hw.WriteAttribute(dataset, "Has M", collections.Select(item => item?.HasM == true ? 1 : 0).ToArray());
		}
	}

	public static void Write(H5Writer hw, IList<Tin> tins, string datasetPrefix)
	{
		string infoDataset = InfoDataset(datasetPrefix);
		string pointsDataset = PointsDataset(datasetPrefix);
		string datasetPrefixTerm = datasetPrefix + " Triangles";
		if (hw.ExistsAsDataset(infoDataset))
		{
			hw.Delete(infoDataset);
		}
		if (hw.ExistsAsDataset(pointsDataset))
		{
			hw.Delete(pointsDataset);
		}
		if (hw.ExistsAsDataset(datasetPrefixTerm))
		{
			hw.Delete(datasetPrefixTerm);
		}
		int[,] intValues = new int[tins.Count, 4];
		int sum = tins.Sum((Tin t) => t.PointCount);
		int[,] intValues2 = new int[tins.Sum((Tin t) => t.TriangleCount), 3];
		bool tinsTinTerm = tins.Any((Tin t) => t.HasZ || t.HasM);
		double[,] doubleValues = ((!tinsTinTerm) ? new double[sum, 2] : new double[sum, 4]);
		int pointCountSum = 0;
		int triangleCountSum = 0;
		for (int intervalIndex = 0; intervalIndex < tins.Count; intervalIndex++)
		{
			Tin tin = tins[intervalIndex];
			int pointCount = tin.PointCount;
			int triangleCount = tin.TriangleCount;
			intValues[intervalIndex, 0] = pointCountSum;
			intValues[intervalIndex, 1] = pointCount;
			intValues[intervalIndex, 2] = triangleCountSum;
			intValues[intervalIndex, 3] = triangleCount;
			for (int intervalIndex2 = 0; intervalIndex2 < tin.PointCount; intervalIndex2++)
			{
				Point point = tin.Points[intervalIndex2];
				int pointCountSumTerm = pointCountSum + intervalIndex2;
				doubleValues[pointCountSumTerm, 0] = point.X;
				doubleValues[pointCountSumTerm, 1] = point.Y;
				if (tinsTinTerm)
				{
					doubleValues[pointCountSumTerm, 2] = tin.Z[intervalIndex2];
					doubleValues[pointCountSumTerm, 3] = tin.M[intervalIndex2];
				}
			}
			for (int intervalIndex3 = 0; intervalIndex3 < tin.TriangleCount; intervalIndex3++)
			{
				TriangleDefinition triangleDefinition = tin.TriangleDefinition[intervalIndex3];
				int triangleCountSumTerm = triangleCountSum + intervalIndex3;
				intValues2[triangleCountSumTerm, 0] = triangleDefinition.p0;
				intValues2[triangleCountSumTerm, 1] = triangleDefinition.p1;
				intValues2[triangleCountSumTerm, 2] = triangleDefinition.p2;
			}
			pointCountSum += pointCount;
			triangleCountSum += triangleCount;
		}
		hw.WriteDataset(infoDataset, intValues);
		hw.WriteDataset(datasetPrefixTerm, intValues2);
		hw.WriteDataset(pointsDataset, doubleValues);
	}

	public static void Write(H5Writer hw, IList<StationElevationProfile> profiles, string dataset)
	{
		hw.WriteJagged(dataset, profiles, 2, (StationElevationProfile pl) => pl?.Count ?? 0, (StationElevationProfile pl, int ptIdx, double[] buf) =>
		{
			Point point = pl[ptIdx];
			buf[0] = point.X;
			buf[1] = point.Y;
		});
	}

	public static void Write(H5Writer hw, IList<Polyline> plines, string dataset)
	{
		WritePointCollections(hw, plines.Cast<PointCollection>().ToArray(), dataset);
	}

	public static void Write(H5Writer hw, IList<IList<Point>> plines, string dataset)
	{
		hw.WriteJagged(dataset, plines, 2, (IList<Point> pl) => pl?.Count ?? 0, (IList<Point> pl, int ptIdx, double[] buf) =>
		{
			Point point = pl[ptIdx];
			buf[0] = point.X;
			buf[1] = point.Y;
		});
	}

	public static List<Polyline> ReadPolylines(H5Reader h5r, string dataset)
	{
		List<Polyline> items = new List<Polyline>();
		h5r.ReadJagged2D(dataset, (double[,] arr) =>
		{
			int length = arr.GetLength(0);
			Polyline polyline = new Polyline(length);
			for (int i = 0; i < length; i++)
			{
				polyline.Add(new Point(arr[i, 0], arr[i, 1]));
			}
			items.Add(polyline);
		});
		return items;
	}

	public static List<StationElevationProfile> ReadStationElevationProfiles(H5Reader h5r, string dataset)
	{
		List<StationElevationProfile> items = new List<StationElevationProfile>();
		h5r.ReadJagged2D(dataset, (double[,] arr) =>
		{
			int length = arr.GetLength(0);
			StationElevationProfile stationElevationProfile = new StationElevationProfile(length);
			for (int i = 0; i < length; i++)
			{
				stationElevationProfile.Add(arr[i, 0], arr[i, 1]);
			}
			items.Add(stationElevationProfile);
		});
		return items;
	}

	public static List<Polygon> ReadPolygons(H5Reader h5r, string dataset)
	{
		List<Polygon> items = new List<Polygon>();
		h5r.ReadJagged2D(dataset, (double[,] arr) =>
		{
			int length = arr.GetLength(0);
			Polygon polygon = new Polygon(length);
			for (int i = 0; i < length; i++)
			{
				polygon.Add(new Point(arr[i, 0], arr[i, 1]));
			}
			items.Add(polygon);
		});
		return items;
	}

	public static void DeletePolyDataset(H5Writer hw, string datasetName)
	{
		string infoDataset = InfoDataset(datasetName);
		if (hw.ExistsAsDataset(infoDataset))
		{
			hw.Delete(infoDataset);
		}
		string partsDataset = PartsDataset(datasetName);
		if (hw.ExistsAsDataset(partsDataset))
		{
			hw.Delete(partsDataset);
		}
		string pointsDataset = PointsDataset(datasetName);
		if (hw.ExistsAsDataset(pointsDataset))
		{
			hw.Delete(pointsDataset);
		}
	}

	private static string InfoDataset(string dsetPrefix)
	{
		return dsetPrefix + " Info";
	}

	private static string PartsDataset(string dsetPrefix)
	{
		return dsetPrefix + " Parts";
	}

	private static string PointsDataset(string dsetPrefix)
	{
		return dsetPrefix + " Points";
	}

	public static void WriteAttribute(H5Writer hw, string objectName, RasterDefinition raster, string attributePrefix = "Raster")
	{
		string attributeName = attributePrefix + " Rows";
		string attributeName2 = attributePrefix + " Cols";
		string attributeName3 = attributePrefix + " Left";
		string attributeName4 = attributePrefix + " Top";
		string attributeName5 = attributePrefix + " Cellsize";
		hw.WriteAttribute(objectName, attributeName, raster.Rows);
		hw.WriteAttribute(objectName, attributeName2, raster.Cols);
		hw.WriteAttribute(objectName, attributeName3, raster.Left);
		hw.WriteAttribute(objectName, attributeName4, raster.Top);
		hw.WriteAttribute(objectName, attributeName5, raster.CellSize);
	}

	public static RasterDefinition TryReadRasterDefinition(H5Reader hr, string objectName, string attributePrefix = "Raster")
	{
		string attributeName = attributePrefix + " Rows";
		string attributeName2 = attributePrefix + " Cols";
		string attributeName3 = attributePrefix + " Left";
		string attributeName4 = attributePrefix + " Top";
		string attributeName5 = attributePrefix + " Cellsize";
		if (!hr.TryReadAttributeScalar(objectName, attributeName, out int ordinal))
		{
			return RasterDefinition.NullRaster;
		}
		if (!hr.TryReadAttributeScalar(objectName, attributeName2, out int ordinal2))
		{
			return RasterDefinition.NullRaster;
		}
		if (!hr.TryReadAttributeScalar(objectName, attributeName3, out double xComponent))
		{
			return RasterDefinition.NullRaster;
		}
		if (!hr.TryReadAttributeScalar(objectName, attributeName4, out double yComponent))
		{
			return RasterDefinition.NullRaster;
		}
		if (!hr.TryReadAttributeScalar(objectName, attributeName5, out double computed))
		{
			return RasterDefinition.NullRaster;
		}
		return new RasterDefinition(new Point(xComponent, yComponent), computed, ordinal, ordinal2);
	}

	public static void WriteAttribute(H5Writer hw, string objectName, Extent ext, string attributePrefix = "Extent")
	{
		string attributeName = attributePrefix + " Left";
		string attributeName2 = attributePrefix + " Top";
		string attributeName3 = attributePrefix + " Right";
		string attributeName4 = attributePrefix + " Bot";
		hw.WriteAttribute(objectName, attributeName, ext.Left);
		hw.WriteAttribute(objectName, attributeName2, ext.Top);
		hw.WriteAttribute(objectName, attributeName3, ext.Right);
		hw.WriteAttribute(objectName, attributeName4, ext.Bottom);
	}

	public static Extent TryReadExtent(H5Reader hr, string objectName, string attributePrefix = "Extent")
	{
		string attributeName = attributePrefix + " Left";
		string attributeName2 = attributePrefix + " Top";
		string attributeName3 = attributePrefix + " Right";
		string attributeName4 = attributePrefix + " Bot";
		if (!hr.TryReadAttributeScalar(objectName, attributeName, out double computed))
		{
			return Extent.NullExtent;
		}
		if (!hr.TryReadAttributeScalar(objectName, attributeName2, out double computed2))
		{
			return Extent.NullExtent;
		}
		if (!hr.TryReadAttributeScalar(objectName, attributeName3, out double computed3))
		{
			return Extent.NullExtent;
		}
		if (!hr.TryReadAttributeScalar(objectName, attributeName4, out double computed4))
		{
			return Extent.NullExtent;
		}
		return new Extent(computed, computed3, computed4, computed2);
	}

	public static void Write(H5Writer hw, Tin tin, string datasetPrefix)
	{
		Write(hw, tin.GetPointBufferUnsafe(), tin.GetZBufferUnsafe(), tin.GetMBufferUnsafe(), datasetPrefix + " Points");
		int[,] connectivityArray = tin.GetConnectivityArray();
		hw.WriteDataset(datasetPrefix + " Connectivity", connectivityArray);
	}

	public static Tin TryReadTin(H5Reader hr, string datasetPrefix)
	{
		Point[] tryReadPoints = TryReadPoints(hr, datasetPrefix + " Points", out var z, out var m);
		hr.ReadDataset(datasetPrefix + " Connectivity", out int[,] values);
		if (tryReadPoints.IsNullOrEmpty() || values == null)
		{
			return null;
		}
		return Tin.CreateWithBuffer(tryReadPoints, values, z, m);
	}

	public static List<Tin> TryReadTins(H5Reader hr, string datasetPrefix)
	{
		string infoDataset = InfoDataset(datasetPrefix);
		if (!hr.ExistsAsDataset(infoDataset))
		{
			return null;
		}
		hr.ReadDataset(infoDataset, out int[,] values);
		Point[] tryReadPoints = TryReadPoints(hr, PointsDataset(datasetPrefix), out var z, out var m);
		hr.ReadDataset(datasetPrefix + " Triangles", out int[,] values2);
		if (tryReadPoints.IsNullOrEmpty() || values == null || values2 == null)
		{
			return null;
		}
		int length = values.GetLength(0);
		List<Tin> tins = new List<Tin>(length);
		for (int i = 0; i < length; i++)
		{
			int ordinal = values[i, 0];
			int ordinal2 = values[i, 1];
			int triStart = values[i, 2];
			int triCount = values[i, 3];
			Tin tin = new Tin(tryReadPoints, values2, ordinal, ordinal2, triStart, triCount);
			if (z != null)
			{
				tin.TrackZ(z.Skip(ordinal).Take(ordinal2));
			}
			if (m != null)
			{
				tin.TrackM(m.Skip(ordinal).Take(ordinal2));
			}
			tins.Add(tin);
		}
		return tins;
	}
}
