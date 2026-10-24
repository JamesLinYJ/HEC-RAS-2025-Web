using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Mapping;
using RasMapperLib.Mesh;
using RasMapperLib.Utilities;
using Utility;
using Utility.Extensions;
using Utility.Memory;
using Utility.Progress;

namespace RasMapperLib;

public class RASGeometryMapPoints : ISizeKnown
{
	private enum QueryType
	{
		None,
		Raster,
		Points,
		Line
	}

	private struct PixelComputeBuffer
	{
		public float[] CWCanGive;

		public float[] CCWCanGive;

		public float[] XProducts;

		public int Count;

		public PixelComputeBuffer(int nFacepoints)
		{
			this = default;
			Resize(nFacepoints);
		}

		public void Resize(int nFacepoints)
		{
			checked
			{
				CWCanGive = new float[nFacepoints - 1 + 1];
				CCWCanGive = new float[nFacepoints - 1 + 1];
				XProducts = new float[nFacepoints - 1 + 1];
				Count = nFacepoints;
			}
		}
	}

	[Serializable]
	[CompilerGenerated]
	internal sealed class _Closure_0024__
	{
		public static readonly _Closure_0024__ _0024I;

		public static Func<PointIntersect, PointOnPolyline> _0024I75_002D1;

		public static Func<PointIntersect, PointOnPolyline> _0024I75_002D3;

		static _Closure_0024__()
		{
			_0024I = new _Closure_0024__();
		}

		[SpecialName]
		internal int _Lambda_0024__23_002D0(SlopingMeshMap mesh)
		{
			return mesh.MeshIndex;
		}

		[SpecialName]
		internal int _Lambda_0024__23_002D1(FlatMeshMap mesh)
		{
			return mesh.MeshIndex;
		}

		[SpecialName]
		internal int _Lambda_0024__23_002D2(int idx)
		{
			return idx;
		}

		[SpecialName]
		internal int _Lambda_0024__28_002D2(SlopingCellPoint cellpoint)
		{
			return cellpoint.Index;
		}

		[SpecialName]
		internal int _Lambda_0024__38_002D0(MappedXSIS xsis)
		{
			return xsis.MapPoints.Count;
		}

		[SpecialName]
		internal int _Lambda_0024__38_002D1(SAMap sa)
		{
			return sa.MapPoints.Count;
		}

		[SpecialName]
		internal int _Lambda_0024__38_002D2(FlatMeshMap area)
		{
			return area.Cells.Sum([SpecialName] (FlatCellMap cell) => cell.MapPoints.Count);
		}

		[SpecialName]
		internal int _Lambda_0024__38_002D3(FlatCellMap cell)
		{
			return cell.MapPoints.Count;
		}

		[SpecialName]
		internal int _Lambda_0024__38_002D4(SlopingMeshMap area)
		{
			return area.Cells.Sum([SpecialName] (SlopingCellMap cell) => cell.MapPoints.Count);
		}

		[SpecialName]
		internal int _Lambda_0024__38_002D5(SlopingCellMap cell)
		{
			return cell.MapPoints.Count;
		}

		[SpecialName]
		internal int _Lambda_0024__48_002D3(FlatCellMap fm)
		{
			return fm.Index;
		}

		[SpecialName]
		internal int _Lambda_0024__48_002D6(SlopingCellMap scm)
		{
			return scm.Index;
		}

		[SpecialName]
		internal List<FlatCellMap> _Lambda_0024__50_002D1()
		{
			return new List<FlatCellMap>();
		}

		[SpecialName]
		internal List<SlopingCellMap> _Lambda_0024__51_002D0()
		{
			return new List<SlopingCellMap>();
		}

		[SpecialName]
		internal int _Lambda_0024__51_002D7(List<SlopingCellMap> slopingCellMaps)
		{
			return slopingCellMaps.Count;
		}

		[SpecialName]
		internal int _Lambda_0024__64_002D0(FlatCellMap ss)
		{
			return ss.Index;
		}

		[SpecialName]
		internal PointM _Lambda_0024__71_002D0(PointOnPolyline pop)
		{
			return pop.PointM;
		}

		[SpecialName]
		internal PointM _Lambda_0024__72_002D2(PointOnPolyline pop)
		{
			return pop.PointM;
		}

		[SpecialName]
		internal PointM _Lambda_0024__73_002D0(PointOnPolyline pop)
		{
			return pop.PointM;
		}

		[SpecialName]
		internal PointOnPolyline _Lambda_0024__75_002D0(PointIntersect pi)
		{
			return pi.APointOnPolyline;
		}

		[SpecialName]
		internal PointOnPolyline _Lambda_0024__75_002D1(PointIntersect pi)
		{
			return pi.APointOnPolyline;
		}

		[SpecialName]
		internal PointOnPolyline _Lambda_0024__75_002D2(PointIntersect pi)
		{
			return pi.APointOnPolyline;
		}

		[SpecialName]
		internal PointOnPolyline _Lambda_0024__75_002D3(PointIntersect pi)
		{
			return pi.APointOnPolyline;
		}

		[SpecialName]
		internal float _Lambda_0024__77_002D0(Tuple<float, float> tup)
		{
			return tup.Item1;
		}

