using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Geospatial.Features;
using Geospatial.GDALAssist;
using Geospatial.Transformers;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using Utility;
using Utility.Extensions;
using Utility.Files;
using Utility.Logging;
using Utility.Memory;
using Utility.Progress;

namespace Geospatial.IO;

public static class ShapefileIO
{
	public static class MessageCodes
	{
		public static readonly MessageClass ErrorParsingShpHeader = new MessageClass("ShpWriter_ErrParsingShpHeader", MessageSeverity.Error, "Error parsing SHP header for \"{Filename}\".");

		public static readonly MessageClass ErrorParsingShpHeaderBadShpType = new MessageClass("ShpWriter_UnexpectedShpSubtype", MessageSeverity.Error, "Error parsing SHP header for \"{Filename}\". Shapefile subtype doesn't match expected value. Expected: '{ExpectedShapeType}', Actual: '{ActualShapeType}'.");

		public static readonly MessageClass TableRowCountMismatch = new MessageClass("ShpWriter_TableRowCountMismatch", MessageSeverity.Error, "Table row count ({TableRowCount}) does not match feature count ({FeatureCount}).");

		public static Message GetErrorParsingShpHeader(string filename)
		{
			return ErrorParsingShpHeader.Create(filename);
		}

		public static Message GetErrorParsingShpHeaderBadShpType(string filename, ShapeType expectedShpType, ShapeType actualShpType)
		{
			return ErrorParsingShpHeaderBadShpType.Create(filename, expectedShpType, actualShpType);
		}

		public static Message GetTableRowCountMismatch(int tableRowCount, int featureCount)
		{
			return TableRowCountMismatch.Create(tableRowCount, featureCount);
		}
	}

	public const string sFileFilter = "Shapefile (*.shp)|*.shp";

	private static readonly ILogger _logger = LogHelper.GetLogger("ShapefileIO");

	private const string _shapefileExt = ".shp";

	private const string _shapefileIndexExt = ".shx";

	private const int RecordHeaderLength = 8;

	public static string DBFFilename(string shpFilename)
	{
		return Path.ChangeExtension(shpFilename, "dbf");
	}

	public static string SHXFilename(string shpFilename)
	{
		return Path.ChangeExtension(shpFilename, "shx");
	}

	public static string PRJFilename(string shpFilename)
	{
		return Path.ChangeExtension(shpFilename, "prj");
	}

	public static OperationResult CheckDependencies(string shpFilename)
	{
		OperationResult operationResult = new OperationResult();
		operationResult.Result = true;
		try
		{
			if (!File.Exists(shpFilename))
			{
				operationResult.AddMessage(FileMessageCodes.GetFileNotFound(shpFilename));
				operationResult.Result = false;
			}
			string dbfFilename = DBFFilename(shpFilename);
			if (!File.Exists(dbfFilename))
			{
				operationResult.AddMessage(FileMessageCodes.GetFileNotFound(dbfFilename));
				operationResult.Result = false;
			}
			string shxFilename = SHXFilename(shpFilename);
			if (!File.Exists(shxFilename))
			{
				operationResult.AddMessage(FileMessageCodes.GetFileNotFound(shxFilename));
				operationResult.Result = false;
			}
			return operationResult;
		}
		catch (Exception ex)
		{
			operationResult.AddMessage(GeneralMessageCodes.GetExceptionOccurred("Reading Shapefile", ex));
			operationResult.Result = false;
			return operationResult;
		}
	}

	public static void DeleteAllAssociations(string shpFilename)
	{
		if (File.Exists(shpFilename))
		{
			File.Delete(shpFilename);
		}
		string path = SHXFilename(shpFilename);
		if (File.Exists(path))
		{
			File.Delete(path);
		}
		string path2 = DBFFilename(shpFilename);
		if (File.Exists(path2))
		{
			File.Delete(path2);
		}
		string path3 = PRJFilename(shpFilename);
		if (File.Exists(path3))
		{
			File.Delete(path3);
		}
	}

	public static bool IsPolygonShapefile(string shpFilename)
	{
		if (!TryParseShapefileHeader(shpFilename, out var header).Result)
		{
			return false;
		}
		return ShapeTypeHelper.IsPolygonType(header.ShpTypeCode);
	}

	public static bool IsPolylineShapefile(string shpFilename)
	{
		if (!TryParseShapefileHeader(shpFilename, out var header).Result)
		{
			return false;
		}
		return ShapeTypeHelper.IsPolylineType(header.ShpTypeCode);
	}

	public static bool IsPointShapefile(string shpFilename)
	{
		if (!TryParseShapefileHeader(shpFilename, out var header).Result)
		{
			return false;
		}
		return ShapeTypeHelper.IsPointType(header.ShpTypeCode);
	}

	public static bool IsMultipointShapefile(string shpFilename)
	{
		if (!TryParseShapefileHeader(shpFilename, out var header).Result)
		{
			return false;
		}
		return ShapeTypeHelper.IsMultipointType(header.ShpTypeCode);
	}

	public static bool HasShapefileExtension(string shpFilename)
	{
		string extension = Path.GetExtension(shpFilename);
		if (!extension.RoughlyEquals(".shp"))
		{
			return extension.RoughlyEquals(".shx");
		}
		return true;
	}

	public static OperationResult TryRead(string shpFilename, out PointFeatureCollection collection, ProgressReporter prog = null)
	{
		return TryReadShapefileInternal(shpFilename, out collection, (ISpatialTransformer)null, prog);
	}

	public static OperationResult TryRead(string shpFilename, out PointFeatureCollection collection, Projection? destProj, ProgressReporter prog = null)
	{
		if (destProj == null)
		{
			return TryReadShapefileInternal(shpFilename, out collection, (ISpatialTransformer)null, prog);
		}
		string prjFilename = PRJFilename(shpFilename);
		if (!File.Exists(prjFilename))
		{
			return TryReadShapefileInternal(shpFilename, out collection, (ISpatialTransformer)null, prog);
		}
		using ReprojectionTransformer reprojectTransform = new ReprojectionTransformer(new ESRIProjection(prjFilename), destProj);
		return TryReadShapefileInternal(shpFilename, out collection, (ISpatialTransformer)reprojectTransform, prog);
	}

	public static OperationResult TryRead(string shpFilename, out PointFeatureCollection collection, ISpatialTransformer reprojectTransform, ProgressReporter prog = null)
	{
		return TryReadShapefileInternal(shpFilename, out collection, reprojectTransform, prog);
	}

