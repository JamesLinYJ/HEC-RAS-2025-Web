using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.Features;
using Geospatial.GDALAssist;
using Geospatial.IO.Network.ArcGisRest;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Memory;
using Utility.Progress;
using Utility.Web;

namespace Geospatial.IO.Network.NLD;

public static class NLDMergedAlignments
{
	private readonly struct LayerFeature(Polyline line, TableRow attrs)
	{
		public readonly Polyline Line = line;

		public readonly TableRow Attrs = attrs;
	}

	internal readonly struct AlignmentInput(Polyline line, string alignType, long segmentId)
	{
		public readonly Polyline Line = line;

		public readonly string AlignType = alignType;

		public readonly long SegmentId = segmentId;
	}

	internal sealed class CenterlineRecord
	{
		[JsonPropertyName("segmentId")]
		public long? SegmentId { get; set; }

		[JsonPropertyName("crestWidth")]
		public double? CrestWidth { get; set; }

		[JsonPropertyName("slopeWaterside")]
		public string SlopeWaterside { get; set; }

		[JsonPropertyName("slopeLandside")]
		public string SlopeLandside { get; set; }

		[JsonPropertyName("bankside")]
		public string Bankside { get; set; }
	}

	internal sealed class Product
	{
		public long SystemId { get; }

		public string Name { get; set; }

		public Polyline SystemLine { get; private set; }

		public List<AlignmentInput> Alignments { get; } = new List<AlignmentInput>();

		public List<CenterlineRecord> Centerlines { get; } = new List<CenterlineRecord>();

		public Dictionary<long, double> FloodwallWidthBySegment { get; } = new Dictionary<long, double>();

		public Product(long systemId, string name)
		{
			SystemId = systemId;
			Name = name ?? "";
		}

		public void ConsiderSystemLine(Polyline line)
		{
			if (Polyline.IsValidPolyline(line) && (SystemLine == null || line.Length > SystemLine.Length))
			{
				SystemLine = line;
			}
		}
	}

	private sealed class MergedLine : IComparable<MergedLine>
	{
		public Polyline Line { get; }

		public double StartStation { get; }

		public string Name { get; }

		public object Width { get; }

		public object LeftSlope { get; }

		public object RightSlope { get; }

		public double EndStation => StartStation + Line.Length;

		public MergedLine(Polyline line, double startStation, string name, object width, object leftSlope, object rightSlope)
		{
			Line = line;
			StartStation = startStation;
			Name = name;
			Width = width;
			LeftSlope = leftSlope;
			RightSlope = rightSlope;
		}

		public int CompareTo(MergedLine other)
		{
			return StartStation.CompareTo(other.StartStation);
		}
	}

	private sealed class SystemProfile
	{
		private readonly double[] _stations;

		private readonly double[] _elevations;

		public int Count => _stations.Length;

		public SystemProfile(double[] stations, double[] elevations)
		{
			_stations = stations;
			_elevations = elevations;
		}

		public double InterpolateElevation(double station)
		{
			if (_stations.Length == 0)
			{
				return double.NaN;
			}
			if (station <= _stations[0])
			{
				return _elevations[0];
			}
			if (station >= _stations[^1])
			{
				return _elevations[^1];
			}
			int intervalIndex = 0;
			int lengthOffset = _stations.Length - 1;
			while (lengthOffset - intervalIndex > 1)
			{
				int ordinal = (intervalIndex + lengthOffset) / 2;
				if (_stations[ordinal] <= station)
				{
					intervalIndex = ordinal;
				}
				else
				{
					lengthOffset = ordinal;
				}
			}
			double stationStep = _stations[lengthOffset] - _stations[intervalIndex];
			if (stationStep <= 0.0)
			{
				return _elevations[intervalIndex];
			}
			double stationStationsTerm = (station - _stations[intervalIndex]) / stationStep;
			return _elevations[intervalIndex] + stationStationsTerm * (_elevations[lengthOffset] - _elevations[intervalIndex]);
		}
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("NLDMergedAlignments");

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true
	};

	private const string ApiLocalBaseUrl = "https://levees.sec.usace.army.mil/api-local/";

	private const string SystemIdField = "SYSTEM_ID";

	private const string SystemNameField = "SYSTEM_NAME";

	private const string SegmentIdField = "SEGMENT_ID";

	private const string WallWidthField = "WALL_WIDTH_FEET";

