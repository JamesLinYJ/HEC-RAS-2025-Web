using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Geospatial.GDALAssist;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;
using TiffAssist;
using Utility.Progress;

namespace RasMapperLib;

public class PolygonFeatureLayer : FeatureLayer
{
	private class ArbitraryLine
	{
		public object Payload;

		public PointM A
		{
			get
			{
				if (Payload is Polyline)
				{
					return ((Polyline)Payload).FirstPointM();
				}
				if (Payload is SegmentM)
				{
					object payload = Payload;
					return ((payload != null) ? ((SegmentM)payload) : default(SegmentM)).A;
				}
				throw new NotImplementedException();
			}
		}

		public List<PointM> AOrAllButLastPoint
		{
			get
			{
				if (Payload is Polyline)
				{
					PointMs pointMs = ((Polyline)Payload).CopyPoints();
					pointMs.RemoveAt(checked(((Polyline)Payload).PointMCount() - 1));
					return pointMs;
				}
				if (Payload is SegmentM)
				{
					List<PointM> pointMsList = new List<PointM>();
					object payload = Payload;
					pointMsList.Add(((payload != null) ? ((SegmentM)payload) : default(SegmentM)).A);
					return pointMsList;
				}
				throw new NotImplementedException();
			}
		}

		public PointM B
		{
			get
			{
				if (Payload is Polyline)
				{
					return ((Polyline)Payload).LastPointM();
				}
				if (Payload is SegmentM)
				{
					object payload = Payload;
					return ((payload != null) ? ((SegmentM)payload) : default(SegmentM)).B;
				}
				throw new NotImplementedException();
			}
		}

		public ArbitraryLine(object payLoad)
		{
			if (payLoad is SegmentM || payLoad is Polyline)
			{
				Payload = RuntimeHelpers.GetObjectValue(payLoad);
				return;
			}
			throw new NotImplementedException("Only works for segmentM and polyline");
		}

		public override int GetHashCode() => HashCode.Combine(A.X, A.Y, B.X, B.Y);