	private static OperationResult TryReadShapefileInternal(string shpFilename, out PointFeatureCollection collection, ISpatialTransformer reprojectTransform, ProgressReporter prog)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		Point[] pts = null;
		double[] z = null;
		double[] m = null;
		OperationResult operationResult = TryReadShapefileInternal(shpFilename, ShapeType.Point, (byte[] shpBytes, int[] records, ShapefileHeader header) =>
		{
			LoadFeatures(shpBytes, records, header.ShapeOordinates, reprojectTransform, out pts, out z, out m);
		}, prog, out var tbl);
		if (operationResult.Result)
		{
			PointCollection pc = PointCollection.CreateWithBuffer(pts, z, m);
			collection = new PointFeatureCollection(pc, tbl);
			return operationResult;
		}
		collection = null;
		return operationResult;
	}

	public static OperationResult TryRead(string shpFilename, out PolylineFeatureCollection collection, ISpatialTransformer reprojectTransform = null, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		Polyline[] plines = null;
		OperationResult operationResult = TryReadShapefileInternal(shpFilename, ShapeType.Line, (byte[] shpBytes, int[] records, ShapefileHeader header) =>
		{
			LoadFeatures((Memory<byte>)shpBytes, (Memory<int>)records, header.ShapeOordinates, reprojectTransform, out plines);
		}, prog, out var tbl);
		if (operationResult.Result)
		{
			PolylineCollection pc = new PolylineCollection(plines);
			collection = new PolylineFeatureCollection(pc, tbl);
			return operationResult;
		}
		collection = null;
		return operationResult;
	}

	public static OperationResult TryRead(string shpFilename, out PolylineCollectionFeatureCollection collection, ProgressReporter prog = null)
	{
		return TryReadShapefileInternal(shpFilename, out collection, (ISpatialTransformer)null, prog);
	}

	public static OperationResult TryRead(string shpFilename, out PolylineCollectionFeatureCollection collection, Projection? destProj, ProgressReporter prog = null)
	{
		if (destProj == null)
		{
			return TryReadShapefileInternal(shpFilename, out collection, (ISpatialTransformer)null, prog);
		}
		string prjFilename = PRJFilename(shpFilename);
		if (!File.Exists(prjFilename))
		{
			return TryReadShapefileInternal(shpFilename, out collection, (ISpatialTransformer)null, prog);
		}
		using ReprojectionTransformer reprojectTransform = new ReprojectionTransformer(new ESRIProjection(prjFilename), destProj);
		return TryReadShapefileInternal(shpFilename, out collection, (ISpatialTransformer)reprojectTransform, prog);
	}

	public static OperationResult TryRead(string shpFilename, out PolylineCollectionFeatureCollection collection, ISpatialTransformer reprojectTransform, ProgressReporter prog = null)
	{
		return TryReadShapefileInternal(shpFilename, out collection, reprojectTransform, prog);
	}

	private static OperationResult TryReadShapefileInternal(string shpFilename, out PolylineCollectionFeatureCollection collection, ISpatialTransformer reprojectTransform, ProgressReporter prog)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		PolylineCollection[] plines = null;
		OperationResult operationResult = TryReadShapefileInternal(shpFilename, ShapeType.Line, (byte[] shpBytes, int[] records, ShapefileHeader header) =>
		{
			LoadFeatures((Memory<byte>)shpBytes, (Memory<int>)records, header.ShapeOordinates, reprojectTransform, out plines);
		}, prog, out var tbl);
		if (operationResult.Result)
		{
			PolylineCollectionCollection mpc = new PolylineCollectionCollection(plines);
			collection = new PolylineCollectionFeatureCollection(mpc, tbl);
			return operationResult;
		}
		collection = null;
		return operationResult;
	}

	public static OperationResult TryRead(string shpFilename, out PolygonFeatureCollection collection, ProgressReporter prog = null)
	{
		return TryReadShapefileInternal(shpFilename, out collection, (ISpatialTransformer)null, prog);
	}

	public static OperationResult TryRead(string shpFilename, out PolygonFeatureCollection collection, Projection? destProj, ProgressReporter prog = null)
	{
		if (destProj == null)
		{
			return TryReadShapefileInternal(shpFilename, out collection, (ISpatialTransformer)null, prog);
		}
		string prjFilename = PRJFilename(shpFilename);
		if (!File.Exists(prjFilename))
		{
			return TryReadShapefileInternal(shpFilename, out collection, (ISpatialTransformer)null, prog);
		}
		using ReprojectionTransformer reprojectTransform = new ReprojectionTransformer(new ESRIProjection(prjFilename), destProj);
		return TryReadShapefileInternal(shpFilename, out collection, (ISpatialTransformer)reprojectTransform, prog);
	}

	public static OperationResult TryRead(string shpFilename, out PolygonFeatureCollection collection, ISpatialTransformer reprojectTransform, ProgressReporter prog = null)
	{
		return TryReadShapefileInternal(shpFilename, out collection, reprojectTransform, prog);
	}

	private static OperationResult TryReadShapefileInternal(string shpFilename, out PolygonFeatureCollection collection, ISpatialTransformer reprojectTransform, ProgressReporter prog)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		Polygon[] pgons = null;
		OperationResult operationResult = TryReadShapefileInternal(shpFilename, ShapeType.Polygon, (byte[] shpBytes, int[] records, ShapefileHeader header) =>
		{
			LoadFeatures((Memory<byte>)shpBytes, (Memory<int>)records, header.ShapeOordinates, reprojectTransform, out pgons);
		}, prog, out var tbl);
		if (operationResult.Result)
		{
			collection = new PolygonFeatureCollection(new PolygonCollection(pgons), tbl);
			return operationResult;
		}
		collection = null;
		return operationResult;
	}

	public static OperationResult TryRead(string shpFilename, out PolygonCollectionFeatureCollection collection, ProgressReporter prog = null)
	{
		return TryReadShapefileInternal(shpFilename, out collection, (ISpatialTransformer)null, prog);
	}

	public static OperationResult TryRead(string shpFilename, out PolygonCollectionFeatureCollection collection, ISpatialTransformer reprojectTransform, ProgressReporter prog = null)
	{
		return TryReadShapefileInternal(shpFilename, out collection, reprojectTransform, prog);
	}

	public static OperationResult TryRead(string shpFilename, out PolygonCollectionFeatureCollection collection, Projection? destProj, ProgressReporter prog = null)
	{
		if (destProj == null)
		{
			return TryReadShapefileInternal(shpFilename, out collection, (ISpatialTransformer)null, prog);
		}
		string prjFilename = PRJFilename(shpFilename);
		if (!File.Exists(prjFilename))
		{
			return TryReadShapefileInternal(shpFilename, out collection, (ISpatialTransformer)null, prog);
		}
		using ReprojectionTransformer reprojectTransform = new ReprojectionTransformer(new ESRIProjection(prjFilename), destProj);
		return TryReadShapefileInternal(shpFilename, out collection, (ISpatialTransformer)reprojectTransform, prog);
	}

	private static OperationResult TryReadShapefileInternal(string shpFilename, out PolygonCollectionFeatureCollection collection, ISpatialTransformer reprojectTransform, ProgressReporter prog)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		PolygonCollection[] pgons = null;
		OperationResult operationResult = TryReadShapefileInternal(shpFilename, ShapeType.Polygon, (byte[] shpBytes, int[] records, ShapefileHeader header) =>
		{
			LoadFeatures((Memory<byte>)shpBytes, (Memory<int>)records, header.ShapeOordinates, reprojectTransform, out pgons);
		}, prog, out var tbl);
		if (operationResult.Result)
		{
			PolygonCollectionCollection mpc = new PolygonCollectionCollection(pgons);
			collection = new PolygonCollectionFeatureCollection(mpc, tbl);
			return operationResult;
		}
		collection = null;
		return operationResult;
	}

	public static OperationResult TryRead(string shpFilename, out PointCollectionFeatureCollection collection, ISpatialTransformer reprojectTransform = null, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		PointCollection[] pts = null;
		OperationResult operationResult = TryReadShapefileInternal(shpFilename, ShapeType.Multipoint, (byte[] shpBytes, int[] records, ShapefileHeader header) =>
		{
			LoadFeatures((Memory<byte>)shpBytes, (Memory<int>)records, header.ShapeOordinates, reprojectTransform, out pts);
		}, prog, out var tbl);
		if (operationResult.Result)
		{
			PointCollectionCollection mpc = new PointCollectionCollection(pts);
			collection = new PointCollectionFeatureCollection(mpc, tbl);
			return operationResult;
		}
		collection = null;
		return operationResult;
	}

	private static OperationResult TryReadShapefileInternal(string shpFilename, ShapeType expectedShape, Action<byte[], int[], ShapefileHeader> parseFeatures, ProgressReporter prog, out Table tbl)
	{
		try
		{
			OperationResult operationResult = CheckDependencies(shpFilename);
			if (!operationResult.Result)
			{
				tbl = null;
				return operationResult;
			}
			string dbf = DBFFilename(shpFilename);
			byte[] shpBytes = null;
			ShapefileHeader header = null;
			bool shpHeaderSuccess = true;
			Task<bool> task = Task.Run(() => shpHeaderSuccess = ReadFileParseHeader(shpFilename, out shpBytes, out header));
			Table attrTable = null;
			OperationResult dbfSuccess = false;
			Task<OperationResult> task2 = Task.Run(() => dbfSuccess = DbfIO.TryRead(dbf, out attrTable));
			int[] records = null;
			Task<int[]> task3 = Task.Run(() => records = LoadOffsetsFromSHX(SHXFilename(shpFilename)));
			task.Wait();
			if (!shpHeaderSuccess)
			{
				tbl = null;
				return OperationResult.Fail(MessageCodes.GetErrorParsingShpHeader(shpFilename));
			}
			if (header.ShapeType != expectedShape)
			{
				tbl = null;
				return OperationResult.Fail(MessageCodes.GetErrorParsingShpHeaderBadShpType(shpFilename, expectedShape, header.ShapeType));
			}
			task3.Wait();
			parseFeatures(shpBytes, records, header);
			task2.Wait();
			if (!dbfSuccess.Result)
			{
				tbl = null;
				return dbfSuccess;
			}
			tbl = attrTable;
			return true;
		}
		catch (Exception ex)
		{
			tbl = null;
			prog.ReportMessage("Unknown error: " + ex.Message);
			return OperationResult.Fail("Reading Shapefile", ex);
		}
	}

	public static OperationResult Write(string shpFilename, PointCollection points, Table attrTable = null, Projection? projection = null)
	{
		if (points == null)
		{
			throw new ArgumentNullException("points");
		}
		Span<Point> pointBufferUnsafe = points.GetPointBufferUnsafe().Span;
		Span<double> z = Span<double>.Empty;
		Span<double> m = Span<double>.Empty;
		Memory<double> doubleSpan;
		if (points.HasZ)
		{
			doubleSpan = points.GetZBufferUnsafe();
			z = doubleSpan.Span;
			if (points.HasM)
			{
				doubleSpan = points.GetMBufferUnsafe();
				m = doubleSpan.Span;
			}
			else
			{
				m = new double[points.Count];
				m.Fill(points.MFillValue);
			}
		}
		else if (points.HasM)
		{
			doubleSpan = points.GetMBufferUnsafe();
			m = doubleSpan.Span;
		}
		return Write(shpFilename, pointBufferUnsafe, z, m, attrTable, projection);
	}

	public static OperationResult Write(string shpFilename, Span<Point> points, Table attrTable = null, Projection? projection = null)
	{
		return Write(shpFilename, points, Span<double>.Empty, Span<double>.Empty, attrTable, projection);
	}

	public static OperationResult Write(string shpFilename, List<Point> points, Table attrTable = null, Projection? projection = null)
	{
		return Write(shpFilename, CollectionsMarshal.AsSpan(points), Span<double>.Empty, Span<double>.Empty, attrTable, projection);
	}

	public static OperationResult Write(string shpFilename, Span<Point> points, Span<double> z, Span<double> m, Table attrTable = null, Projection? projection = null)
	{
		OperationResult operationResult = new OperationResult();
		operationResult.MergeIn(TryAssertNoDepsExist(shpFilename));
		if (!operationResult)
		{
			return operationResult;
		}
		if (attrTable != null && attrTable.Rows.Count != points.Length)
		{
			operationResult.AddFailure(MessageCodes.GetTableRowCountMismatch(attrTable.Rows.Count, points.Length));
			return operationResult;
		}
		bool condition = false;
		int length = points.Length;
		try
		{
			WriteInternal(shpFilename, points, z, m);
			OperationResult other = ((attrTable != null) ? DbfIO.TryWrite(DBFFilename(shpFilename), attrTable) : DbfIO.TryWriteEmpty(DBFFilename(shpFilename), length));
			operationResult.MergeIn(other);
			if (!operationResult)
			{
				return operationResult;
			}
			projection?.ExportEsri(PRJFilename(shpFilename));
			condition = true;
			return operationResult;
		}
		catch (Exception ex)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetExceptionOccurred("Shapefile Write", ex));
			return operationResult;
		}
		finally
		{
			if (!condition)
			{
				CleanupPartialShapefile(shpFilename);
			}
		}
	}

	public static OperationResult Write(string shpFilename, IList<PointCollection> multipoints, Table attrTable = null, Projection? projection = null)
	{
		if (multipoints is IReadOnlyList<PointCollection> multipoints2)
		{
			return Write(shpFilename, multipoints2, attrTable, projection);
		}
		return Write(shpFilename, (IReadOnlyList<PointCollection>)multipoints.AsReadOnly(), attrTable, projection);
	}

	public static OperationResult Write(string shpFilename, IReadOnlyList<PointCollection> multipoints, Table attrTable = null, Projection? projection = null)
	{
		if (multipoints == null)
		{
			throw new ArgumentNullException("multipoints");
		}
		OperationResult operationResult = new OperationResult();
		operationResult.MergeIn(TryAssertNoDepsExist(shpFilename));
		if (!operationResult)
		{
			return operationResult;
		}
		if (attrTable != null && attrTable.Rows.Count != multipoints.Count)
		{
			operationResult.AddFailure(MessageCodes.GetTableRowCountMismatch(attrTable.Rows.Count, multipoints.Count));
			return operationResult;
		}
		bool condition = false;
		try
		{
			WriteInternal(shpFilename, multipoints);
			OperationResult other = ((attrTable != null) ? DbfIO.TryWrite(DBFFilename(shpFilename), attrTable) : DbfIO.TryWriteEmpty(DBFFilename(shpFilename), multipoints.Count));
			operationResult.MergeIn(other);
			if (!operationResult)
			{
				return operationResult;
			}
			projection?.ExportEsri(PRJFilename(shpFilename));
			condition = true;
			return operationResult;
		}
		catch (Exception ex)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetExceptionOccurred("Shapefile Write", ex));
			return operationResult;
		}
		finally
		{
			if (!condition)
			{
				CleanupPartialShapefile(shpFilename);
			}
		}
	}

	public static OperationResult Write(string shpFilename, IList<Polyline> polylines, Table attrTable = null, Projection? projection = null)
	{
		if (polylines is IReadOnlyList<Polyline> polylines2)
		{
			return Write(shpFilename, polylines2, attrTable, projection);
		}
		return Write(shpFilename, (IReadOnlyList<Polyline>)polylines.AsReadOnly(), attrTable, projection);
	}

	public static OperationResult Write(string shpFilename, List<Polyline> polylines, Table attrTable = null, Projection? projection = null)
	{
		return Write(shpFilename, (IReadOnlyList<Polyline>)polylines, attrTable, projection);
	}

	public static OperationResult Write(string shpFilename, Polyline[] polylines, Table attrTable = null, Projection? projection = null)
	{
		return Write(shpFilename, (IReadOnlyList<Polyline>)polylines, attrTable, projection);
	}

	public static OperationResult Write(string shpFilename, IReadOnlyList<Polyline> polylines, Table attrTable = null, Projection? projection = null)
	{
		if (polylines == null)
		{
			throw new ArgumentNullException("polylines");
		}
		OperationResult operationResult = new OperationResult();
		operationResult.MergeIn(TryAssertNoDepsExist(shpFilename));
		if (!operationResult)
		{
			return operationResult;
		}
		if (attrTable != null && attrTable.Rows.Count != polylines.Count)
		{
			operationResult.AddFailure(MessageCodes.GetTableRowCountMismatch(attrTable.Rows.Count, polylines.Count));
			return operationResult;
		}
		bool condition = false;
		try
		{
			WriteInternal(shpFilename, polylines);
			OperationResult other = ((attrTable != null) ? DbfIO.TryWrite(DBFFilename(shpFilename), attrTable) : DbfIO.TryWriteEmpty(DBFFilename(shpFilename), polylines.Count));
			operationResult.MergeIn(other);
			if (!operationResult)
			{
				return operationResult;
			}
			projection?.ExportEsri(PRJFilename(shpFilename));
			condition = true;
			return operationResult;
		}
		catch (Exception ex)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetExceptionOccurred("Shapefile Write", ex));
			return operationResult;
		}
		finally
		{
			if (!condition)
			{
				CleanupPartialShapefile(shpFilename);
			}
		}
	}

	private static void EnsureDirectoryFor(string filename)
	{
		string directoryName = Path.GetDirectoryName(filename);
		if (!Directory.Exists(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
	}

	private static void CleanupPartialShapefile(string shpFilename)
	{
		if (File.Exists(shpFilename))
		{
			File.Delete(shpFilename);
		}
		string path = DBFFilename(shpFilename);
		if (File.Exists(path))
		{
			File.Delete(path);
		}
		string path2 = SHXFilename(shpFilename);
		if (File.Exists(path2))
		{
			File.Delete(path2);
		}
	}

	public static OperationResult Write(string shpFilename, IReadOnlyList<Polygon> polygons, Table attrTable = null, Projection? projection = null)
	{
		if (polygons == null)
		{
			throw new ArgumentNullException("polygons");
		}
		OperationResult operationResult = new OperationResult();
		operationResult.MergeIn(TryAssertNoDepsExist(shpFilename));
		if (!operationResult)
		{
			return operationResult;
		}
		if (attrTable != null && attrTable.Rows.Count != polygons.Count)
		{
			operationResult.AddFailure(MessageCodes.GetTableRowCountMismatch(attrTable.Rows.Count, polygons.Count));
			return operationResult;
		}
		bool condition = false;
		try
		{
			WriteInternal(shpFilename, polygons);
			OperationResult other = ((attrTable != null) ? DbfIO.TryWrite(DBFFilename(shpFilename), attrTable) : DbfIO.TryWriteEmpty(DBFFilename(shpFilename), polygons.Count));
			operationResult.MergeIn(other);
			if (!operationResult)
			{
				return operationResult;
			}
			projection?.ExportEsri(PRJFilename(shpFilename));
			condition = true;
			return operationResult;
		}
		catch (Exception ex)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetExceptionOccurred("Shapefile Write", ex));
			return operationResult;
		}
		finally
		{
			if (!condition)
			{
				CleanupPartialShapefile(shpFilename);
			}
		}
	}

	public static OperationResult Write(string shpFilename, IReadOnlyList<ComplexPolygon> complexPolygons, Table attrTable = null, Projection? projection = null)
	{
		if (complexPolygons == null)
		{
			throw new ArgumentNullException("complexPolygons");
		}
		OperationResult operationResult = new OperationResult();
		operationResult.MergeIn(TryAssertNoDepsExist(shpFilename));
		if (!operationResult)
		{
			return operationResult;
		}
		if (attrTable != null && attrTable.Rows.Count != complexPolygons.Count)
		{
			operationResult.AddFailure(MessageCodes.GetTableRowCountMismatch(attrTable.Rows.Count, complexPolygons.Count));
			return operationResult;
		}
		bool condition = false;
		try
		{
			WriteInternal(shpFilename, complexPolygons);
			OperationResult other = ((attrTable != null) ? DbfIO.TryWrite(DBFFilename(shpFilename), attrTable) : DbfIO.TryWriteEmpty(DBFFilename(shpFilename), complexPolygons.Count));
			operationResult.MergeIn(other);
			if (!operationResult)
			{
				return operationResult;
			}
			projection?.ExportEsri(PRJFilename(shpFilename));
			condition = true;
			return operationResult;
		}
		catch (Exception ex)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetExceptionOccurred("Shapefile Write", ex));
			return operationResult;
		}
		finally
		{
			if (!condition)
			{
				CleanupPartialShapefile(shpFilename);
			}
		}
	}

	public static OperationResult WriteTinAsPolygons(string shpFilename, Tin tin, TinWriteOptions options = null, Projection? projection = null)
	{
		if (tin == null)
		{
			throw new ArgumentNullException("tin");
		}
		if (options == null)
		{
			options = new TinWriteOptions();
		}
		OperationResult operationResult = new OperationResult();
		operationResult.MergeIn(TryAssertNoDepsExist(shpFilename));
		if (!operationResult)
		{
			return operationResult;
		}
		List<TriangleDefinition> triangleDefinition = tin.TriangleDefinition;
		IReadOnlyList<Triangle> triangles = tin.Triangles;
		bool hasZ = tin.HasZ;
		List<Polygon> polygons = new List<Polygon>(triangleDefinition.Count);
		List<int> intList = new List<int>(triangleDefinition.Count);
		for (int i = 0; i < triangleDefinition.Count; i++)
		{
			TriangleDefinition triangleDefinition2 = triangleDefinition[i];
			if (!triangleDefinition2.IsDeleted || options.IncludeDeletedTriangles)
			{
				Triangle triangle = triangles[i];
				Polygon polygon = new Polygon(new Point[3] { triangle.PointA, triangle.PointB, triangle.PointC });
				if (hasZ)
				{
					polygon.TrackZ(new double[3]
					{
						tin.Z[triangleDefinition2.p0],
						tin.Z[triangleDefinition2.p1],
						tin.Z[triangleDefinition2.p2]
					});
				}
				polygons.Add(polygon);
				intList.Add(i);
			}
		}
		Table table = BuildTinAttributeTable(tin, options, polygons, intList);
		bool condition = false;
		try
		{
			WriteInternal(shpFilename, polygons);
			OperationResult other = ((table != null) ? DbfIO.TryWrite(DBFFilename(shpFilename), table) : DbfIO.TryWriteEmpty(DBFFilename(shpFilename), polygons.Count));
			operationResult.MergeIn(other);
			if (!operationResult)
			{
				return operationResult;
			}
			projection?.ExportEsri(PRJFilename(shpFilename));
			condition = true;
			return operationResult;
		}
		catch (Exception ex)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetExceptionOccurred("TIN Shapefile Write", ex));
			return operationResult;
		}
		finally
		{
			if (!condition)
			{
				CleanupPartialShapefile(shpFilename);
			}
		}
	}

	public static OperationResult WriteSubsetTinAsPolygons(string shpFilename, Tin sourceTin, IList<int> triangleIndices, TinWriteOptions options = null, Projection? projection = null)
	{
		if (sourceTin == null)
		{
			throw new ArgumentNullException("sourceTin");
		}
		if (triangleIndices == null)
		{
			throw new ArgumentNullException("triangleIndices");
		}
		Tin subsetTin = sourceTin.GetSubsetTin(triangleIndices, out var triangleIndexMap, out var pointIndexMap);
		return WriteSubsetTinAsPolygons_Internal(shpFilename, subsetTin, triangleIndexMap, pointIndexMap, options, projection);
	}

	public static OperationResult WriteSubsetTinAsPolygons(string shpFilename, Tin sourceTin, Extent subsetExtent, TinWriteOptions options = null, Projection? projection = null)
	{
		if (sourceTin == null)
		{
			throw new ArgumentNullException("sourceTin");
		}
		Tin subsetTin = sourceTin.GetSubsetTin(subsetExtent, out var triangleIndexMap, out var pointIndexMap);
		return WriteSubsetTinAsPolygons_Internal(shpFilename, subsetTin, triangleIndexMap, pointIndexMap, options, projection);
	}

	public static void DebugWriteTin(Tin tin)
	{
		string path = Path.Combine(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString().Substring(0, 8)), "tin.shp");
		if (WriteTinAsPolygons(path, tin).Result)
		{
			FileHelpers.OpenFolderAndSelect(path);
		}
	}

	public static void DebugWriteSubsetTin(Tin tin, Extent extent)
	{
		string path = Path.Combine(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString().Substring(0, 8)), "tin.shp");
		if (WriteSubsetTinAsPolygons(path, tin, extent).Result)
		{
			FileHelpers.OpenFolderAndSelect(path);
		}
	}

	private static OperationResult WriteSubsetTinAsPolygons_Internal(string shpFilename, Tin subsetTin, int[] triMap, int[] ptMap, TinWriteOptions options = null, Projection? projection = null)
	{
		if (options == null)
		{
			options = new TinWriteOptions();
		}
		Table table = new Table();
		int index = table.AddColumn("SrcTriIdx", typeof(int));
		int index2 = table.AddColumn("SrcP0", typeof(int));
		int index3 = table.AddColumn("SrcP1", typeof(int));
		int index4 = table.AddColumn("SrcP2", typeof(int));
		for (int i = 0; i < subsetTin.TriangleCount; i++)
		{
			TriangleDefinition triangleDefinition = subsetTin.TriangleDefinition[i];
			if (!triangleDefinition.IsDeleted || options.IncludeDeletedTriangles)
			{
				TableRow tableRow = table.AddNewRow();
				tableRow[index] = triMap[i];
				tableRow[index2] = ptMap[triangleDefinition.p0];
				tableRow[index3] = ptMap[triangleDefinition.p1];
				tableRow[index4] = ptMap[triangleDefinition.p2];
			}
		}
		TinWriteOptions options2 = new TinWriteOptions
		{
			AttributeTable = table,
			IncludeDeletedTriangles = options.IncludeDeletedTriangles,
			IncludeIndices = options.IncludeIndices,
			IncludeIsDeleted = options.IncludeIsDeleted,
			IncludeArea = options.IncludeArea,
			IncludePerimeterEdges = options.IncludePerimeterEdges,
			IncludeVertexZ = options.IncludeVertexZ
		};
		return WriteTinAsPolygons(shpFilename, subsetTin, options2, projection);
	}

	private static Table BuildTinAttributeTable(Tin tin, TinWriteOptions options, List<Polygon> polygons, List<int> originalIndices)
	{
		bool hasAttributeTable = options.AttributeTable != null;
		bool includeIndicesOptionsTerm = options.IncludeIndices || options.IncludeIsDeleted || options.IncludeArea || options.IncludePerimeterEdges || options.IncludeVertexZ;
		if (!hasAttributeTable && !includeIndicesOptionsTerm)
		{
			return null;
		}
		if (hasAttributeTable && options.AttributeTable.Rows.Count != polygons.Count)
		{
			throw new Exception("Table rows don't match the number of non-deleted triangles.");
		}
		Table table = (hasAttributeTable ? options.AttributeTable.CloneStructure() : new Table());
		int index = -1;
		int index2 = -1;
		int index3 = -1;
		int index4 = -1;
		int index5 = -1;
		int index6 = -1;
		int index7 = -1;
		int index8 = -1;
		int index9 = -1;
		int index10 = -1;
		int index11 = -1;
		int index12 = -1;
		int index13 = -1;
		int index14 = -1;
		int index15 = -1;
		if (options.IncludeIndices)
		{
			index = table.AddColumn("TriIdx", typeof(int));
			index2 = table.AddColumn("P0", typeof(int));
			index3 = table.AddColumn("P1", typeof(int));
			index4 = table.AddColumn("P2", typeof(int));
			index5 = table.AddColumn("AT0", typeof(int));
			index6 = table.AddColumn("AT1", typeof(int));
			index7 = table.AddColumn("AT2", typeof(int));
		}
		if (options.IncludeIsDeleted)
		{
			index8 = table.AddColumn("IsDeleted", typeof(int));
		}
		if (options.IncludeArea)
		{
			index9 = table.AddColumn("Area", typeof(double));
		}
		if (options.IncludePerimeterEdges)
		{
			index10 = table.AddColumn("E0Perim", typeof(int));
			index11 = table.AddColumn("E1Perim", typeof(int));
			index12 = table.AddColumn("E2Perim", typeof(int));
		}
		if (options.IncludeVertexZ && tin.HasZ)
		{
			index13 = table.AddColumn("Z0", typeof(double));
			index14 = table.AddColumn("Z1", typeof(double));
			index15 = table.AddColumn("Z2", typeof(double));
		}
		List<TriangleDefinition> triangleDefinition = tin.TriangleDefinition;
		for (int i = 0; i < polygons.Count; i++)
		{
			TableRow tableRow2;
			if (hasAttributeTable)
			{
				object[] objectValues = new object[table.Columns.Count];
				TableRow tableRow = options.AttributeTable.Rows[i];
				for (int j = 0; j < options.AttributeTable.Columns.Count; j++)
				{
					objectValues[j] = tableRow[j];
				}
				tableRow2 = table.AddNewRow(objectValues);
			}
			else
			{
				tableRow2 = table.AddNewRow();
			}
			int originalIndex = originalIndices[i];
			TriangleDefinition triangleDefinition2 = triangleDefinition[originalIndex];
			if (options.IncludeIndices)
			{
				tableRow2[index] = originalIndex;
				tableRow2[index2] = triangleDefinition2.p0;
				tableRow2[index3] = triangleDefinition2.p1;
				tableRow2[index4] = triangleDefinition2.p2;
				tableRow2[index5] = triangleDefinition2.at0;
				tableRow2[index6] = triangleDefinition2.at1;
				tableRow2[index7] = triangleDefinition2.at2;
			}
			if (options.IncludeIsDeleted)
			{
				tableRow2[index8] = (triangleDefinition2.IsDeleted ? 1 : 0);
			}
			if (options.IncludeArea)
			{
				tableRow2[index9] = tin.Triangles[originalIndex].Area;
			}
			if (options.IncludePerimeterEdges)
			{
				tableRow2[index10] = (IsEdgePerimeter(triangleDefinition, triangleDefinition2.at0) ? 1 : 0);
				tableRow2[index11] = (IsEdgePerimeter(triangleDefinition, triangleDefinition2.at1) ? 1 : 0);
				tableRow2[index12] = (IsEdgePerimeter(triangleDefinition, triangleDefinition2.at2) ? 1 : 0);
			}
			if (options.IncludeVertexZ && tin.HasZ)
			{
				tableRow2[index13] = tin.Z[triangleDefinition2.p0];
				tableRow2[index14] = tin.Z[triangleDefinition2.p1];
				tableRow2[index15] = tin.Z[triangleDefinition2.p2];
			}
		}
		return table;
	}

	private static bool IsEdgePerimeter(List<TriangleDefinition> tDefs, int adjTriIdx)
	{
		if (adjTriIdx < 0 || adjTriIdx >= tDefs.Count)
		{
			return true;
		}
		return tDefs[adjTriIdx].IsDeleted;
	}

	private static void WriteInternal(string shpFilename, Span<Point> ptSpan, Span<double> zSpan, Span<double> mSpan)
	{
		bool notIsEmpty = !zSpan.IsEmpty;
		bool notIsEmpty2 = !mSpan.IsEmpty;
		if (notIsEmpty && !notIsEmpty2)
		{
			throw new Exception("Cannot have Z buffer without an M buffer, per spec.");
		}
		int forPoint = ShapeTypeHelper.GetForPoint(notIsEmpty, notIsEmpty2);
		using FileStream fileStream = new FileStream(shpFilename, FileMode.CreateNew);
		using BinaryWriter binaryWriter = new BinaryWriter(fileStream);
		Span<byte> byteSpan = stackalloc byte[100];
		binaryWriter.Write(byteSpan);
		int[,] shxOffsetWords = new int[ptSpan.Length, 2];
		int curOffsetWords = 50;
		int ordinal = 28;
		if (notIsEmpty)
		{
			ordinal += 16;
		}
		else if (notIsEmpty2)
		{
			ordinal += 8;
		}
		int ordinal2 = ordinal - 8;
		Span<byte> byteSpan2 = stackalloc byte[ordinal];
		for (int i = 0; i < ptSpan.Length; i++)
		{
			Span<byte> recordSlice = byteSpan2;
			WritePerRecordHeader(ref recordSlice, i + 1, ordinal2);
			WriteShxEntryAndAdvance(shxOffsetWords, i, ref curOffsetWords, ordinal2);
			WriteLE(ref recordSlice, forPoint);
			WritePoint(ref recordSlice, ptSpan[i]);
			if (notIsEmpty)
			{
				WriteLE(ref recordSlice, zSpan[i]);
				WriteLE(ref recordSlice, mSpan[i]);
			}
			else if (notIsEmpty2)
			{
				WriteLE(ref recordSlice, mSpan[i]);
			}
			binaryWriter.Write(byteSpan2);
		}
		Extent ext = PointCollection.ComputeExtent(ptSpan);
		int totalFileLength = (int)fileStream.Length;
		BuildShapefileHeader(byteSpan, totalFileLength, forPoint, ext);
		fileStream.Position = 0L;
		binaryWriter.Write(byteSpan);
		WriteShxInternal(SHXFilename(shpFilename), byteSpan, shxOffsetWords, forPoint, ext);
	}

	private static void WriteInternal(string shpFilename, IReadOnlyList<PointCollection> multipoints)
	{
		EnsureDirectoryFor(shpFilename);
		PolyStats(multipoints, out var hasZ, out var hasM, out var maxPtCt, out var ext);
		int forMultipoint = ShapeTypeHelper.GetForMultipoint(hasZ, hasM);
		using FileStream fileStream = new FileStream(shpFilename, FileMode.CreateNew);
		using BinaryWriter binaryWriter = new BinaryWriter(fileStream);
		Span<byte> byteSpan = stackalloc byte[100];
		binaryWriter.Write(byteSpan);
		int[,] shxOffsetWords = new int[multipoints.Count, 2];
		int curOffsetWords = 50;
		Span<byte> byteSpan2 = new byte[8 + MultpointRecordContentLength(maxPtCt, hasZ, hasM)];
		for (int i = 0; i < multipoints.Count; i++)
		{
			PointCollection pointCollection = multipoints[i];
			int count = pointCollection.Count;
			int multpointRecordContentLength = MultpointRecordContentLength(count, pointCollection.HasZ, pointCollection.HasM);
			int length = 8 + multpointRecordContentLength;
			WriteShxEntryAndAdvance(shxOffsetWords, i, ref curOffsetWords, multpointRecordContentLength);
			Span<byte> recordSlice = byteSpan2.Slice(0, length);
			Span<byte> recordSliceSpan = recordSlice;
			WritePerRecordHeader(ref recordSlice, i + 1, multpointRecordContentLength);
			WriteLE(ref recordSlice, forMultipoint);
			WriteBBox(ref recordSlice, pointCollection.Extent);
			WriteLE(ref recordSlice, count);
			Span<Point> pointBufferUnsafe = pointCollection.GetPointBufferUnsafe().Span;
			for (int j = 0; j < pointCollection.Count; j++)
			{
				Point pt = pointBufferUnsafe[j];
				WritePoint(ref recordSlice, pt);
			}
			Memory<double> doubleSpan;
			if (pointCollection.HasZ)
			{
				doubleSpan = pointCollection.GetZBufferUnsafe();
				Span<double> doubleSpan2 = doubleSpan.Span;
				Buffers.MaxMin(doubleSpan2, out var max, out var min);
				WriteLE(ref recordSlice, min);
				WriteLE(ref recordSlice, max);
				WriteLE(ref recordSlice, doubleSpan2);
			}
			if (pointCollection.HasM)
			{
				doubleSpan = pointCollection.GetMBufferUnsafe();
				Span<double> doubleSpan3 = doubleSpan.Span;
				Buffers.MaxMin(doubleSpan3, out var max2, out var min2);
				WriteLE(ref recordSlice, min2);
				WriteLE(ref recordSlice, max2);
				WriteLE(ref recordSlice, doubleSpan3);
			}
			binaryWriter.Write(recordSliceSpan);
		}
		int totalFileLength = (int)fileStream.Length;
		BuildShapefileHeader(byteSpan, totalFileLength, forMultipoint, ext);
		fileStream.Position = 0L;
		binaryWriter.Write(byteSpan);
		WriteShxInternal(SHXFilename(shpFilename), byteSpan, shxOffsetWords, forMultipoint, ext);
	}

	private static void WriteInternal(string shpFilename, IReadOnlyList<Polyline> polylines)
	{
		EnsureDirectoryFor(shpFilename);
		PolyStats(polylines, out var hasZ, out var hasM, out var maxPtCt, out var ext);
		int forPolyline = ShapeTypeHelper.GetForPolyline(hasZ, hasM);
		using FileStream fileStream = new FileStream(shpFilename, FileMode.CreateNew);
		using BinaryWriter binaryWriter = new BinaryWriter(fileStream);
		Span<byte> byteSpan = stackalloc byte[100];
		binaryWriter.Write(byteSpan);
		int[,] shxOffsetWords = new int[polylines.Count, 2];
		int curOffsetWords = 50;
		Span<byte> byteSpan2 = new byte[8 + PolyRecordContentLength(1, maxPtCt, hasZ, hasM)];
		for (int i = 0; i < polylines.Count; i++)
		{
			Polyline polyline = polylines[i];
			int count = polyline.Count;
			int polyRecordContentLength = PolyRecordContentLength(1, count, polyline.HasZ, polyline.HasM);
			int length = 8 + polyRecordContentLength;
			WriteShxEntryAndAdvance(shxOffsetWords, i, ref curOffsetWords, polyRecordContentLength);
			Span<byte> recordSlice = byteSpan2.Slice(0, length);
			Span<byte> recordSliceSpan = recordSlice;
			WritePerRecordHeader(ref recordSlice, i + 1, polyRecordContentLength);
			WriteLE(ref recordSlice, forPolyline);
			WriteBBox(ref recordSlice, polyline.Extent);
			WriteLE(ref recordSlice, 1);
			WriteLE(ref recordSlice, count);
			WriteLE(ref recordSlice, 0);
			Span<Point> pointBufferUnsafe = polyline.GetPointBufferUnsafe().Span;
			for (int j = 0; j < polyline.Count; j++)
			{
				Point pt = pointBufferUnsafe[j];
				WritePoint(ref recordSlice, pt);
			}
			Memory<double> doubleSpan;
			if (polyline.HasZ)
			{
				doubleSpan = polyline.GetZBufferUnsafe();
				Span<double> doubleSpan2 = doubleSpan.Span;
				Buffers.MaxMin(doubleSpan2, out var max, out var min);
				WriteLE(ref recordSlice, min);
				WriteLE(ref recordSlice, max);
				WriteLE(ref recordSlice, doubleSpan2);
			}
			if (polyline.HasM)
			{
				doubleSpan = polyline.GetMBufferUnsafe();
				Span<double> doubleSpan3 = doubleSpan.Span;
				Buffers.MaxMin(doubleSpan3, out var max2, out var min2);
				WriteLE(ref recordSlice, min2);
				WriteLE(ref recordSlice, max2);
				WriteLE(ref recordSlice, doubleSpan3);
			}
			binaryWriter.Write(recordSliceSpan);
		}
		int totalFileLength = (int)fileStream.Length;
		BuildShapefileHeader(byteSpan, totalFileLength, forPolyline, ext);
		fileStream.Position = 0L;
		binaryWriter.Write(byteSpan);
		WriteShxInternal(SHXFilename(shpFilename), byteSpan, shxOffsetWords, forPolyline, ext);
	}

	private static void WriteInternal(string shpFilename, IReadOnlyList<Polygon> polygons)
	{
		EnsureDirectoryFor(shpFilename);
		PolyStats(polygons, out var hasZ, out var hasM, out var maxPtCt, out var ext);
		int forPolygon = ShapeTypeHelper.GetForPolygon(hasZ, hasM);
		using FileStream fileStream = new FileStream(shpFilename, FileMode.CreateNew);
		using BinaryWriter binaryWriter = new BinaryWriter(fileStream);
		Span<byte> byteSpan = stackalloc byte[100];
		binaryWriter.Write(byteSpan);
		int[,] shxOffsetWords = new int[polygons.Count, 2];
		int curOffsetWords = 50;
		Span<byte> byteSpan2 = new byte[8 + PolyRecordContentLength(1, maxPtCt + 1, hasZ, hasM)];
		for (int i = 0; i < polygons.Count; i++)
		{
			Polygon polygon = polygons[i];
			int count = polygon.Count;
			int polyRecordContentLength = PolyRecordContentLength(1, count + 1, polygon.HasZ, polygon.HasM);
			int length = 8 + polyRecordContentLength;
			WriteShxEntryAndAdvance(shxOffsetWords, i, ref curOffsetWords, polyRecordContentLength);
			Span<byte> recordSlice = byteSpan2.Slice(0, length);
			Span<byte> recordSliceSpan = recordSlice;
			WritePerRecordHeader(ref recordSlice, i + 1, polyRecordContentLength);
			WriteLE(ref recordSlice, forPolygon);
			WriteBBox(ref recordSlice, polygon.Extent);
			WriteLE(ref recordSlice, 1);
			WriteLE(ref recordSlice, count + 1);
			WriteLE(ref recordSlice, 0);
			Span<Point> pointBufferUnsafe = polygon.GetPointBufferUnsafe().Span;
			for (int j = 0; j < polygon.Count; j++)
			{
				Point pt = pointBufferUnsafe[j];
				WritePoint(ref recordSlice, pt);
			}
			WritePoint(ref recordSlice, polygon[0]);
			Memory<double> doubleSpan;
			if (polygon.HasZ)
			{
				doubleSpan = polygon.GetZBufferUnsafe();
				Span<double> doubleSpan2 = doubleSpan.Span;
				Buffers.MaxMin(doubleSpan2, out var max, out var min);
				WriteLE(ref recordSlice, min);
				WriteLE(ref recordSlice, max);
				WriteLE(ref recordSlice, doubleSpan2);
				WriteLE(ref recordSlice, doubleSpan2[0]);
			}
			if (polygon.HasM)
			{
				doubleSpan = polygon.GetMBufferUnsafe();
				Span<double> doubleSpan3 = doubleSpan.Span;
				Buffers.MaxMin(doubleSpan3, out var max2, out var min2);
				WriteLE(ref recordSlice, min2);
				WriteLE(ref recordSlice, max2);
				WriteLE(ref recordSlice, doubleSpan3);
				WriteLE(ref recordSlice, doubleSpan3[0]);
			}
			binaryWriter.Write(recordSliceSpan);
		}
		int totalFileLength = (int)fileStream.Length;
		BuildShapefileHeader(byteSpan, totalFileLength, forPolygon, ext);
		fileStream.Position = 0L;
		binaryWriter.Write(byteSpan);
		WriteShxInternal(SHXFilename(shpFilename), byteSpan, shxOffsetWords, forPolygon, ext);
	}

	private static void WriteInternal(string shpFilename, IReadOnlyList<ComplexPolygon> complexPolygons)
	{
		EnsureDirectoryFor(shpFilename);
		foreach (ComplexPolygon complexPolygon in complexPolygons)
		{
			if (complexPolygon.RingCount == 0)
			{
				throw new InvalidDataException("A polygon record must contain at least one ring.");
			}
			bool recordHasZ = complexPolygon.Rings[0].HasZ;
			bool recordHasM = complexPolygon.Rings[0].HasM;
			for (int ringIndex = 1; ringIndex < complexPolygon.RingCount; ringIndex++)
			{
				if (complexPolygon.Rings[ringIndex].HasZ != recordHasZ ||
					complexPolygon.Rings[ringIndex].HasM != recordHasM)
				{
					throw new InvalidDataException("All rings in a polygon record must share the record's Z/M dimensions.");
				}
			}
		}
		ComplexPolyStats(complexPolygons, out var hasZ, out var hasM, out var maxPtCt, out var maxPartCount, out var ext);
		int forPolygon = ShapeTypeHelper.GetForPolygon(hasZ, hasM);
		using FileStream fileStream = new FileStream(shpFilename, FileMode.CreateNew);
		using BinaryWriter binaryWriter = new BinaryWriter(fileStream);
		Span<byte> byteSpan = stackalloc byte[100];
		binaryWriter.Write(byteSpan);
		int[,] shxOffsetWords = new int[complexPolygons.Count, 2];
		int curOffsetWords = 50;
		Span<byte> byteSpan2 = new byte[8 + PolyRecordContentLength(maxPartCount, maxPtCt + maxPartCount, hasZ, hasM)];
		for (int i = 0; i < complexPolygons.Count; i++)
		{
			ComplexPolygon complexPolygon = complexPolygons[i];
			int count = complexPolygon.Points.Count;
			int ringCount = complexPolygon.RingCount;
			bool recordHasZ = complexPolygon.Rings[0].HasZ;
			bool recordHasM = complexPolygon.Rings[0].HasM;
			int polyRecordContentLength = PolyRecordContentLength(ringCount, count + ringCount, recordHasZ, recordHasM);
			int length = 8 + polyRecordContentLength;
			WriteShxEntryAndAdvance(shxOffsetWords, i, ref curOffsetWords, polyRecordContentLength);
			Span<byte> recordSlice = byteSpan2.Slice(0, length);
			Span<byte> recordSliceSpan = recordSlice;
			WritePerRecordHeader(ref recordSlice, i + 1, polyRecordContentLength);
			WriteLE(ref recordSlice, forPolygon);
			WriteBBox(ref recordSlice, complexPolygon.Extent);
			WriteLE(ref recordSlice, ringCount);
			WriteLE(ref recordSlice, count + complexPolygon.RingCount);
			int countOffsetSum = 0;
			for (int j = 0; j < ringCount; j++)
			{
				WriteLE(ref recordSlice, countOffsetSum);
				countOffsetSum += complexPolygon.Rings[j].Points.Count + 1;
			}
			for (int k = 0; k < ringCount; k++)
			{
				Polygon polygon = complexPolygon.Rings[k];
				Span<Point> pointBufferUnsafe = polygon.GetPointBufferUnsafe().Span;
				for (int l = 0; l < polygon.Count; l++)
				{
					Point pt = pointBufferUnsafe[l];
					WritePoint(ref recordSlice, pt);
				}
				WritePoint(ref recordSlice, polygon[0]);
			}
			if (recordHasZ)
			{
				WriteComplexPolygonDimension(ref recordSlice, complexPolygon, writeZ: true);
			}
			if (recordHasM)
			{
				WriteComplexPolygonDimension(ref recordSlice, complexPolygon, writeZ: false);
			}
			binaryWriter.Write(recordSliceSpan);
		}
		int totalFileLength = (int)fileStream.Length;
		BuildShapefileHeader(byteSpan, totalFileLength, forPolygon, ext);
		fileStream.Position = 0L;
		binaryWriter.Write(byteSpan);
		WriteShxInternal(SHXFilename(shpFilename), byteSpan, shxOffsetWords, forPolygon, ext);
	}

	private static void WriteComplexPolygonDimension(ref Span<byte> recordSlice, ComplexPolygon complexPolygon, bool writeZ)
	{
		double min = double.MaxValue;
		double max = double.MinValue;
		for (int ringIndex = 0; ringIndex < complexPolygon.RingCount; ringIndex++)
		{
			Polygon ring = complexPolygon.Rings[ringIndex];
			Span<double> values = (writeZ ? ring.GetZBufferUnsafe() : ring.GetMBufferUnsafe()).Span;
			Buffers.MaxMin(values, out double ringMax, out double ringMin);
			min = Math.Min(min, ringMin);
			max = Math.Max(max, ringMax);
		}
		WriteLE(ref recordSlice, min);
		WriteLE(ref recordSlice, max);
		for (int ringIndex = 0; ringIndex < complexPolygon.RingCount; ringIndex++)
		{
			Polygon ring = complexPolygon.Rings[ringIndex];
			Span<double> values = (writeZ ? ring.GetZBufferUnsafe() : ring.GetMBufferUnsafe()).Span;
			WriteLE(ref recordSlice, values);
			WriteLE(ref recordSlice, values[0]);
		}
	}

	private static void WriteShxInternal(string shxFn, Span<byte> header, int[,] shxOffsetWords, int shapeTypeCode, Extent ext)
	{
		EnsureDirectoryFor(shxFn);
		int length = shxOffsetWords.GetLength(0);
		using FileStream output = new FileStream(shxFn, FileMode.CreateNew);
		using BinaryWriter binaryWriter = new BinaryWriter(output);
		header.Fill(0);
		int totalFileLength = 100 + 8 * length;
		BuildShapefileHeader(header, totalFileLength, shapeTypeCode, ext);
		binaryWriter.Write(header);
		WriteShxOffsets(binaryWriter, shxOffsetWords);
	}

	public static void DebugWriteTin(string directory, Tin tin)
	{
		if (!Directory.Exists(directory))
		{
			Directory.CreateDirectory(directory);
		}
		Write_GDAL(Path.Combine(directory, "TIN Points.shp"), "TIN Points", tin.Points.ToArray());
		Write_GDAL(Path.Combine(directory, "TIN Edges.shp"), "TIN Edges", (from ed in tin.GetAllEdgeDefinitions()
			select tin.GetEdgeSegment(ed).ToPolyline()).ToArray());
		Write_GDAL(Path.Combine(directory, "TIN Triangles (All).shp"), "TIN Triangles", tin.Triangles.Select((Triangle t) => t.AsPolygon()).ToArray().AsMemory());
		Write_GDAL(Path.Combine(directory, "TIN Triangles (Deleted).shp"), "TIN Triangles", (from tIdx in tin.TriangleDefinition.WhereByIndex((TriangleDefinition td) => td.IsDeleted)
			select tin.Triangles[tIdx].AsPolygon()).ToArray().AsMemory());
		Write_GDAL(Path.Combine(directory, "TIN Triangles (Kept).shp"), "TIN Triangles", (from tIdx in tin.TriangleDefinition.WhereByIndex((TriangleDefinition td) => !td.IsDeleted)
			select tin.Triangles[tIdx].AsPolygon()).ToArray().AsMemory());
	}

	public static void DebugWriteTin(string directory, TinFactory tinFac)
	{
		if (!Directory.Exists(directory))
		{
			Directory.CreateDirectory(directory);
		}
		DebugWriteTin(directory, tinFac.Tin);
	}

	public static void Write_GDAL(string fn, string layerName, Memory<Point> points, Projection? projection = null)
	{
		using (Driver driver = Ogr.GetDriverByName("ESRI Shapefile"))
		{
			using DataSource dataSource = driver.CreateDataSource(fn, null);
			using Layer layer = dataSource.CreateLayer(layerName, null, wkbGeometryType.wkbPoint, null);
			using FeatureDefn feature_def = layer.GetLayerDefn();
			Span<Point> pointsSpan = points.Span;
			for (int i = 0; i < pointsSpan.Length; i++)
			{
				Point point = pointsSpan[i];
				using Geometry geometry = new Geometry(wkbGeometryType.wkbPoint);
				geometry.SetPoint(0, point.X, point.Y, 0.0);
				using Feature feature = new Feature(feature_def);
				feature.SetGeometry(geometry);
				if (layer.CreateFeature(feature) != 0)
				{
					_logger.LogWarning("Could not write point feature.");
				}
			}
		}
		projection?.ExportEsri(PRJFilename(fn));
	}

	public static void Write_GDAL(string fn, string layerName, Memory<Polyline> polylines, Projection? projection = null)
	{
		using (Driver driver = Ogr.GetDriverByName("ESRI Shapefile"))
		{
			using DataSource dataSource = driver.CreateDataSource(fn, null);
			using Layer layer = dataSource.CreateLayer(layerName, null, wkbGeometryType.wkbLineString, null);
			using FeatureDefn feature_def = layer.GetLayerDefn();
			Span<Polyline> polylinesSpan = polylines.Span;
			for (int i = 0; i < polylinesSpan.Length; i++)
			{
				Polyline polyline = polylinesSpan[i];
				using Geometry geometry = new Geometry(wkbGeometryType.wkbLineString);
				for (int j = 0; j < polyline.Count; j++)
				{
					Point point = polyline[j];
					geometry.SetPoint(j, point.X, point.Y, 0.0);
				}
				using Feature feature = new Feature(feature_def);
				feature.SetGeometry(geometry);
				if (layer.CreateFeature(feature) != 0)
				{
					_logger.LogWarning("Could not write polyline feature.");
				}
			}
		}
		projection?.ExportEsri(PRJFilename(fn));
	}

	public static void Write_GDAL(string fn, string layerName, Memory<Polygon> polygons, Projection? projection = null)
	{
		using (Driver driver = Ogr.GetDriverByName("ESRI Shapefile"))
		{
			using DataSource dataSource = driver.CreateDataSource(fn, null);
			using Layer layer = dataSource.CreateLayer(layerName, null, wkbGeometryType.wkbPolygon, null);
			using FeatureDefn feature_def = layer.GetLayerDefn();
			Span<Polygon> polygonsSpan = polygons.Span;
			for (int i = 0; i < polygonsSpan.Length; i++)
			{
				Polygon polygon = polygonsSpan[i];
				using Geometry geometry = new Geometry(wkbGeometryType.wkbLinearRing);
				for (int j = 0; j < polygon.Count; j++)
				{
					Point point = polygon[j];
					geometry.AddPoint(point.X, point.Y, 0.0);
				}
				geometry.CloseRings();
				using Geometry geometry2 = new Geometry(wkbGeometryType.wkbPolygon);
				geometry2.AddGeometry(geometry);
				using Feature feature = new Feature(feature_def);
				feature.SetGeometry(geometry2);
				if (layer.CreateFeature(feature) != 0)
				{
					_logger.LogWarning("Could not write polygon feature.");
				}
			}
		}
		projection?.ExportEsri(PRJFilename(fn));
	}

	private static bool ReadFileParseHeader(string shpFilename, out byte[] shpBytes, out ShapefileHeader header)
	{
		shpBytes = File.ReadAllBytes(shpFilename);
		return TryParseShapefileHeaderInternal(shpBytes, out header);
	}

	public static OperationResult TryParseShapefileHeader(string shpFilename, out ShapefileHeader header)
	{
		if (!File.Exists(shpFilename))
		{
			header = null;
			return OperationResult.Fail(FileMessageCodes.GetFileNotFound(shpFilename));
		}
		using FileStream fileStream = new FileStream(shpFilename, FileMode.Open, FileAccess.Read);
		if (fileStream.Length < 100)
		{
			header = null;
			return OperationResult.Fail(FileMessageCodes.GetFileTooSmall(shpFilename, 100));
		}
		Span<byte> byteSpan = stackalloc byte[100];
		if (fileStream.Read(byteSpan) != byteSpan.Length)
		{
			header = null;
			return OperationResult.Fail(MessageCodes.GetErrorParsingShpHeader(shpFilename));
		}
		if (TryParseShapefileHeaderInternal(byteSpan, out header))
		{
			return OperationResult.Success();
		}
		return OperationResult.Fail(MessageCodes.GetErrorParsingShpHeader(shpFilename));
	}

	private static bool TryParseShapefileHeaderInternal(Span<byte> shpBytes, out ShapefileHeader header)
	{
		if (shpBytes.Length < 100)
		{
			header = null;
			return false;
		}
		if (IntBE(shpBytes) != 9994)
		{
			header = null;
			return false;
		}
		for (int i = 4; i <= 20; i += 4)
		{
			if (IntLE(shpBytes.Slice(i)) != 0)
			{
				header = null;
				return false;
			}
		}
		header = new ShapefileHeader();
		int intBE = IntBE(shpBytes.Slice(24));
		header.FileLength = intBE * 2;
		if (IntLE(shpBytes.Slice(28)) != 1000)
		{
			header = null;
			return false;
		}
		header.ShpTypeCode = IntLE(shpBytes.Slice(32));
		double minX = DoubleLE(shpBytes.Slice(36));
		double minY = DoubleLE(shpBytes.Slice(44));
		double maxX = DoubleLE(shpBytes.Slice(52));
		double maxY = DoubleLE(shpBytes.Slice(60));
		header.CachedExtent = new Extent(minX, maxX, minY, maxY);
		return true;
	}

	private static void BuildShapefileHeader(Span<byte> header, int totalFileLength, int shapeType, Extent ext, double minz = 0.0, double maxz = 0.0, double minm = 0.0, double maxm = 0.0)
	{
		if (header.Length < 100)
		{
			throw new ArgumentException("header incorrect size, must be at least " + 100 + " bytes.");
		}
		BinaryPrimitives.WriteInt32BigEndian(header, 9994);
		header = header.Slice(4);
		int ordinal = 20;
		header.Slice(0, ordinal).Fill(0);
		header = header.Slice(ordinal);
		BinaryPrimitives.WriteInt32BigEndian(header, totalFileLength / 2);
		header = header.Slice(4);
		BinaryPrimitives.WriteInt32LittleEndian(header, 1000);
		header = header.Slice(4);
		BinaryPrimitives.WriteInt32LittleEndian(header, shapeType);
		header = header.Slice(4);
		WriteBBox(ref header, ext);
		BinaryPrimitives.WriteDoubleLittleEndian(header, minz);
		header = header.Slice(8);
		BinaryPrimitives.WriteDoubleLittleEndian(header, maxz);
		header = header.Slice(8);
		BinaryPrimitives.WriteDoubleLittleEndian(header, minm);
		header = header.Slice(8);
		BinaryPrimitives.WriteDoubleLittleEndian(header, maxm);
		header = header.Slice(8);
	}

	private static int[] LoadOffsetsFromSHX(string shxFilename)
	{
		Span<byte> allBytes = File.ReadAllBytes(shxFilename);
		int count = (IntBE(allBytes.Slice(24)) - 50) / 4;
		int[] intValues = new int[count];
		int ordinal = 100;
		for (int i = 0; i < count; i++)
		{
			intValues[i] = IntBE(allBytes.Slice(ordinal)) * 2;
			ordinal += 8;
		}
		return intValues;
	}

	private static void WriteShxOffsets(BinaryWriter bw, int[,] offset)
	{
		Span<byte> byteSpan = new byte[1024];
		int ordinal = 0;
		for (int i = 0; i < offset.GetLength(0); i++)
		{
			int offsetTerm = offset[i, 0];
			int offsetTerm2 = offset[i, 1];
			WriteBE(byteSpan.Slice(ordinal), offsetTerm);
			WriteBE(byteSpan.Slice(ordinal + 4), offsetTerm2);
			ordinal += 8;
			if (ordinal == byteSpan.Length)
			{
				ordinal = 0;
				bw.Write(byteSpan);
			}
		}
		if (ordinal > 0)
		{
			bw.Write(byteSpan.Slice(0, ordinal));
		}
	}

	private static void LoadFeatures(Memory<byte> shpBytes, Memory<int> recordOffsets, ShapeOordinates oord, ISpatialTransformer spatialTransform, out Point[] points, out double[] z, out double[] m)
	{
		points = new Point[recordOffsets.Length];
		switch (oord)
		{
		case ShapeOordinates.XY:
			z = null;
			m = null;
			LoadPoints(shpBytes, recordOffsets, points);
			break;
		case ShapeOordinates.XYM:
			z = null;
			m = new double[recordOffsets.Length];
			LoadPointsM(shpBytes, recordOffsets, points, m);
			break;
		case ShapeOordinates.XYZM:
			z = new double[recordOffsets.Length];
			m = new double[recordOffsets.Length];
			LoadPointsZM(shpBytes, recordOffsets, points, z, m);
			break;
		default:
			throw new NotSupportedException(oord.ToString());
		}
		spatialTransform?.Transform(points, points);
	}

	private static void LoadFeatures(Memory<byte> shpBytes, Memory<int> recordOffsets, ShapeOordinates oord, ISpatialTransformer spatialTransform, out PointCollection[] multipoints)
	{
		multipoints = new PointCollection[recordOffsets.Length];
		PointCollection[] pl = multipoints;
		LoadRawData(shpBytes, recordOffsets, oord, ShapeType.Multipoint, (int n, int subN, Point[] pts, double[] z, double[] m) =>
		{
			if (subN > 0)
			{
				_logger.LogWarning("Multipart Multipoints not allowed per spec.");
			}
			else if (pts == null)
			{
				pl[n] = PointCollection.NullPointCollection();
			}
			else
			{
				if (spatialTransform != null)
				{
					spatialTransform.Transform(pts, pts);
				}
				pl[n] = PointCollection.CreateWithBuffer(pts, z, m);
			}
		});
	}

	private static void LoadFeatures(Memory<byte> shpBytes, Memory<int> recordOffsets, ShapeOordinates oord, ISpatialTransformer spatialTransform, out Polyline[] polylines)
	{
		polylines = new Polyline[recordOffsets.Length];
		Polyline[] pl = polylines;
		int nMultipartSkips = 0;
		LoadRawData(shpBytes, recordOffsets, oord, ShapeType.Line, (int n, int subN, Point[] pts, double[] z, double[] m) =>
		{
			if (subN > 0)
			{
				nMultipartSkips++;
			}
			else if (pts == null)
			{
				pl[n] = Polyline.NullPolyline();
			}
			else
			{
				if (spatialTransform != null)
				{
					spatialTransform.Transform(pts, pts);
				}
				pl[n] = Polyline.CreateWithBuffer(pts, z, m);
			}
		});
		if (nMultipartSkips > 0)
		{
			_logger.LogWarning("Attempted to read multipart polygons into Polygon container - skipping internal rings. ({SkipCount}) occurrences).", nMultipartSkips);
		}
	}

	private static void LoadFeatures(Memory<byte> shpBytes, Memory<int> recordOffsets, ShapeOordinates oord, ISpatialTransformer spatialTransform, out PolylineCollection[] polylines)
	{
		polylines = new PolylineCollection[recordOffsets.Length];
		PolylineCollection[] pl = polylines;
		LoadRawData(shpBytes, recordOffsets, oord, ShapeType.Line, (int n, int subN, Point[] pts, double[] z, double[] m) =>
		{
			PolylineCollection polylineCollection = pl[n];
			if (polylineCollection == null)
			{
				polylineCollection = new PolylineCollection();
				pl[n] = polylineCollection;
			}
			if (pts == null)
			{
				polylineCollection.Add(Polyline.NullPolyline());
			}
			else
			{
				if (spatialTransform != null)
				{
					spatialTransform.Transform(pts, pts);
				}
				polylineCollection.Add(Polyline.CreateWithBuffer(pts, z, m));
			}
		});
	}

	private static void LoadFeatures(Memory<byte> shpBytes, Memory<int> recordOffsets, ShapeOordinates oord, ISpatialTransformer spatialTransform, out Polygon[] polygons)
	{
		polygons = new Polygon[recordOffsets.Length];
		Polygon[] pl = polygons;
		int nMultipartSkips = 0;
		LoadRawData(shpBytes, recordOffsets, oord, ShapeType.Polygon, (int n, int subN, Point[] pts, double[] z, double[] m) =>
		{
			if (subN > 0)
			{
				nMultipartSkips++;
			}
			else if (pts == null)
			{
				pl[n] = Polygon.NullPolygon();
			}
			else
			{
				if (spatialTransform != null)
				{
					spatialTransform.Transform(pts, pts);
				}
				pl[n] = Polygon.CreateWithBuffer(pts, z, m);
			}
		});
		if (nMultipartSkips > 0)
		{
			_logger.LogWarning("Attempted to read multipart polygons into Polygon container - skipping internal rings. ({SkipCount}) occurrences).", nMultipartSkips);
		}
	}

	private static void LoadFeatures(Memory<byte> shpBytes, Memory<int> recordOffsets, ShapeOordinates oord, ISpatialTransformer spatialTransform, out PolygonCollection[] polygons)
	{
		polygons = new PolygonCollection[recordOffsets.Length];
		PolygonCollection[] pl = polygons;
		LoadRawData(shpBytes, recordOffsets, oord, ShapeType.Polygon, (int n, int subN, Point[] pts, double[] z, double[] m) =>
		{
			PolygonCollection polygonCollection = pl[n];
			if (polygonCollection == null)
			{
				polygonCollection = new PolygonCollection();
				pl[n] = polygonCollection;
			}
			if (pts == null)
			{
				polygonCollection.Add(Polygon.NullPolygon());
			}
			else
			{
				if (spatialTransform != null)
				{
					spatialTransform.Transform(pts, pts);
				}
				polygonCollection.Add(Polygon.CreateWithBuffer(pts, z, m));
			}
		});
	}

	private static void RecordHeader(Span<byte> shpSpan, out int recordNumber, out int shapeLength)
	{
		recordNumber = -1;
		shapeLength = IntBE(shpSpan.Slice(4)) * 2;
	}

	private static void LoadRawData(Memory<byte> shpBytes, Memory<int> records, ShapeOordinates oords, ShapeType expectedType, Action<int, int, Point[], double[], double[]> onLoad)
	{
		int length = records.Length;
		Span<int> recordsSpan = records.Span;
		Span<byte> shpBytesSpan = shpBytes.Span;
		for (int i = 0; i < length; i++)
		{
			int recordsSpanTerm = recordsSpan[i];
			RecordHeader(shpBytesSpan.Slice(recordsSpanTerm, 8), out var _, out var shapeLength);
			Span<byte> slice = shpBytesSpan.Slice(recordsSpanTerm + 8, shapeLength);
			int intLE = IntLE(slice);
			if (intLE == 0)
			{
				onLoad(i, 0, null, null, null);
				continue;
			}
			ShapeType shapeType = ShapeTypeHelper.GetShapeType(intLE);
			if (shapeType != expectedType)
			{
				_logger.LogWarning("ShapeType given ({TypeInFile}) doesn't match expected {ExpectedType}", shapeType, expectedType);
				onLoad(i, 0, null, null, null);
				continue;
			}
			int count = 1;
			int intLE2 = 0;
			int countTerm = 0;
			Span<int> intSpan;
			if (expectedType == ShapeType.Polygon || expectedType == ShapeType.Line)
			{
				count = IntLE(slice.Slice(36));
				intLE2 = IntLE(slice.Slice(40));
				intSpan = new int[count];
				for (int j = 0; j < count; j++)
				{
					intSpan[j] = IntLE(slice.Slice(44 + j * 4));
				}
				countTerm = 44 + count * 4;
			}
			else
			{
				intSpan = new int[1];
				intLE2 = IntLE(slice.Slice(36));
				countTerm = 40;
			}
			Span<byte> b = slice.Slice(countTerm);
			if (b.Length < intLE2 * 16)
			{
				_logger.LogWarning("Malformed Shapefile. Not enough bytes remaining in the shape to read point XY data.");
				onLoad(i, 0, null, null, null);
				continue;
			}
			int scaledIntLE2 = intLE2 * 16;
			int scaledIntLE22 = intLE2 * 8;
			Span<byte> trySlice = TrySlice(b, scaledIntLE2 + 16, scaledIntLE22);
			Span<byte> trySliceSpan = TrySlice(b, scaledIntLE2 + scaledIntLE22 + 32, scaledIntLE22);
			for (int k = 0; k < count; k++)
			{
				int ordinal = intSpan[k];
				int count2 = ((k != count - 1) ? (intSpan[k + 1] - ordinal) : (intLE2 - ordinal));
				if (expectedType == ShapeType.Polygon)
				{
					count2--;
				}
				Point[] pointsXY = ReadPointsXY(b.Slice(ordinal * 16), count2);
				double[] doubleValues = null;
				double[] doubleValuesArray = null;
				switch (oords)
				{
				case ShapeOordinates.XYZM:
					if (!trySlice.IsEmpty)
					{
						doubleValues = new double[count2];
						Buffers.ArrayCopyUnsafe(trySlice.Slice(ordinal * 8, count2 * 8), doubleValues);
					}
					if (!trySliceSpan.IsEmpty)
					{
						doubleValuesArray = new double[count2];
						Buffers.ArrayCopyUnsafe(trySliceSpan.Slice(ordinal * 8, count2 * 8), doubleValuesArray);
					}
					break;
				case ShapeOordinates.XYM:
					if (!trySlice.IsEmpty)
					{
						doubleValuesArray = new double[count2];
						Buffers.ArrayCopyUnsafe(trySlice.Slice(ordinal * 8, count2 * 8), doubleValuesArray);
					}
					break;
				}
				onLoad(i, k, pointsXY, doubleValues, doubleValuesArray);
			}
		}
	}

	private static void LoadPoints(Memory<byte> shpBytes, Memory<int> records, Point[] points)
	{
		int length = records.Length;
		if (points.Length != length)
		{
			throw new ArgumentException("points must be presized to the expected length.");
		}
		Span<int> recordsSpan = records.Span;
		Span<byte> shpBytesSpan = shpBytes.Span;
		for (int i = 0; i < length; i++)
		{
			int recordsSpanTerm = recordsSpan[i] + 8;
			int intLE = IntLE(shpBytesSpan.Slice(recordsSpanTerm));
			recordsSpanTerm += 4;
			if (intLE == 0)
			{
				points[i] = Point.NullPoint;
			}
			else
			{
				points[i] = new Point(DoubleLE(shpBytesSpan.Slice(recordsSpanTerm)), DoubleLE(shpBytesSpan.Slice(recordsSpanTerm + 8)));
			}
		}
	}

	private static void LoadPointsM(Memory<byte> shpBytes, Memory<int> records, Point[] points, double[] m)
	{
		int length = records.Length;
		if (points.Length != length)
		{
			throw new ArgumentException("points must be presized to the expected length.");
		}
		Span<int> recordsSpan = records.Span;
		Span<byte> shpBytesSpan = shpBytes.Span;
		for (int i = 0; i < length; i++)
		{
			int recordsSpanTerm = recordsSpan[i] + 8;
			int intLE = IntLE(shpBytesSpan.Slice(recordsSpanTerm));
			recordsSpanTerm += 4;
			if (intLE == 0)
			{
				points[i] = Point.NullPoint;
				m[i] = double.NaN;
			}
			else
			{
				points[i] = new Point(DoubleLE(shpBytesSpan.Slice(recordsSpanTerm)), DoubleLE(shpBytesSpan.Slice(recordsSpanTerm + 8)));
				m[i] = DoubleLE(shpBytesSpan.Slice(recordsSpanTerm + 16));
			}
		}
	}

	private static void LoadPointsZM(Memory<byte> shpBytes, Memory<int> records, Point[] points, double[] z, double[] m)
	{
		int length = records.Length;
		if (points.Length != length)
		{
			throw new ArgumentException("points must be presized to the expected length.");
		}
		Span<int> recordsSpan = records.Span;
		Span<byte> shpBytesSpan = shpBytes.Span;
		for (int i = 0; i < length; i++)
		{
			int recordsSpanTerm = recordsSpan[i];
			ParseRecordHeader(shpBytesSpan.Slice(recordsSpanTerm), out var _, out var contentLenBytes);
			recordsSpanTerm += 8;
			int intLE = IntLE(shpBytesSpan.Slice(recordsSpanTerm));
			recordsSpanTerm += 4;
			contentLenBytes -= 4;
			if (intLE == 0)
			{
				points[i] = Point.NullPoint;
				m[i] = double.NaN;
				z[i] = double.NaN;
				continue;
			}
			points[i] = new Point(DoubleLE(shpBytesSpan.Slice(recordsSpanTerm)), DoubleLE(shpBytesSpan.Slice(recordsSpanTerm + 8)));
			if (contentLenBytes > 16)
			{
				z[i] = DoubleLE(shpBytesSpan.Slice(recordsSpanTerm + 16));
			}
			if (contentLenBytes > 24)
			{
				m[i] = DoubleLE(shpBytesSpan.Slice(recordsSpanTerm + 24));
			}
		}
	}

	private unsafe static Point[] ReadPointsXY(Memory<byte> bytes, int pointCount)
	{
		if (BitConverter.IsLittleEndian)
		{
			Point[] points = new Point[pointCount];
			Buffers.ArrayCopyUnsafe<byte, Point>(bytes.Slice(0, pointCount * sizeof(Point)), points);
			return points;
		}
		throw new NotImplementedException();
	}

	private unsafe static Point[] ReadPointsXY(Span<byte> bytes, int pointCount)
	{
		if (BitConverter.IsLittleEndian)
		{
			Point[] points = new Point[pointCount];
			Buffers.ArrayCopyUnsafe(bytes.Slice(0, pointCount * sizeof(Point)), points);
			return points;
		}
		throw new NotImplementedException();
	}

	private static int MultpointRecordContentLength(int pointCount, bool hasZ, bool hasM)
	{
		int pointCountTerm = 40 + pointCount * 2 * 8;
		if (hasM)
		{
			pointCountTerm += 16 + pointCount * 8;
		}
		if (hasZ)
		{
			pointCountTerm += 16 + pointCount * 8;
		}
		return pointCountTerm;
	}

	private static int PolyRecordContentLength(int partCount, int pointCount, bool hasZ, bool hasM)
	{
		int partCountPointCountTerm = 44 + partCount * 4 + pointCount * 2 * 8;
		if (hasM)
		{
			partCountPointCountTerm += 16 + pointCount * 8;
		}
		if (hasZ)
		{
			partCountPointCountTerm += 16 + pointCount * 8;
		}
		return partCountPointCountTerm;
	}

	private static void PolyStats<T>(IReadOnlyList<T> polys, out bool hasZ, out bool hasM, out int maxPtCt, out Extent ext) where T : PointCollection
	{
		hasZ = false;
		hasM = false;
		maxPtCt = 0;
		ext = Extent.NullExtent;
		for (int i = 0; i < polys.Count; i++)
		{
			PointCollection pointCollection = polys[i];
			hasZ |= pointCollection.HasZ;
			hasM |= pointCollection.HasM;
			ext = ext.Union(pointCollection.Extent);
			maxPtCt = Math.Max(maxPtCt, pointCollection.Count);
		}
	}

	private static void ComplexPolyStats(IReadOnlyList<ComplexPolygon> polys, out bool hasZ, out bool hasM, out int maxPtCt, out int maxPartCount, out Extent ext)
	{
		hasZ = false;
		hasM = false;
		maxPtCt = 0;
		maxPartCount = 0;
		ext = Extent.NullExtent;
		for (int i = 0; i < polys.Count; i++)
		{
			ComplexPolygon complexPolygon = polys[i];
			for (int j = 0; j < complexPolygon.RingCount; j++)
			{
				PointCollection pointCollection = complexPolygon.Rings[j];
				hasZ |= pointCollection.HasZ;
				hasM |= pointCollection.HasM;
				ext = ext.Union(pointCollection.Extent);
			}
			maxPtCt = Math.Max(maxPtCt, complexPolygon.Points.Count);
			maxPartCount = Math.Max(maxPartCount, complexPolygon.RingCount);
		}
	}

	private static void WritePerRecordHeader(ref Span<byte> recordSlice, int recordNumber, int recordLenBytes)
	{
		WriteBE(ref recordSlice, recordNumber);
		WriteBE(ref recordSlice, recordLenBytes / 2);
	}

	private static void WriteShxEntryAndAdvance(int[,] shxOffsetWords, int index, ref int curOffsetWords, int recordContentLenBytes)
	{
		shxOffsetWords[index, 0] = curOffsetWords;
		shxOffsetWords[index, 1] = recordContentLenBytes / 2;
		curOffsetWords += (8 + recordContentLenBytes) / 2;
	}

	private static void ParseRecordHeader(ReadOnlySpan<byte> recordBytes, out int recordNumber, out int contentLenBytes)
	{
		recordNumber = IntBE(recordBytes);
		contentLenBytes = IntBE(recordBytes.Slice(4)) * 2;
	}

	private static void WriteBBox(ref Span<byte> slice, Extent ext)
	{
		BinaryPrimitives.WriteDoubleLittleEndian(slice, ext.MinX);
		slice = slice.Slice(8);
		BinaryPrimitives.WriteDoubleLittleEndian(slice, ext.MinY);
		slice = slice.Slice(8);
		BinaryPrimitives.WriteDoubleLittleEndian(slice, ext.MaxX);
		slice = slice.Slice(8);
		BinaryPrimitives.WriteDoubleLittleEndian(slice, ext.MaxY);
		slice = slice.Slice(8);
	}

	private static void WritePoint(ref Span<byte> slice, Point pt)
	{
		BinaryPrimitives.WriteDoubleLittleEndian(slice, pt.X);
		slice = slice.Slice(8);
		BinaryPrimitives.WriteDoubleLittleEndian(slice, pt.Y);
		slice = slice.Slice(8);
	}

	private static Span<byte> TrySlice(Span<byte> b, int start, int ct)
	{
		if (start + ct <= b.Length)
		{
			return b.Slice(start, ct);
		}
		return Span<byte>.Empty;
	}

	public static void AssertNoDepsExist(string shpFilename)
	{
		if (File.Exists(shpFilename))
		{
			throw new Exception(shpFilename + " already exists.");
		}
		if (File.Exists(DBFFilename(shpFilename)))
		{
			throw new Exception(shpFilename + " already exists.");
		}
		if (File.Exists(SHXFilename(shpFilename)))
		{
			throw new Exception(shpFilename + " already exists.");
		}
	}

	public static OperationResult TryAssertNoDepsExist(string shpFilename)
	{
		OperationResult operationResult = new OperationResult();
		if (File.Exists(shpFilename))
		{
			operationResult.AddFailure(FileMessageCodes.GetFileAlreadyExists(shpFilename));
		}
		string dbfFilename = DBFFilename(shpFilename);
		if (File.Exists(dbfFilename))
		{
			operationResult.AddFailure(FileMessageCodes.GetFileAlreadyExists(dbfFilename));
		}
		string shxFilename = SHXFilename(shpFilename);
		if (File.Exists(shxFilename))
		{
			operationResult.AddFailure(FileMessageCodes.GetFileAlreadyExists(shxFilename));
		}
		return operationResult;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int IntBE(ReadOnlySpan<byte> bytes)
	{
		return BinaryPrimitives.ReadInt32BigEndian(bytes);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int IntLE(ReadOnlySpan<byte> bytes)
	{
		return BinaryPrimitives.ReadInt32LittleEndian(bytes);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static double DoubleLE(ReadOnlySpan<byte> bytes)
	{
		return BinaryPrimitives.ReadDoubleLittleEndian(bytes);
	}

	private static void WriteLE(ref Span<byte> slice, int ordinal)
	{
		BinaryPrimitives.WriteInt32LittleEndian(slice, ordinal);
		slice = slice.Slice(4);
	}

	private static void WriteBE(ref Span<byte> slice, int ordinal)
	{
		BinaryPrimitives.WriteInt32BigEndian(slice, ordinal);
		slice = slice.Slice(4);
	}

	private static void WriteBE(Span<byte> slice, int ordinal)
	{
		BinaryPrimitives.WriteInt32BigEndian(slice, ordinal);
	}

	private static void WriteLE(ref Span<byte> slice, double computed)
	{
		BinaryPrimitives.WriteDoubleLittleEndian(slice, computed);
		slice = slice.Slice(8);
	}

	private static void WriteLE(ref Span<byte> slice, Span<double> values)
	{
		if (BitConverter.IsLittleEndian)
		{
			Buffers.ArrayCopyUnsafe(values, slice);
		}
		else
		{
			for (int i = 0; i < values.Length; i++)
			{
				BinaryPrimitives.WriteDoubleLittleEndian(slice.Slice(i * 8), values[i]);
			}
		}
		slice = slice.Slice(values.Length * 8);
	}

	private static void WriteBE(ref Span<byte> slice, double computed)
	{
		BinaryPrimitives.WriteDoubleBigEndian(slice, computed);
		slice = slice.Slice(8);
	}
}