	private const string AlignTypeField = "ALIGN_TYPE";

	private const string ColSystemName = "SystemName";

	private const string ColSystemId = "SystemID";

	private const string ColName = "Name";

	private const string ColWidth = "Width";

	private const string ColLeftSlope = "LeftSlope";

	private const string ColRightSlope = "RightSlope";

	private const string TypeLevee = "levee";

	private const string TypeFloodwall = "floodwall";

	private const string TypeDataGap = "Data Gap";

	private const double GapAbsoluteTolerance = 0.1;

	private const double GapRelativeTolerance = 1E-05;

	public static async Task<PolylineFeatureCollection> QueryMergedAlignmentsAsync(Extent extent, Projection projection, bool ensureElevationData = true, bool orientToSystemLine = true, ArcGisQueryResponse alignmentLines = null, ProgressReporter reporter = null, CancellationToken ct = default(CancellationToken))
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		reporter.ReportMessage("Downloading NLD system routes...");
		ArcGisQueryResponse systemResp = (await NLDRequests.QueryByExtentAsync(NLDLayer.SystemRoutes, extent, projection, "*", null, ct)).Item2;
		ArcGisQueryResponse alignmentResp = alignmentLines;
		if (alignmentResp == null)
		{
			reporter.ReportMessage("Downloading NLD alignment lines...");
			alignmentResp = (await NLDRequests.QueryByExtentAsync(NLDLayer.AlignmentLines, extent, projection, "*", null, ct)).Item2;
		}
		reporter.ReportMessage("Downloading NLD floodwalls...");
		ArcGisQueryResponse arcGisQueryResponse = (await NLDRequests.QueryByExtentAsync(NLDLayer.Floodwalls, extent, projection, "*", null, ct)).Item2;
		ct.ThrowIfCancellationRequested();
		Dictionary<long, Product> products = BuildProducts(ToFeatures(systemResp, projection), ToFeatures(alignmentResp, projection), ToFeatures(arcGisQueryResponse, projection));
		reporter.ReportMessage("Downloading NLD levee centerline details...");
		foreach (Product product in products.Values)
		{
			ct.ThrowIfCancellationRequested();
			List<CenterlineRecord> centerlines = product.Centerlines;
			centerlines.AddRange(await FetchCenterlinesAsync(product.SystemId, ct));
		}
		reporter.ReportMessage("Building RAS Merged Alignments...");
		return BuildMergedAlignments(products.Values, ensureElevationData, orientToSystemLine);
	}

	internal static PolylineFeatureCollection BuildMergedAlignments(IEnumerable<Product> products, bool ensureElevationData, bool orientToSystemLine)
	{
		PolylineCollection polylineCollection = new PolylineCollection();
		Table table = BuildOutputTable();
		foreach (Product product in products)
		{
			AppendProduct(product, polylineCollection, table, ensureElevationData, orientToSystemLine);
		}
		return new PolylineFeatureCollection(polylineCollection, table);
	}

	private static Table BuildOutputTable()
	{
		Table table = new Table();
		table.AddColumn("SystemName", typeof(string));
		table.AddColumn("SystemID", typeof(long));
		table.AddColumn("Name", typeof(string));
		table.AddColumn("Width", typeof(float));
		table.AddColumn("LeftSlope", typeof(float));
		table.AddColumn("RightSlope", typeof(float));
		return table;
	}

	private static List<LayerFeature> ToFeatures(ArcGisQueryResponse response, Projection targetProjection)
	{
		List<LayerFeature> layerFeatures = new List<LayerFeature>();
		if (response?.Features == null)
		{
			return layerFeatures;
		}
		ArcGisRestConversions.TryGetResponseProjectionOrDefault(response, out var projection);
		ArcGisRestConversions.TryConvertToPolylines(response, projection, targetProjection, out var polylines, out var attributeTable);
		for (int i = 0; i < polylines.Count; i++)
		{
			layerFeatures.Add(new LayerFeature(polylines[i], attributeTable[i]));
		}
		return layerFeatures;
	}

	private static Dictionary<long, Product> BuildProducts(List<LayerFeature> systems, List<LayerFeature> alignments, List<LayerFeature> floodwalls)
	{
		Dictionary<long, Product> products = new Dictionary<long, Product>();
		foreach (LayerFeature system in systems)
		{
			if (system.Line.IsValidPolyline() && TryReadSystemId(system.Attrs, out var systemId))
			{
				string attr = ReadAttr(system.Attrs, "SYSTEM_NAME", "");
				if (!products.TryGetValue(systemId, out var productsSystemIdTerm))
				{
					productsSystemIdTerm = (products[systemId] = new Product(systemId, attr));
				}
				productsSystemIdTerm.ConsiderSystemLine(system.Line);
				if (string.IsNullOrEmpty(productsSystemIdTerm.Name) && !string.IsNullOrEmpty(attr))
				{
					productsSystemIdTerm.Name = attr;
				}
			}
		}
		foreach (LayerFeature alignment in alignments)
		{
			if (alignment.Line.IsValidPolyline() && TryGetProduct(alignment, products, out var product2))
			{
				string alignType = ReadAttr(alignment.Attrs, "ALIGN_TYPE", "");
				long segmentId = ReadSegmentId(alignment.Attrs);
				product2.Alignments.Add(new AlignmentInput(alignment.Line, alignType, segmentId));
			}
		}
		foreach (LayerFeature floodwall in floodwalls)
		{
			if (!TryGetProduct(floodwall, products, out var product3))
			{
				continue;
			}
			long segmentId2 = ReadSegmentId(floodwall.Attrs);
			if (segmentId2 != 0L)
			{
				double floodwallWidthBySegment = ReadAttr(floodwall.Attrs, "WALL_WIDTH_FEET", double.NaN);
				if (!double.IsNaN(floodwallWidthBySegment) && floodwallWidthBySegment > 0.0 && !product3.FloodwallWidthBySegment.ContainsKey(segmentId2))
				{
					product3.FloodwallWidthBySegment[segmentId2] = floodwallWidthBySegment;
				}
			}
		}
		return products;
	}

	private static void AppendProduct(Product product, PolylineCollection outLines, Table outTable, bool ensureElevation, bool orient)
	{
		Polyline systemLine = product.SystemLine;
		if (!Polyline.IsValidPolyline(systemLine))
		{
			return;
		}
		double[] cumulativeSegmentLengths = systemLine.CumulativeSegmentLengths();
		SystemProfile systemProfile = BuildProfile(systemLine);
		Dictionary<long, CenterlineRecord> leveeLookup = BuildLeveeLookup(product.Centerlines);
		List<MergedLine> mergedLines = new List<MergedLine>();
		foreach (AlignmentInput alignment in product.Alignments)
		{
			if (!alignment.Line.IsValidPolyline())
			{
				continue;
			}
			string trim = (alignment.AlignType ?? "").Trim();
			string toLowerInvariant = trim.ToLowerInvariant();
			double naN = double.NaN;
			double leftSlope = double.NaN;
			double rightSlope = double.NaN;
			double naN2;
			if (toLowerInvariant == "levee")
			{
				if (leveeLookup.TryGetValue(alignment.SegmentId, out var localValue))
				{
					naN = localValue.CrestWidth ?? double.NaN;
					MapSlopesByBankside(localValue.Bankside, SlopeStringToDouble(localValue.SlopeWaterside), SlopeStringToDouble(localValue.SlopeLandside), out leftSlope, out rightSlope);
				}
			}
			else if (toLowerInvariant == "floodwall" && product.FloodwallWidthBySegment.TryGetValue(alignment.SegmentId, out naN2))
			{
				naN = naN2;
			}
			double startStation = OrientAndStation(alignment.Line, systemLine, cumulativeSegmentLengths, orient, ref leftSlope, ref rightSlope);
			mergedLines.Add(new MergedLine(alignment.Line, startStation, trim, ToCell(naN), ToCell(leftSlope), ToCell(rightSlope)));
		}
		mergedLines.Sort();
		FillGaps(mergedLines, systemLine);
		foreach (MergedLine mergedLine in mergedLines)
		{
			if (Polyline.IsValidPolyline(mergedLine.Line))
			{
				if (ensureElevation && systemProfile.Count > 0)
				{
					ApplyProfileZ(mergedLine.Line, mergedLine.StartStation, systemProfile);
				}
				outLines.Add(mergedLine.Line);
				outTable.AddNewRow(new object[6]
				{
					product.Name,
					product.SystemId,
					mergedLine.Name ?? "",
					mergedLine.Width,
					mergedLine.LeftSlope,
					mergedLine.RightSlope
				});
			}
		}
	}

	private static Dictionary<long, CenterlineRecord> BuildLeveeLookup(List<CenterlineRecord> centerlines)
	{
		Dictionary<long, CenterlineRecord> leveeLookup = new Dictionary<long, CenterlineRecord>();
		foreach (CenterlineRecord centerline in centerlines)
		{
			if (!centerline.SegmentId.HasValue)
			{
				continue;
			}
			long segmentIdCenterlineTerm = centerline.SegmentId.Value;
			if (!leveeLookup.TryGetValue(segmentIdCenterlineTerm, out var localValue))
			{
				leveeLookup[segmentIdCenterlineTerm] = Normalize(centerline);
				continue;
			}
			if (!(localValue.CrestWidth > 0.0) && centerline.CrestWidth > 0.0)
			{
				localValue.CrestWidth = centerline.CrestWidth;
			}
			if (double.IsNaN(SlopeStringToDouble(localValue.SlopeWaterside)) && !double.IsNaN(SlopeStringToDouble(centerline.SlopeWaterside)))
			{
				localValue.SlopeWaterside = centerline.SlopeWaterside;
				localValue.SlopeLandside = centerline.SlopeLandside;
				localValue.Bankside = centerline.Bankside;
			}
		}
		return leveeLookup;
		static CenterlineRecord Normalize(CenterlineRecord r)
		{
			return new CenterlineRecord
			{
				SegmentId = r.SegmentId,
				CrestWidth = ((r.CrestWidth > 0.0) ? r.CrestWidth : ((double?)null)),
				SlopeWaterside = r.SlopeWaterside,
				SlopeLandside = r.SlopeLandside,
				Bankside = r.Bankside
			};
		}
	}

	private static void MapSlopesByBankside(string bankside, double watersideSlope, double landsideSlope, out double leftSlope, out double rightSlope)
	{
		if ((bankside ?? "").ToLowerInvariant().Contains("right descending"))
		{
			leftSlope = landsideSlope;
			rightSlope = watersideSlope;
		}
		else
		{
			leftSlope = watersideSlope;
			rightSlope = landsideSlope;
		}
	}

	private static void FillGaps(List<MergedLine> all, Polyline systemLine)
	{
		double length = systemLine.Length;
		if (all.Count == 0)
		{
			all.Add(new MergedLine(systemLine.Clone(), 0.0, "Data Gap", null, null, null));
			return;
		}
		double scaledLength = Math.Max(0.1, length * 1E-05);
		List<MergedLine> mergedLines = new List<MergedLine>(all.Count);
		double endStationAll2Term = 0.0;
		foreach (MergedLine all2 in all)
		{
			if (all2.StartStation - endStationAll2Term > scaledLength)
			{
				Polyline polyline = systemLine.Subsection(endStationAll2Term, all2.StartStation);
				if (Polyline.IsValidPolyline(polyline))
				{
					mergedLines.Add(new MergedLine(polyline, endStationAll2Term, "Data Gap", null, null, null));
				}
			}
			mergedLines.Add(all2);
			endStationAll2Term = Math.Max(endStationAll2Term, all2.EndStation);
		}
		if (length - endStationAll2Term > scaledLength)
		{
			Polyline polyline2 = systemLine.Subsection(endStationAll2Term, length);
			if (Polyline.IsValidPolyline(polyline2))
			{
				mergedLines.Add(new MergedLine(polyline2, endStationAll2Term, "Data Gap", null, null, null));
			}
		}
		all.Clear();
		all.AddRange(mergedLines);
	}

	private static double OrientAndStation(Polyline line, Polyline systemLine, double[] cumulativeSegmentLengths, bool orient, ref double leftSlope, ref double rightSlope)
	{
		double length = line.Length;
		double stationOf = StationOf(systemLine, line.PointAtDistance(length * 0.2).Point, cumulativeSegmentLengths);
		double stationOf2 = StationOf(systemLine, line.PointAtDistance(length * 0.8).Point, cumulativeSegmentLengths);
		if (orient && stationOf2 < stationOf)
		{
			line.Reverse();
			double rightSlope2 = rightSlope;
			double leftSlope2 = leftSlope;
			leftSlope = rightSlope2;
			rightSlope = leftSlope2;
			stationOf = stationOf2;
		}
		double stationOf3 = StationOf(systemLine, line.First(), cumulativeSegmentLengths);
		double stationOf4 = StationOf(systemLine, line.Last(), cumulativeSegmentLengths);
		if (stationOf3 > stationOf4)
		{
			if (stationOf4 < stationOf)
			{
				stationOf4 = line.Length;
			}
			else
			{
				stationOf3 = 0.0;
			}
		}
		return stationOf3;
	}

	private static double StationOf(Polyline systemLine, Point pt, double[] cumulativeSegmentLengths)
	{
		PointOnPolyline ptOnPoly = systemLine.NearestPointOnPolyline(pt);
		return systemLine.DistanceAlongFast(ptOnPoly, ref cumulativeSegmentLengths);
	}

	private static SystemProfile BuildProfile(Polyline systemLine)
	{
		List<double> doubleList = new List<double>();
		List<double> doubleListList = new List<double>();
		if (systemLine.HasZ)
		{
			double[] cumulativePointStations = systemLine.CumulativePointStations();
			for (int i = 0; i < systemLine.PointCount; i++)
			{
				double systemLineTerm = systemLine.Z[i];
				if (!double.IsNaN(systemLineTerm) && systemLineTerm != 0.0)
				{
					doubleList.Add(cumulativePointStations[i]);
					doubleListList.Add(systemLineTerm);
				}
			}
		}
		return new SystemProfile(doubleList.ToArray(), doubleListList.ToArray());
	}

	private static void ApplyProfileZ(Polyline line, double startStation, SystemProfile profile)
	{
		double[] cumulativePointStations = line.CumulativePointStations();
		double[] doubleValues = new double[line.PointCount];
		for (int i = 0; i < line.PointCount; i++)
		{
			doubleValues[i] = profile.InterpolateElevation(startStation + cumulativePointStations[i]);
		}
		line.TrackZ(doubleValues);
	}

	private static object ToCell(double computed)
	{
		if (!double.IsNaN(computed) && !(computed <= 0.0))
		{
			return (float)computed;
		}
		return null;
	}

	private static bool TryGetProduct(LayerFeature feature, Dictionary<long, Product> products, out Product product)
	{
		product = null;
		if (TryReadSystemId(feature.Attrs, out var systemId))
		{
			return products.TryGetValue(systemId, out product);
		}
		return false;
	}

	private static bool TryReadSystemId(TableRow row, out long systemId)
	{
		systemId = 0L;
		double attr = ReadAttr(row, "SYSTEM_ID", double.NaN);
		if (double.IsNaN(attr))
		{
			return false;
		}
		systemId = (long)Math.Round(attr);
		return true;
	}

	private static long ReadSegmentId(TableRow row)
	{
		double attr = ReadAttr(row, "SEGMENT_ID", double.NaN);
		if (!double.IsNaN(attr))
		{
			return (long)Math.Round(attr);
		}
		return 0L;
	}

	private static T ReadAttr<T>(TableRow row, string column, T fallback)
	{
		if (row?.Table != null && row.Table.TryGetColumnIndex(column, out var idx))
		{
			return row.ValueAs(idx, fallback);
		}
		return fallback;
	}

	private static double SlopeStringToDouble(string slopeStr)
	{
		if (string.IsNullOrEmpty(slopeStr))
		{
			return double.NaN;
		}
		string[] parts = slopeStr.Split(':');
		if (parts.Length == 0)
		{
			return double.NaN;
		}
		if (parts[0].Contains("V") && parts.Length > 1)
		{
			parts[0] = parts[1];
		}
		if (double.TryParse(parts[0].ToLowerInvariant().Replace("h", ""), out var result) && !double.IsNaN(result))
		{
			return result;
		}
		return double.NaN;
	}

	private static async Task<List<CenterlineRecord>> FetchCenterlinesAsync(long systemId, CancellationToken ct)
	{
		string requestUri = $"{"https://levees.sec.usace.army.mil/api-local/"}centerlines?system_id={systemId}";
		try
		{
			HttpResponseMessage httpResponseMessage = await SharedWebResourceManager.Client.GetAsync(requestUri, ct);
			httpResponseMessage.EnsureSuccessStatusCode();
			return JsonSerializer.Deserialize<List<CenterlineRecord>>(await httpResponseMessage.Content.ReadAsStringAsync(ct), JsonOptions) ?? new List<CenterlineRecord>();
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Failed to fetch NLD centerlines for system {SystemId}", systemId);
			return new List<CenterlineRecord>();
		}
	}
}
