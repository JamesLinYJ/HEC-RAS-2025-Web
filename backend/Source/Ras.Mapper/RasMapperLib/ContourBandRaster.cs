using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Scripting;
using RasMapperLib.Utilities;
using Utility;
using Utility.Extensions;
using Utility.Progress;

namespace RasMapperLib;

public class ContourBandRaster
{
	private float _noData;

	private string _shpFilename;

	private int _bandCount;

	private float[] _bandMin;

	private float[] _bandMax;

	private string[] _bandXYFilename;

	private BinaryWriter[] _bandBW;

	private float _currentRangeMin;

	private float _currentRangeMax;

	private BinaryWriter _currentBW;

	private string _containedPolygonFN;

	private BinaryWriter _containedPolygonBW;

	private string _currentXYFilename;

	private RasterM _currentRasterM;

	public ContourBandRaster(string shapefilename, float rangeMin, float rangeMax)
		: this(shapefilename, new float[1] { rangeMin }, new float[1] { rangeMax })
	{
	}

	public ContourBandRaster(string shapefilename, float[] bandMin, float[] bandMax)
	{
		_noData = -9999f;
		_shpFilename = shapefilename;
		_bandCount = bandMin.Count();
		checked
		{
			_bandMin = new float[_bandCount - 1 + 1];
			_bandMax = new float[_bandCount - 1 + 1];
			_bandXYFilename = new string[_bandCount - 1 + 1];
			_bandBW = new BinaryWriter[_bandCount - 1 + 1];
			_containedPolygonFN = GetTempXYName();
			_containedPolygonFN = System.IO.Path.ChangeExtension(_containedPolygonFN, ".contained.xy");
			_containedPolygonBW = new BinaryWriter(File.Open(_containedPolygonFN, FileMode.Create));
			int count = _bandCount - 1;
			for (int i = 0; i <= count; i++)
			{
				_bandMin[i] = bandMin[i];
				_bandMax[i] = bandMax[i];
				_bandXYFilename[i] = GetTempXYName();
				_bandBW[i] = new BinaryWriter(File.Open(_bandXYFilename[i], FileMode.Create));
			}
		}
	}

	private static string GetTempXYName()
	{
		string tempXYName = System.IO.Path.GetTempPath() + Conversions.ToString(System.IO.Path.DirectorySeparatorChar) + Guid.NewGuid().ToString() + ".xy";
		if (File.Exists(tempXYName))
		{
			File.Delete(tempXYName);
		}
		return tempXYName;
	}

	private static string RangeName(float v)
	{
		if (v == float.MaxValue)
		{
			return "Max";
		}
		if (v == float.MinValue)
		{
			return "Min";
		}
		return Strings.Format(v);
	}

	private void SetCurrentBand(int b)
	{
		_currentRangeMin = _bandMin[b];
		_currentRangeMax = _bandMax[b];
		_currentBW = _bandBW[b];
		_currentXYFilename = _bandXYFilename[b];
	}

	public void ContourTINValues(Tin TIN, float[] values, ProgressReporter reporter = null)
	{
		ContourTIN(TIN, [SpecialName] (int i) => values[i], reporter);
	}

	public void ContourTINZ(Tin TIN)
	{
		ContourTIN(TIN, [SpecialName] (int i) => (float)((MultiPoint)TIN).get_Z(i));
	}

	public void ContourTINM(Tin TIN)
	{
		ContourTIN(TIN, [SpecialName] (int i) => (float)((MultiPoint)TIN).get_M(i));
	}