		public override bool Equals(object other)
		{
			if (other is not ArbitraryLine arbitraryLine) return false;
			if (A.EqualXY(arbitraryLine.A))
			{
				return B.EqualXY(arbitraryLine.B);
			}
			return false;
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__38_002D0
	{
		public List<SegmentM> segmentList;

		public SpatialIndex<int> spIdx;

		public List<Feature> allBinsPolylines;

		public _Closure_0024__38_002D0(_Closure_0024__38_002D0 other)
		{
			if (other != null)
			{
				segmentList = other.segmentList;
				spIdx = other.spIdx;
				allBinsPolylines = other.allBinsPolylines;
			}
		}

		[SpecialName]
		internal ArbitraryLine _Lambda_0024__1(int idx)
		{
			Feature payLoad = allBinsPolylines[idx];
			return new ArbitraryLine(payLoad);
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__38_002D1
	{
		public int binIdx;

		public _Closure_0024__38_002D0 outerClosure;

		public _Closure_0024__38_002D1(_Closure_0024__38_002D1 other)
		{
			if (other != null)
			{
				binIdx = other.binIdx;
			}
		}

		[SpecialName]
		internal ArbitraryLine _Lambda_0024__0(int idx)
		{
			SegmentM segmentM = outerClosure.segmentList[outerClosure.spIdx.Bins[binIdx][idx]];
			return new ArbitraryLine(segmentM);
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__39_002D0
	{
		public Dictionary<PointM, int> startingPtToPolylineIdx;

		public Dictionary<PointM, List<ArbitraryLine>> pointDictionary;

		public _Closure_0024__39_002D0(_Closure_0024__39_002D0 other)
		{
			if (other != null)
			{
				startingPtToPolylineIdx = other.startingPtToPolylineIdx;
				pointDictionary = other.pointDictionary;
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__39_002D1
	{
		public PointM curPoint;

		public List<PointM> localPolyLinesSmallestY;

		public List<int> localPolyLinesSmallestYIdx;

		public PointMs pointList;

		public List<Feature> localPolyLinesList;

		public PointM smallestYPoint;

		public int smallestYPointIdx;

		public bool makeNewPolyline;

		public _Closure_0024__39_002D0 outerClosure;

		public _Closure_0024__39_002D1(_Closure_0024__39_002D1 other)
		{
			if (other != null)
			{
				curPoint = other.curPoint;
				localPolyLinesSmallestY = other.localPolyLinesSmallestY;
				localPolyLinesSmallestYIdx = other.localPolyLinesSmallestYIdx;
				pointList = other.pointList;
				localPolyLinesList = other.localPolyLinesList;
				smallestYPoint = other.smallestYPoint;
				smallestYPointIdx = other.smallestYPointIdx;
				makeNewPolyline = other.makeNewPolyline;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0()
		{
			if (outerClosure.startingPtToPolylineIdx.ContainsKey(curPoint))
			{
				PointM pointM = localPolyLinesSmallestY[outerClosure.startingPtToPolylineIdx[curPoint]];
				int localPolyLinesSmallestYIdx = this.localPolyLinesSmallestYIdx[outerClosure.startingPtToPolylineIdx[curPoint]];
				int count = pointList.Count;
				pointList.AddRange(((Polyline)localPolyLinesList[outerClosure.startingPtToPolylineIdx[curPoint]]).CopyPoints());
				int startingPtToPolylineIdx = outerClosure.startingPtToPolylineIdx[curPoint];
				localPolyLinesList[startingPtToPolylineIdx] = new Polyline(pointList);
				smallestYPoint = CompareSmallestYPoint(pointM, smallestYPoint);
				if (smallestYPoint.EqualXY(pointM))
				{
					smallestYPointIdx = checked(localPolyLinesSmallestYIdx + count);
				}
				localPolyLinesSmallestY[startingPtToPolylineIdx] = smallestYPoint;
				this.localPolyLinesSmallestYIdx[startingPtToPolylineIdx] = smallestYPointIdx;
				outerClosure.startingPtToPolylineIdx.Remove(curPoint);
				outerClosure.startingPtToPolylineIdx.Add(pointList.First(), startingPtToPolylineIdx);
				makeNewPolyline = false;
				return true;
			}
			return false;
		}

		[SpecialName]
		internal void _Lambda_0024__1(int idx)
		{
			outerClosure.pointDictionary[curPoint].RemoveAt(idx);
			if (outerClosure.pointDictionary[curPoint].Count > 0)
			{
				outerClosure.pointDictionary[curPoint].RemoveAt(idx);
			}
		}
	}

	private const string sArea = "Area";

	private const string sCount = "Count";

	private const string sLength = "Length";

	private const string sParts = "Parts";

	private const string sDevelopmentTin = "Development TIN";

	public override string SimpleFeatureTypeName { get; set; }

	public Polygon this[int index] => Polygon(index);

	public PolygonFeatureLayer(string name, bool forwardEditRequestsToParent)
		: base(name, forwardEditRequestsToParent)
	{
		SimpleFeatureTypeName = "Polygon";
	}

	public PolygonFeatureLayer()
		: base("Polygon Layer")
	{
		SimpleFeatureTypeName = "Polygon";
	}

	public PolygonFeatureLayer(string name)
		: base(name)
	{
		SimpleFeatureTypeName = "Polygon";
	}

	public PolygonFeatureLayer(string name, string filename)
		: base(name, filename)
	{
		SimpleFeatureTypeName = "Polygon";
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddAttributeColumn("Count", typeof(int), _featureColumnGroup, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Length", typeof(double), _featureColumnGroup, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Area", typeof(double), _featureColumnGroup, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Development TIN", typeof(DevelopmentTin), null, DataColumnViewState.Invisible);
	}

	public override List<PlotOption> GetStaticPlotOptions()
	{
		List<PlotOption> staticPlotOptions = base.GetStaticPlotOptions();
		staticPlotOptions.AddRange(RasMapperLib.Polygon.GetAvailablePlotOptions(SharedData.DebugMode));
		return staticPlotOptions;
	}

	public IEnumerable<Polygon> Polygons()
	{
		foreach (Polygon polygon in FeaturesOfType<Polygon>())
		{
			yield return polygon;
		}
	}

	public DataRow AddFeature(Polygon poly)
	{
		return AddFeature((Feature)poly);
	}

	public Polygon Polygon(int fid)
	{
		if (!(Feature(fid) is Polygon result))
		{
			return (Polygon)ResolveNullFeature();
		}
		return result;
	}

	public override Feature ResolveNullFeature()
	{
		return RasMapperLib.Polygon.NullPolygon();
	}

	protected void EnsurePositivePolygonOrientations()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Polygon polygon = Polygon(i);
				if (polygon.Area < 0.0)
				{
					SetFeature(i, polygon.Reverse());
				}
			}
		}
	}

	public DevelopmentTin GetDevelopmentTin(int fid)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(FeatureRow(fid)["Development TIN"]);
		if (objectValue == null || Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
		{
			DevelopmentTin developmentTin = ComputeDevelopmentTIN(Polygon(fid));
			SetDevelopmentTin(fid, developmentTin);
			return developmentTin;
		}
		return objectValue as DevelopmentTin;
	}

	private void SetDevelopmentTin(int fid, DevelopmentTin devTin)
	{
		FeatureRow(fid)["Development TIN"] = devTin;
	}

	public static DevelopmentTin ComputeDevelopmentTIN(Polygon pgon)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		if (pgon == null || pgon.Count <= 2)
		{
			return null;
		}
		double areaTolerance = GetAreaTolerance(pgon);
		DevelopmentTin result = new DelaunayTINFactory(areaTolerance).ComputeTIN(pgon, null, null, null, removeDeletedTriangles: false);
		stopwatch.Stop();
		return result;
	}

	public SpatialIndex<int> GetSegmentSpatialIndex(int fid)
	{
		Polygon polygon = Polygon(fid);
		if (!RasMapperLib.Polygon.IsValidPolygon(polygon))
		{
			return null;
		}
		return polygon.GenerateSegmentSpatialIndex();
	}

	public double GetAreaTolerance(int fid)
	{
		return GetAreaTolerance(Polygon(fid));
	}

	public static double GetAreaTolerance(Polygon pgon)
	{
		if (pgon == null || pgon.Count <= 2)
		{
			return double.NaN;
		}
		return System.Math.Sqrt(System.Math.Abs(pgon.Area)) / 1000000.0;
	}

	public IEnumerable<int> HitTestIndexes(PointM ptm)
	{
		return from fid in Enumerable.Range(0, FeatureCount())
			where Polygon(fid).Contains(ptm)
			select fid;
	}

	public IEnumerable<Polygon> HitTest(PointM ptm)
	{
		return from pgon in Polygons()
			where pgon?.Contains(ptm) ?? false
			select pgon;
	}

	public void SelectPolygonsUsingArea(double area, bool greaterThan)
	{
		SelectedFeatures.ClearSelection();
		List<int> intList = new List<int>();
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if ((FeatureRowFilter != null && !FeatureRowFilter(FeatureRow(i))) || !FeatureIsHitTestable(i))
				{
					continue;
				}
				double area2 = Polygon(i).Area;
				if (greaterThan)
				{
					if (area2 >= area)
					{
						intList.Add(i);
					}
				}
				else if (area2 <= area)
				{
					intList.Add(i);
				}
				SelectedFeatures.AddRange(intList);
			}
		}
	}

	public override List<Type> AvailableFeatureTypes()
	{
		return new Type[1] { typeof(Polygon) }.ToList();
	}

	protected override void ResetFeatureAttributes(int fid)
	{
		Polygon polygon = Polygon(fid);
		if (polygon == null)
		{
			FeatureRow(fid)["Count"] = 0;
			FeatureRow(fid)["Length"] = 0.0;
			FeatureRow(fid)["Area"] = 0.0;
		}
		else
		{
			FeatureRow(fid)["Count"] = polygon.Count;
			FeatureRow(fid)["Length"] = polygon.Length;
			FeatureRow(fid)["Area"] = polygon.Area;
		}
		SetDevelopmentTin(fid, null);
	}

	public PolygonFeatureLayer MergeAllPolygons()
	{
		Clipper clipper = new Clipper();
		List<IntPoint> intPoints = new List<IntPoint>();
		double computed = 1000.0;
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Feature feature = Feature(i);
				if (!(feature is Polygon))
				{
					continue;
				}
				Polygon polygon = (Polygon)feature;
				int count2 = polygon.PartsCount() - 1;
				for (int j = 0; j <= count2; j++)
				{
					intPoints.Clear();
					int partStartIndex = polygon.PartStartIndex(j);
					int partEndIndex = polygon.PartEndIndex(j);
					for (int k = partStartIndex; k <= partEndIndex; k++)
					{
						PointM pointM = polygon.PointM(k);
						intPoints.Add(new IntPoint((long)System.Math.Round(pointM.X * computed), (long)System.Math.Round(pointM.Y * computed)));
					}
					if ((i == 0) & (j == 0))
					{
						clipper.AddPath(intPoints, PolyType.ptSubject, x: true);
					}
					else
					{
						clipper.AddPath(intPoints, PolyType.ptClip, x: true);
					}
				}
			}
			List<List<IntPoint>> intPointListList = new List<List<IntPoint>>();
			try
			{
				clipper.Execute(ClipType.ctUnion, intPointListList, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				Interaction.MsgBox(ex2.Message);
				ProjectData.ClearProjectError();
			}
			if (intPointListList.Count > 0)
			{
				List<int> intList = new List<int>();
				PointMs pointMs = new PointMs();
				foreach (List<IntPoint> intPointsList in intPointListList)
				{
					if (intPointsList.Count > 0)
					{
						intList.Add(pointMs.Count);
						for (int l = intPointsList.Count - 1; l >= 0; l += -1)
						{
							IntPoint intPoint = intPointsList[l];
							pointMs.Add((double)intPoint.X / computed, (double)intPoint.Y / computed);
						}
					}
				}
				PolygonFeatureLayer polygonFeatureLayer = new PolygonFeatureLayer("Merged");
				Polygon poly = new Polygon(pointMs, intList.ToArray());
				polygonFeatureLayer.AddFeature(poly);
				return polygonFeatureLayer;
			}
			return null;
		}
	}

	public void RasterizePolygonAttributes()
	{
	}

	public void ConvertFeaturesToRaster(string rasterFilename, string attColname, Type attColType, float cellSize)
	{
		SharedData.RasMapper.StatusMessageAdd("Writing raster: " + rasterFilename);
		string stdOut = "";
		string stdErr = "";
		Executables.GdalRasterize(GdalRasterizeArgs(SourceFilename, isFloat: true, attColname, attColType, cellSize, rasterFilename), out stdOut, out stdErr);
		SharedData.RasMapper.StatusMessageAdd(stdOut);
		if (Operators.CompareString(stdErr, "", TextCompare: false) != 0)
		{
			SharedData.RasMapper.StatusMessageAdd(stdErr);
		}
		int imageWidth;
		int imageHeight;
		using (FloatTiffReader floatTiffReader = new FloatTiffReader(rasterFilename))
		{
			imageWidth = floatTiffReader.GetImageWidth(0);
			imageHeight = floatTiffReader.GetImageHeight(0);
		}
		InterpolatedLayer.AddOverlays(rasterFilename, imageWidth, imageHeight, UseNearest: true, null, null, null);
		SharedData.RasMapper.StatusMessageAdd(stdOut);
	}

	private string GdalRasterizeArgs(string inputfilename, bool isFloat, string attColname, Type attColType, float cellSize, string outputfilename)
	{
		string textValue;
		string textValue2;
		if (attColType == typeof(float) || attColType == typeof(double))
		{
			textValue = "-ot Float32";
			textValue2 = " -a_nodata -9999";
		}
		else if (attColType == typeof(byte))
		{
			textValue = "-ot BYTE";
			textValue2 = " -a_nodata 0";
		}
		else if (attColType == typeof(short))
		{
			textValue = "-ot Int16";
			textValue2 = " -a_nodata 0";
		}
		else
		{
			textValue = "-ot Int32";
			textValue2 = " -a_nodata 0";
		}
		string attColnameTerm = " -a " + attColname;
		string cellSizeTerm = " -tr " + cellSize + " " + cellSize;
		string getFileNameWithoutExtensionPathTerm = " -l " + System.IO.Path.GetFileNameWithoutExtension(inputfilename);
		inputfilename = " \"" + inputfilename + "\"";
		outputfilename = " \"" + outputfilename + "\"";
		return textValue + textValue2 + " -co COMPRESS=DEFLATE -co TILED=YES " + attColnameTerm + cellSizeTerm + getFileNameWithoutExtensionPathTerm + inputfilename + outputfilename;
	}

	public static List<Feature> MakePolygonsWithSegmentList(Extent ext, List<SegmentM> segmentList, ProgressReporter progressReporter = null)
	{
		_Closure_0024__38_002D0 capturedLocals = default;
		_Closure_0024__38_002D0 capturedLocals2 = new _Closure_0024__38_002D0(capturedLocals);
		capturedLocals2.segmentList = segmentList;
		if (progressReporter == null)
		{
			progressReporter = ProgressReporter.None();
		}
		capturedLocals2.spIdx = new SpatialIndex<int>(ext);
		checked
		{
			int count = capturedLocals2.segmentList.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				capturedLocals2.spIdx.Add(capturedLocals2.segmentList[i], i);
			}
			List<Feature> polyLinesList = new List<Feature>();
			List<PointM> polyLinesSmallestY = new List<PointM>();
			List<int> polyLinesSmallestYIdx = new List<int>();
			List<Feature> features = new List<Feature>();
			int count2 = capturedLocals2.spIdx.Bins.Length - 1;
			_Closure_0024__38_002D1 capturedLocals3 = default;
			for (int j = 0; j <= count2; j++)
			{
				capturedLocals3 = new _Closure_0024__38_002D1(capturedLocals3);
				capturedLocals3.outerClosure = capturedLocals2;
				capturedLocals3.binIdx = j;
				progressReporter.ReportMessage("Working on section " + (capturedLocals3.binIdx + 1) + " of " + capturedLocals3.outerClosure.spIdx.Bins.Length);
				GeneratePolygonsFromLines(capturedLocals3._Lambda_0024__0, capturedLocals3.outerClosure.spIdx.Bins[j].Count, ref polyLinesList, ref polyLinesSmallestY, ref polyLinesSmallestYIdx, features);
				progressReporter.ReportProgress((int)System.Math.Round((double)(70 * capturedLocals3.binIdx) / (double)capturedLocals3.outerClosure.spIdx.Bins.Length - 1.0));
			}
			capturedLocals2.allBinsPolylines = new List<Feature>(polyLinesList);
			Func<int, ArbitraryLine> getLineFunc = [SpecialName] (int idx) =>
			{
				Feature payLoad = capturedLocals2.allBinsPolylines[idx];
				return new ArbitraryLine(payLoad);
			};
			progressReporter.ReportMessage("Combining lines from all sections");
			GeneratePolygonsFromLines(getLineFunc, capturedLocals2.allBinsPolylines.Count, ref polyLinesList, ref polyLinesSmallestY, ref polyLinesSmallestYIdx, features);
			progressReporter.ReportProgress(100);
			return features;
		}
	}

	private static void GeneratePolygonsFromLines(Func<int, ArbitraryLine> getLineFunc, int lineListCount, ref List<Feature> polyLinesList, ref List<PointM> polyLinesSmallestY, ref List<int> polyLinesSmallestYIdx, List<Feature> polygonList)
	{
		_Closure_0024__39_002D0 capturedLocals = new _Closure_0024__39_002D0(null);
		_ = polyLinesList.Count;
		capturedLocals.pointDictionary = new Dictionary<PointM, List<ArbitraryLine>>();
		checked
		{
			int count = lineListCount - 1;
			for (int i = 0; i <= count; i++)
			{
				ArbitraryLine arbitraryLine = getLineFunc(i);
				if (!capturedLocals.pointDictionary.ContainsKey(arbitraryLine.A))
				{
					capturedLocals.pointDictionary[arbitraryLine.A] = new List<ArbitraryLine>();
					capturedLocals.pointDictionary[arbitraryLine.A].Add(arbitraryLine);
				}
				else if (!capturedLocals.pointDictionary[arbitraryLine.A].Contains(arbitraryLine))
				{
					capturedLocals.pointDictionary[arbitraryLine.A].Add(arbitraryLine);
				}
				if (!capturedLocals.pointDictionary.ContainsKey(arbitraryLine.B))
				{
					capturedLocals.pointDictionary[arbitraryLine.B] = new List<ArbitraryLine>();
					capturedLocals.pointDictionary[arbitraryLine.B].Add(arbitraryLine);
				}
				else if (!capturedLocals.pointDictionary[arbitraryLine.B].Contains(arbitraryLine))
				{
					capturedLocals.pointDictionary[arbitraryLine.B].Add(arbitraryLine);
				}
			}
			capturedLocals.startingPtToPolylineIdx = new Dictionary<PointM, int>();
			int unity = -1;
			_Closure_0024__39_002D1 capturedLocals2 = default;
			foreach (KeyValuePair<PointM, List<ArbitraryLine>> localValue in capturedLocals.pointDictionary)
			{
				capturedLocals2 = new _Closure_0024__39_002D1(capturedLocals2);
				capturedLocals2.outerClosure = capturedLocals;
				unity++;
				capturedLocals2.pointList = new PointMs();
				capturedLocals2.smallestYPoint = PointM.NullPoint();
				capturedLocals2.smallestYPointIdx = -1;
				capturedLocals2.curPoint = localValue.Key;
				capturedLocals2.makeNewPolyline = true;
				capturedLocals2.localPolyLinesSmallestY = polyLinesSmallestY;
				capturedLocals2.localPolyLinesSmallestYIdx = polyLinesSmallestYIdx;
				capturedLocals2.localPolyLinesList = polyLinesList;
				Func<bool> func = capturedLocals2._Lambda_0024__0;
				if (!func() && capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint].Count == 0)
				{
					continue;
				}
				if (capturedLocals2.makeNewPolyline)
				{
					do
					{
						Action<int> action = capturedLocals2._Lambda_0024__1;
						if (func())
						{
							break;
						}
						ArbitraryLine arbitraryLine2 = null;
						if (capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint].Count <= 2)
						{
							if (capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint].Count > 0 && capturedLocals2.curPoint.EqualXY(capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][0].A))
							{
								arbitraryLine2 = capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][0];
								action(0);
							}
							else
							{
								if (capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint].Count <= 1 || !capturedLocals2.curPoint.EqualXY(capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][1].A))
								{
									break;
								}
								arbitraryLine2 = capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][1];
								action(0);
							}
						}
						else if (capturedLocals2.pointList.Count == 0)
						{
							if (capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint].Count > 0 && capturedLocals2.curPoint.EqualXY(capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][0].A))
							{
								arbitraryLine2 = capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][0];
							}
							else if (capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint].Count > 1 && capturedLocals2.curPoint.EqualXY(capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][1].A))
							{
								arbitraryLine2 = capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][1];
							}
							action(0);
						}
						else if (arbitraryLine2.A.Y < arbitraryLine2.B.Y)
						{
							if (capturedLocals2.curPoint.EqualXY(capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][0].A) && capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][0].B.X < capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][0].A.X)
							{
								arbitraryLine2 = capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][0];
								action(0);
							}
							else if (capturedLocals2.curPoint.EqualXY(capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][1].A) && capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][1].B.X < capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][1].A.X)
							{
								arbitraryLine2 = capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][1];
								action(0);
							}
							else if (capturedLocals2.curPoint.EqualXY(capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][2].A) && capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][2].B.X < capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][2].A.X)
							{
								arbitraryLine2 = capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][2];
								action(2);
							}
							else
							{
								arbitraryLine2 = capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][3];
								action(2);
							}
						}
						else if (arbitraryLine2.A.Y > arbitraryLine2.B.Y)
						{
							if (capturedLocals2.curPoint.EqualXY(capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][0].A) && capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][0].B.X > capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][0].A.X)
							{
								arbitraryLine2 = capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][0];
								action(0);
							}
							else if (capturedLocals2.curPoint.EqualXY(capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][1].A) && capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][1].B.X > capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][1].A.X)
							{
								arbitraryLine2 = capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][1];
								action(0);
							}
							else if (capturedLocals2.curPoint.EqualXY(capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][2].A) && capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][2].B.X > capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][2].A.X)
							{
								arbitraryLine2 = capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][2];
								action(2);
							}
							else
							{
								arbitraryLine2 = capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][3];
								action(2);
							}
						}
						else if (arbitraryLine2.A.X < arbitraryLine2.B.X)
						{
							if (capturedLocals2.curPoint.EqualXY(capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][0].A) && capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][0].B.Y > capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][0].A.Y)
							{
								arbitraryLine2 = capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][0];
								action(0);
							}
							else if (capturedLocals2.curPoint.EqualXY(capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][1].A) && capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][1].B.Y > capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][1].A.Y)
							{
								arbitraryLine2 = capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][1];
								action(0);
							}
							else if (capturedLocals2.curPoint.EqualXY(capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][2].A) && capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][2].B.Y > capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][2].A.Y)
							{
								arbitraryLine2 = capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][2];
								action(2);
							}
							else
							{
								arbitraryLine2 = capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][3];
								action(2);
							}
						}
						else if (arbitraryLine2.A.X > arbitraryLine2.B.X)
						{
							if (capturedLocals2.curPoint.EqualXY(capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][0].A) && capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][0].B.Y < capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][0].A.Y)
							{
								arbitraryLine2 = capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][0];
								action(0);
							}
							else if (capturedLocals2.curPoint.EqualXY(capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][1].A) && capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][1].B.Y < capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][1].A.Y)
							{
								arbitraryLine2 = capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][1];
								action(0);
							}
							else if (capturedLocals2.curPoint.EqualXY(capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][2].A) && capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][2].B.Y < capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][2].A.Y)
							{
								arbitraryLine2 = capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][2];
								action(2);
							}
							else
							{
								arbitraryLine2 = capturedLocals2.outerClosure.pointDictionary[capturedLocals2.curPoint][3];
								action(2);
							}
						}
						List<PointM> aOrAllButLastPoint = arbitraryLine2.AOrAllButLastPoint;
						if (aOrAllButLastPoint.Count == 1 && capturedLocals2.pointList.Count >= 2)
						{
							PointM pointM = capturedLocals2.pointList[capturedLocals2.pointList.Count - 2];
							PointM pointM2 = capturedLocals2.pointList[capturedLocals2.pointList.Count - 1];
							PointM aOrAllButLastPoint2 = aOrAllButLastPoint[0];
							if ((pointM2.X - pointM.X) * (aOrAllButLastPoint2.Y - pointM2.Y) - (pointM2.Y - pointM.Y) * (aOrAllButLastPoint2.X - pointM2.X) == 0.0)
							{
								capturedLocals2.pointList.RemoveAt(capturedLocals2.pointList.Count - 1);
							}
							capturedLocals2.pointList.Add(aOrAllButLastPoint2);
						}
						else
						{
							capturedLocals2.pointList.AddRange(arbitraryLine2.AOrAllButLastPoint);
						}
						if (arbitraryLine2.Payload is SegmentM)
						{
							capturedLocals2.smallestYPoint = CompareSmallestYPoint(capturedLocals2.pointList.Last(), capturedLocals2.smallestYPoint);
							if (capturedLocals2.smallestYPoint.EqualXY(capturedLocals2.pointList.Last()))
							{
								capturedLocals2.smallestYPointIdx = capturedLocals2.pointList.Count - 1;
							}
						}
						else
						{
							int indexOf = polyLinesList.IndexOf((Feature)arbitraryLine2.Payload);
							if (indexOf != -1)
							{
								capturedLocals2.smallestYPoint = CompareSmallestYPoint(polyLinesSmallestY[indexOf], capturedLocals2.smallestYPoint);
								if (capturedLocals2.smallestYPoint.EqualXY(polyLinesSmallestY[indexOf]))
								{
									capturedLocals2.smallestYPointIdx = polyLinesSmallestYIdx[indexOf];
								}
							}
						}
						capturedLocals2.curPoint = arbitraryLine2.B;
					}
					while (!capturedLocals2.curPoint.EqualXY(localValue.Key));
				}
				if (capturedLocals2.curPoint.EqualXY(localValue.Key))
				{
					if (capturedLocals2.pointList.Count > 2)
					{
						capturedLocals2.pointList.Add(capturedLocals2.curPoint);
						int countOffset = capturedLocals2.smallestYPointIdx - 1;
						if (countOffset < 0)
						{
							countOffset = capturedLocals2.pointList.Count - 2;
						}
						PointM a = capturedLocals2.smallestYPoint;
						PointM b = capturedLocals2.pointList[countOffset];
						PointM c = capturedLocals2.pointList[capturedLocals2.smallestYPointIdx + 1];
						double crossProduct = Vector.CrossProduct(a, b, c);
						PointM pointM5 = capturedLocals2.pointList[1];
						PointM pointM3 = capturedLocals2.pointList[0];
						PointM pointM4 = capturedLocals2.pointList[capturedLocals2.pointList.Count - 2];
						if ((pointM3.X - pointM5.X) * (pointM4.Y - pointM3.Y) - (pointM3.Y - pointM5.Y) * (pointM4.X - pointM3.X) == 0.0)
						{
							capturedLocals2.pointList.RemoveAt(capturedLocals2.pointList.Count - 1);
							capturedLocals2.pointList.RemoveAt(0);
							capturedLocals2.pointList.Add(pointM5);
						}
						Polygon polygon = new Polygon(capturedLocals2.pointList);
						if (polygon.Area < 0.0)
						{
							polygonList.Add(polygon);
						}
					}
				}
				else if (capturedLocals2.makeNewPolyline)
				{
					capturedLocals2.outerClosure.startingPtToPolylineIdx.Add(localValue.Key, polyLinesList.Count);
					polyLinesList.Add(new Polyline(capturedLocals2.pointList));
					polyLinesSmallestY.Add(capturedLocals2.smallestYPoint);
					polyLinesSmallestYIdx.Add(capturedLocals2.smallestYPointIdx);
				}
			}
		}
	}

	private static PointM CompareSmallestYPoint(PointM pt1, PointM currentSmallestYPoint)
	{
		if (currentSmallestYPoint.IsNull() || pt1.Y <= currentSmallestYPoint.Y)
		{
			if (pt1.Y == currentSmallestYPoint.Y && pt1.X < currentSmallestYPoint.X)
			{
				return currentSmallestYPoint;
			}
			currentSmallestYPoint = pt1;
		}
		return currentSmallestYPoint;
	}
}