		[SpecialName]
		internal float _Lambda_0024__77_002D1(Tuple<float, float> tup)
		{
			return tup.Item1;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__28_002D0
	{
		public int meshIdx;

		public _Closure_0024__28_002D0(_Closure_0024__28_002D0 other)
		{
			if (other != null)
			{
				meshIdx = other.meshIdx;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(FlatMeshMap md)
		{
			return md.MeshIndex == meshIdx;
		}

		[SpecialName]
		internal bool _Lambda_0024__1(SlopingMeshMap md)
		{
			return md.MeshIndex == meshIdx;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__47_002D0
	{
		public BitArray mask;

		public RASGeometryMapPoints rasGeometryMapPoints;

		public _Closure_0024__47_002D0(_Closure_0024__47_002D0 other)
		{
			if (other != null)
			{
				mask = other.mask;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__47_002D1
	{
		public int lambdafID;

		public _Closure_0024__47_002D0 outerClosure;

		public _Closure_0024__47_002D1(_Closure_0024__47_002D1 other)
		{
			if (other != null)
			{
				lambdafID = other.lambdafID;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int n)
		{
			if (!outerClosure.mask[n])
			{
				if (outerClosure.rasGeometryMapPoints.MappedSAs.Count == 0 || outerClosure.rasGeometryMapPoints.MappedSAs.Last().ID != lambdafID)
				{
					outerClosure.rasGeometryMapPoints.MappedSAs.Add(new SAMap(lambdafID));
				}
				outerClosure.rasGeometryMapPoints.MappedSAs.Last().MapPoints.Add(n);
				outerClosure.mask[n] = true;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__48_002D0
	{
		public List<int> nearbyCells;

		public DistinctSpatialIndex spIndex;

		public List<FlatCellMap> flatCellList;

		public List<int> flatCells;

		public MeshFV2D mesh;

		public List<int> uniqueFPs;

		public List<int> uniqueFaces;

		public List<SlopingCellMap> slopingCellList;

		public List<int> slopedCells;

		public int lMeshID;

		public _Closure_0024__48_002D1 outerClosure;

		public _Closure_0024__48_002D0(_Closure_0024__48_002D0 other)
		{
			if (other != null)
			{
				nearbyCells = other.nearbyCells;
				spIndex = other.spIndex;
				flatCellList = other.flatCellList;
				flatCells = other.flatCells;
				mesh = other.mesh;
				uniqueFPs = other.uniqueFPs;
				uniqueFaces = other.uniqueFaces;
				slopingCellList = other.slopingCellList;
				slopedCells = other.slopedCells;
				lMeshID = other.lMeshID;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0()
		{
			nearbyCells = spIndex.GetDistinctElements(outerClosure.rasGeometryMapPoints._raster.MinX, outerClosure.rasGeometryMapPoints._raster.MaxX, outerClosure.rasGeometryMapPoints._raster.MinY, outerClosure.rasGeometryMapPoints._raster.MaxY);
		}

		[SpecialName]
		internal void _Lambda_0024__1()
		{
			flatCellList = outerClosure.rasGeometryMapPoints.ComputeRasterizedPixelLocations(flatCells, mesh, outerClosure.mask);
		}

		[SpecialName]
		internal void _Lambda_0024__2()
		{
			outerClosure.rasGeometryMapPoints.ComputeUniqueFacesAndFacepoints(flatCellList.Select([SpecialName] (FlatCellMap fm) => fm.Index).ToList(), mesh, ref uniqueFPs, ref uniqueFaces);
		}

		[SpecialName]
		internal void _Lambda_0024__4()
		{
			slopingCellList = outerClosure.rasGeometryMapPoints.ComputeSlopedCellsSquareOptimization(slopedCells, lMeshID, outerClosure.mask);
		}

		[SpecialName]
		internal void _Lambda_0024__5()
		{
			outerClosure.rasGeometryMapPoints.ComputeUniqueFacesAndFacepoints(slopingCellList.Select([SpecialName] (SlopingCellMap scm) => scm.Index).ToList(), mesh, ref uniqueFPs, ref uniqueFaces);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__48_002D1
	{
		public BitArray mask;

		public RASGeometryMapPoints rasGeometryMapPoints;

		public _Closure_0024__48_002D1(_Closure_0024__48_002D1 other)
		{
			if (other != null)
			{
				mask = other.mask;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__50_002D0
	{
		public ThreadLocal<List<FlatCellMap>> threadLocalCellLists;

		public ConcurrentBag<List<FlatCellMap>> cellListThreadsafeCollection;

		public MeshFV2D mesh;

		public RasterizeRectangle squareRasterizer;

		public BitArray mask;

		public ThreadLocal<RasterizePolygon> threadLocalRasterizers;

		public RASGeometryMapPoints rasGeometryMapPoints;

		public _Closure_0024__50_002D0(_Closure_0024__50_002D0 other)
		{
			if (other != null)
			{
				threadLocalCellLists = other.threadLocalCellLists;
				cellListThreadsafeCollection = other.cellListThreadsafeCollection;
				mesh = other.mesh;
				squareRasterizer = other.squareRasterizer;
				mask = other.mask;
				threadLocalRasterizers = other.threadLocalRasterizers;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__2(int cIdx)
		{
			_Closure_0024__50_002D1 localValue = default;
			_Closure_0024__50_002D1 capturedLocals = new _Closure_0024__50_002D1(localValue);
			capturedLocals.outerClosure = this;
			bool isValueCreated = threadLocalCellLists.IsValueCreated;
			List<FlatCellMap> flatCellMaps = threadLocalCellLists.Value;
			if (!isValueCreated)
			{
				cellListThreadsafeCollection.Add(flatCellMaps);
			}
			capturedLocals.points = new List<int>();
			checked
			{
				if (mesh.SquareCells[cIdx])
				{
					List<Point2Double> fpLocations = (from fpidx in mesh.CellFacePoints(cIdx)
						select mesh.FacePoints[fpidx].Point).ToList();
					int leftIdx = default;
					int rightIdx = default;
					int topIdx = default;
					int botIdx = default;
					if (!squareRasterizer.ComputeCells(fpLocations, ref leftIdx, ref rightIdx, ref topIdx, ref botIdx))
					{
						return;
					}
					int topIdx2 = topIdx;
					int botIdx2 = botIdx;
					for (int topIdx3 = topIdx2; topIdx3 <= botIdx2; topIdx3++)
					{
						int cellIndex = rasGeometryMapPoints._raster.CellIndex(topIdx3, 0);
						int leftIdx2 = leftIdx;
						int rightIdx2 = rightIdx;
						for (int leftIdx3 = leftIdx2; leftIdx3 <= rightIdx2; leftIdx3++)
						{
							int ordinal = cellIndex + leftIdx3;
							if (!mask[ordinal])
							{
								mask[ordinal] = true;
								capturedLocals.points.Add(ordinal);
							}
						}
					}
				}
				else
				{
					RasterizePolygon rasterizePolygon = threadLocalRasterizers.Value;
					List<Segment2Double> segments = mesh.CellSegment2DoubleList(cIdx);
					rasterizePolygon.ComputeCells(segments, [SpecialName] (int n) =>
					{
						if (!capturedLocals.outerClosure.mask[n])
						{
							capturedLocals.points.Add(n);
							capturedLocals.outerClosure.mask[n] = true;
						}
					});
				}
				if (capturedLocals.points.Count != 0)
				{
					flatCellMaps.Add(new FlatCellMap(cIdx, capturedLocals.points));
				}
			}
		}

		[SpecialName]
		internal Point2Double _Lambda_0024__3(int fpidx)
		{
			return mesh.FacePoints[fpidx].Point;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__50_002D1
	{
		public List<int> points;

		public _Closure_0024__50_002D0 outerClosure;

		public _Closure_0024__50_002D1(_Closure_0024__50_002D1 other)
		{
			if (other != null)
			{
				points = other.points;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__4(int n)
		{
			if (!outerClosure.mask[n])
			{
				points.Add(n);
				outerClosure.mask[n] = true;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__51_002D0
	{
		public object lockReuse;

		public int countTotal;

		public MeshFV2D mesh;

		public ThreadLocal<List<SlopingCellMap>> threadLocalCellLists;

		public ConcurrentBag<List<SlopingCellMap>> trackCellLists;

		public Dictionary<int, SlopingCellMap> translatableCells;

		public BitArray mask;

		public RasterM rast;

		public Func<int, int> borrowReusable;

		public ThreadLocal<RasterizePolygon> threadLocalRasterizer;

		public float areaTolerance;

		public RASGeometryMapPoints rasGeometryMapPoints;

		public _Closure_0024__51_002D0(_Closure_0024__51_002D0 other)
		{
			if (other != null)
			{
				lockReuse = other.lockReuse;
				countTotal = other.countTotal;
				mesh = other.mesh;
				threadLocalCellLists = other.threadLocalCellLists;
				trackCellLists = other.trackCellLists;
				translatableCells = other.translatableCells;
				mask = other.mask;
				rast = other.rast;
				borrowReusable = other.borrowReusable;
				threadLocalRasterizer = other.threadLocalRasterizer;
				areaTolerance = other.areaTolerance;
			}
		}

		[SpecialName]
		internal int _Lambda_0024__2(int numNeeded)
		{
			object lockReuse = this.lockReuse;
			ObjectFlowControl.CheckForSyncLockOnValueType(lockReuse);
			bool lockTaken = false;
			checked
			{
				try
				{
					Monitor.Enter(lockReuse, ref lockTaken);
					if (countTotal - rasGeometryMapPoints._reusablePixelsInUse >= numNeeded)
					{
						int reusablePixelsInUse = rasGeometryMapPoints._reusablePixelsInUse;
						rasGeometryMapPoints._reusablePixelsInUse += numNeeded;
						return reusablePixelsInUse;
					}
					return -1;
				}
				finally
				{
					if (lockTaken)
					{
						Monitor.Exit(lockReuse);
					}
				}
			}
		}

		[SpecialName]
		internal void _Lambda_0024__3(int cIdx)
		{
			_Closure_0024__51_002D4 localValue = new _Closure_0024__51_002D4(null);
			localValue.outerClosure = this;
			localValue.cIdx = cIdx;
			localValue.cell = mesh.Cell(localValue.cIdx);
			if (mesh.CellIsVirtual(localValue.cIdx))
			{
				return;
			}
			bool isValueCreated = threadLocalCellLists.IsValueCreated;
			List<SlopingCellMap> slopingCellMaps = threadLocalCellLists.Value;
			if (!isValueCreated)
			{
				trackCellLists.Add(slopingCellMaps);
			}
			checked
			{
				if (rasGeometryMapPoints._canTranslate)
				{
					SlopingCellMap slopingCellMap = null;
					translatableCells.TryGetValue(localValue.cIdx, out slopingCellMap);
					if (slopingCellMap != null)
					{
						List<SlopingCellPoint> mapPoints = slopingCellMap.MapPoints;
						int countOffset = mapPoints.Count - 1;
						for (int i = 0; i <= countOffset; i++)
						{
							SlopingCellPoint mapPoint = mapPoints[i];
							mapPoint.Index += rasGeometryMapPoints._indexSlide;
							if (mapPoint.Index >= 0 && mapPoint.Index < mask.Length)
							{
								mask[mapPoint.Index] = true;
								mapPoints[i] = mapPoint;
							}
						}
						slopingCellMaps.Add(slopingCellMap);
						return;
					}
				}
				if (mesh.SquareCells[localValue.cIdx])
				{
					List<SlopingCellPoint> bensWeightsSquareCellREUSE = rasGeometryMapPoints.BensWeightsSquareCellREUSE(mesh, localValue.cIdx, rast, borrowReusable, mask);
					if (!bensWeightsSquareCellREUSE.IsNullOrEmpty())
					{
						slopingCellMaps.Add(new SlopingCellMap(localValue.cIdx, bensWeightsSquareCellREUSE));
					}
					return;
				}
				_Closure_0024__51_002D1 localValue2 = default;
				_Closure_0024__51_002D1 capturedLocals = new _Closure_0024__51_002D1(localValue2);
				capturedLocals.outerClosure = localValue;
				RasterizePolygon rasterizePolygon = threadLocalRasterizer.Value;
				capturedLocals.pixelsInThisCell = new List<int>();
				new PointM(capturedLocals.outerClosure.cell.Point.X, capturedLocals.outerClosure.cell.Point.Y);
				Polygon polygon = mesh.CellPolygon(capturedLocals.outerClosure.cIdx);
				rasterizePolygon.ComputeCells(polygon, [SpecialName] (int n) =>
				{
					if (!capturedLocals.outerClosure.outerClosure.mask[n])
					{
						capturedLocals.pixelsInThisCell.Add(n);
						capturedLocals.outerClosure.outerClosure.mask[n] = true;
					}
				});
				if (capturedLocals.pixelsInThisCell.Count == 0)
				{
					return;
				}
				_Closure_0024__51_002D3 localValue3 = default;
				_Closure_0024__51_002D3 capturedLocals2 = new _Closure_0024__51_002D3(localValue3);
				capturedLocals2.outerClosure = capturedLocals;
				capturedLocals2.mapArr = new SlopingCellPoint[capturedLocals2.outerClosure.pixelsInThisCell.Count - 1 + 1];
				if (capturedLocals2.outerClosure.outerClosure.cell.Faces.Count < 2)
				{
					return;
				}
				if (capturedLocals2.outerClosure.outerClosure.cell.Faces.Count == 2)
				{
					_Closure_0024__51_002D2 localValue4 = default;
					_Closure_0024__51_002D2 capturedLocals3 = new _Closure_0024__51_002D2(localValue4);
					capturedLocals3.outerClosure = capturedLocals2;
					Face face = mesh.Faces[capturedLocals3.outerClosure.outerClosure.outerClosure.cell.Faces[0]];
					FacePoint facePoint = mesh.FacePoints[face.GetFPPrev(capturedLocals3.outerClosure.outerClosure.outerClosure.cIdx)];
					FacePoint facePoint2 = mesh.FacePoints[face.GetFPNext(capturedLocals3.outerClosure.outerClosure.outerClosure.cIdx)];
					capturedLocals3.fp1Pt = facePoint.PointM();
					capturedLocals3.fp2Pt = facePoint2.PointM();
					Utility.Parallel.SmartFor(capturedLocals3.outerClosure.outerClosure.pixelsInThisCell.Count, [SpecialName] (int start, int finish) =>
					{
						for (int j = start; j <= finish; j++)
						{
							float[] floatValues = new float[2];
							float[] floatValuesArray = new float[4];
							int pixelsInThisCell = capturedLocals3.outerClosure.outerClosure.pixelsInThisCell[j];
							PointM pointM = capturedLocals3.outerClosure.outerClosure.outerClosure.outerClosure.rasGeometryMapPoints._raster.CellCenter(pixelsInThisCell);
							float distanceTo = (float)pointM.DistanceTo(capturedLocals3.fp1Pt);
							float distanceTo2 = (float)pointM.DistanceTo(capturedLocals3.fp2Pt);
							if (distanceTo == 0f)
							{
								floatValues[0] = 1f;
								floatValues[1] = 0f;
							}
							else if (distanceTo2 == 0f)
							{
								floatValues[0] = 0f;
								floatValues[1] = 1f;
							}
							else
							{
								float computed = distanceTo + distanceTo2;
								float computed2 = distanceTo / computed;
								float computed3 = distanceTo2 / computed;
								floatValues[0] = 1f - computed2;
								floatValues[1] = 1f - computed3;
							}
							floatValuesArray[0] = floatValues[0];
							floatValuesArray[1] = floatValues[1];
							capturedLocals3.outerClosure.mapArr[j] = new SlopingCellPoint(pixelsInThisCell, floatValues, floatValuesArray);
						}
					}, 256);
				}
				else
				{
					Utility.Parallel.SmartFor(capturedLocals2.outerClosure.pixelsInThisCell.Count, [SpecialName] (int start, int finish) =>
					{
						float[] fpWeights = null;
						float[] velocityWeights = null;
						PixelComputeBuffer buf = new PixelComputeBuffer(capturedLocals2.outerClosure.outerClosure.cell.Faces.Count);
						for (int j = start; j <= finish; j++)
						{
							int pixelsInThisCell2 = capturedLocals2.outerClosure.pixelsInThisCell[j];
							capturedLocals2.outerClosure.outerClosure.outerClosure.rasGeometryMapPoints.BensWeights(capturedLocals2.outerClosure.outerClosure.outerClosure.rasGeometryMapPoints._raster.CellCenter(pixelsInThisCell2), capturedLocals2.outerClosure.outerClosure.outerClosure.mesh, capturedLocals2.outerClosure.outerClosure.cIdx, capturedLocals2.outerClosure.outerClosure.outerClosure.areaTolerance, buf, ref fpWeights, ref velocityWeights);
							capturedLocals2.mapArr[j] = new SlopingCellPoint(pixelsInThisCell2, fpWeights, velocityWeights);
						}
					}, 256);
				}
				slopingCellMaps.Add(new SlopingCellMap(capturedLocals2.outerClosure.outerClosure.cIdx, capturedLocals2.mapArr.ToList()));
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__51_002D1
	{
		public List<int> pixelsInThisCell;

		public _Closure_0024__51_002D4 outerClosure;

		public _Closure_0024__51_002D1(_Closure_0024__51_002D1 other)
		{
			if (other != null)
			{
				pixelsInThisCell = other.pixelsInThisCell;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__4(int n)
		{
			if (!outerClosure.outerClosure.mask[n])
			{
				pixelsInThisCell.Add(n);
				outerClosure.outerClosure.mask[n] = true;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__51_002D2
	{
		public PointM fp1Pt;

		public PointM fp2Pt;

		public _Closure_0024__51_002D3 outerClosure;

		public _Closure_0024__51_002D2(_Closure_0024__51_002D2 other)
		{
			if (other != null)
			{
				fp1Pt = other.fp1Pt;
				fp2Pt = other.fp2Pt;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__5(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				float[] floatValues = new float[2];
				float[] floatValuesArray = new float[4];
				int pixelsInThisCell = outerClosure.outerClosure.pixelsInThisCell[i];
				PointM pointM = outerClosure.outerClosure.outerClosure.outerClosure.rasGeometryMapPoints._raster.CellCenter(pixelsInThisCell);
				float distanceTo = (float)pointM.DistanceTo(fp1Pt);
				float distanceTo2 = (float)pointM.DistanceTo(fp2Pt);
				if (distanceTo == 0f)
				{
					floatValues[0] = 1f;
					floatValues[1] = 0f;
				}
				else if (distanceTo2 == 0f)
				{
					floatValues[0] = 0f;
					floatValues[1] = 1f;
				}
				else
				{
					float computed = distanceTo + distanceTo2;
					float computed2 = distanceTo / computed;
					float computed3 = distanceTo2 / computed;
					floatValues[0] = 1f - computed2;
					floatValues[1] = 1f - computed3;
				}
				floatValuesArray[0] = floatValues[0];
				floatValuesArray[1] = floatValues[1];
				outerClosure.mapArr[i] = new SlopingCellPoint(pixelsInThisCell, floatValues, floatValuesArray);
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__51_002D3
	{
		public SlopingCellPoint[] mapArr;

		public _Closure_0024__51_002D1 outerClosure;

		public _Closure_0024__51_002D3(_Closure_0024__51_002D3 other)
		{
			if (other != null)
			{
				mapArr = other.mapArr;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__6(int start, int finish)
		{
			float[] fpWeights = null;
			float[] velocityWeights = null;
			PixelComputeBuffer buf = new PixelComputeBuffer(outerClosure.outerClosure.cell.Faces.Count);
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				int pixelsInThisCell = outerClosure.pixelsInThisCell[i];
				outerClosure.outerClosure.outerClosure.rasGeometryMapPoints.BensWeights(outerClosure.outerClosure.outerClosure.rasGeometryMapPoints._raster.CellCenter(pixelsInThisCell), outerClosure.outerClosure.outerClosure.mesh, outerClosure.outerClosure.cIdx, outerClosure.outerClosure.outerClosure.areaTolerance, buf, ref fpWeights, ref velocityWeights);
				mapArr[i] = new SlopingCellPoint(pixelsInThisCell, fpWeights, velocityWeights);
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__51_002D4
	{
		public Cell cell;

		public int cIdx;

		public _Closure_0024__51_002D0 outerClosure;

		public _Closure_0024__51_002D4(_Closure_0024__51_002D4 other)
		{
			if (other != null)
			{
				cell = other.cell;
				cIdx = other.cIdx;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__52_002D0
	{
		public MeshFV2D mesh;

		public _Closure_0024__52_002D0(_Closure_0024__52_002D0 other)
		{
			if (other != null)
			{
				mesh = other.mesh;
			}
		}

		[SpecialName]
		internal Point2Double _Lambda_0024__0(int fpidx)
		{
			return mesh.FacePoints[fpidx].Point;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__59_002D0
	{
		public BitArray mask;

		public RASGeometryMapPoints rasGeometryMapPoints;

		public _Closure_0024__59_002D0(_Closure_0024__59_002D0 other)
		{
			if (other != null)
			{
				mask = other.mask;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__59_002D1
	{
		public int lambdafID;

		public _Closure_0024__59_002D0 outerClosure;

		public _Closure_0024__59_002D1(_Closure_0024__59_002D1 other)
		{
			if (other != null)
			{
				lambdafID = other.lambdafID;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int n, float z, float m, float streamNX, float streamNY)
		{
			if (!outerClosure.mask[n])
			{
				if (outerClosure.rasGeometryMapPoints.MappedXSISs.Count == 0 || outerClosure.rasGeometryMapPoints.MappedXSISs.Last().ID != lambdafID)
				{
					outerClosure.rasGeometryMapPoints.MappedXSISs.Add(new MappedXSIS(lambdafID));
				}
				outerClosure.rasGeometryMapPoints.MappedXSISs.Last().Add(n, z, m, streamNX, streamNY);
				outerClosure.mask[n] = true;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__60_002D0
	{
		public MeshFV2D mesh;

		public _Closure_0024__60_002D0(_Closure_0024__60_002D0 other)
		{
			if (other != null)
			{
				mesh = other.mesh;
			}
		}

		[SpecialName]
		internal Point2Double _Lambda_0024__0(int fpidx)
		{
			return mesh.FacePoints[fpidx].Point;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__76_002D0
	{
		public Polyline pline;

		public _Closure_0024__76_002D0(_Closure_0024__76_002D0 other)
		{
			if (other != null)
			{
				pline = other.pline;
			}
		}

		[SpecialName]
		internal Tuple<float, float> _Lambda_0024__0(Tuple<PointOnPolyline, PointOnPolyline> tup)
		{
			return new Tuple<float, float>((float)pline.DistanceAlong(tup.Item1), (float)pline.DistanceAlong(tup.Item2));
		}

		[SpecialName]
		internal Tuple<float, float> _Lambda_0024__1(Tuple<PointOnPolyline, PointOnPolyline> tup)
		{
			return new Tuple<float, float>((float)pline.DistanceAlong(tup.Item1), (float)pline.DistanceAlong(tup.Item2));
		}
	}

	private long _size;

	private RASGeometry _geometry;

	private RASStorageArea _SA;

	private RASD2FlowArea _D2;

	private RASXSInterpolationSurface _XSIS;

	private double _tolerance;

	public List<MappedXSIS> MappedXSISs;

	public List<SAMap> MappedSAs;

	private List<FlatMeshMap> _flatAreas;

	private List<SlopingMeshMap> _slopingAreas;

	private List<SlopingCellPoint> _reusableSquarePixels;

	private int _reusablePixelsInUse;

	private Stopwatch _stopWatch;

	private QueryType _queryType;

	private RasterM _raster;

	private int _pointCount;

	private int _indexSlide;

	private bool _canTranslate;

	private Dictionary<int, SlopingCellMap>[] _translatableCells;

	private BitArray _mappedPixels;

	public long Size
	{
		get
		{
			if (_size == 0L)
			{
				_size = CalculateSize();
			}
			return _size;
		}
	}

	public RasterM Raster
	{
		get
		{
			if (_queryType == QueryType.Raster)
			{
				return _raster;
			}
			return null;
		}
	}

	public int InputCount
	{
		get
		{
			if (_queryType == QueryType.Points)
			{
				return _pointCount;
			}
			if (_raster != null && _queryType == QueryType.Raster)
			{
				return checked(_raster.CellMaxIndex() + 1);
			}
			if (_queryType == QueryType.Line)
			{
				throw new NotImplementedException();
			}
			return -1;
		}
	}

	public int MappedPixelCount
	{
		get
		{
			int sumSum = 0;
			checked
			{
				if (MappedXSISs != null)
				{
					sumSum += MappedXSISs.Sum([SpecialName] (MappedXSIS xsis) => xsis.MapPoints.Count);
				}
				if (MappedSAs != null)
				{
					sumSum += MappedSAs.Sum([SpecialName] (SAMap sa) => sa.MapPoints.Count);
				}
				if (_flatAreas != null)
				{
					sumSum += _flatAreas.Sum([SpecialName] (FlatMeshMap area) => area.Cells.Sum([SpecialName] (FlatCellMap cell) => cell.MapPoints.Count));
				}
				if (_slopingAreas != null)
				{
					sumSum += _slopingAreas.Sum([SpecialName] (SlopingMeshMap area) => area.Cells.Sum([SpecialName] (SlopingCellMap cell) => cell.MapPoints.Count));
				}
				return sumSum;
			}
		}
	}

	public BitArray MappedPixels => _mappedPixels;

	private long CalculateSize()
	{
		if (_raster == null)
		{
			return 0L;
		}
		return checked(_raster.Rows * _raster.Cols * 30);
	}

	long ISizeKnown.CalculateSize()
	{
		//ILSpy generated this explicit interface implementation from .override directive in CalculateSize
		return this.CalculateSize();
	}

	public string Search(int searchPixelIdx)
	{
		checked
		{
			int count = _flatAreas.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				FlatMeshMap flatMeshMap = _flatAreas[i];
				int count2 = flatMeshMap.Cells.Count - 1;
				for (int j = 0; j <= count2; j++)
				{
					FlatCellMap flatCellMap = flatMeshMap.Cells[j];
					int count3 = flatCellMap.MapPoints.Count - 1;
					for (int k = 0; k <= count3; k++)
					{
						if (flatCellMap.MapPoints[k] == searchPixelIdx)
						{
							return "Flat Mesh[" + i + "].Cells[" + j + "].MapPoints[" + k + "]";
						}
					}
				}
			}
			int count4 = _slopingAreas.Count - 1;
			for (int l = 0; l <= count4; l++)
			{
				SlopingMeshMap slopingMeshMap = _slopingAreas[l];
				int count5 = slopingMeshMap.Cells.Count - 1;
				for (int m = 0; m <= count5; m++)
				{
					SlopingCellMap slopingCellMap = slopingMeshMap.Cells[m];
					int count6 = slopingCellMap.MapPoints.Count - 1;
					for (int n = 0; n <= count6; n++)
					{
						if (slopingCellMap.MapPoints[n].Index == searchPixelIdx)
						{
							return "Sloping Mesh[" + l + "].Cells[" + m + "].MapPoints[" + n + "]";
						}
					}
				}
			}
			return "Not found";
		}
	}

	public List<FlatMeshMap> FlatMapped2DAreas()
	{
		return _flatAreas;
	}

	public List<SlopingMeshMap> SlopingMapped2DAreas()
	{
		return _slopingAreas;
	}

	public IEnumerable<int> AllMappedMeshIndexes()
	{
		List<int> intList = new List<int>();
		intList.AddRange(_slopingAreas.Select([SpecialName] (SlopingMeshMap mesh) => mesh.MeshIndex));
		intList.AddRange(_flatAreas.Select([SpecialName] (FlatMeshMap mesh) => mesh.MeshIndex));
		return from idx in intList.Distinct()
			orderby idx
			select idx;
	}

	public Dictionary<int, Tuple<FlatMeshMap, SlopingMeshMap>> JoinedMappedMeshes()
	{
		Dictionary<int, Tuple<FlatMeshMap, SlopingMeshMap>> valueTupleByInt = new Dictionary<int, Tuple<FlatMeshMap, SlopingMeshMap>>();
		foreach (FlatMeshMap flatMapped2DArea in FlatMapped2DAreas())
		{
			valueTupleByInt[flatMapped2DArea.MeshIndex] = new Tuple<FlatMeshMap, SlopingMeshMap>(flatMapped2DArea, null);
		}
		foreach (SlopingMeshMap slopingMapped2DArea in SlopingMapped2DAreas())
		{
			if (valueTupleByInt.ContainsKey(slopingMapped2DArea.MeshIndex))
			{
				Tuple<FlatMeshMap, SlopingMeshMap> tuple = valueTupleByInt[slopingMapped2DArea.MeshIndex];
				tuple = new Tuple<FlatMeshMap, SlopingMeshMap>(tuple.Item1, slopingMapped2DArea);
				valueTupleByInt[slopingMapped2DArea.MeshIndex] = tuple;
			}
			else
			{
				valueTupleByInt[slopingMapped2DArea.MeshIndex] = new Tuple<FlatMeshMap, SlopingMeshMap>(null, slopingMapped2DArea);
			}
		}
		return valueTupleByInt;
	}

	public bool HasMapped2DCells()
	{
		foreach (SlopingMeshMap slopingMapped2DArea in SlopingMapped2DAreas())
		{
			foreach (SlopingCellMap cell in slopingMapped2DArea.Cells)
			{
				if (cell.MapPoints.Count > 0)
				{
					return true;
				}
			}
		}
		foreach (FlatMeshMap flatMapped2DArea in FlatMapped2DAreas())
		{
			foreach (FlatCellMap cell2 in flatMapped2DArea.Cells)
			{
				if (cell2.MapPoints.Count > 0)
				{
					return true;
				}
			}
		}
		return false;
	}

	public bool HasMappedStorageAreas()
	{
		foreach (SAMap mappedSA in MappedSAs)
		{
			if (mappedSA.MapPoints.Count > 0)
			{
				return true;
			}
		}
		return false;
	}

	public bool HasMapped1DAreas()
	{
		return MappedXSISs.Count > 0;
	}

	public IEnumerable<FlatCellMap> AllMappedCells(int meshIdx)
	{
		_Closure_0024__28_002D0 capturedLocals = default;
		_Closure_0024__28_002D0 capturedLocals2 = new _Closure_0024__28_002D0(capturedLocals);
		capturedLocals2.meshIdx = meshIdx;
		FlatMeshMap flatMeshMap = _flatAreas.FirstOrDefault([SpecialName] (FlatMeshMap md) => md.MeshIndex == capturedLocals2.meshIdx);
		SlopingMeshMap slopingMeshMap = _slopingAreas.FirstOrDefault([SpecialName] (SlopingMeshMap md) => md.MeshIndex == capturedLocals2.meshIdx);
		if (flatMeshMap != null)
		{
			foreach (FlatCellMap cell in flatMeshMap.Cells)
			{
				yield return cell;
			}
		}
		if (slopingMeshMap == null)
		{
			yield break;
		}
		foreach (SlopingCellMap cell2 in slopingMeshMap.Cells)
		{
			yield return new FlatCellMap(cell2.Index, cell2.MapPoints.Select([SpecialName] (SlopingCellPoint cellpoint) => cellpoint.Index).ToList());
		}
	}

	public static void ForceLoadGeometry(RASGeometry geom)
	{
		geom.StorageArea.FeatureTable();
		geom.D2FlowArea.FeatureTable();
		_ = geom.XSInterpolationSurface;
	}

	public RASGeometryMapPoints(RASGeometry geometry)
	{
		_size = 0L;
		MappedXSISs = new List<MappedXSIS>();
		MappedSAs = new List<SAMap>();
		_flatAreas = new List<FlatMeshMap>();
		_slopingAreas = new List<SlopingMeshMap>();
		_reusableSquarePixels = new List<SlopingCellPoint>();
		_reusablePixelsInUse = 0;
		_raster = null;
		_pointCount = -1;
		_indexSlide = 0;
		_canTranslate = false;
		_geometry = geometry;
		_tolerance = geometry.FilterToleranceTIN;
		_queryType = QueryType.None;
		_SA = _geometry.StorageArea;
		_D2 = _geometry.D2FlowArea;
		_XSIS = _geometry.XSInterpolationSurface;
	}

	private long TimeIt(Action action)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		action();
		stopwatch.Stop();
		return stopwatch.ElapsedMilliseconds;
	}

	private void ClearStoredLists()
	{
		MappedSAs.Clear();
		MappedXSISs.Clear();
		_flatAreas.Clear();
		_slopingAreas.Clear();
	}

	public void ComputeForRaster(RasterM raster)
	{
		_canTranslate = false;
		checked
		{
			if (_queryType == QueryType.Raster && _raster != null)
			{
				if (_raster.CellSize != raster.CellSize || _raster.Width != raster.Width || _raster.Height != raster.Height || _raster.Rows != raster.Rows || _raster.Cols != raster.Cols || !_raster.Overlaps(raster) || _raster.MaxX == raster.MinX || raster.MaxX == _raster.MinX || _raster.MaxY == _raster.MinY || raster.MaxY == _raster.MinY)
				{
					List<SlopingCellPoint> reusableSquarePixels = new List<SlopingCellPoint>();
					foreach (SlopingMeshMap slopingArea in _slopingAreas)
					{
						MeshFV2D meshFV2D = _D2.Mesh(slopingArea.MeshIndex);
						if (!meshFV2D.IsComplete())
						{
							continue;
						}
						meshFV2D.EnsureSquareCellsCached();
						foreach (SlopingCellMap cell in slopingArea.Cells)
						{
							if (meshFV2D.SquareCells[cell.Index])
							{
								reusableSquarePixels.AddRange(cell.MapPoints);
							}
						}
					}
					_canTranslate = false;
					_translatableCells = null;
					_reusableSquarePixels = reusableSquarePixels;
				}
				else
				{
					_canTranslate = true;
					int systemMaxXTerm = (int)System.Math.Round((_raster.MaxX - raster.MaxX) / raster.CellSize);
					int systemMaxYTerm = (int)System.Math.Round((raster.MaxY - _raster.MaxY) / raster.CellSize);
					_indexSlide = systemMaxYTerm * _raster.Cols + systemMaxXTerm;
					Stopwatch stopwatch = Stopwatch.StartNew();
					_translatableCells = new Dictionary<int, SlopingCellMap>[_D2.FeatureCount() - 1 + 1];
					List<SlopingCellPoint> reusableSquarePixelsList = new List<SlopingCellPoint>();
					foreach (SlopingMeshMap slopingArea2 in _slopingAreas)
					{
						MeshFV2D meshFV2D2 = _D2.Mesh(slopingArea2.MeshIndex);
						if (!meshFV2D2.IsComplete())
						{
							continue;
						}
						meshFV2D2.EnsureSquareCellsCached();
						Dictionary<int, SlopingCellMap> translatableCells = new Dictionary<int, SlopingCellMap>();
						foreach (SlopingCellMap cell2 in slopingArea2.Cells)
						{
							int index = cell2.Index;
							Extent extent = meshFV2D2.CellExtent(index);
							if (extent != null)
							{
								if (raster.Contains(extent) && _raster.Contains(extent))
								{
									translatableCells.Add(index, cell2);
								}
								else if (meshFV2D2.SquareCells[cell2.Index])
								{
									reusableSquarePixelsList.AddRange(cell2.MapPoints);
								}
							}
						}
						_translatableCells[slopingArea2.MeshIndex] = translatableCells;
					}
					int count = _D2.FeatureCount() - 1;
					for (int i = 0; i <= count; i++)
					{
						if (_translatableCells[i] == null)
						{
							_translatableCells[i] = new Dictionary<int, SlopingCellMap>();
						}
					}
					stopwatch.Stop();
					_reusableSquarePixels = reusableSquarePixelsList;
				}
			}
			else
			{
				_reusableSquarePixels = new List<SlopingCellPoint>();
			}
			_queryType = QueryType.Raster;
			ClearStoredLists();
			if (_XSIS.FeatureCount() + _SA.FeatureCount() + _D2.FeatureCount() != 0)
			{
				_raster = new RasterM(raster);
				BitArray mask = new BitArray(raster.Rows * raster.Cols);
				RasterizePolygon rasterizePolygons = null;
				if (raster.Overlaps(_D2.Extent))
				{
					Rasterize2DFlowAreasWithSpatialIndex(rasterizePolygons, mask);
				}
				if (raster.Overlaps(_SA.Extent))
				{
					RasterizeStorageAreas(rasterizePolygons, mask);
				}
				if (raster.Overlaps(_XSIS.Extent) && _XSIS.FeatureCount() > 0)
				{
					RasterizeCrossSectionRegions(mask);
				}
			}
		}
	}

	public void ComputeFor1D(RasterM raster)
	{
		_queryType = QueryType.Raster;
		ClearStoredLists();
		if (_XSIS.FeatureCount() != 0)
		{
			_raster = new RasterM(raster);
			_mappedPixels = new BitArray(checked(raster.Rows * raster.Cols));
			if (raster.Overlaps(_XSIS.Extent) && _XSIS.FeatureCount() > 0)
			{
				RasterizeCrossSectionRegions(_mappedPixels);
			}
		}
	}

	private void RasterizeStorageAreas(RasterizePolygon rasterizePolygons, BitArray mask)
	{
		_Closure_0024__47_002D0 capturedLocals = new _Closure_0024__47_002D0(null);
		capturedLocals.rasGeometryMapPoints = this;
		capturedLocals.mask = mask;
		checked
		{
			int count = _SA.FeatureCount() - 1;
			_Closure_0024__47_002D1 capturedLocals2 = default;
			for (int i = 0; i <= count; i++)
			{
				if (!(_SA.Feature(i) is Polygon))
				{
					continue;
				}
				Polygon polygon = (Polygon)_SA.Feature(i);
				if (Raster.Overlaps(polygon))
				{
					capturedLocals2 = new _Closure_0024__47_002D1(capturedLocals2);
					capturedLocals2.outerClosure = capturedLocals;
					capturedLocals2.lambdafID = i;
					if (rasterizePolygons == null)
					{
						rasterizePolygons = new RasterizePolygon(Raster);
					}
					rasterizePolygons.ComputeCells(polygon, capturedLocals2._Lambda_0024__0);
				}
			}
		}
	}

	private void Rasterize2DFlowAreasWithSpatialIndex(RasterizePolygon rasterizePolygons, BitArray mask)
	{
		_Closure_0024__48_002D1 capturedLocals = new _Closure_0024__48_002D1(null);
		capturedLocals.rasGeometryMapPoints = this;
		capturedLocals.mask = mask;
		if (rasterizePolygons == null)
		{
			rasterizePolygons = new RasterizePolygon(_raster);
		}
		_reusablePixelsInUse = 0;
		checked
		{
			int count = _D2.FeatureCount() - 1;
			_Closure_0024__48_002D0 capturedLocals2 = default;
			for (int i = 0; i <= count; i++)
			{
				capturedLocals2 = new _Closure_0024__48_002D0(capturedLocals2);
				capturedLocals2.outerClosure = capturedLocals;
				capturedLocals2.lMeshID = i;
				capturedLocals2.mesh = _D2.Mesh(i);
				if (!capturedLocals2.mesh.IsComplete() || !_raster.Overlaps(capturedLocals2.mesh))
				{
					continue;
				}
				capturedLocals2.spIndex = SharedData.GetMeshCellSpatialIndex(capturedLocals2.mesh);
				float[] meshCellAreas = SharedData.GetMeshCellAreas(capturedLocals2.mesh);
				capturedLocals2.nearbyCells = null;
				TimeIt(capturedLocals2._Lambda_0024__0);
				capturedLocals2.flatCellList = new List<FlatCellMap>();
				capturedLocals2.slopingCellList = new List<SlopingCellMap>();
				capturedLocals2.uniqueFPs = null;
				capturedLocals2.uniqueFaces = null;
				capturedLocals2.flatCells = null;
				capturedLocals2.slopedCells = null;
				SplitCellsOnThreshold(capturedLocals2.mesh, capturedLocals2.nearbyCells, meshCellAreas, ref capturedLocals2.flatCells, ref capturedLocals2.slopedCells);
				long timeIt = TimeIt(capturedLocals2._Lambda_0024__1);
				TimeIt(capturedLocals2._Lambda_0024__2);
				_flatAreas.Add(new FlatMeshMap(i, capturedLocals2.flatCellList, capturedLocals2.uniqueFaces, capturedLocals2.uniqueFPs));
				long timeIt2 = TimeIt(capturedLocals2._Lambda_0024__4);
				TimeIt(capturedLocals2._Lambda_0024__5);
				_slopingAreas.Add(new SlopingMeshMap(i, capturedLocals2.slopingCellList, capturedLocals2.uniqueFaces, capturedLocals2.uniqueFPs));
				string textValue = "";
				if (capturedLocals2.slopingCellList.Count > 0)
				{
					int faceCount = capturedLocals2.mesh.FaceCount;
					int facePointCount = capturedLocals2.mesh.FacePointCount;
					if (capturedLocals2.uniqueFaces != null)
					{
						faceCount = capturedLocals2.uniqueFaces.Count;
					}
					if (capturedLocals2.uniqueFPs != null)
					{
						facePointCount = capturedLocals2.uniqueFPs.Count;
					}
					textValue = "Sloping: " + timeIt2 + "ms with " + capturedLocals2.slopingCellList.Count + " cells, Unique Faces: " + System.Math.Round((double)(faceCount * 100) / (double)capturedLocals2.mesh.FaceCount) + "%, UniqueFacePoints: " + System.Math.Round((double)(facePointCount * 100) / (double)capturedLocals2.mesh.FacePointCount) + "%";
				}
				if (capturedLocals2.flatCellList.Count > 0)
				{
					if (Operators.CompareString(textValue, "", TextCompare: false) != 0)
					{
						textValue += "\r";
					}
					textValue = textValue + "Flat: " + timeIt + "ms with " + capturedLocals2.flatCellList.Count + " cells";
				}
			}
		}
	}

	private void SplitCellsOnThreshold(MeshFV2D mesh, List<int> nearbyCells, float[] areas, ref List<int> flatCells, ref List<int> slopedCells)
	{
		float cellSize = (float)_raster.CellSize;
		float cellSizePixelRenderingCutoffTerm = cellSize * cellSize * (float)MeshFV2D.PixelRenderingCutoff;
		double cellMaximumSize = mesh.CellMaximumSize;
		double cellMinimumSize = mesh.CellMinimumSize;
		if (double.IsNaN(cellMaximumSize) || double.IsNaN(cellMinimumSize))
		{
			cellMaximumSize = double.MaxValue;
			cellMinimumSize = 0.0;
		}
		if (cellMinimumSize > (double)cellSizePixelRenderingCutoffTerm)
		{
			flatCells = new List<int>();
			slopedCells = nearbyCells;
			return;
		}
		if (cellMaximumSize < (double)cellSizePixelRenderingCutoffTerm)
		{
			flatCells = nearbyCells;
			slopedCells = new List<int>();
			return;
		}
		flatCells = nearbyCells.Where([SpecialName] (int a) => areas[a] <= cellSizePixelRenderingCutoffTerm).ToList();
		slopedCells = nearbyCells.Where([SpecialName] (int a) => areas[a] > cellSizePixelRenderingCutoffTerm).ToList();
	}

	private List<FlatCellMap> ComputeRasterizedPixelLocations(List<int> candidateCells, MeshFV2D mesh, BitArray mask)
	{
		_Closure_0024__50_002D0 capturedLocals = default;
		_Closure_0024__50_002D0 capturedLocals2 = new _Closure_0024__50_002D0(capturedLocals);
		capturedLocals2.rasGeometryMapPoints = this;
		capturedLocals2.mesh = mesh;
		capturedLocals2.mask = mask;
		capturedLocals2.threadLocalRasterizers = new ThreadLocal<RasterizePolygon>([SpecialName] () => new RasterizePolygon(_raster));
		capturedLocals2.threadLocalCellLists = new ThreadLocal<List<FlatCellMap>>([SpecialName] () => new List<FlatCellMap>());
		capturedLocals2.squareRasterizer = new RasterizeRectangle(_raster);
		capturedLocals2.mesh.EnsureSquareCellsCached();
		capturedLocals2.cellListThreadsafeCollection = new ConcurrentBag<List<FlatCellMap>>();
		System.Threading.Tasks.Parallel.ForEach(candidateCells, checked([SpecialName] (int cIdx) =>
		{
			_Closure_0024__50_002D1 capturedLocals3 = default;
			_Closure_0024__50_002D1 capturedLocals4 = new _Closure_0024__50_002D1(capturedLocals3);
			capturedLocals4.outerClosure = capturedLocals2;
			bool isValueCreated = capturedLocals2.threadLocalCellLists.IsValueCreated;
			List<FlatCellMap> flatCellMaps = capturedLocals2.threadLocalCellLists.Value;
			if (!isValueCreated)
			{
				capturedLocals2.cellListThreadsafeCollection.Add(flatCellMaps);
			}
			capturedLocals4.points = new List<int>();
			if (capturedLocals2.mesh.SquareCells[cIdx])
			{
				List<Point2Double> fpLocations = (from fpidx in capturedLocals2.mesh.CellFacePoints(cIdx)
					select capturedLocals2.mesh.FacePoints[fpidx].Point).ToList();
				int leftIdx = default;
				int rightIdx = default;
				int topIdx = default;
				int botIdx = default;
				if (!capturedLocals2.squareRasterizer.ComputeCells(fpLocations, ref leftIdx, ref rightIdx, ref topIdx, ref botIdx))
				{
					return;
				}
				int topIdx2 = topIdx;
				int botIdx2 = botIdx;
				for (int topIdx3 = topIdx2; topIdx3 <= botIdx2; topIdx3++)
				{
					int cellIndex = capturedLocals2.rasGeometryMapPoints._raster.CellIndex(topIdx3, 0);
					int leftIdx2 = leftIdx;
					int rightIdx2 = rightIdx;
					for (int leftIdx3 = leftIdx2; leftIdx3 <= rightIdx2; leftIdx3++)
					{
						int cellIndexPlusLeftIdx3 = cellIndex + leftIdx3;
						if (!capturedLocals2.mask[cellIndexPlusLeftIdx3])
						{
							capturedLocals2.mask[cellIndexPlusLeftIdx3] = true;
							capturedLocals4.points.Add(cellIndexPlusLeftIdx3);
						}
					}
				}
			}
			else
			{
				RasterizePolygon rasterizePolygon = capturedLocals2.threadLocalRasterizers.Value;
				List<Segment2Double> segments = capturedLocals2.mesh.CellSegment2DoubleList(cIdx);
				rasterizePolygon.ComputeCells(segments, [SpecialName] (int n) =>
				{
					if (!capturedLocals4.outerClosure.mask[n])
					{
						capturedLocals4.points.Add(n);
						capturedLocals4.outerClosure.mask[n] = true;
					}
				});
			}
			if (capturedLocals4.points.Count != 0)
			{
				flatCellMaps.Add(new FlatCellMap(cIdx, capturedLocals4.points));
			}
		}));
		List<FlatCellMap> flatCellMapsList = new List<FlatCellMap>();
		foreach (List<FlatCellMap> flatCellMaps2 in capturedLocals2.cellListThreadsafeCollection)
		{
			flatCellMapsList.AddRange(flatCellMaps2);
		}
		capturedLocals2.threadLocalRasterizers.Dispose();
		capturedLocals2.threadLocalCellLists.Dispose();
		return flatCellMapsList;
	}

	private List<SlopingCellMap> ComputeSlopedCellsSquareOptimization(List<int> slopedCells, int meshIdx, BitArray mask)
	{
		_Closure_0024__51_002D0 capturedLocals = default;
		_Closure_0024__51_002D0 capturedLocals2 = new _Closure_0024__51_002D0(capturedLocals);
		capturedLocals2.rasGeometryMapPoints = this;
		capturedLocals2.mask = mask;
		capturedLocals2.mesh = _D2.Mesh(meshIdx);
		capturedLocals2.rast = Raster;
		new RasterizeTriangle(capturedLocals2.rast);
		capturedLocals2.mesh.EnsureSquareCellsCached();
		capturedLocals2.threadLocalCellLists = new ThreadLocal<List<SlopingCellMap>>([SpecialName] () => new List<SlopingCellMap>());
		capturedLocals2.threadLocalRasterizer = new ThreadLocal<RasterizePolygon>([SpecialName] () => new RasterizePolygon(Raster));
		capturedLocals2.trackCellLists = new ConcurrentBag<List<SlopingCellMap>>();
		capturedLocals2.areaTolerance = (float)capturedLocals2.mesh.AreaTolerance;
		capturedLocals2.lockReuse = RuntimeHelpers.GetObjectValue(new object());
		capturedLocals2.countTotal = _reusableSquarePixels.Count;
		checked
		{
			capturedLocals2.borrowReusable = [SpecialName] (int numNeeded) =>
			{
				object lockReuse = capturedLocals2.lockReuse;
				ObjectFlowControl.CheckForSyncLockOnValueType(lockReuse);
				bool lockTaken = false;
				try
				{
					Monitor.Enter(lockReuse, ref lockTaken);
					if (capturedLocals2.countTotal - capturedLocals2.rasGeometryMapPoints._reusablePixelsInUse >= numNeeded)
					{
						int reusablePixelsInUse = capturedLocals2.rasGeometryMapPoints._reusablePixelsInUse;
						capturedLocals2.rasGeometryMapPoints._reusablePixelsInUse += numNeeded;
						return reusablePixelsInUse;
					}
					return -1;
				}
				finally
				{
					if (lockTaken)
					{
						Monitor.Exit(lockReuse);
					}
				}
			};
			capturedLocals2.translatableCells = null;
			if (_canTranslate)
			{
				capturedLocals2.translatableCells = _translatableCells[meshIdx];
			}
			System.Threading.Tasks.Parallel.ForEach(slopedCells, [SpecialName] (int cIdx) =>
			{
				_Closure_0024__51_002D4 capturedLocals3 = new _Closure_0024__51_002D4(null);
				capturedLocals3.outerClosure = capturedLocals2;
				capturedLocals3.cIdx = cIdx;
				capturedLocals3.cell = capturedLocals2.mesh.Cell(capturedLocals3.cIdx);
				if (!capturedLocals2.mesh.CellIsVirtual(capturedLocals3.cIdx))
				{
					bool isValueCreated = capturedLocals2.threadLocalCellLists.IsValueCreated;
					List<SlopingCellMap> slopingCellMaps = capturedLocals2.threadLocalCellLists.Value;
					if (!isValueCreated)
					{
						capturedLocals2.trackCellLists.Add(slopingCellMaps);
					}
					if (capturedLocals2.rasGeometryMapPoints._canTranslate)
					{
						SlopingCellMap slopingCellMap = null;
						capturedLocals2.translatableCells.TryGetValue(capturedLocals3.cIdx, out slopingCellMap);
						if (slopingCellMap != null)
						{
							List<SlopingCellPoint> mapPoints = slopingCellMap.MapPoints;
							int count = mapPoints.Count - 1;
							for (int i = 0; i <= count; i++)
							{
								SlopingCellPoint mapPoint = mapPoints[i];
								mapPoint.Index += capturedLocals2.rasGeometryMapPoints._indexSlide;
								if (mapPoint.Index >= 0 && mapPoint.Index < capturedLocals2.mask.Length)
								{
									capturedLocals2.mask[mapPoint.Index] = true;
									mapPoints[i] = mapPoint;
								}
							}
							slopingCellMaps.Add(slopingCellMap);
							return;
						}
					}
					if (capturedLocals2.mesh.SquareCells[capturedLocals3.cIdx])
					{
						List<SlopingCellPoint> bensWeightsSquareCellREUSE = capturedLocals2.rasGeometryMapPoints.BensWeightsSquareCellREUSE(capturedLocals2.mesh, capturedLocals3.cIdx, capturedLocals2.rast, capturedLocals2.borrowReusable, capturedLocals2.mask);
						if (!bensWeightsSquareCellREUSE.IsNullOrEmpty())
						{
							slopingCellMaps.Add(new SlopingCellMap(capturedLocals3.cIdx, bensWeightsSquareCellREUSE));
						}
					}
					else
					{
						_Closure_0024__51_002D1 capturedLocals4 = default;
						_Closure_0024__51_002D1 capturedLocals5 = new _Closure_0024__51_002D1(capturedLocals4);
						capturedLocals5.outerClosure = capturedLocals3;
						RasterizePolygon rasterizePolygon = capturedLocals2.threadLocalRasterizer.Value;
						capturedLocals5.pixelsInThisCell = new List<int>();
						new PointM(capturedLocals5.outerClosure.cell.Point.X, capturedLocals5.outerClosure.cell.Point.Y);
						Polygon polygon = capturedLocals2.mesh.CellPolygon(capturedLocals5.outerClosure.cIdx);
						rasterizePolygon.ComputeCells(polygon, [SpecialName] (int n) =>
						{
							if (!capturedLocals5.outerClosure.outerClosure.mask[n])
							{
								capturedLocals5.pixelsInThisCell.Add(n);
								capturedLocals5.outerClosure.outerClosure.mask[n] = true;
							}
						});
						if (capturedLocals5.pixelsInThisCell.Count != 0)
						{
							_Closure_0024__51_002D3 capturedLocals6 = default;
							_Closure_0024__51_002D3 capturedLocals7 = new _Closure_0024__51_002D3(capturedLocals6);
							capturedLocals7.outerClosure = capturedLocals5;
							capturedLocals7.mapArr = new SlopingCellPoint[capturedLocals7.outerClosure.pixelsInThisCell.Count - 1 + 1];
							if (capturedLocals7.outerClosure.outerClosure.cell.Faces.Count >= 2)
							{
								if (capturedLocals7.outerClosure.outerClosure.cell.Faces.Count == 2)
								{
									_Closure_0024__51_002D2 capturedLocals8 = default;
									_Closure_0024__51_002D2 capturedLocals9 = new _Closure_0024__51_002D2(capturedLocals8);
									capturedLocals9.outerClosure = capturedLocals7;
									Face face = capturedLocals2.mesh.Faces[capturedLocals9.outerClosure.outerClosure.outerClosure.cell.Faces[0]];
									FacePoint facePoint = capturedLocals2.mesh.FacePoints[face.GetFPPrev(capturedLocals9.outerClosure.outerClosure.outerClosure.cIdx)];
									FacePoint facePoint2 = capturedLocals2.mesh.FacePoints[face.GetFPNext(capturedLocals9.outerClosure.outerClosure.outerClosure.cIdx)];
									capturedLocals9.fp1Pt = facePoint.PointM();
									capturedLocals9.fp2Pt = facePoint2.PointM();
									Utility.Parallel.SmartFor(capturedLocals9.outerClosure.outerClosure.pixelsInThisCell.Count, [SpecialName] (int start, int finish) =>
									{
										for (int j = start; j <= finish; j++)
										{
											float[] floatValues = new float[2];
											float[] floatValuesArray = new float[4];
											int pixelsInThisCell = capturedLocals9.outerClosure.outerClosure.pixelsInThisCell[j];
											PointM pointM = capturedLocals9.outerClosure.outerClosure.outerClosure.outerClosure.rasGeometryMapPoints._raster.CellCenter(pixelsInThisCell);
											float distanceTo = (float)pointM.DistanceTo(capturedLocals9.fp1Pt);
											float distanceTo2 = (float)pointM.DistanceTo(capturedLocals9.fp2Pt);
											if (distanceTo == 0f)
											{
												floatValues[0] = 1f;
												floatValues[1] = 0f;
											}
											else if (distanceTo2 == 0f)
											{
												floatValues[0] = 0f;
												floatValues[1] = 1f;
											}
											else
											{
												float distanceToPlusDistanceTo2 = distanceTo + distanceTo2;
												float distanceToOverDistanceToPlusDistanceTo2 = distanceTo / distanceToPlusDistanceTo2;
												float distanceTo2OverDistanceToPlusDistanceTo2 = distanceTo2 / distanceToPlusDistanceTo2;
												floatValues[0] = 1f - distanceToOverDistanceToPlusDistanceTo2;
												floatValues[1] = 1f - distanceTo2OverDistanceToPlusDistanceTo2;
											}
											floatValuesArray[0] = floatValues[0];
											floatValuesArray[1] = floatValues[1];
											capturedLocals9.outerClosure.mapArr[j] = new SlopingCellPoint(pixelsInThisCell, floatValues, floatValuesArray);
										}
									}, 256);
								}
								else
								{
									Utility.Parallel.SmartFor(capturedLocals7.outerClosure.pixelsInThisCell.Count, [SpecialName] (int start, int finish) =>
									{
										float[] fpWeights = null;
										float[] velocityWeights = null;
										PixelComputeBuffer buf = new PixelComputeBuffer(capturedLocals7.outerClosure.outerClosure.cell.Faces.Count);
										for (int j = start; j <= finish; j++)
										{
											int pixelsInThisCell2 = capturedLocals7.outerClosure.pixelsInThisCell[j];
											capturedLocals7.outerClosure.outerClosure.outerClosure.rasGeometryMapPoints.BensWeights(capturedLocals7.outerClosure.outerClosure.outerClosure.rasGeometryMapPoints._raster.CellCenter(pixelsInThisCell2), capturedLocals7.outerClosure.outerClosure.outerClosure.mesh, capturedLocals7.outerClosure.outerClosure.cIdx, capturedLocals7.outerClosure.outerClosure.outerClosure.areaTolerance, buf, ref fpWeights, ref velocityWeights);
											capturedLocals7.mapArr[j] = new SlopingCellPoint(pixelsInThisCell2, fpWeights, velocityWeights);
										}
									}, 256);
								}
								slopingCellMaps.Add(new SlopingCellMap(capturedLocals7.outerClosure.outerClosure.cIdx, capturedLocals7.mapArr.ToList()));
							}
						}
					}
				}
			});
			int capacity = capturedLocals2.trackCellLists.Sum([SpecialName] (List<SlopingCellMap> slopingCellMapsList) => slopingCellMapsList.Count);
			List<SlopingCellMap> slopingCellMaps2 = new List<SlopingCellMap>(capacity);
			foreach (List<SlopingCellMap> slopingCellMaps3 in capturedLocals2.trackCellLists)
			{
				slopingCellMaps2.AddRange(slopingCellMaps3);
			}
			capturedLocals2.threadLocalCellLists.Dispose();
			capturedLocals2.threadLocalRasterizer.Dispose();
			return slopingCellMaps2;
		}
	}

	private List<SlopingCellPoint> BensWeightsSquareCellREUSE(MeshFV2D mesh, int cIdx, RasterM backgroundRaster, Func<int, int> borrowReusablePixels, BitArray mask)
	{
		_Closure_0024__52_002D0 capturedLocals = default;
		_Closure_0024__52_002D0 capturedLocals2 = new _Closure_0024__52_002D0(capturedLocals);
		capturedLocals2.mesh = mesh;
		List<SlopingCellPoint> slopingCellPoints = new List<SlopingCellPoint>();
		Point2Double point = capturedLocals2.mesh.Cell(cIdx).Point;
		List<Point2Double> point2Doubles = (from fpidx in capturedLocals2.mesh.CellFacePoints(cIdx)
			select capturedLocals2.mesh.FacePoints[fpidx].Point).ToList();
		double systemPoint2DoublesTerm = System.Math.Min(point2Doubles[0].X, point2Doubles[2].X);
		double systemPoint2DoublesTerm2 = System.Math.Max(point2Doubles[0].Y, point2Doubles[2].Y);
		double systemPoint2DoublesTerm3 = System.Math.Max(point2Doubles[0].X, point2Doubles[2].X);
		double systemPoint2DoublesTerm4 = System.Math.Min(point2Doubles[0].Y, point2Doubles[2].Y);
		_ = (float)(systemPoint2DoublesTerm3 - systemPoint2DoublesTerm);
		double computed = systemPoint2DoublesTerm3 - systemPoint2DoublesTerm;
		double computed2 = systemPoint2DoublesTerm2 - systemPoint2DoublesTerm4;
		int[] intValues = new int[4];
		int ordinal = 0;
		checked
		{
			do
			{
				Point2Double point2Double = point2Doubles[ordinal];
				if (point2Double.X <= point.X && point2Double.Y <= point.Y)
				{
					intValues[0] = ordinal;
				}
				else if (point2Double.X <= point.X && point2Double.Y >= point.Y)
				{
					intValues[1] = ordinal;
				}
				else if (point2Double.X >= point.X && point2Double.Y <= point.Y)
				{
					intValues[3] = ordinal;
				}
				else
				{
					intValues[2] = ordinal;
				}
				ordinal++;
			}
			while (ordinal <= 3);
			int leftIdx = default;
			int rightIdx = default;
			int topIdx = default;
			int botIdx = default;
			new RasterizeRectangle(backgroundRaster).ComputeCells(point2Doubles, ref leftIdx, ref rightIdx, ref topIdx, ref botIdx);
			if (rightIdx < 0)
			{
				return null;
			}
			if (leftIdx >= backgroundRaster.Cols)
			{
				return null;
			}
			if (botIdx < 0)
			{
				return null;
			}
			if (topIdx >= backgroundRaster.Rows)
			{
				return null;
			}
			if (leftIdx < 0)
			{
				leftIdx = 0;
			}
			if (rightIdx >= backgroundRaster.Cols)
			{
				rightIdx = backgroundRaster.Cols - 1;
			}
			if (topIdx < 0)
			{
				topIdx = 0;
			}
			if (botIdx >= backgroundRaster.Rows)
			{
				botIdx = backgroundRaster.Rows - 1;
			}
			int rightIdxLeftIdxTerm = rightIdx - leftIdx + 1;
			int botIdxTopIdxTerm = botIdx - topIdx + 1;
			int ordinal2 = rightIdxLeftIdxTerm * botIdxTopIdxTerm;
			int borrowReusablePixels2 = borrowReusablePixels(ordinal2);
			List<SlopingCellPoint> slopingCellPointsList;
			if (borrowReusablePixels2 == -1)
			{
				slopingCellPointsList = new List<SlopingCellPoint>();
				borrowReusablePixels2 = 0;
				int count = ordinal2 - 1;
				for (int ordinal3 = 0; ordinal3 <= count; ordinal3++)
				{
					slopingCellPointsList.Add(new SlopingCellPoint(-1, new float[4] { 1f, 2f, 3f, 4f }, new float[8] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f }));
				}
			}
			else
			{
				slopingCellPointsList = _reusableSquarePixels;
			}
			double cellCenterX = backgroundRaster.CellCenterX(leftIdx);
			double cellCenterY = backgroundRaster.CellCenterY(topIdx);
			double cellCenterXTerm = (cellCenterX - systemPoint2DoublesTerm) / computed;
			double cellCenterYTerm = (cellCenterY - systemPoint2DoublesTerm4) / computed2;
			double cellSizeBackgroundRasterTerm = backgroundRaster.CellSize / computed;
			double cellSizeBackgroundRasterTerm2 = -1.0 * backgroundRaster.CellSize / computed2;
			PixelComputeBuffer buf = new PixelComputeBuffer(4);
			double[] doubleValues = new double[rightIdx - leftIdx + 1];
			double[] doubleValuesArray = new double[rightIdx - leftIdx + 1];
			int leftIdx2 = leftIdx;
			int rightIdx2 = rightIdx;
			for (int leftIdx3 = leftIdx2; leftIdx3 <= rightIdx2; leftIdx3++)
			{
				double leftIdx3LeftIdxTerm = cellCenterXTerm + cellSizeBackgroundRasterTerm * (double)(leftIdx3 - leftIdx);
				if (leftIdx3LeftIdxTerm <= 0.0)
				{
					leftIdx3LeftIdxTerm = 1E-05;
				}
				if (leftIdx3LeftIdxTerm >= 1.0)
				{
					leftIdx3LeftIdxTerm = 0.99999;
				}
				doubleValues[leftIdx3 - leftIdx] = leftIdx3LeftIdxTerm;
				doubleValuesArray[leftIdx3 - leftIdx] = 1.0 - leftIdx3LeftIdxTerm;
			}
			int topIdx2 = topIdx;
			int botIdx2 = botIdx;
			for (int topIdx3 = topIdx2; topIdx3 <= botIdx2; topIdx3++)
			{
				int topIdx3AboveTopIdx = topIdx3 - topIdx;
				double topIdx3TopIdxTerm = cellCenterYTerm + cellSizeBackgroundRasterTerm2 * (double)(topIdx3 - topIdx);
				if (topIdx3TopIdxTerm <= 0.0)
				{
					topIdx3TopIdxTerm = 1E-05;
				}
				if (topIdx3TopIdxTerm >= 1.0)
				{
					topIdx3TopIdxTerm = 0.99999;
				}
				double computed3 = 1.0 - topIdx3TopIdxTerm;
				int leftIdx4 = leftIdx;
				int rightIdx3 = rightIdx;
				for (int leftIdx5 = leftIdx4; leftIdx5 <= rightIdx3; leftIdx5++)
				{
					int leftIdx5AboveLeftIdx = leftIdx5 - leftIdx;
					int ordinal4 = topIdx3AboveTopIdx * rightIdxLeftIdxTerm + leftIdx5AboveLeftIdx;
					int index = backgroundRaster.CellIndex(topIdx3, leftIdx5);
					if (!mask[index])
					{
						mask[index] = true;
						SlopingCellPoint slopingCellPoint = slopingCellPointsList[borrowReusablePixels2 + ordinal4];
						slopingCellPoint.Index = index;
						float[] fPPrevWeights = slopingCellPoint.FPPrevWeights;
						float[] velocityWeights = slopingCellPoint.VelocityWeights;
						double leftIdx5LeftIdxTerm = doubleValuesArray[leftIdx5 - leftIdx];
						double leftIdx5LeftIdxTerm2 = doubleValues[leftIdx5 - leftIdx];
						fPPrevWeights[intValues[0]] = (float)(leftIdx5LeftIdxTerm * computed3);
						fPPrevWeights[intValues[1]] = (float)(leftIdx5LeftIdxTerm * topIdx3TopIdxTerm);
						fPPrevWeights[intValues[2]] = (float)(leftIdx5LeftIdxTerm2 * topIdx3TopIdxTerm);
						fPPrevWeights[intValues[3]] = (float)(leftIdx5LeftIdxTerm2 * computed3);
						Donate(fPPrevWeights, velocityWeights, buf);
						slopingCellPoints.Add(slopingCellPoint);
					}
				}
			}
			return slopingCellPoints;
		}
	}

	private void BensWeights(PointM point, MeshFV2D mesh, int cIdx, float areaTolerance, PixelComputeBuffer buf, ref float[] fpWeights, ref float[] velocityWeights)
	{
		Cell cell = mesh.Cell(cIdx);
		int count = cell.Faces.Count;
		int count2 = count;
		float[] pixelXProducts = GetPixelXProducts(mesh, cIdx, point, buf);
		checked
		{
			int count3 = pixelXProducts.Length - 1;
			for (int i = 0; i <= count3; i++)
			{
				if (pixelXProducts[i] == 0f)
				{
					pixelXProducts[i] = 1E-05f;
				}
			}
			double[] doubleValues = new double[count2 - 1 + 1];
			double computed = 0.0;
			int count4 = count2 - 1;
			for (int j = 0; j <= count4; j++)
			{
				int ordinal = j;
				int count2Offset = j - 1;
				if (count2Offset == -1)
				{
					count2Offset = count2 - 1;
				}
				double unity = 1.0;
				int count5 = count2 - 1;
				for (int k = 0; k <= count5; k++)
				{
					if (k != ordinal && k != count2Offset)
					{
						unity *= (double)pixelXProducts[k];
					}
				}
				int index = j;
				int count2Offset2 = j - 1;
				if (count2Offset2 == -1)
				{
					count2Offset2 = count2 - 1;
				}
				int index2 = cell.Faces[index];
				int index3 = cell.Faces[count2Offset2];
				int fPPrev = mesh.Faces[index3].GetFPPrev(cIdx);
				int fPNext = mesh.Faces[index3].GetFPNext(cIdx);
				int fPNext2 = mesh.Faces[index2].GetFPNext(cIdx);
				Point2Double point2 = mesh.FacePoints[fPPrev].Point;
				Point2Double point3 = mesh.FacePoints[fPNext].Point;
				Point2Double point4 = mesh.FacePoints[fPNext2].Point;
				computed += (doubleValues[j] = unity * Vector.CrossProduct(point3, point4, point2));
			}
			fpWeights = new float[count2 - 1 + 1];
			bool condition = false;
			int count6 = count2 - 1;
			for (int l = 0; l <= count6; l++)
			{
				if (doubleValues[l] < 0.0)
				{
					condition = true;
				}
				fpWeights[l] = (float)(doubleValues[l] / computed);
			}
			velocityWeights = new float[count * 2 - 1 + 1];
			if (condition)
			{
				float fpWeightSum = 0f;
				int count7 = fpWeights.Count() - 1;
				for (int m = 0; m <= count7; m++)
				{
					if (fpWeights[m] < 0f)
					{
						fpWeights[m] = 0f;
					}
					else
					{
						fpWeightSum += fpWeights[m];
					}
				}
				if (fpWeightSum != 0f)
				{
					int count8 = count2 - 1;
					for (int n = 0; n <= count8; n++)
					{
						fpWeights[n] /= fpWeightSum;
						velocityWeights[n] = fpWeights[n];
					}
				}
			}
			Donate(fpWeights, velocityWeights, buf);
		}
	}

	private void Donate(float[] fpWeights, float[] velocityWeights, PixelComputeBuffer buf)
	{
		int count = buf.Count;
		float[] cWCanGive = buf.CWCanGive;
		float[] cCWCanGive = buf.CCWCanGive;
		checked
		{
			int count2 = count - 1;
			for (int i = 0; i <= count2; i++)
			{
				int countOffset = i - 1;
				if (countOffset == -1)
				{
					countOffset = count - 1;
				}
				int ordinal = i + 1;
				if (ordinal == count)
				{
					ordinal = 0;
				}
				float fpWeight = fpWeights[i];
				float fpWeight2 = fpWeights[countOffset];
				float fpWeight3 = fpWeights[ordinal];
				float fpWeight2PlusFpWeight3 = fpWeight2 + fpWeight3;
				if (fpWeight2PlusFpWeight3 != 0f)
				{
					float fpWeightOverFpWeight2PlusFpWeight3 = fpWeight / fpWeight2PlusFpWeight3;
					cWCanGive[i] = fpWeightOverFpWeight2PlusFpWeight3 * fpWeight2;
					cCWCanGive[i] = fpWeightOverFpWeight2PlusFpWeight3 * fpWeight3;
				}
				velocityWeights[i] = fpWeights[i];
			}
			int count3 = count - 1;
			for (int j = 0; j <= count3; j++)
			{
				int ordinal2 = j;
				int ordinal3 = j + 1;
				if (ordinal3 == count)
				{
					ordinal3 = 0;
				}
				_ = fpWeights[ordinal2];
				_ = fpWeights[ordinal3];
				float cCWCanGive2 = cCWCanGive[ordinal2];
				float cWCanGive2 = cWCanGive[ordinal3];
				float systemCCWCanGive2Term = System.Math.Min(cCWCanGive2, cWCanGive2);
				velocityWeights[ordinal2] -= systemCCWCanGive2Term;
				velocityWeights[ordinal3] -= systemCCWCanGive2Term;
				velocityWeights[count + j] = systemCCWCanGive2Term * 2f;
			}
		}
	}

	private float[] GetPixelXProducts(MeshFV2D mesh, int cIdx, PointM p, PixelComputeBuffer buf)
	{
		Cell cell = mesh.Cell(cIdx);
		int count = cell.Faces.Count;
		float[] xProducts = buf.XProducts;
		Point2Double a = new Point2Double(p);
		checked
		{
			int count2 = count - 1;
			for (int i = 0; i <= count2; i++)
			{
				int index = cell.Faces[i];
				Face face = mesh.Faces[index];
				int fPPrev = face.GetFPPrev(cIdx);
				int fPNext = face.GetFPNext(cIdx);
				Point2Double point = mesh.FacePoints[fPPrev].Point;
				Point2Double point2 = mesh.FacePoints[fPNext].Point;
				xProducts[i] = (float)Vector.CrossProduct(a, point, point2);
			}
			return xProducts;
		}
	}

	private float[] GetPixelXProducts(MeshFV2D mesh, int cIdx, List<PointM> points, PixelComputeBuffer[] buf)
	{
		Cell cell = mesh.Cell(cIdx);
		int count = cell.Faces.Count;
		float[] xProducts = buf[0].XProducts;
		PointM ptM = points.First();
		Point2Double a = new Point2Double(ptM);
		checked
		{
			int count2 = count - 1;
			for (int i = 0; i <= count2; i++)
			{
				int index = cell.Faces[i];
				Face face = mesh.Faces[index];
				int fPPrev = face.GetFPPrev(cIdx);
				int fPNext = face.GetFPNext(cIdx);
				Point2Double point = mesh.FacePoints[fPPrev].Point;
				Point2Double point2 = mesh.FacePoints[fPNext].Point;
				xProducts[i] = (float)Vector.CrossProduct(a, point, point2);
			}
			return xProducts;
		}
	}

	private void ComputeUniqueFacesAndFacepoints(List<int> cellList, MeshFV2D mesh, ref List<int> uniqueFPs, ref List<int> uniqueFaces)
	{
		if ((double)cellList.Count > 0.1 * (double)mesh.CellCount || cellList.Count == 0)
		{
			uniqueFPs = null;
			uniqueFaces = null;
			return;
		}
		uniqueFPs = new List<int>();
		uniqueFaces = new List<int>();
		HashSet<int> intSet = new HashSet<int>();
		HashSet<int> intSetSet = new HashSet<int>();
		foreach (int cell in cellList)
		{
			mesh.Cell(cell);
			List<int> cellFacePoints = mesh.CellFacePoints(cell);
			foreach (int cellFacePoint in cellFacePoints)
			{
				if (!intSet.Add(cellFacePoint))
				{
					continue;
				}
				foreach (int face in mesh.FacePoints[cellFacePoint].Faces)
				{
					intSetSet.Add(face);
				}
			}
		}
		uniqueFPs = intSet.ToList();
		uniqueFaces = intSetSet.ToList();
	}

	private void RasterizeCrossSectionRegions(BitArray mask)
	{
		_Closure_0024__59_002D0 capturedLocals = new _Closure_0024__59_002D0(null);
		capturedLocals.rasGeometryMapPoints = this;
		capturedLocals.mask = mask;
		RasterizeTriangle rasterizeTriangle = new RasterizeTriangle(Raster);
		checked
		{
			_Closure_0024__59_002D1 capturedLocals2 = default;
			PointM p = default;
			PointM p2 = default;
			PointM p3 = default;
			foreach (int renderOrder in _XSIS.GetRenderOrder())
			{
				capturedLocals2 = new _Closure_0024__59_002D1(capturedLocals2);
				capturedLocals2.outerClosure = capturedLocals;
				Tin normalizedMTIN = _XSIS.GetNormalizedMTIN(renderOrder);
				if (Raster.Overlaps(normalizedMTIN))
				{
					capturedLocals2.lambdafID = renderOrder;
					int count = normalizedMTIN.TriangleCount() - 1;
					for (int i = 0; i <= count; i++)
					{
						normalizedMTIN.TrianglePointMs(i, ref p, ref p2, ref p3);
						rasterizeTriangle.ComputeCellsZMandDirection(p, p2, p3, capturedLocals2._Lambda_0024__0);
					}
				}
			}
		}
	}

	private List<SlopingCellPoint> BensWeightsSquareCell_OLD_NOREUSE(MeshFV2D mesh, int cIdx, RasterM backgroundRaster)
	{
		_Closure_0024__60_002D0 capturedLocals = default;
		_Closure_0024__60_002D0 capturedLocals2 = new _Closure_0024__60_002D0(capturedLocals);
		capturedLocals2.mesh = mesh;
		List<SlopingCellPoint> slopingCellPoints = new List<SlopingCellPoint>();
		Point2Double point = capturedLocals2.mesh.Cell(cIdx).Point;
		List<Point2Double> point2Doubles = (from fpidx in capturedLocals2.mesh.CellFacePoints(cIdx)
			select capturedLocals2.mesh.FacePoints[fpidx].Point).ToList();
		double systemPoint2DoublesTerm = System.Math.Min(point2Doubles[0].X, point2Doubles[2].X);
		double systemPoint2DoublesTerm2 = System.Math.Max(point2Doubles[0].Y, point2Doubles[2].Y);
		double systemPoint2DoublesTerm3 = System.Math.Max(point2Doubles[0].X, point2Doubles[2].X);
		double systemPoint2DoublesTerm4 = System.Math.Min(point2Doubles[0].Y, point2Doubles[2].Y);
		_ = (float)(systemPoint2DoublesTerm3 - systemPoint2DoublesTerm);
		double computed = systemPoint2DoublesTerm3 - systemPoint2DoublesTerm;
		double computed2 = systemPoint2DoublesTerm2 - systemPoint2DoublesTerm4;
		int[] intValues = new int[4];
		int ordinal = 0;
		checked
		{
			do
			{
				Point2Double point2Double = point2Doubles[ordinal];
				if (point2Double.X <= point.X && point2Double.Y <= point.Y)
				{
					intValues[0] = ordinal;
				}
				else if (point2Double.X <= point.X && point2Double.Y >= point.Y)
				{
					intValues[1] = ordinal;
				}
				else if (point2Double.X >= point.X && point2Double.Y <= point.Y)
				{
					intValues[3] = ordinal;
				}
				else
				{
					intValues[2] = ordinal;
				}
				ordinal++;
			}
			while (ordinal <= 3);
			int leftIdx = default;
			int rightIdx = default;
			int topIdx = default;
			int botIdx = default;
			new RasterizeRectangle(backgroundRaster).ComputeCells(point2Doubles, ref leftIdx, ref rightIdx, ref topIdx, ref botIdx);
			if (rightIdx < 0)
			{
				return null;
			}
			if (leftIdx >= backgroundRaster.Cols)
			{
				return null;
			}
			if (botIdx < 0)
			{
				return null;
			}
			if (topIdx >= backgroundRaster.Rows)
			{
				return null;
			}
			if (leftIdx < 0)
			{
				leftIdx = 0;
			}
			if (rightIdx >= backgroundRaster.Cols)
			{
				rightIdx = backgroundRaster.Cols - 1;
			}
			if (topIdx < 0)
			{
				topIdx = 0;
			}
			if (botIdx >= backgroundRaster.Rows)
			{
				botIdx = backgroundRaster.Rows - 1;
			}
			double cellCenterX = backgroundRaster.CellCenterX(leftIdx);
			double cellCenterY = backgroundRaster.CellCenterY(topIdx);
			double cellCenterXTerm = (cellCenterX - systemPoint2DoublesTerm) / computed;
			double cellCenterYTerm = (cellCenterY - systemPoint2DoublesTerm4) / computed2;
			double cellSizeBackgroundRasterTerm = backgroundRaster.CellSize / computed;
			double cellSizeBackgroundRasterTerm2 = -1.0 * backgroundRaster.CellSize / computed2;
			double[] doubleValues = new double[rightIdx - leftIdx + 1];
			double[] doubleValuesArray = new double[rightIdx - leftIdx + 1];
			int leftIdx2 = leftIdx;
			int rightIdx2 = rightIdx;
			for (int leftIdx3 = leftIdx2; leftIdx3 <= rightIdx2; leftIdx3++)
			{
				double leftIdx3LeftIdxTerm = cellCenterXTerm + cellSizeBackgroundRasterTerm * (double)(leftIdx3 - leftIdx);
				if (leftIdx3LeftIdxTerm <= 0.0)
				{
					leftIdx3LeftIdxTerm = 1E-05;
				}
				if (leftIdx3LeftIdxTerm >= 1.0)
				{
					leftIdx3LeftIdxTerm = 0.99999;
				}
				doubleValues[leftIdx3 - leftIdx] = leftIdx3LeftIdxTerm;
				doubleValuesArray[leftIdx3 - leftIdx] = 1.0 - leftIdx3LeftIdxTerm;
			}
			int topIdx2 = topIdx;
			int botIdx2 = botIdx;
			for (int topIdx3 = topIdx2; topIdx3 <= botIdx2; topIdx3++)
			{
				double topIdx3TopIdxTerm = cellCenterYTerm + cellSizeBackgroundRasterTerm2 * (double)(topIdx3 - topIdx);
				if (topIdx3TopIdxTerm <= 0.0)
				{
					topIdx3TopIdxTerm = 1E-05;
				}
				if (topIdx3TopIdxTerm >= 1.0)
				{
					topIdx3TopIdxTerm = 0.99999;
				}
				double computed3 = 1.0 - topIdx3TopIdxTerm;
				int leftIdx4 = leftIdx;
				int rightIdx3 = rightIdx;
				for (int leftIdx5 = leftIdx4; leftIdx5 <= rightIdx3; leftIdx5++)
				{
					double leftIdx5LeftIdxTerm = doubleValuesArray[leftIdx5 - leftIdx];
					double leftIdx5LeftIdxTerm2 = doubleValues[leftIdx5 - leftIdx];
					float[] floatValues = new float[4];
					float[] floatValuesArray = new float[4];
					floatValues[intValues[0]] = (float)(leftIdx5LeftIdxTerm * computed3);
					floatValues[intValues[1]] = (float)(leftIdx5LeftIdxTerm * topIdx3TopIdxTerm);
					floatValues[intValues[2]] = (float)(leftIdx5LeftIdxTerm2 * topIdx3TopIdxTerm);
					floatValues[intValues[3]] = (float)(leftIdx5LeftIdxTerm2 * computed3);
					floatValuesArray[intValues[2]] = (float)(1.0 / computed3);
					floatValuesArray[intValues[1]] = (float)(1.0 / leftIdx5LeftIdxTerm2);
					floatValuesArray[intValues[0]] = (float)(1.0 / topIdx3TopIdxTerm);
					floatValuesArray[intValues[3]] = (float)(1.0 / leftIdx5LeftIdxTerm);
					float computed4 = floatValuesArray[0] + floatValuesArray[1] + floatValuesArray[2] + floatValuesArray[3];
					floatValuesArray[0] /= computed4;
					floatValuesArray[1] /= computed4;
					floatValuesArray[2] /= computed4;
					floatValuesArray[3] /= computed4;
					slopingCellPoints.Add(new SlopingCellPoint(backgroundRaster.CellIndex(topIdx3, leftIdx5), floatValues, floatValuesArray));
				}
			}
			return slopingCellPoints;
		}
	}

	public void ComputeForPoint(PointM point)
	{
		PointMs pointMs = new PointMs();
		pointMs.Add(point);
		ComputeForPoints(pointMs);
	}

	public void ComputeForPoints(IPoints points, bool reportTime = false, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		_queryType = QueryType.Points;
		_raster = null;
		_pointCount = points.PointMCount();
		ClearStoredLists();
		checked
		{
			if (points.PointMCount() == 0 || _XSIS.FeatureCount() + _SA.FeatureCount() + _D2.FeatureCount() == 0)
			{
				return;
			}
			Extent extent = points.ComputeExtent();
			points.PointMCount();
			_ = 10;
			BitArray mask = new BitArray(points.PointMCount());
			Extent extent2 = extent.IntersectionXY(_geometry.Extent);
			if (extent2 != null)
			{
				if (extent2.Width == 0.0 || extent2.Height == 0.0)
				{
					extent2 = extent2.Offset(0.01, 0.01);
				}
				DistinctSpatialIndex distinctSpatialIndex = new DistinctSpatialIndex(extent2, points.PointMCount() - 1);
				distinctSpatialIndex.AddRangeInBounds(points);
				Compute2DPointsSloped(points, distinctSpatialIndex, mask, reportTime, reporter);
				ComputeSAPoints(points, distinctSpatialIndex, mask, reportTime);
				ComputeXSISPoints(points, distinctSpatialIndex, mask, reportTime);
			}
		}
	}

	private void ComputeSAPoints(IPoints ipts, DistinctSpatialIndex spatialExtent, BitArray mask, bool reportTime)
	{
		if (_SA.Extent == null || !_SA.Extent.MBROverlaps(spatialExtent.Extent))
		{
			return;
		}
		if (reportTime)
		{
			MeasureTimeStart();
		}
		checked
		{
			int count = _SA.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Polygon polygon = (Polygon)_SA.Feature(i);
				if (!polygon.MBROverlaps(spatialExtent.Extent))
				{
					continue;
				}
				int count2 = ipts.PointMCount() - 1;
				for (int j = 0; j <= count2; j++)
				{
					if (mask[j])
					{
						continue;
					}
					PointM pointM = ipts.PointM(j);
					if (polygon.Contains(pointM))
					{
						if (MappedSAs.Count == 0 || MappedSAs.Last().ID != i)
						{
							MappedSAs.Add(new SAMap(i));
						}
						MappedSAs.Last().MapPoints.Add(j);
						mask[j] = true;
					}
				}
			}
			if (reportTime)
			{
				MeasureTimeStop("Finding points in Storage Area");
			}
		}
	}

	private void Compute2DPointsSloped(IPoints ipts, DistinctSpatialIndex spatialExtent, BitArray mask, bool reportTime, ProgressReporter reporter = null)
	{
		if (reportTime)
		{
			MeasureTimeStart();
		}
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		if (_D2.Extent == null || !_D2.Extent.MBROverlaps(spatialExtent.Extent))
		{
			return;
		}
		int featureCount = _D2.FeatureCount();
		checked
		{
			int featureCountOffset = featureCount - 1;
			int ordinal = 0;
			while (true)
			{
				if (ordinal <= featureCountOffset)
				{
					MeshFV2D meshFV2D = _D2.Mesh(ordinal);
					if (meshFV2D.IsComplete() && meshFV2D.MBROverlaps(spatialExtent.Extent))
					{
						DistinctSpatialIndex meshCellSpatialIndex = SharedData.GetMeshCellSpatialIndex(meshFV2D);
						Dictionary<int, FlatCellMap> flatCellMapByInt = new Dictionary<int, FlatCellMap>();
						float featureCountTerm = (float)ordinal / (float)featureCount;
						int pointMCount = ipts.PointMCount();
						int count = pointMCount - 1;
						for (int i = 0; i <= count; i++)
						{
							if (mask[i])
							{
								continue;
							}
							PointM pointM = ipts.PointM(i);
							List<int> elements = meshCellSpatialIndex.GetElements(pointM.X, pointM.Y);
							foreach (int element in elements)
							{
								meshFV2D.Cell(element);
								Polygon polygon = meshFV2D.CellPolygon(element);
								if (polygon != null && polygon.ContainsFuzzy(pointM.X, pointM.Y))
								{
									if (flatCellMapByInt.ContainsKey(element))
									{
										flatCellMapByInt[element].MapPoints.Add(i);
									}
									else
									{
										flatCellMapByInt.Add(element, new FlatCellMap(element, new List<int>(new int[1] { i })));
									}
									mask[i] = true;
									break;
								}
							}
							if (unchecked(i % 1000) == 0)
							{
								float pointMCountTerm = (float)(i + 1) / (float)pointMCount;
								reporter.ReportProgress(100 * (int)System.Math.Round(featureCountTerm + pointMCountTerm / (float)featureCount));
								if (reporter.CancelRequested)
								{
									return;
								}
							}
						}
						if (flatCellMapByInt.Count > 0)
						{
							List<SlopingCellMap> d2MapCells = ConvertFlatToSloped(meshFV2D, ipts, flatCellMapByInt.Values.ToList());
							List<int> uniqueFPs = null;
							List<int> uniqueFaces = null;
							ComputeUniqueFacesAndFacepoints(flatCellMapByInt.Values.Select([SpecialName] (FlatCellMap ss) => ss.Index).ToList(), meshFV2D, ref uniqueFPs, ref uniqueFaces);
							_slopingAreas.Add(new SlopingMeshMap(ordinal, d2MapCells, uniqueFaces, uniqueFPs));
						}
					}
					ordinal++;
					continue;
				}
				if (reportTime)
				{
					MeasureTimeStop("Finding points in 2D Flow Areas");
				}
				break;
			}
		}
	}

	private List<SlopingCellMap> ConvertFlatToSloped(MeshFV2D mesh, IPoints ipts, List<FlatCellMap> flatVals)
	{
		List<SlopingCellMap> slopingCellMaps = new List<SlopingCellMap>();
		float areaTolerance = (float)mesh.AreaTolerance;
		foreach (FlatCellMap flatVal in flatVals)
		{
			List<SlopingCellPoint> slopingCellPoints = new List<SlopingCellPoint>();
			PixelComputeBuffer buf = new PixelComputeBuffer(mesh.Cell(flatVal.Index).Faces.Count);
			foreach (int mapPoint in flatVal.MapPoints)
			{
				float[] fpWeights = null;
				float[] velocityWeights = null;
				BensWeights(ipts.PointM(mapPoint), mesh, flatVal.Index, areaTolerance, buf, ref fpWeights, ref velocityWeights);
				slopingCellPoints.Add(new SlopingCellPoint(mapPoint, fpWeights, velocityWeights));
			}
			slopingCellMaps.Add(new SlopingCellMap(flatVal.Index, slopingCellPoints));
		}
		return slopingCellMaps;
	}

	private void Compute2DPointsFlat(IPoints ipts, Extent ext, BitArray mask)
	{
		if (_D2.Extent == null || !_D2.Extent.MBROverlaps(ext))
		{
			return;
		}
		checked
		{
			int count = _D2.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				MeshFV2D meshFV2D = _D2.Mesh(i);
				if (!meshFV2D.IsComplete() || !meshFV2D.MBROverlaps(ext))
				{
					continue;
				}
				DistinctSpatialIndex meshCellSpatialIndex = SharedData.GetMeshCellSpatialIndex(meshFV2D);
				Dictionary<int, FlatCellMap> flatCellMapByInt = new Dictionary<int, FlatCellMap>();
				int count2 = ipts.PointMCount() - 1;
				for (int j = 0; j <= count2; j++)
				{
					if (mask[j])
					{
						continue;
					}
					PointM pointM = ipts.PointM(j);
					List<int> elements = meshCellSpatialIndex.GetElements(pointM.X, pointM.Y);
					foreach (int element in elements)
					{
						meshFV2D.Cell(element);
						Polygon polygon = meshFV2D.CellPolygon(element);
						if (polygon != null && polygon.Contains(pointM))
						{
							if (flatCellMapByInt.ContainsKey(element))
							{
								flatCellMapByInt[element].MapPoints.Add(j);
							}
							else
							{
								flatCellMapByInt.Add(element, new FlatCellMap(element, new List<int>(new int[1] { j })));
							}
							mask[j] = true;
						}
					}
				}
				if (flatCellMapByInt.Count > 0)
				{
					_flatAreas.Add(new FlatMeshMap(i, flatCellMapByInt.Values.ToList(), null, null));
				}
			}
		}
	}

	private void ComputeXSISPoints(IPoints ipts, DistinctSpatialIndex spatialExtent, BitArray mask, bool reportTime)
	{
		if (_XSIS.Extent == null || !_XSIS.Extent.MBROverlaps(spatialExtent.Extent))
		{
			return;
		}
		if (reportTime)
		{
			MeasureTimeStart();
		}
		checked
		{
			int count = _XSIS.FeatureCount() - 1;
			double Z = default;
			double M = default;
			for (int i = 0; i <= count; i++)
			{
				Tin normalizedMTIN = _XSIS.GetNormalizedMTIN(i);
				if (!normalizedMTIN.MBROverlaps(spatialExtent.Extent))
				{
					continue;
				}
				List<int> elements = spatialExtent.GetElements(normalizedMTIN);
				SpatialIndex<int> spatialIndex = new SpatialIndex<int>(normalizedMTIN, 64);
				foreach (int element2 in elements)
				{
					spatialIndex.AddInBounds(ipts.PointM(element2), element2);
				}
				int count2 = normalizedMTIN.TriangleCount() - 1;
				for (int j = 0; j <= count2; j++)
				{
					Extent extent = normalizedMTIN.TriangleExtent(j);
					foreach (int element in spatialIndex.GetElements(extent))
					{
						if (mask[element])
						{
							continue;
						}
						PointM pointM = ipts.PointM(element);
						if (!extent.Contains(pointM))
						{
							continue;
						}
						TriangleShape triangleShape = normalizedMTIN.TriangleShape(j);
						triangleShape.tolerance = 0.0001;
						if (triangleShape.ComputeZM(pointM.X, pointM.Y, ref Z, ref M))
						{
							if (MappedXSISs.Count == 0 || MappedXSISs.Last().ID != i)
							{
								MappedXSISs.Add(new MappedXSIS(i));
							}
							MappedXSISs.Last().Add(element, (float)Z, (float)M);
							mask[element] = true;
						}
					}
				}
			}
			if (reportTime)
			{
				MeasureTimeStop("Finding points in XS Interpolation Surface Regions");
			}
		}
	}

	public void MeasureTimeStart()
	{
		if (_stopWatch == null)
		{
			_stopWatch = new Stopwatch();
		}
		else
		{
			_stopWatch.Reset();
		}
		_stopWatch.Start();
	}

	public void MeasureTimeStop(string label)
	{
		_stopWatch.Stop();
	}

	private bool LineInitialize()
	{
		_queryType = QueryType.Line;
		ClearStoredLists();
		if (checked(_XSIS.FeatureCount() + _SA.FeatureCount() + _D2.FeatureCount()) == 0)
		{
			return false;
		}
		return true;
	}

	public void ComputeForLine(Polyline pline, ref List<PointOnPolyline> intersections, double maxSegmentLen = double.NaN)
	{
		if (LineInitialize())
		{
			intersections = ComputeGeometryIntersections(pline);
			if (!double.IsNaN(maxSegmentLen))
			{
				SubsampleIntersections(pline, ref intersections, maxSegmentLen);
			}
			ComputeForPoints(new PointMs(intersections.Select([SpecialName] (PointOnPolyline pop) => pop.PointM)));
		}
	}

	public void ComputeForLine(Polyline pline, TerrainLayer cutTerrain, ref List<PointOnPolyline> intersections, double maxSegmentLen = double.NaN)
	{
		if (!LineInitialize())
		{
			return;
		}
		intersections = ComputeGeometryIntersections(pline);
		if (cutTerrain != null)
		{
			Profile source = cutTerrain.ComputeProfile(pline, (float)_geometry.FilterToleranceXY);
			intersections.AddRange(source.Select([SpecialName] (Point2D p2d) => pline.PointAtDistance(p2d.X)));
			intersections = intersections.OrderBy([SpecialName] (PointOnPolyline pop) => pline.DistanceAlong(pop)).ToList();
		}
		if (!double.IsNaN(maxSegmentLen))
		{
			SubsampleIntersections(pline, ref intersections, maxSegmentLen);
		}
		ComputeForPoints(new PointMs(intersections.Select([SpecialName] (PointOnPolyline pop) => pop.PointM)));
	}

	public void ComputeForLine(Polyline pline, List<PointOnPolyline> extraPoints, ref List<PointOnPolyline> intersections, double maxSegmentLen = double.NaN, double dedupeLength = 0.01)
	{
		if (!LineInitialize())
		{
			return;
		}
		intersections = ComputeGeometryIntersections(pline, ensureSorted: false);
		if (extraPoints != null)
		{
			intersections.AddRange(extraPoints);
		}
		intersections = PointOnPolyline.Sort(intersections);
		if (!double.IsNaN(maxSegmentLen))
		{
			SubsampleIntersections(pline, ref intersections, maxSegmentLen);
		}
		checked
		{
			if (dedupeLength > 0.0 && !double.IsNaN(dedupeLength))
			{
				List<PointOnPolyline> intersectionsList = new List<PointOnPolyline>();
				intersectionsList.Add(intersections.First());
				int count = intersections.Count - 1;
				for (int i = 1; i <= count; i++)
				{
					PointOnPolyline pointOnPolyline = intersectionsList.Last();
					PointOnPolyline pointOnPolyline2 = intersections[i];
					if (pointOnPolyline2.PointM.DistanceTo(pointOnPolyline.PointM) > dedupeLength)
					{
						intersectionsList.Add(pointOnPolyline2);
					}
				}
				intersections = intersectionsList;
			}
			ComputeForPoints(new PointMs(intersections.Select([SpecialName] (PointOnPolyline pop) => pop.PointM)));
		}
	}

	public void SubsampleIntersections(Polyline pline, ref List<PointOnPolyline> intersections, double maxSegLength)
	{
		List<PointOnPolyline> pointOnPolylines = new List<PointOnPolyline>();
		checked
		{
			int count = intersections.Count - 2;
			for (int i = 0; i <= count; i++)
			{
				PointOnPolyline ptOnPoly = intersections[i];
				PointOnPolyline ptOnPoly2 = intersections[i + 1];
				double distanceAlong = pline.DistanceAlong(ptOnPoly);
				double distanceAlongPlineTerm = pline.DistanceAlong(ptOnPoly2) - distanceAlong;
				if (!(distanceAlongPlineTerm > maxSegLength))
				{
					continue;
				}
				int systemMaxSegLengthTerm = (int)System.Math.Ceiling(distanceAlongPlineTerm / maxSegLength);
				if (systemMaxSegLengthTerm > 0)
				{
					double computed = distanceAlongPlineTerm / (double)systemMaxSegLengthTerm;
					int count2 = systemMaxSegLengthTerm;
					for (int j = 1; j <= count2; j++)
					{
						pointOnPolylines.Add(pline.PointAtDistance(distanceAlong + (double)j * computed));
					}
				}
			}
			intersections.AddRange(pointOnPolylines);
			intersections = PointOnPolyline.Sort(intersections);
		}
	}

	private List<PointOnPolyline> ComputeGeometryIntersections(Polyline pline, bool ensureSorted = true)
	{
		Extent extent = pline.ComputeExtent();
		double length = pline.Length;
		double filterToleranceXY = _geometry.FilterToleranceXY;
		List<PointOnPolyline> pointOnPolylines = new List<PointOnPolyline>();
		checked
		{
			int count = _D2.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				MeshFV2D meshFV2D = _D2.Meshes().ElementAtOrDefault(i);
				if (!meshFV2D.IsComplete())
				{
					continue;
				}
				Extent feature = meshFV2D.Perimeter.ComputeExtent();
				if (!extent.MBROverlaps(feature))
				{
					continue;
				}
				List<int> elements = SharedData.GetMeshFaceSpatialIndex(meshFV2D).GetElements(pline);
				foreach (int element in elements)
				{
					List<SegmentM> faceSegments = meshFV2D.FaceSegments(element).ToList();
					if (meshFV2D.FaceIsPerimeter(element))
					{
						foreach (SegmentM faceSegment in faceSegments)
						{
							List<PointIntersect> lineIntersectionMulti = pline.GetLineIntersectionMulti(faceSegment);
							foreach (PointOnPolyline pointOnPolyline in lineIntersectionMulti.Select([SpecialName] (PointIntersect pi) => pi.APointOnPolyline))
							{
								double distanceAlong = pline.DistanceAlong(pointOnPolyline);
								double distanceAlongPlusFilterToleranceXYOverLength = (distanceAlong + filterToleranceXY) / length;
								double fraction = (distanceAlong - filterToleranceXY) / length;
								if (distanceAlongPlusFilterToleranceXYOverLength < 0.0)
								{
									distanceAlongPlusFilterToleranceXYOverLength = 0.0;
								}
								if (distanceAlongPlusFilterToleranceXYOverLength > 1.0)
								{
									distanceAlongPlusFilterToleranceXYOverLength = 1.0;
								}
								pointOnPolylines.Add(pline.PointAtFraction(distanceAlongPlusFilterToleranceXYOverLength));
								pointOnPolylines.Add(pline.PointAtFraction(fraction));
							}
						}
						continue;
					}
					foreach (SegmentM faceSegment2 in faceSegments)
					{
						pointOnPolylines.AddRange(pline.GetLineIntersectionMulti(faceSegment2).Select((_Closure_0024__._0024I75_002D1 == null) ? (_Closure_0024__._0024I75_002D1 = [SpecialName] (PointIntersect pi) => pi.APointOnPolyline) : _Closure_0024__._0024I75_002D1));
					}
				}
			}
			int count2 = _SA.FeatureCount() - 1;
			IEnumerator<PointOnPolyline> pointOnPolylines2 = default;
			for (int ordinal = 0; ordinal <= count2; ordinal++)
			{
				Polygon polygon = _SA.Polygon(ordinal);
				Extent feature2 = polygon.ComputeExtent();
				if (!extent.MBROverlaps(feature2))
				{
					continue;
				}
				try
				{
					SpatialIndex<int> segmentSpIdx = null;
					pointOnPolylines2 = (from pi in pline.GetLineIntersectionMulti(polygon, ref segmentSpIdx)
						select pi.APointOnPolyline).GetEnumerator();
					while (pointOnPolylines2.MoveNext())
					{
						PointOnPolyline current5 = pointOnPolylines2.Current;
						double distanceAlong2 = pline.DistanceAlong(current5);
						double distanceAlong2PlusFilterToleranceXYOverLength = (distanceAlong2 + filterToleranceXY) / length;
						double fraction2 = (distanceAlong2 - filterToleranceXY) / length;
						if (distanceAlong2PlusFilterToleranceXYOverLength < 0.0)
						{
							distanceAlong2PlusFilterToleranceXYOverLength = 0.0;
						}
						if (distanceAlong2PlusFilterToleranceXYOverLength > 1.0)
						{
							distanceAlong2PlusFilterToleranceXYOverLength = 1.0;
						}
						pointOnPolylines.Add(pline.PointAtFraction(distanceAlong2PlusFilterToleranceXYOverLength));
						pointOnPolylines.Add(pline.PointAtFraction(fraction2));
					}
				}
				finally
				{
					pointOnPolylines2?.Dispose();
				}
			}
			int count3 = _XSIS.FeatureCount() - 1;
			for (int ordinal2 = 0; ordinal2 <= count3; ordinal2++)
			{
				Tin normalizedMTIN = _XSIS.GetNormalizedMTIN(ordinal2);
				DistinctSpatialIndex distinctSpatialIndex = new DistinctSpatialIndex(normalizedMTIN.ComputeExtent(), normalizedMTIN.EdgeCount());
				int count4 = normalizedMTIN.EdgeCount() - 1;
				for (int ordinal3 = 0; ordinal3 <= count4; ordinal3++)
				{
					SegmentM segment = normalizedMTIN.Edge(ordinal3);
					distinctSpatialIndex.Add(segment, ordinal3);
				}
				List<int> elements2 = distinctSpatialIndex.GetElements(pline);
				foreach (int elements22 in elements2)
				{
					pointOnPolylines.AddRange(pline.GetLineIntersectionMulti(normalizedMTIN.Edge(elements22)).Select((_Closure_0024__._0024I75_002D3 == null) ? (_Closure_0024__._0024I75_002D3 = [SpecialName] (PointIntersect pi) => pi.APointOnPolyline) : _Closure_0024__._0024I75_002D3));
				}
			}
			int count5 = pline.Count - 2;
			for (int ordinal4 = 0; ordinal4 <= count5; ordinal4++)
			{
				pointOnPolylines.Add(new PointOnPolyline(pline.PointM(ordinal4), ordinal4, 0.0));
			}
			pointOnPolylines.Add(pline.LastPointOnPolyline());
			if (ensureSorted)
			{
				return PointOnPolyline.Sort(pointOnPolylines);
			}
			return pointOnPolylines;
		}
	}

	private void ComputeBlockRanges(Polyline pline, ref List<Tuple<float, float>> blocked2d, ref List<Tuple<float, float>> blockedSA)
	{
		_Closure_0024__76_002D0 capturedLocals = default;
		_Closure_0024__76_002D0 capturedLocals2 = new _Closure_0024__76_002D0(capturedLocals);
		capturedLocals2.pline = pline;
		Extent extent = capturedLocals2.pline.ComputeExtent();
		blocked2d = new List<Tuple<float, float>>();
		foreach (MeshFV2D meshe in _D2.Meshes())
		{
			if (!meshe.IsComplete())
			{
				continue;
			}
			Extent feature = meshe.Perimeter.ComputeExtent();
			if (!extent.MBROverlaps(feature))
			{
				continue;
			}
			Polygon perimeter = meshe.Perimeter;
			if (Polygon.IsValidPolygon(perimeter))
			{
				IEnumerable<Tuple<float, float>> enumerable = from tup in perimeter.EntryExitPoints(capturedLocals2.pline)
					select new Tuple<float, float>((float)capturedLocals2.pline.DistanceAlong(tup.Item1), (float)capturedLocals2.pline.DistanceAlong(tup.Item2));
				blocked2d = CombineBlockRange(blocked2d, (List<Tuple<float, float>>)enumerable);
			}
		}
		blockedSA = new List<Tuple<float, float>>();
		checked
		{
			int count = _SA.FeatureCount() - 1;
			for (int ordinal = 0; ordinal <= count; ordinal++)
			{
				Polygon polygon = _SA.Polygon(ordinal);
				if (!Polygon.IsValidPolygon(polygon))
				{
					continue;
				}
				Extent feature2 = polygon.ComputeExtent();
				if (capturedLocals2.pline.MBROverlaps(feature2))
				{
					IEnumerable<Tuple<float, float>> enumerable2 = from tup in polygon.EntryExitPoints(capturedLocals2.pline)
						select new Tuple<float, float>((float)capturedLocals2.pline.DistanceAlong(tup.Item1), (float)capturedLocals2.pline.DistanceAlong(tup.Item2));
					blockedSA = CombineBlockRange(blockedSA, (List<Tuple<float, float>>)enumerable2);
				}
			}
		}
	}

	private List<Tuple<float, float>> CombineBlockRange(List<Tuple<float, float>> c1List, List<Tuple<float, float>> c2List)
	{
		List<Tuple<float, float>> valueTupleList = new List<Tuple<float, float>>();
		c1List = c1List.OrderBy([SpecialName] (Tuple<float, float> tup) => tup.Item1).ToList();
		c2List = c2List.OrderBy([SpecialName] (Tuple<float, float> tup) => tup.Item1).ToList();
		int ordinal = 0;
		int ordinal2 = 0;
		checked
		{
			while (true)
			{
				if (ordinal == c1List.Count)
				{
					valueTupleList.AddRange(c2List.GetRange(ordinal2, c2List.Count - ordinal2));
					return valueTupleList;
				}
				if (ordinal2 == c2List.Count)
				{
					valueTupleList.AddRange(c2List.GetRange(ordinal, c1List.Count - ordinal));
				}
				float c1ListTerm = c1List[ordinal].Item1;
				float c1ListTerm2 = c1List[ordinal].Item2;
				float c2ListTerm = c2List[ordinal2].Item1;
				float c2ListTerm2 = c2List[ordinal2].Item2;
				if (c2ListTerm == c1ListTerm2)
				{
					valueTupleList.Add(new Tuple<float, float>(c1ListTerm, c2ListTerm2));
					ordinal++;
					ordinal2++;
					continue;
				}
				if (c1ListTerm == c2ListTerm2)
				{
					valueTupleList.Add(new Tuple<float, float>(c2ListTerm, c1ListTerm2));
					ordinal++;
					ordinal2++;
					continue;
				}
				if (c1ListTerm > c2ListTerm2)
				{
					valueTupleList.Add(new Tuple<float, float>(c2ListTerm, c2ListTerm2));
					ordinal2++;
					continue;
				}
				if (c2ListTerm > c1ListTerm2)
				{
					valueTupleList.Add(new Tuple<float, float>(c2ListTerm, c2ListTerm2));
					ordinal++;
					continue;
				}
				if (c1ListTerm > c2ListTerm)
				{
					valueTupleList.Add(new Tuple<float, float>(c2ListTerm, c1ListTerm2));
					ordinal++;
					ordinal2++;
					continue;
				}
				if (!(c2ListTerm > c1ListTerm))
				{
					break;
				}
				valueTupleList.Add(new Tuple<float, float>(c1ListTerm, c2ListTerm2));
				ordinal++;
				ordinal2++;
			}
			throw new ArgumentException();
		}
	}
}