	private void ContourTIN(Tin TIN, Func<int, float> v, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		reporter.ReportProgress(0);
		if (TIN.TriangleCount() == 0)
		{
			return;
		}
		Clipper clipper = new Clipper();
		List<IntPoint> intPolygon = new List<IntPoint>();
		PointMs pointMs = new PointMs();
		pointMs.Capacity = 5;
		checked
		{
			int unity = (int)System.Math.Round(System.Math.Min(64.0, (double)TIN.TriangleCount() / 1024.0 / 4.0));
			if (unity <= 0)
			{
				unity = 1;
			}
			SpatialIndex<int> spatialIndex = new SpatialIndex<int>(TIN, unity);
			int count = TIN.TriangleCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				spatialIndex.Add(TIN.TriangleExtent(i), i);
			}
			reporter.ReportProgress(10);
			int systemTerm = 10;
			float scaledBandCount = (float)(1.0 / (double)_bandCount);
			int count2 = _bandCount - 1;
			int p = default;
			int p2 = default;
			int p3 = default;
			for (int j = 0; j <= count2; j++)
			{
				SetCurrentBand(j);
				int count3 = spatialIndex.Bins.Count() - 1;
				for (int k = 0; k <= count3; k++)
				{
					float binsSpatialIndexTerm = (float)((double)(k + 1) / (double)spatialIndex.Bins.Count());
					reporter.ReportProgress((int)System.Math.Round((float)systemTerm + 100f * binsSpatialIndexTerm * scaledBandCount));
					if (spatialIndex.Bins[k].Count == 0)
					{
						continue;
					}
					clipper.Clear();
					int clipperPathCount = 0;
					foreach (int bin in spatialIndex.Bins[k])
					{
						TIN.TrianglePoints(bin, ref p, ref p2, ref p3);
						if (v(p) != _noData && v(p2) != _noData && v(p3) != _noData)
						{
							PointM pt = TIN.PointM(p);
							PointM pt2 = TIN.PointM(p2);
							PointM pt3 = TIN.PointM(p3);
							if (ContourBandTriangle(pt, v(p), pt2, v(p2), pt3, v(p3), pointMs))
							{
								AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
							}
						}
					}
					if (clipperPathCount != 0)
					{
						List<List<IntPoint>> polygons = GeoProcessing.MergeClipperPoints(clipper);
						WriteClipperPolygonsToBinary(_currentBW, polygons);
					}
				}
				systemTerm = (int)System.Math.Round((float)systemTerm + scaledBandCount * 100f);
			}
			reporter.ReportProgress(100);
		}
	}

	private bool ContourBandTriangle(PointM pt0, float v0, PointM pt1, float v1, PointM pt2, float v2, PointMs points)
	{
		float max = default;
		float min = default;
		RasMapperLib.Utilities.Math.MaxMin3(v0, v1, v2, ref max, ref min);
		if (min == max)
		{
			float min2 = min;
			if (_currentRangeMin <= min2 && _currentRangeMax > min2)
			{
				points.Add(pt0);
				points.Add(pt1);
				points.Add(pt2);
				return true;
			}
			return false;
		}
		if (_currentRangeMax <= min || _currentRangeMin > max)
		{
			return false;
		}
		if (_currentRangeMin <= min && _currentRangeMax > max)
		{
			points.Add(pt0);
			points.Add(pt1);
			points.Add(pt2);
			return true;
		}
		ContourBandTriangleEdge(pt0, v0, pt1, v1, points);
		ContourBandTriangleEdge(pt1, v1, pt2, v2, points);
		ContourBandTriangleEdge(pt2, v2, pt0, v0, points);
		return true;
	}

	private bool ContourBandTriangleEdge(PointM ptA, float vA, PointM ptB, float vB, PointMs points)
	{
		float vATerm;
		float vBTerm;
		if (vA < vB)
		{
			vATerm = vA;
			vBTerm = vB;
		}
		else
		{
			vATerm = vB;
			vBTerm = vA;
		}
		if (_currentRangeMax < vATerm)
		{
			return false;
		}
		if (_currentRangeMin > vBTerm)
		{
			return false;
		}
		if (_currentRangeMin <= vATerm && _currentRangeMax >= vBTerm)
		{
			points.AddNew(ptA);
			points.AddNew(ptB);
			return true;
		}
		float vBAboveVA = vB - vA;
		if (vA < vB)
		{
			if (_currentRangeMin <= vA)
			{
				points.AddNew(ptA);
			}
			else
			{
				double t = (_currentRangeMin - vA) / vBAboveVA;
				points.AddNew(ptA.AtFractionBetween(ptB, t));
			}
			if (_currentRangeMax >= vB)
			{
				points.AddNew(ptB);
			}
			else
			{
				double t2 = (_currentRangeMax - vA) / vBAboveVA;
				points.AddNew(ptA.AtFractionBetween(ptB, t2));
			}
		}
		else
		{
			if (_currentRangeMax >= vA)
			{
				points.AddNew(ptA);
			}
			else
			{
				double t3 = (_currentRangeMax - vA) / vBAboveVA;
				points.AddNew(ptA.AtFractionBetween(ptB, t3));
			}
			if (_currentRangeMin <= vB)
			{
				points.AddNew(ptB);
			}
			else
			{
				double t4 = (_currentRangeMin - vA) / vBAboveVA;
				points.AddNew(ptA.AtFractionBetween(ptB, t4));
			}
		}
		return true;
	}

	public void ContourRaster(RasterM raster, float[] values)
	{
		Clipper clipper = new Clipper();
		List<IntPoint> intPolygon = new List<IntPoint>();
		_currentRasterM = raster;
		int rows = raster.Rows;
		int cols = raster.Cols;
		PointMs pointMs = new PointMs();
		checked
		{
			bool[] boolValues = new bool[cols - 1 + 1];
			int count = _bandCount - 1;
			for (int i = 0; i <= count; i++)
			{
				SetCurrentBand(i);
				clipper.Clear();
				int clipperPathCount = 0;
				int count2 = rows - 2;
				for (int j = 0; j <= count2; j++)
				{
					Buffers.ArraySetAllVB(boolValues, v: false);
					int count3 = cols - 2;
					for (int k = 0; k <= count3; k++)
					{
						int colsTerm = k + j * cols;
						float computed = values[colsTerm];
						float computed2 = values[colsTerm + 1];
						float colsTerm2 = values[colsTerm + cols + 1];
						float colsTerm3 = values[colsTerm + cols];
						int ordinal = 0;
						if (computed != _noData)
						{
							ordinal++;
						}
						if (computed2 != _noData)
						{
							ordinal++;
						}
						if (colsTerm2 != _noData)
						{
							ordinal++;
						}
						if (colsTerm3 != _noData)
						{
							ordinal++;
						}
						switch (ordinal)
						{
						case 4:
						{
							float computed3 = computed;
							if (computed3 > computed2)
							{
								computed3 = computed2;
							}
							if (computed3 > colsTerm2)
							{
								computed3 = colsTerm2;
							}
							if (computed3 > colsTerm3)
							{
								computed3 = colsTerm3;
							}
							float computed4 = computed;
							if (computed4 < computed2)
							{
								computed4 = computed2;
							}
							if (computed4 < colsTerm2)
							{
								computed4 = colsTerm2;
							}
							if (computed4 < colsTerm3)
							{
								computed4 = colsTerm3;
							}
							if (computed4 == computed3)
							{
								float computed5 = computed4;
								if (_currentRangeMin <= computed5 && _currentRangeMax > computed5)
								{
									boolValues[k] = true;
								}
							}
							else
							{
								if (_currentRangeMax <= computed3 || _currentRangeMin > computed4)
								{
									break;
								}
								if (_currentRangeMin <= computed3 && _currentRangeMax > computed4)
								{
									boolValues[k] = true;
									break;
								}
								float v = (float)((double)(computed + computed2 + colsTerm2 + colsTerm3) * 0.25);
								PointM pointM5 = RenderBlockPointA(j, k);
								PointM pointM6 = RenderBlockPointB(j, k);
								PointM pointM7 = RenderBlockPointC(j, k);
								PointM pointM8 = RenderBlockPointD(j, k);
								PointM pt2 = RenderBlockPointE(j, k);
								if (ContourBandTriangle(pointM6, computed2, pointM5, computed, pt2, v, pointMs))
								{
									AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
								}
								if (ContourBandTriangle(pointM7, colsTerm2, pointM6, computed2, pt2, v, pointMs))
								{
									AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
								}
								if (ContourBandTriangle(pointM8, colsTerm3, pointM7, colsTerm2, pt2, v, pointMs))
								{
									AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
								}
								if (ContourBandTriangle(pointM5, computed, pointM8, colsTerm3, pt2, v, pointMs))
								{
									AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
								}
							}
							break;
						}
						case 3:
						{
							float computed6 = 0f;
							if (computed != _noData)
							{
								computed6 += computed;
							}
							if (computed2 != _noData)
							{
								computed6 += computed2;
							}
							if (colsTerm2 != _noData)
							{
								computed6 += colsTerm2;
							}
							if (colsTerm3 != _noData)
							{
								computed6 += colsTerm3;
							}
							computed6 /= 3f;
							PointM pointM = RenderBlockPointA(j, k);
							PointM pointM2 = RenderBlockPointB(j, k);
							PointM pointM3 = RenderBlockPointC(j, k);
							PointM pointM4 = RenderBlockPointD(j, k);
							PointM pt = RenderBlockPointE(j, k);
							if (computed == _noData)
							{
								if (ContourBandTriangle(pointM3, colsTerm2, pointM2, computed2, pt, computed6, pointMs))
								{
									AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
								}
								if (ContourBandTriangle(pointM4, colsTerm3, pointM3, colsTerm2, pt, computed6, pointMs))
								{
									AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
								}
							}
							else if (computed2 == _noData)
							{
								if (ContourBandTriangle(pointM4, colsTerm3, pointM3, colsTerm2, pt, computed6, pointMs))
								{
									AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
								}
								if (ContourBandTriangle(pointM, computed, pointM4, colsTerm3, pt, computed6, pointMs))
								{
									AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
								}
							}
							else if (colsTerm2 == _noData)
							{
								if (ContourBandTriangle(pointM2, computed2, pointM, computed, pt, computed6, pointMs))
								{
									AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
								}
								if (ContourBandTriangle(pointM, computed, pointM4, colsTerm3, pt, computed6, pointMs))
								{
									AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
								}
							}
							else if (colsTerm3 == _noData)
							{
								if (ContourBandTriangle(pointM2, computed2, pointM, computed, pt, computed6, pointMs))
								{
									AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
								}
								if (ContourBandTriangle(pointM3, colsTerm2, pointM2, computed2, pt, computed6, pointMs))
								{
									AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
								}
							}
							break;
						}
						}
					}
					int intervalIndex = 0;
					int intervalIndex2 = 0;
					while (intervalIndex != cols)
					{
						if (boolValues[intervalIndex])
						{
							for (intervalIndex2 = intervalIndex; boolValues[intervalIndex2 + 1]; intervalIndex2++)
							{
							}
							pointMs.Add(RenderBlockPointA(j, intervalIndex));
							pointMs.Add(RenderBlockPointD(j, intervalIndex));
							pointMs.Add(RenderBlockPointC(j, intervalIndex2));
							pointMs.Add(RenderBlockPointB(j, intervalIndex2));
							AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
							intervalIndex = intervalIndex2 + 1;
						}
						else
						{
							intervalIndex++;
						}
					}
				}
				List<List<IntPoint>> polygons = GeoProcessing.MergeClipperPoints(clipper);
				WriteClipperPolygonsToBinary(_currentBW, polygons);
			}
		}
	}

	public void ContourRaster_CompletedPolygonOptimization_DONOTUSE(RasterM raster, float[] values)
	{
		if (_bandCount != 1)
		{
			throw new NotImplementedException();
		}
		Clipper clipper = new Clipper();
		List<IntPoint> intPolygon = new List<IntPoint>();
		_currentRasterM = raster;
		int rows = _currentRasterM.Rows;
		int cols = _currentRasterM.Cols;
		PointMs pointMs = new PointMs();
		checked
		{
			bool[] boolValues = new bool[cols - 1 + 1];
			int count = _bandCount - 1;
			for (int i = 0; i <= count; i++)
			{
				SetCurrentBand(i);
				clipper.Clear();
				int clipperPathCount = 0;
				int count2 = rows - 2;
				for (int j = 0; j <= count2; j++)
				{
					Buffers.ArraySetAllVB(boolValues, v: false);
					int count3 = cols - 2;
					for (int k = 0; k <= count3; k++)
					{
						int colsTerm = k + j * cols;
						float computed = values[colsTerm];
						float computed2 = values[colsTerm + 1];
						float colsTerm2 = values[colsTerm + cols + 1];
						float colsTerm3 = values[colsTerm + cols];
						int ordinal = 0;
						if (computed != _noData)
						{
							ordinal++;
						}
						if (computed2 != _noData)
						{
							ordinal++;
						}
						if (colsTerm2 != _noData)
						{
							ordinal++;
						}
						if (colsTerm3 != _noData)
						{
							ordinal++;
						}
						switch (ordinal)
						{
						case 4:
						{
							float computed3 = computed;
							if (computed3 > computed2)
							{
								computed3 = computed2;
							}
							if (computed3 > colsTerm2)
							{
								computed3 = colsTerm2;
							}
							if (computed3 > colsTerm3)
							{
								computed3 = colsTerm3;
							}
							float computed4 = computed;
							if (computed4 < computed2)
							{
								computed4 = computed2;
							}
							if (computed4 < colsTerm2)
							{
								computed4 = colsTerm2;
							}
							if (computed4 < colsTerm3)
							{
								computed4 = colsTerm3;
							}
							if (computed4 == computed3)
							{
								float computed5 = computed4;
								if (_currentRangeMin <= computed5 && _currentRangeMax > computed5)
								{
									boolValues[k] = true;
								}
							}
							else
							{
								if (_currentRangeMax <= computed3 || _currentRangeMin > computed4)
								{
									break;
								}
								if (_currentRangeMin <= computed3 && _currentRangeMax > computed4)
								{
									boolValues[k] = true;
									break;
								}
								float v = (float)((double)(computed + computed2 + colsTerm2 + colsTerm3) * 0.25);
								PointM pointM5 = RenderBlockPointA(j, k);
								PointM pointM6 = RenderBlockPointB(j, k);
								PointM pointM7 = RenderBlockPointC(j, k);
								PointM pointM8 = RenderBlockPointD(j, k);
								PointM pt2 = RenderBlockPointE(j, k);
								if (ContourBandTriangle(pointM6, computed2, pointM5, computed, pt2, v, pointMs))
								{
									AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
								}
								if (ContourBandTriangle(pointM7, colsTerm2, pointM6, computed2, pt2, v, pointMs))
								{
									AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
								}
								if (ContourBandTriangle(pointM8, colsTerm3, pointM7, colsTerm2, pt2, v, pointMs))
								{
									AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
								}
								if (ContourBandTriangle(pointM5, computed, pointM8, colsTerm3, pt2, v, pointMs))
								{
									AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
								}
							}
							break;
						}
						case 3:
						{
							float computed6 = 0f;
							if (computed != _noData)
							{
								computed6 += computed;
							}
							if (computed2 != _noData)
							{
								computed6 += computed2;
							}
							if (colsTerm2 != _noData)
							{
								computed6 += colsTerm2;
							}
							if (colsTerm3 != _noData)
							{
								computed6 += colsTerm3;
							}
							computed6 /= 3f;
							PointM pointM = RenderBlockPointA(j, k);
							PointM pointM2 = RenderBlockPointB(j, k);
							PointM pointM3 = RenderBlockPointC(j, k);
							PointM pointM4 = RenderBlockPointD(j, k);
							PointM pt = RenderBlockPointE(j, k);
							if (computed == _noData)
							{
								if (ContourBandTriangle(pointM3, colsTerm2, pointM2, computed2, pt, computed6, pointMs))
								{
									AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
								}
								if (ContourBandTriangle(pointM4, colsTerm3, pointM3, colsTerm2, pt, computed6, pointMs))
								{
									AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
								}
							}
							else if (computed2 == _noData)
							{
								if (ContourBandTriangle(pointM4, colsTerm3, pointM3, colsTerm2, pt, computed6, pointMs))
								{
									AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
								}
								if (ContourBandTriangle(pointM, computed, pointM4, colsTerm3, pt, computed6, pointMs))
								{
									AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
								}
							}
							else if (colsTerm2 == _noData)
							{
								if (ContourBandTriangle(pointM2, computed2, pointM, computed, pt, computed6, pointMs))
								{
									AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
								}
								if (ContourBandTriangle(pointM, computed, pointM4, colsTerm3, pt, computed6, pointMs))
								{
									AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
								}
							}
							else if (colsTerm3 == _noData)
							{
								if (ContourBandTriangle(pointM2, computed2, pointM, computed, pt, computed6, pointMs))
								{
									AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
								}
								if (ContourBandTriangle(pointM3, colsTerm2, pointM2, computed2, pt, computed6, pointMs))
								{
									AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
								}
							}
							break;
						}
						}
					}
					int intervalIndex = 0;
					int intervalIndex2 = 0;
					while (intervalIndex != cols)
					{
						if (boolValues[intervalIndex])
						{
							for (intervalIndex2 = intervalIndex; boolValues[intervalIndex2 + 1]; intervalIndex2++)
							{
							}
							pointMs.Add(RenderBlockPointA(j, intervalIndex));
							pointMs.Add(RenderBlockPointD(j, intervalIndex));
							pointMs.Add(RenderBlockPointC(j, intervalIndex2));
							pointMs.Add(RenderBlockPointB(j, intervalIndex2));
							AddPointsToClipper(clipper, ref clipperPathCount, intPolygon, pointMs);
							intervalIndex = intervalIndex2 + 1;
						}
						else
						{
							intervalIndex++;
						}
					}
				}
				List<List<IntPoint>> mergeClipperPoints = GeoProcessing.MergeClipperPoints(clipper);
				Clipper clipper2 = new Clipper();
				int clipperPathCount2 = 0;
				foreach (List<IntPoint> mergeClipperPoint in mergeClipperPoints)
				{
					AddLongPointsToClipper(clipper2, ref clipperPathCount2, mergeClipperPoint);
				}
				mergeClipperPoints = GeoProcessing.MergeClipperPoints(clipper2);
				List<List<IntPoint>> contained = null;
				List<List<IntPoint>> notContained = null;
				ContainedPolygons(mergeClipperPoints, _currentRasterM, ref contained, ref notContained);
				WriteClipperPolygonsToBinary(_currentBW, notContained);
				WriteClipperPolygonsToBinary(_containedPolygonBW, contained);
			}
		}
	}

	public static void DebugPrint(List<List<IntPoint>> pts)
	{
		foreach (List<IntPoint> pt in pts)
		{
			foreach (IntPoint pt2 in pt)
			{
				_ = pt2;
			}
			pt.First();
		}
	}

	private static void ContainedPolygons(List<List<IntPoint>> merged, RasterM raster, ref List<List<IntPoint>> contained, ref List<List<IntPoint>> notContained)
	{
		contained = new List<List<IntPoint>>();
		notContained = new List<List<IntPoint>>();
		PointMs points = new PointMs
		{
			raster.CellCenter(0, 0),
			checked(raster.CellCenter(raster.Rows - 1, raster.Cols - 1))
		};
		List<IntPoint> intPoints = new List<IntPoint>();
		GeoProcessing.ConvertDoublePointsToLongPoints(points, intPoints);
		long x = intPoints[0].X;
		long y = intPoints[0].Y;
		long x2 = intPoints[1].X;
		long y2 = intPoints[1].Y;
		foreach (List<IntPoint> mergedList in merged)
		{
			long minValue = long.MinValue;
			long minValue2 = long.MinValue;
			long maxValue = long.MaxValue;
			long maxValue2 = long.MaxValue;
			foreach (IntPoint merged2 in mergedList)
			{
				if (merged2.X > minValue)
				{
					minValue = merged2.X;
				}
				if (merged2.Y > minValue2)
				{
					minValue2 = merged2.Y;
				}
				if (merged2.X < maxValue)
				{
					maxValue = merged2.X;
				}
				if (merged2.Y < maxValue2)
				{
					maxValue2 = merged2.Y;
				}
			}
			if (x < maxValue && y > minValue2 && x2 > minValue && y2 < maxValue2)
			{
				contained.Add(mergedList);
			}
			else
			{
				notContained.Add(mergedList);
			}
		}
	}

	public PointM RenderBlockPointA(int rbrow, int rbcol)
	{
		return new PointM(_currentRasterM.CellCenterX(rbcol), _currentRasterM.CellCenterY(rbrow));
	}

	public PointM RenderBlockPointB(int rbrow, int rbcol)
	{
		return new PointM(_currentRasterM.CellCenterX(checked(rbcol + 1)), _currentRasterM.CellCenterY(rbrow));
	}

	public PointM RenderBlockPointC(int rbrow, int rbcol)
	{
		return checked(new PointM(_currentRasterM.CellCenterX(rbcol + 1), _currentRasterM.CellCenterY(rbrow + 1)));
	}

	public PointM RenderBlockPointD(int rbrow, int rbcol)
	{
		return new PointM(_currentRasterM.CellCenterX(rbcol), _currentRasterM.CellCenterY(checked(rbrow + 1)));
	}

	public PointM RenderBlockPointE(int rbrow, int rbcol)
	{
		return new PointM(_currentRasterM.CellMaxX(rbcol), _currentRasterM.CellMinY(rbrow));
	}

	public PolygonFeatureLayer Complete()
	{
		ShapefileStorage.Delete(_shpFilename);
		PolygonFeatureLayer polygonFeatureLayer = new PolygonFeatureLayer("Contour Bands");
		string columnName = "Range Min";
		string columnName2 = "Range Max";
		polygonFeatureLayer.AddAttributeColumn(columnName, typeof(string));
		polygonFeatureLayer.AddAttributeColumn(columnName2, typeof(string));
		_containedPolygonBW.Close();
		if (File.Exists(_containedPolygonFN))
		{
			File.Delete(_containedPolygonFN);
		}
		checked
		{
			int count = _bandCount - 1;
			for (int i = 0; i <= count; i++)
			{
				SetCurrentBand(i);
				_currentBW.Close();
				if (new FileInfo(_currentXYFilename).Length <= 0)
				{
					continue;
				}
				MergePolygonCommand.ExecuteOutOfProcess(_currentXYFilename);
				string path = CreateOutputFilename(_currentXYFilename);
				if (File.Exists(path))
				{
					List<List<IntPoint>> intPointListList = new List<List<IntPoint>>();
					using (BinaryReader binaryReader = new BinaryReader(File.Open(path, FileMode.Open)))
					{
						while (binaryReader.BaseStream.Position < binaryReader.BaseStream.Length)
						{
							List<IntPoint> intPoints = new List<IntPoint>();
							int capacity = binaryReader.ReadInt32();
							if (intPoints.Capacity < capacity)
							{
								intPoints.Capacity = capacity;
							}
							int readInt32 = capacity;
							for (int j = 1; j <= readInt32; j++)
							{
								long xx = binaryReader.ReadInt64();
								long yy = binaryReader.ReadInt64();
								intPoints.Add(new IntPoint(xx, yy));
							}
							intPointListList.Add(intPoints);
						}
					}
					if (intPointListList.Count > 0)
					{
						Polygon poly = GeoProcessing.ConvertClipperPointsToPolygon(intPointListList);
						DataRow dataRow = polygonFeatureLayer.AddFeature(poly);
						dataRow[columnName] = RangeName(_currentRangeMin);
						dataRow[columnName2] = RangeName(_currentRangeMax);
					}
				}
				File.Delete(_currentXYFilename);
				File.Delete(path);
			}
			polygonFeatureLayer.SourceFilename = _shpFilename;
			polygonFeatureLayer.Save();
			return polygonFeatureLayer;
		}
	}

	public static FeatureLayer MergeContoursWriteShapefile(IList<ContourBandRaster> cbrs)
	{
		if (cbrs.IsNullOrEmpty())
		{
			throw new ArgumentNullException("cbrs");
		}
		foreach (ContourBandRaster cbr in cbrs)
		{
			cbr.CloseBinaryWriters();
		}
		int bandCount = cbrs.First()._bandCount;
		checked
		{
			int count = cbrs.Count - 1;
			for (int i = 1; i <= count; i++)
			{
				if (cbrs[i]._bandCount != bandCount)
				{
					throw new ArgumentException("Each ContourBandRaster must have the same number of bands.", "cbrs");
				}
			}
			if (bandCount != 1)
			{
				throw new NotImplementedException();
			}
			ContourBandRaster contourBandRaster = cbrs.First();
			string shpFilename = contourBandRaster._shpFilename;
			float v = contourBandRaster._bandMin.First();
			float v2 = contourBandRaster._bandMax.First();
			List<string> stringList = cbrs.Select([SpecialName] (ContourBandRaster cbr) => cbr._bandXYFilename[0]).ToList();
			List<string> stringListList = cbrs.Select([SpecialName] (ContourBandRaster cbr) => cbr._containedPolygonFN).ToList();
			cbrs = null;
			string tempXYName = GetTempXYName();
			using (BinaryWriter binaryWriter = new BinaryWriter(File.Open(tempXYName, FileMode.Create)))
			{
				byte[] byteValues = new byte[1048576];
				foreach (string textValue in stringList)
				{
					if (!File.Exists(textValue))
					{
						continue;
					}
					using (BinaryReader binaryReader = new BinaryReader(File.OpenRead(textValue)))
					{
						int read = 0;
						for (read = binaryReader.Read(byteValues, 0, byteValues.Count()); read > 0; read = binaryReader.Read(byteValues, 0, byteValues.Count()))
						{
							binaryWriter.Write(byteValues, 0, read);
						}
					}
					File.Delete(textValue);
				}
			}
			ShapefileStorage.Delete(shpFilename);
			PolygonFeatureLayer polygonFeatureLayer = new PolygonFeatureLayer("Contour Bands");
			string columnName = "Range Min";
			string columnName2 = "Range Max";
			polygonFeatureLayer.AddAttributeColumn(columnName, typeof(string));
			polygonFeatureLayer.AddAttributeColumn(columnName2, typeof(string));
			if (new FileInfo(tempXYName).Length == 0L)
			{
				return null;
			}
			MergePolygonCommand.ExecuteOutOfProcess(tempXYName);
			string path = CreateOutputFilename(tempXYName);
			if (!File.Exists(path))
			{
				throw new Exception("Compute maps failed to create inundation boundary " + System.IO.Path.GetFileName(shpFilename));
			}
			using (BinaryWriter binaryWriter2 = new BinaryWriter(File.Open(path, FileMode.Append)))
			{
				byte[] byteValuesArray = new byte[1048576];
				foreach (string textValue2 in stringListList)
				{
					if (!File.Exists(textValue2))
					{
						continue;
					}
					using (BinaryReader binaryReader2 = new BinaryReader(File.OpenRead(textValue2)))
					{
						int read2 = 0;
						for (read2 = binaryReader2.Read(byteValuesArray, 0, byteValuesArray.Count()); read2 > 0; read2 = binaryReader2.Read(byteValuesArray, 0, byteValuesArray.Count()))
						{
							binaryWriter2.Write(byteValuesArray, 0, read2);
						}
					}
					File.Delete(textValue2);
				}
			}
			List<List<IntPoint>> intPointListList = new List<List<IntPoint>>();
			using (BinaryReader binaryReader3 = new BinaryReader(File.Open(path, FileMode.Open)))
			{
				while (binaryReader3.BaseStream.Position < binaryReader3.BaseStream.Length)
				{
					int readInt32 = binaryReader3.ReadInt32();
					List<IntPoint> intPoints = new List<IntPoint>(readInt32);
					int readInt322 = readInt32;
					for (int unity = 1; unity <= readInt322; unity++)
					{
						long xx = binaryReader3.ReadInt64();
						long yy = binaryReader3.ReadInt64();
						intPoints.Add(new IntPoint(xx, yy));
					}
					intPointListList.Add(intPoints);
				}
			}
			File.Delete(tempXYName);
			File.Delete(path);
			if (intPointListList.Count <= 0)
			{
				return null;
			}
			GC.Collect(0, GCCollectionMode.Forced);
			GC.Collect(1, GCCollectionMode.Forced);
			GC.Collect(2, GCCollectionMode.Forced);
			Polygon poly = GeoProcessing.ConvertClipperPointsToPolygon_MEMORYREUSE(intPointListList);
			DataRow dataRow = polygonFeatureLayer.AddFeature(poly);
			dataRow[columnName] = RangeName(v);
			dataRow[columnName2] = RangeName(v2);
			polygonFeatureLayer.SourceFilename = shpFilename;
			polygonFeatureLayer.Save();
			return polygonFeatureLayer;
		}
	}

	private void CloseBinaryWriters()
	{
		_containedPolygonBW.Close();
		checked
		{
			int count = _bandCount - 1;
			for (int i = 0; i <= count; i++)
			{
				_bandBW[i].Close();
			}
		}
	}

	private void AddPointsToClipper(Clipper clipper, ref int clipperPathCount, List<IntPoint> intPolygon, PointMs points)
	{
		GeoProcessing.ConvertDoublePointsToLongPoints(points, intPolygon);
		AddLongPointsToClipper(clipper, ref clipperPathCount, intPolygon);
	}

	private static void AddLongPointsToClipper(Clipper clipper, ref int clipperPathCount, List<IntPoint> intPolygon)
	{
		if (clipperPathCount == 0)
		{
			clipper.AddPath(intPolygon, PolyType.ptSubject, x: true);
		}
		else
		{
			clipper.AddPath(intPolygon, PolyType.ptClip, x: true);
		}
		checked
		{
			clipperPathCount++;
			intPolygon.Clear();
		}
	}

	private static void WriteClipperPolygonsToBinary(BinaryWriter bw, List<List<IntPoint>> polygons)
	{
		if (polygons == null)
		{
			return;
		}
		foreach (List<IntPoint> polygon in polygons)
		{
			bw.Write(polygon.Count);
			foreach (IntPoint polygon2 in polygon)
			{
				bw.Write(polygon2.X);
				bw.Write(polygon2.Y);
			}
		}
	}

	public static string CreateOutputFilename(string filename)
	{
		return filename + ".out";
	}

	public static void ExternalProcessMerge(string xyfilename)
	{
		Clipper clipper = new Clipper();
		using (BinaryReader binaryReader = new BinaryReader(File.Open(xyfilename, FileMode.Open)))
		{
			List<IntPoint> intPoints = new List<IntPoint>();
			int clipperPathCount = 0;
			while (binaryReader.BaseStream.Position < binaryReader.BaseStream.Length)
			{
				int capacity = binaryReader.ReadInt32();
				if (intPoints.Capacity < capacity)
				{
					intPoints.Capacity = capacity;
				}
				int readInt32 = capacity;
				for (int i = 1; i <= readInt32; i = checked(i + 1))
				{
					long xx = binaryReader.ReadInt64();
					long yy = binaryReader.ReadInt64();
					intPoints.Add(new IntPoint(xx, yy));
				}
				AddLongPointsToClipper(clipper, ref clipperPathCount, intPoints);
			}
		}
		List<List<IntPoint>> polygons = GeoProcessing.MergeClipperPoints(clipper);
		string path = CreateOutputFilename(xyfilename);
		using (BinaryWriter bw = new BinaryWriter(File.Open(path, FileMode.Create)))
		{
			WriteClipperPolygonsToBinary(bw, polygons);
		}
		clipper = null;
		GC.Collect();
	}

	public static void ExternalProcessMergeNew(string xyfilename)
	{
		Clipper clipper = new Clipper();
		checked
		{
			using (BinaryReader binaryReader = new BinaryReader(File.Open(xyfilename, FileMode.Open)))
			{
				List<List<IntPoint>> intPointListList = new List<List<IntPoint>>();
				while (binaryReader.BaseStream.Position < binaryReader.BaseStream.Length)
				{
					int readInt32 = binaryReader.ReadInt32();
					List<IntPoint> intPoints = new List<IntPoint>(readInt32);
					int readInt322 = readInt32;
					for (int i = 1; i <= readInt322; i++)
					{
						long xx = binaryReader.ReadInt64();
						long yy = binaryReader.ReadInt64();
						intPoints.Add(new IntPoint(xx, yy));
					}
					intPointListList.Add(intPoints);
				}
				clipper.AddPath(intPointListList.Last(), PolyType.ptSubject, x: true);
				intPointListList.RemoveAt(intPointListList.Count - 1);
				if (intPointListList.Count > 0)
				{
					clipper.AddPaths(intPointListList, PolyType.ptClip, x: true);
				}
			}
			List<List<IntPoint>> polygons = GeoProcessing.MergeClipperPoints(clipper);
			string path = CreateOutputFilename(xyfilename);
			using (BinaryWriter bw = new BinaryWriter(File.Open(path, FileMode.Create)))
			{
				WriteClipperPolygonsToBinary(bw, polygons);
			}
			clipper = null;
			GC.Collect();
		}
	}
}
