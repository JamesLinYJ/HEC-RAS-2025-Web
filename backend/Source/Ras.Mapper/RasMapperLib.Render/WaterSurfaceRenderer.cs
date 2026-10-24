using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using H5Assist.Caching.VB;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Mapping;
using RasMapperLib.Mesh;
using Utility;
using Utility.Extensions;

namespace RasMapperLib.Render;

public class WaterSurfaceRenderer : Renderer
{
	[CompilerGenerated]
	internal new sealed class _Closure_0024__15_002D0
	{
		public float[] xsWaterSurfaces;

		public RASXS xs;

		public Tuple<float, float>[] xsEncroachments;

		public float[] outWaterSurfaces;

		public Action<int, int, XSISMapPoint, float> injector;

		public WaterSurfaceRenderer waterSurfaceRenderer;

		public _Closure_0024__15_002D0(_Closure_0024__15_002D0 other)
		{
			if (other != null)
			{
				xsWaterSurfaces = other.xsWaterSurfaces;
				xs = other.xs;
				xsEncroachments = other.xsEncroachments;
				outWaterSurfaces = other.outWaterSurfaces;
				injector = other.injector;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(MappedXSIS interpSurface)
		{
			int iD = interpSurface.ID;
			int upstreamXSID = waterSurfaceRenderer._XSIS.GetUpstreamXSID(iD);
			int downstreamXSID = waterSurfaceRenderer._XSIS.GetDownstreamXSID(iD);
			if (xsWaterSurfaces.Length <= upstreamXSID || xsWaterSurfaces.Length <= downstreamXSID)
			{
				return;
			}
			float xsWaterSurface = xsWaterSurfaces[upstreamXSID];
			float xsWaterSurface2 = xsWaterSurfaces[downstreamXSID];
			LeveeProperties leveeProperties = xs.GetLeveeProperties(upstreamXSID);
			LeveeProperties leveeProperties2 = xs.GetLeveeProperties(downstreamXSID);
			bool condition = false;
			if (leveeProperties.LeftExists && leveeProperties2.LeftExists && leveeProperties.LeftElevation > xsWaterSurface && leveeProperties2.LeftElevation > xsWaterSurface2)
			{
				condition = true;
			}
			bool condition2 = false;
			if (leveeProperties.RightExists && leveeProperties2.RightExists && leveeProperties.RightElevation > xsWaterSurface && leveeProperties2.RightElevation > xsWaterSurface2)
			{
				condition2 = true;
			}
			float computeMUpstreamXSIDTerm = -2f;
			float computeMUpstreamXSIDTerm2 = 2f;
			float computeMDownstreamXSIDTerm = -2f;
			float computeMDownstreamXSIDTerm2 = 2f;
			if (!xsEncroachments.IsNullOrEmpty())
			{
				Tuple<float, float> tuple = xsEncroachments[upstreamXSID];
				Tuple<float, float> tuple2 = xsEncroachments[downstreamXSID];
				if (!float.IsNaN(tuple.Item1) && !float.IsNaN(tuple2.Item1))
				{
					computeMUpstreamXSIDTerm = xs.ComputeM(upstreamXSID, tuple.Item1);
					computeMDownstreamXSIDTerm = xs.ComputeM(downstreamXSID, tuple2.Item1);
				}
				if (!float.IsNaN(tuple.Item2) && !float.IsNaN(tuple2.Item2))
				{
					computeMUpstreamXSIDTerm2 = xs.ComputeM(upstreamXSID, tuple.Item2);
					computeMDownstreamXSIDTerm2 = xs.ComputeM(downstreamXSID, tuple2.Item2);
				}
			}
			foreach (XSISMapPoint mapPoint in interpSurface.MapPoints)
			{
				float mapPointTerm = 1f - mapPoint.Z;
				float mapPointTerm2 = xsWaterSurface * mapPoint.Z + xsWaterSurface2 * mapPointTerm;
				if (condition)
				{
					float leftMLeveePropertiesTerm = leveeProperties.LeftM * mapPoint.Z + leveeProperties2.LeftM * mapPointTerm;
					if (mapPoint.M < leftMLeveePropertiesTerm)
					{
						continue;
					}
				}
				if (condition2)
				{
					float rightMLeveePropertiesTerm = leveeProperties.RightM * mapPoint.Z + leveeProperties2.RightM * mapPointTerm;
					if (mapPoint.M > rightMLeveePropertiesTerm)
					{
						continue;
					}
				}
				float mapPointTerm3 = computeMUpstreamXSIDTerm * mapPoint.Z + computeMDownstreamXSIDTerm * mapPointTerm;
				float mapPointTerm4 = computeMUpstreamXSIDTerm2 * mapPoint.Z + computeMDownstreamXSIDTerm2 * mapPointTerm;
				if (mapPoint.M < mapPointTerm3 || mapPoint.M > mapPointTerm4)
				{
					outWaterSurfaces[mapPoint.Index] = -9999f;
					continue;
				}
				outWaterSurfaces[mapPoint.Index] = mapPointTerm2;
				injector?.Invoke(upstreamXSID, downstreamXSID, mapPoint, mapPointTerm2);
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__16_002D0
	{
		public float[] waterSurfaces;

		public _Closure_0024__16_002D0(_Closure_0024__16_002D0 other)
		{
			if (other != null)
			{
				waterSurfaces = other.waterSurfaces;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__16_002D1
	{
		public List<int> pts;

		public float saWS;

		public _Closure_0024__16_002D0 outerClosure;

		public _Closure_0024__16_002D1(_Closure_0024__16_002D1 other)
		{
			if (other != null)
			{
				pts = other.pts;
				saWS = other.saWS;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				outerClosure.waterSurfaces[pts[i]] = saWS;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__17_002D0
	{
		public FlatMeshMap flatArea;

		public _Closure_0024__17_002D3 outerClosure;

		public _Closure_0024__17_002D0(_Closure_0024__17_002D0 other)
		{
			if (other != null)
			{
				flatArea = other.flatArea;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__17_002D1
	{
		public MeshFV2D mesh;

		public _Closure_0024__17_002D2 outerClosure;

		public _Closure_0024__17_002D1(_Closure_0024__17_002D1 other)
		{
			if (other != null)
			{
				mesh = other.mesh;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int fin)
		{
			for (int i = start; i <= fin; i = checked(i + 1))
			{
				int index = outerClosure.outerClosure.flatArea.Cells[i].Index;
				float cellMinimumElevation = mesh.CellMinimumElevation(index);
				if ((double)(outerClosure.cellWaterSurfaces[index] - cellMinimumElevation) < outerClosure.outerClosure.outerClosure.waterSurfaceRenderer._depthThreshold)
				{
					outerClosure.cellWaterSurfaces[index] = cellMinimumElevation;
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__17_002D2
	{
		public float[] cellWaterSurfaces;

		public float[] cellInTimes;

		public _Closure_0024__17_002D0 outerClosure;

		public _Closure_0024__17_002D2(_Closure_0024__17_002D2 other)
		{
			if (other != null)
			{
				cellWaterSurfaces = other.cellWaterSurfaces;
				cellInTimes = other.cellInTimes;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1()
		{
			outerClosure.outerClosure.waterSurfaceRenderer.ComputeCellValuesFlat(outerClosure.flatArea, cellWaterSurfaces, outerClosure.outerClosure.outWaterSurfaces, null, cellWaterSurfaces);
		}

		[SpecialName]
		internal void _Lambda_0024__2()
		{
			outerClosure.outerClosure.waterSurfaceRenderer.ComputeCellValuesFlat(outerClosure.flatArea, cellInTimes, outerClosure.outerClosure.outTimes, null, cellWaterSurfaces);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__17_002D3
	{
		public float[] outWaterSurfaces;

		public float[] outTimes;

		public WaterSurfaceRenderer waterSurfaceRenderer;

		public _Closure_0024__17_002D3(_Closure_0024__17_002D3 other)
		{
			if (other != null)
			{
				outWaterSurfaces = other.outWaterSurfaces;
				outTimes = other.outTimes;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__17_002D4
	{
		public SlopingMeshMap slopedArea;

		public _Closure_0024__17_002D3 outerClosure;

		public _Closure_0024__17_002D4(_Closure_0024__17_002D4 other)
		{
			if (other != null)
			{
				slopedArea = other.slopedArea;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__17_002D5
	{
		public MeshFV2D mesh;

		public _Closure_0024__17_002D6 outerClosure;

		public _Closure_0024__17_002D5(_Closure_0024__17_002D5 other)
		{
			if (other != null)
			{
				mesh = other.mesh;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__3(int start, int fin)
		{
			for (int i = start; i <= fin; i = checked(i + 1))
			{
				int index = outerClosure.outerClosure.slopedArea.Cells[i].Index;
				float cellMinimumElevation = mesh.CellMinimumElevation(index);
				if ((double)(outerClosure.cellWaterSurfaces[index] - cellMinimumElevation) < outerClosure.outerClosure.outerClosure.waterSurfaceRenderer._depthThreshold)
				{
					outerClosure.cellWaterSurfaces[index] = cellMinimumElevation;
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__17_002D6
	{
		public float[] cellWaterSurfaces;

		public float[] cellInTimes;

		public _Closure_0024__17_002D4 outerClosure;

		public _Closure_0024__17_002D6(_Closure_0024__17_002D6 other)
		{
			if (other != null)
			{
				cellWaterSurfaces = other.cellWaterSurfaces;
				cellInTimes = other.cellInTimes;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__4()
		{
			outerClosure.outerClosure.waterSurfaceRenderer.ComputeCellValuesFlat(outerClosure.slopedArea, cellWaterSurfaces, outerClosure.outerClosure.outWaterSurfaces, null, cellWaterSurfaces);
		}

		[SpecialName]
		internal void _Lambda_0024__5()
		{
			outerClosure.outerClosure.waterSurfaceRenderer.ComputeWS2DSloped(outerClosure.slopedArea, cellWaterSurfaces, outerClosure.outerClosure.outWaterSurfaces);
		}

		[SpecialName]
		internal void _Lambda_0024__6()
		{
			outerClosure.outerClosure.waterSurfaceRenderer.ComputeCellValuesFlat(outerClosure.slopedArea, cellInTimes, outerClosure.outerClosure.outTimes, null, cellWaterSurfaces);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__8_002D0
	{
		public float[][] cellFPLocalValues;

		public MeshFV2D mesh;

		public float[][] fpValuesPerCell;

		public _Closure_0024__8_002D0(_Closure_0024__8_002D0 other)
		{
			if (other != null)
			{
				cellFPLocalValues = other.cellFPLocalValues;
				mesh = other.mesh;
				fpValuesPerCell = other.fpValuesPerCell;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				cellFPLocalValues[i] = mesh.GetFPLocalValues(i, fpValuesPerCell);
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__8_002D1
	{
		public Func<float[]> getReusableBuffer;

		public string oneD;

		public float[] xsWaterSurfaces;

		public Tuple<float, float>[] xsEncroachments;

		public string sA;

		public float[] saWS;

		public float[][] meshCellWaterSurfaces;

		public float[][][] meshCellFPLocalValues;

		public bool allowNegativeValues;

		public WaterSurfaceRenderer waterSurfaceRenderer;

		public _Closure_0024__8_002D1(_Closure_0024__8_002D1 other)
		{
			if (other != null)
			{
				getReusableBuffer = other.getReusableBuffer;
				oneD = other.oneD;
				xsWaterSurfaces = other.xsWaterSurfaces;
				xsEncroachments = other.xsEncroachments;
				sA = other.sA;
				saWS = other.saWS;
				meshCellWaterSurfaces = other.meshCellWaterSurfaces;
				meshCellFPLocalValues = other.meshCellFPLocalValues;
				allowNegativeValues = other.allowNegativeValues;
			}
		}

		[SpecialName]
		internal float[] _Lambda_0024__1(RASGeometryMapPoints mps, float[] terrain)
		{
			_Closure_0024__8_002D2 localValue = default;
			_Closure_0024__8_002D2 capturedLocals = new _Closure_0024__8_002D2(localValue);
			capturedLocals.outerClosure = this;
			capturedLocals.mps = mps;
			capturedLocals.terrain = terrain;
			capturedLocals.output = getReusableBuffer();
			Buffers.ArraySetAllVB(ref capturedLocals.output, -9999f, capturedLocals.mps.InputCount);
			List<Task> tasks = new List<Task>();
			if (!string.IsNullOrEmpty(oneD) && capturedLocals.mps.MappedXSISs.Count > 0)
			{
				tasks.Add(Task.Factory.StartNew([SpecialName] () =>
				{
					capturedLocals.outerClosure.waterSurfaceRenderer.ComputeWaterSurface1DInternal(capturedLocals.mps, capturedLocals.outerClosure.xsWaterSurfaces, capturedLocals.outerClosure.xsEncroachments, capturedLocals.output);
				}));
			}
			if (!string.IsNullOrEmpty(sA) && capturedLocals.mps.MappedSAs.Count > 0)
			{
				tasks.Add(Task.Factory.StartNew([SpecialName] () =>
				{
					capturedLocals.outerClosure.waterSurfaceRenderer.ComputeWaterSurfaceInternalSA(capturedLocals.mps, capturedLocals.outerClosure.saWS, capturedLocals.output);
				}));
			}
			if (!meshCellWaterSurfaces.IsNullOrEmpty() && capturedLocals.mps.HasMapped2DCells())
			{
				using (List<FlatMeshMap>.Enumerator flatMapped2DAreas = capturedLocals.mps.FlatMapped2DAreas().GetEnumerator())
				{
					_Closure_0024__8_002D3 localValue2 = default;
					_Closure_0024__8_002D4 localValue3 = default;
					while (flatMapped2DAreas.MoveNext())
					{
						localValue2 = new _Closure_0024__8_002D3(localValue2);
						localValue2.outerClosure = capturedLocals;
						localValue2.flatArea = flatMapped2DAreas.Current;
						localValue3 = new _Closure_0024__8_002D4(localValue3);
						localValue3.outerClosure = localValue2;
						localValue3.cellWaterSurfaces = meshCellWaterSurfaces[localValue3.outerClosure.flatArea.MeshIndex];
						tasks.Add(Task.Factory.StartNew(localValue3._Lambda_0024__4));
					}
				}
				using List<SlopingMeshMap>.Enumerator slopingMapped2DAreas = capturedLocals.mps.SlopingMapped2DAreas().GetEnumerator();
				_Closure_0024__8_002D5 localValue4 = default;
				_Closure_0024__8_002D6 localValue5 = default;
				while (slopingMapped2DAreas.MoveNext())
				{
					localValue4 = new _Closure_0024__8_002D5(localValue4);
					localValue4.outerClosure = capturedLocals;
					localValue4.slopedArea = slopingMapped2DAreas.Current;
					localValue5 = new _Closure_0024__8_002D6(localValue5);
					localValue5.outerClosure = localValue4;
					localValue5.cellWaterSurfaces = meshCellWaterSurfaces[localValue5.outerClosure.slopedArea.MeshIndex];
					localValue5.cellFacePointValues = meshCellFPLocalValues[localValue5.outerClosure.slopedArea.MeshIndex];
					if (SharedData.CellRenderMode == SharedData.CellRenderMethod.AsComputed)
					{
						tasks.Add(Task.Factory.StartNew(localValue5._Lambda_0024__5));
						continue;
					}
					if (SharedData.CellRenderMode == SharedData.CellRenderMethod.Pretty)
					{
						tasks.Add(Task.Factory.StartNew(localValue5._Lambda_0024__6));
						continue;
					}
					throw new NotImplementedException();
				}
			}
			if (tasks.Count == 0)
			{
				return capturedLocals.output;
			}
			Task.WaitAll(tasks.ToArray());
			checked
			{
				if (allowNegativeValues)
				{
					Utility.Parallel.SmartFor(capturedLocals.output.Count(), [SpecialName] (int start, int finish) =>
					{
						for (int i = start; i <= finish; i++)
						{
							if (capturedLocals.terrain[i] == -9999f)
							{
								capturedLocals.output[i] = -9999f;
							}
						}
					}, 256);
				}
				else
				{
					Utility.Parallel.SmartFor(capturedLocals.output.Count(), [SpecialName] (int start, int finish) =>
					{
						for (int i = start; i <= finish; i++)
						{
							if (capturedLocals.output[i] < capturedLocals.terrain[i] + capturedLocals.outerClosure.waterSurfaceRenderer.MinWSPlotTolerance || capturedLocals.terrain[i] == -9999f)
							{
								capturedLocals.output[i] = -9999f;
							}
						}
					}, 256);
				}
				return capturedLocals.output;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__8_002D2
	{
		public RASGeometryMapPoints mps;

		public float[] output;

		public float[] terrain;

		public _Closure_0024__8_002D1 outerClosure;

		public _Closure_0024__8_002D2(_Closure_0024__8_002D2 other)
		{
			if (other != null)
			{
				mps = other.mps;
				output = other.output;
				terrain = other.terrain;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__2()
		{
			outerClosure.waterSurfaceRenderer.ComputeWaterSurface1DInternal(mps, outerClosure.xsWaterSurfaces, outerClosure.xsEncroachments, output);
		}

		[SpecialName]
		internal void _Lambda_0024__3()
		{
			outerClosure.waterSurfaceRenderer.ComputeWaterSurfaceInternalSA(mps, outerClosure.saWS, output);
		}

		[SpecialName]
		internal void _Lambda_0024__7(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				if (terrain[i] == -9999f)
				{
					output[i] = -9999f;
				}
			}
		}

		[SpecialName]
		internal void _Lambda_0024__8(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				if (output[i] < terrain[i] + outerClosure.waterSurfaceRenderer.MinWSPlotTolerance || terrain[i] == -9999f)
				{
					output[i] = -9999f;
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__8_002D3
	{
		public FlatMeshMap flatArea;

		public _Closure_0024__8_002D2 outerClosure;

		public _Closure_0024__8_002D3(_Closure_0024__8_002D3 other)
		{
			if (other != null)
			{
				flatArea = other.flatArea;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__8_002D4
	{
		public float[] cellWaterSurfaces;

		public _Closure_0024__8_002D3 outerClosure;

		public _Closure_0024__8_002D4(_Closure_0024__8_002D4 other)
		{
			if (other != null)
			{
				cellWaterSurfaces = other.cellWaterSurfaces;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__4()
		{
			outerClosure.outerClosure.outerClosure.waterSurfaceRenderer.ComputeCellValuesFlat(outerClosure.flatArea, cellWaterSurfaces, outerClosure.outerClosure.output, null, cellWaterSurfaces);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__8_002D5
	{
		public SlopingMeshMap slopedArea;

		public _Closure_0024__8_002D2 outerClosure;

		public _Closure_0024__8_002D5(_Closure_0024__8_002D5 other)
		{
			if (other != null)
			{
				slopedArea = other.slopedArea;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__8_002D6
	{
		public float[] cellWaterSurfaces;

		public float[][] cellFacePointValues;

		public _Closure_0024__8_002D5 outerClosure;

		public _Closure_0024__8_002D6(_Closure_0024__8_002D6 other)
		{
			if (other != null)
			{
				cellWaterSurfaces = other.cellWaterSurfaces;
				cellFacePointValues = other.cellFacePointValues;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__5()
		{
			outerClosure.outerClosure.outerClosure.waterSurfaceRenderer.ComputeCellValuesFlat(outerClosure.slopedArea, cellWaterSurfaces, outerClosure.outerClosure.output, null, cellWaterSurfaces);
		}

		[SpecialName]
		internal void _Lambda_0024__6()
		{
			outerClosure.outerClosure.outerClosure.waterSurfaceRenderer.ComputeFromFacepointValuesPixelParallelCellLocal(outerClosure.slopedArea, cellFacePointValues, outerClosure.outerClosure.output, null, cellWaterSurfaces);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__9_002D0
	{
		public float[] localOutputTimes;

		public float[] xsWaterSurfaceTimes;

		public _Closure_0024__9_002D0(_Closure_0024__9_002D0 other)
		{
			if (other != null)
			{
				localOutputTimes = other.localOutputTimes;
				xsWaterSurfaceTimes = other.xsWaterSurfaceTimes;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int usxs, int dsxs, XSISMapPoint pixel, float ws)
		{
			if (ws == -9999f)
			{
				localOutputTimes[pixel.Index] = -9999f;
				return;
			}
			float xsWaterSurfaceTime = xsWaterSurfaceTimes[usxs];
			float xsWaterSurfaceTime2 = xsWaterSurfaceTimes[dsxs];
			float pixelTerm = 1f - pixel.Z;
			float pixelTerm2 = xsWaterSurfaceTime * pixel.Z + xsWaterSurfaceTime2 * pixelTerm;
			localOutputTimes[pixel.Index] = pixelTerm2;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__9_002D1
	{
		public RASGeometryMapPoints mps;

		public float[] lWaterSurfaces;

		public float[] clipTerrain;

		public WaterSurfaceRenderer waterSurfaceRenderer;

		public _Closure_0024__9_002D1(_Closure_0024__9_002D1 other)
		{
			if (other != null)
			{
				mps = other.mps;
				lWaterSurfaces = other.lWaterSurfaces;
				clipTerrain = other.clipTerrain;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__4(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				if (lWaterSurfaces[i] < clipTerrain[i] + waterSurfaceRenderer.MinWSPlotTolerance || clipTerrain[i] == -9999f)
				{
					lWaterSurfaces[i] = -9999f;
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__9_002D2
	{
		public float[] xsWaterSurfaces;

		public Tuple<float, float>[] xsEncroachments;

		public Action<int, int, XSISMapPoint, float> injector;

		public _Closure_0024__9_002D1 outerClosure;

		public _Closure_0024__9_002D2(_Closure_0024__9_002D2 other)
		{
			if (other != null)
			{
				xsWaterSurfaces = other.xsWaterSurfaces;
				xsEncroachments = other.xsEncroachments;
				injector = other.injector;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1()
		{
			outerClosure.waterSurfaceRenderer.ComputeWaterSurface1DInternal(outerClosure.mps, xsWaterSurfaces, xsEncroachments, outerClosure.lWaterSurfaces, injector);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__9_002D3
	{
		public float[] localOutputTimes;

		public _Closure_0024__9_002D3(_Closure_0024__9_002D3 other)
		{
			if (other != null)
			{
				localOutputTimes = other.localOutputTimes;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__9_002D4
	{
		public List<int> pts;

		public float saTime;

		public _Closure_0024__9_002D3 outerClosure;

		public _Closure_0024__9_002D4(_Closure_0024__9_002D4 other)
		{
			if (other != null)
			{
				pts = other.pts;
				saTime = other.saTime;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__2(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				outerClosure.localOutputTimes[pts[i]] = saTime;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__9_002D5
	{
		public float[] ws;

		public _Closure_0024__9_002D1 outerClosure;

		public _Closure_0024__9_002D5(_Closure_0024__9_002D5 other)
		{
			if (other != null)
			{
				ws = other.ws;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__3()
		{
			outerClosure.waterSurfaceRenderer.ComputeWaterSurfaceInternalSA(outerClosure.mps, ws, outerClosure.lWaterSurfaces);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__9_002D6
	{
		public float[] lTimes;

		public _Closure_0024__9_002D1 outerClosure;

		public _Closure_0024__9_002D6(_Closure_0024__9_002D6 other)
		{
			if (other != null)
			{
				lTimes = other.lTimes;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__5(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				if (outerClosure.lWaterSurfaces[i] < outerClosure.clipTerrain[i] + outerClosure.waterSurfaceRenderer.MinWSPlotTolerance || outerClosure.clipTerrain[i] == -9999f)
				{
					lTimes[i] = -9999f;
				}
			}
		}
	}

	protected Names _names;

	protected H5RowCache<float> _rowCache;

	private double _depthThreshold;

	public bool ReportTimeAtWaterSurface;

	public static void ComputeTimeAtWaterSurface(RASResults res, int profile, RASGeometryMapPoints mps, ref float[] waterSurfaces, ref float[] times, float[] clipTerrain = null, CacheCollection cache = null)
	{
		WaterSurfaceRenderer waterSurfaceRenderer = new WaterSurfaceRenderer(res, cache);
		waterSurfaceRenderer.ReportTimeAtWaterSurface = true;
		waterSurfaceRenderer.Compute(profile, mps, ref waterSurfaces, clipTerrain, ref times);
	}

	public static void Compute(RASResults res, int profile, RASGeometryMapPoints mps, ref float[] waterSurfaces, float[] clipTerrain = null, CacheCollection cache = null)
	{
		WaterSurfaceRenderer waterSurfaceRenderer = new WaterSurfaceRenderer(res, cache);
		float[] outputTimes = null;
		waterSurfaceRenderer.Compute(profile, mps, ref waterSurfaces, clipTerrain, ref outputTimes);
	}

	public WaterSurfaceRenderer(RASResults res, CacheCollection cache = null, double depthThreshold = 0.0)
		: base(res, cache, needsVelocityFlowData: false, needsPostProcessFile: false)
	{
		_depthThreshold = 0.0;
		ReportTimeAtWaterSurface = false;
		_names = GetWaterSurfaceDatasetNames();
		_depthThreshold = depthThreshold;
		_names.Validate(_hdfFilename);
		_rowCache = _cache.GetRowCache<float>(_hdfFilename);
	}

	public static Func<RASGeometryMapPoints, float[], float[]> GetComputer(RASResults res, int profile, CacheCollection cache, Func<float[]> GetReusableBuffer, Action<float[]> ReturnReusableBuffer, bool allowNegativeValues = false)
	{
		return new WaterSurfaceRenderer(res, cache).GetComputer(profile, allowNegativeValues, GetReusableBuffer, ReturnReusableBuffer);
	}

	public Func<RASGeometryMapPoints, float[], float[]> GetComputer(int profile, bool allowNegativeValues, Func<float[]> GetReusableBuffer, Action<float[]> ReturnReusableBuffer)
	{
		_Closure_0024__8_002D1 capturedLocals = default;
		_Closure_0024__8_002D1 capturedLocals2 = new _Closure_0024__8_002D1(capturedLocals);
		capturedLocals2.waterSurfaceRenderer = this;
		capturedLocals2.allowNegativeValues = allowNegativeValues;
		capturedLocals2.getReusableBuffer = GetReusableBuffer;
		Stopwatch stopwatch = Stopwatch.StartNew();
		capturedLocals2.oneD = null;
		int row = default;
		_names.Get1D(profile, ref capturedLocals2.oneD, ref row);
		capturedLocals2.sA = null;
		int row2 = default;
		_names.GetSA(profile, ref capturedLocals2.sA, ref row2);
		List<string> datasets = null;
		int row3 = default;
		_names.Get2D(profile, ref datasets, ref row3);
		capturedLocals2.xsWaterSurfaces = null;
		capturedLocals2.xsEncroachments = null;
		if (!string.IsNullOrEmpty(capturedLocals2.oneD))
		{
			try
			{
				capturedLocals2.xsWaterSurfaces = _rowCache.EnsureGetRow(capturedLocals2.oneD, row);
				capturedLocals2.xsEncroachments = _result.ReadXSEncroachments(profile);
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				ProjectData.ClearProjectError();
			}
		}
		capturedLocals2.saWS = null;
		if (!string.IsNullOrEmpty(capturedLocals2.sA))
		{
			capturedLocals2.saWS = _rowCache.EnsureGetRow(capturedLocals2.sA, row2);
		}
		stopwatch.Restart();
		bool condition = false;
		capturedLocals2.meshCellWaterSurfaces = null;
		FaceValues[][] meshFaceWS = null;
		float[][][] meshFacePointValues = null;
		capturedLocals2.meshCellFPLocalValues = null;
		checked
		{
			if (!datasets.IsNullOrEmpty())
			{
				capturedLocals2.meshCellWaterSurfaces = ReadMappedMeshProfile(datasets, row3, _rowCache);
				if (condition)
				{
					Console.WriteLine("It took " + stopwatch.ElapsedMilliseconds + " ms to do water surface renderer step 4: ReadMappedMeshProfile");
				}
				stopwatch.Restart();
				SlopingFactors.ComputeAllSlopingWSFacePointValues(_d2FlowArea, capturedLocals2.meshCellWaterSurfaces, ref meshFaceWS, ref meshFacePointValues);
				if (condition)
				{
					Console.WriteLine("It took " + stopwatch.ElapsedMilliseconds + " ms to do water surface renderer step 4: ComputeAllSlopingWSFacePointValues");
				}
				stopwatch.Restart();
				capturedLocals2.meshCellFPLocalValues = new float[_d2FlowArea.MeshCount - 1 + 1][][];
				int count = _d2FlowArea.MeshCount - 1;
				_Closure_0024__8_002D0 capturedLocals3 = default;
				for (int i = 0; i <= count; i++)
				{
					capturedLocals3 = new _Closure_0024__8_002D0(capturedLocals3);
					capturedLocals3.mesh = _d2FlowArea.Meshes().ElementAtOrDefault(i);
					capturedLocals3.fpValuesPerCell = meshFacePointValues[i];
					capturedLocals3.cellFPLocalValues = new float[capturedLocals3.mesh.CellCount - 1 + 1][];
					Utility.Parallel.SmartFor(capturedLocals3.mesh.CellCount, capturedLocals3._Lambda_0024__0, 1000);
					if (condition)
					{
						Console.WriteLine("It took " + stopwatch.ElapsedMilliseconds + " ms to do water surface renderer step 4: assign cellFPLocalValues");
					}
					stopwatch.Restart();
					capturedLocals2.meshCellFPLocalValues[i] = capturedLocals3.cellFPLocalValues;
				}
			}
			return [SpecialName] (RASGeometryMapPoints mps, float[] terrain) =>
			{
				_Closure_0024__8_002D2 capturedLocals4 = default;
				_Closure_0024__8_002D2 capturedLocals5 = new _Closure_0024__8_002D2(capturedLocals4);
				capturedLocals5.outerClosure = capturedLocals2;
				capturedLocals5.mps = mps;
				capturedLocals5.terrain = terrain;
				capturedLocals5.output = capturedLocals2.getReusableBuffer();
				Buffers.ArraySetAllVB(ref capturedLocals5.output, -9999f, capturedLocals5.mps.InputCount);
				List<Task> tasks = new List<Task>();
				if (!string.IsNullOrEmpty(capturedLocals2.oneD) && capturedLocals5.mps.MappedXSISs.Count > 0)
				{
					tasks.Add(Task.Factory.StartNew([SpecialName] () =>
					{
						capturedLocals5.outerClosure.waterSurfaceRenderer.ComputeWaterSurface1DInternal(capturedLocals5.mps, capturedLocals5.outerClosure.xsWaterSurfaces, capturedLocals5.outerClosure.xsEncroachments, capturedLocals5.output);
					}));
				}
				if (!string.IsNullOrEmpty(capturedLocals2.sA) && capturedLocals5.mps.MappedSAs.Count > 0)
				{
					tasks.Add(Task.Factory.StartNew([SpecialName] () =>
					{
						capturedLocals5.outerClosure.waterSurfaceRenderer.ComputeWaterSurfaceInternalSA(capturedLocals5.mps, capturedLocals5.outerClosure.saWS, capturedLocals5.output);
					}));
				}
				if (!capturedLocals2.meshCellWaterSurfaces.IsNullOrEmpty() && capturedLocals5.mps.HasMapped2DCells())
				{
					using (List<FlatMeshMap>.Enumerator flatMapped2DAreas = capturedLocals5.mps.FlatMapped2DAreas().GetEnumerator())
					{
						_Closure_0024__8_002D3 capturedLocals6 = default;
						_Closure_0024__8_002D4 capturedLocals7 = default;
						while (flatMapped2DAreas.MoveNext())
						{
							capturedLocals6 = new _Closure_0024__8_002D3(capturedLocals6);
							capturedLocals6.outerClosure = capturedLocals5;
							capturedLocals6.flatArea = flatMapped2DAreas.Current;
							capturedLocals7 = new _Closure_0024__8_002D4(capturedLocals7);
							capturedLocals7.outerClosure = capturedLocals6;
							capturedLocals7.cellWaterSurfaces = capturedLocals2.meshCellWaterSurfaces[capturedLocals7.outerClosure.flatArea.MeshIndex];
							tasks.Add(Task.Factory.StartNew(capturedLocals7._Lambda_0024__4));
						}
					}
					using List<SlopingMeshMap>.Enumerator slopingMapped2DAreas = capturedLocals5.mps.SlopingMapped2DAreas().GetEnumerator();
					_Closure_0024__8_002D5 capturedLocals8 = default;
					_Closure_0024__8_002D6 capturedLocals9 = default;
					while (slopingMapped2DAreas.MoveNext())
					{
						capturedLocals8 = new _Closure_0024__8_002D5(capturedLocals8);
						capturedLocals8.outerClosure = capturedLocals5;
						capturedLocals8.slopedArea = slopingMapped2DAreas.Current;
						capturedLocals9 = new _Closure_0024__8_002D6(capturedLocals9);
						capturedLocals9.outerClosure = capturedLocals8;
						capturedLocals9.cellWaterSurfaces = capturedLocals2.meshCellWaterSurfaces[capturedLocals9.outerClosure.slopedArea.MeshIndex];
						capturedLocals9.cellFacePointValues = capturedLocals2.meshCellFPLocalValues[capturedLocals9.outerClosure.slopedArea.MeshIndex];
						if (SharedData.CellRenderMode == SharedData.CellRenderMethod.AsComputed)
						{
							tasks.Add(Task.Factory.StartNew(capturedLocals9._Lambda_0024__5));
						}
						else
						{
							if (SharedData.CellRenderMode != SharedData.CellRenderMethod.Pretty)
							{
								throw new NotImplementedException();
							}
							tasks.Add(Task.Factory.StartNew(capturedLocals9._Lambda_0024__6));
						}
					}
				}
				if (tasks.Count == 0)
				{
					return capturedLocals5.output;
				}
				Task.WaitAll(tasks.ToArray());
				if (capturedLocals2.allowNegativeValues)
				{
					Utility.Parallel.SmartFor(capturedLocals5.output.Count(), [SpecialName] (int start, int finish) =>
					{
						for (int j = start; j <= finish; j++)
						{
							if (capturedLocals5.terrain[j] == -9999f)
							{
								capturedLocals5.output[j] = -9999f;
							}
						}
					}, 256);
				}
				else
				{
					Utility.Parallel.SmartFor(capturedLocals5.output.Count(), [SpecialName] (int start, int finish) =>
					{
						for (int j = start; j <= finish; j++)
						{
							if (capturedLocals5.output[j] < capturedLocals5.terrain[j] + capturedLocals5.outerClosure.waterSurfaceRenderer.MinWSPlotTolerance || capturedLocals5.terrain[j] == -9999f)
							{
								capturedLocals5.output[j] = -9999f;
							}
						}
					}, 256);
				}
				return capturedLocals5.output;
			};
		}
	}

	public virtual void Compute(int profile, RASGeometryMapPoints mps, ref float[] waterSurfaces, [Optional][DefaultParameterValue(null)] float[] clipTerrain, [Optional][DefaultParameterValue(null)] ref float[] outputTimes)
	{
		_Closure_0024__9_002D1 capturedLocals = default;
		_Closure_0024__9_002D1 capturedLocals2 = new _Closure_0024__9_002D1(capturedLocals);
		capturedLocals2.waterSurfaceRenderer = this;
		capturedLocals2.mps = mps;
		capturedLocals2.clipTerrain = clipTerrain;
		if (ReportTimeAtWaterSurface)
		{
			if (profile != int.MaxValue && profile != int.MinValue)
			{
				throw new Exception("Option ReportTimeAtWaterSurface can only be used with Max or Min profiles.");
			}
			Buffers.ArraySetAllVB(ref outputTimes, -9999f, capturedLocals2.mps.InputCount);
		}
		ValidateProfileIndex(profile);
		Buffers.ArraySetAllVB(ref waterSurfaces, -9999f, capturedLocals2.mps.InputCount);
		List<Task> tasks = new List<Task>();
		capturedLocals2.lWaterSurfaces = waterSurfaces;
		string dataset = null;
		int row = default;
		_names.Get1D(profile, ref dataset, ref row);
		if (!string.IsNullOrEmpty(dataset) && capturedLocals2.mps.MappedXSISs.Count > 0)
		{
			_Closure_0024__9_002D2 capturedLocals3 = default;
			_Closure_0024__9_002D2 capturedLocals4 = new _Closure_0024__9_002D2(capturedLocals3);
			capturedLocals4.outerClosure = capturedLocals2;
			capturedLocals4.xsWaterSurfaces = _rowCache.EnsureGetRow(dataset, row);
			capturedLocals4.xsEncroachments = _result.ReadXSEncroachments(profile);
			capturedLocals4.injector = null;
			if (ReportTimeAtWaterSurface)
			{
				_Closure_0024__9_002D0 capturedLocals5 = default;
				_Closure_0024__9_002D0 capturedLocals6 = new _Closure_0024__9_002D0(capturedLocals5);
				capturedLocals6.localOutputTimes = outputTimes;
				capturedLocals6.xsWaterSurfaceTimes = _rowCache.EnsureGetRow(dataset, 2);
				capturedLocals4.injector = [SpecialName] (int usxs, int dsxs, XSISMapPoint pixel, float ws) =>
				{
					if (ws == -9999f)
					{
						capturedLocals6.localOutputTimes[pixel.Index] = -9999f;
					}
					else
					{
						float xsWaterSurfaceTime = capturedLocals6.xsWaterSurfaceTimes[usxs];
						float xsWaterSurfaceTime2 = capturedLocals6.xsWaterSurfaceTimes[dsxs];
						float pixelTerm = 1f - pixel.Z;
						float pixelTerm2 = xsWaterSurfaceTime * pixel.Z + xsWaterSurfaceTime2 * pixelTerm;
						capturedLocals6.localOutputTimes[pixel.Index] = pixelTerm2;
					}
				};
			}
			tasks.Add(Task.Factory.StartNew([SpecialName] () =>
			{
				capturedLocals4.outerClosure.waterSurfaceRenderer.ComputeWaterSurface1DInternal(capturedLocals4.outerClosure.mps, capturedLocals4.xsWaterSurfaces, capturedLocals4.xsEncroachments, capturedLocals4.outerClosure.lWaterSurfaces, capturedLocals4.injector);
			}));
		}
		string dataset2 = null;
		int row2 = default;
		_names.GetSA(profile, ref dataset2, ref row2);
		if (!string.IsNullOrEmpty(dataset2) && capturedLocals2.mps.MappedSAs.Count > 0)
		{
			_Closure_0024__9_002D5 capturedLocals7 = default;
			_Closure_0024__9_002D5 capturedLocals8 = new _Closure_0024__9_002D5(capturedLocals7);
			capturedLocals8.outerClosure = capturedLocals2;
			capturedLocals8.ws = _rowCache.EnsureGetRow(dataset2, row2);
			if (ReportTimeAtWaterSurface)
			{
				_Closure_0024__9_002D3 capturedLocals9 = new _Closure_0024__9_002D3(null);
				capturedLocals9.localOutputTimes = outputTimes;
				float[] ensureGetRow = _rowCache.EnsureGetRow(dataset2, 1);
				_Closure_0024__9_002D4 capturedLocals10 = default;
				foreach (SAMap mappedSA in capturedLocals8.outerClosure.mps.MappedSAs)
				{
					capturedLocals10 = new _Closure_0024__9_002D4(capturedLocals10);
					capturedLocals10.outerClosure = capturedLocals9;
					capturedLocals10.saTime = ensureGetRow[mappedSA.ID];
					capturedLocals10.pts = mappedSA.MapPoints;
					Utility.Parallel.SmartFor(capturedLocals10.pts.Count, capturedLocals10._Lambda_0024__2, 256);
				}
			}
			tasks.Add(Task.Factory.StartNew([SpecialName] () =>
			{
				capturedLocals8.outerClosure.waterSurfaceRenderer.ComputeWaterSurfaceInternalSA(capturedLocals8.outerClosure.mps, capturedLocals8.ws, capturedLocals8.outerClosure.lWaterSurfaces);
			}));
		}
		List<string> datasets = null;
		int row3 = default;
		_names.Get2D(profile, ref datasets, ref row3);
		float[][] meshCellWaterSurfaces = ReadMappedMeshProfile(datasets, row3, _rowCache, capturedLocals2.mps);
		float[][] meshInTimes = null;
		if (ReportTimeAtWaterSurface)
		{
			meshInTimes = ReadMappedMeshProfile(datasets, 1, _rowCache, capturedLocals2.mps);
		}
		tasks.AddRange(ComputeWaterSurfaceInternal2D(capturedLocals2.mps, meshCellWaterSurfaces, waterSurfaces, meshInTimes, outputTimes));
		if (tasks.Count == 0)
		{
			return;
		}
		Task.WaitAll(tasks.ToArray());
		if (capturedLocals2.clipTerrain == null)
		{
			return;
		}
		checked
		{
			Utility.Parallel.SmartFor(waterSurfaces.Count(), [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					if (capturedLocals2.lWaterSurfaces[i] < capturedLocals2.clipTerrain[i] + capturedLocals2.waterSurfaceRenderer.MinWSPlotTolerance || capturedLocals2.clipTerrain[i] == -9999f)
					{
						capturedLocals2.lWaterSurfaces[i] = -9999f;
					}
				}
			});
			if (!ReportTimeAtWaterSurface)
			{
				return;
			}
			_Closure_0024__9_002D6 capturedLocals11 = default;
			_Closure_0024__9_002D6 capturedLocals12 = new _Closure_0024__9_002D6(capturedLocals11);
			capturedLocals12.outerClosure = capturedLocals2;
			capturedLocals12.lTimes = outputTimes;
			Utility.Parallel.SmartFor(waterSurfaces.Count(), [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					if (capturedLocals12.outerClosure.lWaterSurfaces[i] < capturedLocals12.outerClosure.clipTerrain[i] + capturedLocals12.outerClosure.waterSurfaceRenderer.MinWSPlotTolerance || capturedLocals12.outerClosure.clipTerrain[i] == -9999f)
					{
						capturedLocals12.lTimes[i] = -9999f;
					}
				}
			});
		}
	}

	public float[][] GetMeshCellWaterSurface(int profile)
	{
		List<string> datasets = null;
		int row = default;
		_names.Get2D(profile, ref datasets, ref row);
		return ReadMappedMeshProfile(datasets, row, _rowCache);
	}

	public void ComputeWaterSurface1D(RASGeometryMapPoints mps, int profile, ref float[] outWSEL)
	{
		Buffers.ArraySetAllVB(ref outWSEL, -9999f, mps.InputCount);
		string dataset = null;
		int row = default;
		_names.Get1D(profile, ref dataset, ref row);
		H5RowCache<float> rowCache = _cache.GetRowCache<float>(_hdfFilename);
		if (!string.IsNullOrEmpty(dataset) && mps.MappedXSISs.Count > 0)
		{
			float[] xsWaterSurfaces = rowCache.EnsureGetRow(dataset, row);
			Tuple<float, float>[] xsEncroachments = _result.ReadXSEncroachments(profile);
			ComputeWaterSurface1DInternal(mps, xsWaterSurfaces, xsEncroachments, outWSEL);
		}
	}

	public void ComputeWaterSurface1D(int profile, ref float[] outWSEL)
	{
		outWSEL = null;
		string dataset = null;
		int row = default;
		_names.Get1D(profile, ref dataset, ref row);
		H5RowCache<float> rowCache = _cache.GetRowCache<float>(_hdfFilename);
		if (dataset != null)
		{
			outWSEL = rowCache.EnsureGetRow(dataset, row);
		}
	}

	public void ComputeWaterSurface2D(RASGeometryMapPoints mps, int profile, ref float[] outWSEL)
	{
		Buffers.ArraySetAllVB(ref outWSEL, -9999f, mps.InputCount);
		List<string> datasets = null;
		int row = default;
		_names.Get2D(profile, ref datasets, ref row);
		H5RowCache<float> rowCache = _cache.GetRowCache<float>(_hdfFilename);
		Task.WaitAll(ComputeWaterSurfaceInternal2D(mps, ReadMappedMeshProfile(datasets, row, rowCache, mps), outWSEL).ToArray());
	}

	public void ComputeWaterSurface2D(int profile, ref float[][] outWSEL)
	{
		outWSEL = null;
		List<string> datasets = null;
		int row = default;
		_names.Get2D(profile, ref datasets, ref row);
		H5RowCache<float> rowCache = _cache.GetRowCache<float>(_hdfFilename);
		if (datasets != null)
		{
			outWSEL = ReadMappedMeshProfile(datasets, row, rowCache);
		}
	}

	protected void ComputeWaterSurface1DInternal(RASGeometryMapPoints mps, float[] xsWaterSurfaces, Tuple<float, float>[] xsEncroachments, float[] outWaterSurfaces, Action<int, int, XSISMapPoint, float> injector = null)
	{
		_Closure_0024__15_002D0 capturedLocals = default;
		_Closure_0024__15_002D0 capturedLocals2 = new _Closure_0024__15_002D0(capturedLocals);
		capturedLocals2.waterSurfaceRenderer = this;
		capturedLocals2.xsWaterSurfaces = xsWaterSurfaces;
		capturedLocals2.xsEncroachments = xsEncroachments;
		capturedLocals2.outWaterSurfaces = outWaterSurfaces;
		capturedLocals2.injector = injector;
		RASXSInterpolationSurface xSInterpolationSurface = _geometry.XSInterpolationSurface;
		capturedLocals2.xs = _geometry.XS;
		capturedLocals2.xs.FeatureTable();
		xSInterpolationSurface.EnsureInterpolationSurface();
		System.Threading.Tasks.Parallel.ForEach(mps.MappedXSISs, [SpecialName] (MappedXSIS interpSurface) =>
		{
			int iD = interpSurface.ID;
			int upstreamXSID = capturedLocals2.waterSurfaceRenderer._XSIS.GetUpstreamXSID(iD);
			int downstreamXSID = capturedLocals2.waterSurfaceRenderer._XSIS.GetDownstreamXSID(iD);
			if (capturedLocals2.xsWaterSurfaces.Length > upstreamXSID && capturedLocals2.xsWaterSurfaces.Length > downstreamXSID)
			{
				float xsWaterSurface = capturedLocals2.xsWaterSurfaces[upstreamXSID];
				float xsWaterSurface2 = capturedLocals2.xsWaterSurfaces[downstreamXSID];
				LeveeProperties leveeProperties = capturedLocals2.xs.GetLeveeProperties(upstreamXSID);
				LeveeProperties leveeProperties2 = capturedLocals2.xs.GetLeveeProperties(downstreamXSID);
				bool condition = false;
				if (leveeProperties.LeftExists && leveeProperties2.LeftExists && leveeProperties.LeftElevation > xsWaterSurface && leveeProperties2.LeftElevation > xsWaterSurface2)
				{
					condition = true;
				}
				bool condition2 = false;
				if (leveeProperties.RightExists && leveeProperties2.RightExists && leveeProperties.RightElevation > xsWaterSurface && leveeProperties2.RightElevation > xsWaterSurface2)
				{
					condition2 = true;
				}
				float capturedLocals3 = -2f;
				float capturedLocals4 = 2f;
				float capturedLocals5 = -2f;
				float capturedLocals6 = 2f;
				if (!capturedLocals2.xsEncroachments.IsNullOrEmpty())
				{
					Tuple<float, float> tuple = capturedLocals2.xsEncroachments[upstreamXSID];
					Tuple<float, float> tuple2 = capturedLocals2.xsEncroachments[downstreamXSID];
					if (!float.IsNaN(tuple.Item1) && !float.IsNaN(tuple2.Item1))
					{
						capturedLocals3 = capturedLocals2.xs.ComputeM(upstreamXSID, tuple.Item1);
						capturedLocals5 = capturedLocals2.xs.ComputeM(downstreamXSID, tuple2.Item1);
					}
					if (!float.IsNaN(tuple.Item2) && !float.IsNaN(tuple2.Item2))
					{
						capturedLocals4 = capturedLocals2.xs.ComputeM(upstreamXSID, tuple.Item2);
						capturedLocals6 = capturedLocals2.xs.ComputeM(downstreamXSID, tuple2.Item2);
					}
				}
				foreach (XSISMapPoint mapPoint in interpSurface.MapPoints)
				{
					float mapPointTerm = 1f - mapPoint.Z;
					float mapPointTerm2 = xsWaterSurface * mapPoint.Z + xsWaterSurface2 * mapPointTerm;
					if (condition)
					{
						float leftMLeveePropertiesTerm = leveeProperties.LeftM * mapPoint.Z + leveeProperties2.LeftM * mapPointTerm;
						if (mapPoint.M < leftMLeveePropertiesTerm)
						{
							continue;
						}
					}
					if (condition2)
					{
						float rightMLeveePropertiesTerm = leveeProperties.RightM * mapPoint.Z + leveeProperties2.RightM * mapPointTerm;
						if (mapPoint.M > rightMLeveePropertiesTerm)
						{
							continue;
						}
					}
					float mapPointTerm3 = capturedLocals3 * mapPoint.Z + capturedLocals5 * mapPointTerm;
					float mapPointTerm4 = capturedLocals4 * mapPoint.Z + capturedLocals6 * mapPointTerm;
					if (mapPoint.M < mapPointTerm3 || mapPoint.M > mapPointTerm4)
					{
						capturedLocals2.outWaterSurfaces[mapPoint.Index] = -9999f;
					}
					else
					{
						capturedLocals2.outWaterSurfaces[mapPoint.Index] = mapPointTerm2;
						capturedLocals2.injector?.Invoke(upstreamXSID, downstreamXSID, mapPoint, mapPointTerm2);
					}
				}
			}
		});
	}

	protected void ComputeWaterSurfaceInternalSA(RASGeometryMapPoints mps, float[] saWaterSurfaces, float[] waterSurfaces)
	{
		_Closure_0024__16_002D0 capturedLocals = new _Closure_0024__16_002D0(null);
		capturedLocals.waterSurfaces = waterSurfaces;
		_Closure_0024__16_002D1 capturedLocals2 = default;
		foreach (SAMap mappedSA in mps.MappedSAs)
		{
			capturedLocals2 = new _Closure_0024__16_002D1(capturedLocals2);
			capturedLocals2.outerClosure = capturedLocals;
			capturedLocals2.saWS = saWaterSurfaces[mappedSA.ID];
			capturedLocals2.pts = mappedSA.MapPoints;
			Utility.Parallel.SmartFor(capturedLocals2.pts.Count, capturedLocals2._Lambda_0024__0, 256);
		}
	}

	protected List<Task> ComputeWaterSurfaceInternal2D(RASGeometryMapPoints mps, float[][] meshCellWaterSurfaces, float[] outWaterSurfaces, float[][] meshInTimes = null, float[] outTimes = null)
	{
		_Closure_0024__17_002D3 capturedLocals = new _Closure_0024__17_002D3(null);
		capturedLocals.waterSurfaceRenderer = this;
		capturedLocals.outWaterSurfaces = outWaterSurfaces;
		capturedLocals.outTimes = outTimes;
		List<Task> waterSurfaceInternal2D = new List<Task>();
		if (meshCellWaterSurfaces == null)
		{
			return waterSurfaceInternal2D;
		}
		using (List<FlatMeshMap>.Enumerator flatMapped2DAreas = mps.FlatMapped2DAreas().GetEnumerator())
		{
			_Closure_0024__17_002D0 capturedLocals2 = default;
			_Closure_0024__17_002D2 capturedLocals3 = default;
			_Closure_0024__17_002D1 capturedLocals4 = default;
			while (flatMapped2DAreas.MoveNext())
			{
				capturedLocals2 = new _Closure_0024__17_002D0(capturedLocals2);
				capturedLocals2.outerClosure = capturedLocals;
				capturedLocals2.flatArea = flatMapped2DAreas.Current;
				capturedLocals3 = new _Closure_0024__17_002D2(capturedLocals3);
				capturedLocals3.outerClosure = capturedLocals2;
				capturedLocals3.cellWaterSurfaces = meshCellWaterSurfaces[capturedLocals3.outerClosure.flatArea.MeshIndex];
				capturedLocals3.cellInTimes = null;
				if (ReportTimeAtWaterSurface)
				{
					capturedLocals3.cellInTimes = meshInTimes[capturedLocals3.outerClosure.flatArea.MeshIndex];
				}
				if (_depthThreshold > 0.0)
				{
					capturedLocals4 = new _Closure_0024__17_002D1(capturedLocals4);
					capturedLocals4.outerClosure = capturedLocals3;
					capturedLocals4.mesh = _d2FlowArea.Meshes().ElementAtOrDefault(capturedLocals4.outerClosure.outerClosure.flatArea.MeshIndex);
					Utility.Parallel.SmartFor(capturedLocals4.outerClosure.outerClosure.flatArea.Cells.Count, capturedLocals4._Lambda_0024__0);
				}
				waterSurfaceInternal2D.Add(Task.Factory.StartNew(capturedLocals3._Lambda_0024__1));
				if (ReportTimeAtWaterSurface)
				{
					waterSurfaceInternal2D.Add(Task.Factory.StartNew(capturedLocals3._Lambda_0024__2));
				}
			}
		}
		using (List<SlopingMeshMap>.Enumerator slopingMapped2DAreas = mps.SlopingMapped2DAreas().GetEnumerator())
		{
			_Closure_0024__17_002D4 capturedLocals5 = default;
			_Closure_0024__17_002D6 capturedLocals6 = default;
			_Closure_0024__17_002D5 capturedLocals7 = default;
			while (slopingMapped2DAreas.MoveNext())
			{
				capturedLocals5 = new _Closure_0024__17_002D4(capturedLocals5);
				capturedLocals5.outerClosure = capturedLocals;
				capturedLocals5.slopedArea = slopingMapped2DAreas.Current;
				capturedLocals6 = new _Closure_0024__17_002D6(capturedLocals6);
				capturedLocals6.outerClosure = capturedLocals5;
				capturedLocals6.cellWaterSurfaces = meshCellWaterSurfaces[capturedLocals6.outerClosure.slopedArea.MeshIndex];
				capturedLocals6.cellInTimes = null;
				if (ReportTimeAtWaterSurface)
				{
					capturedLocals6.cellInTimes = meshInTimes[capturedLocals6.outerClosure.slopedArea.MeshIndex];
				}
				if (_depthThreshold > 0.0)
				{
					capturedLocals7 = new _Closure_0024__17_002D5(capturedLocals7);
					capturedLocals7.outerClosure = capturedLocals6;
					capturedLocals7.mesh = _d2FlowArea.Meshes().ElementAtOrDefault(capturedLocals7.outerClosure.outerClosure.slopedArea.MeshIndex);
					Utility.Parallel.SmartFor(capturedLocals7.outerClosure.outerClosure.slopedArea.Cells.Count, capturedLocals7._Lambda_0024__3);
				}
				if (SharedData.CellRenderMode == SharedData.CellRenderMethod.AsComputed)
				{
					waterSurfaceInternal2D.Add(Task.Factory.StartNew(capturedLocals6._Lambda_0024__4));
				}
				else
				{
					if (SharedData.CellRenderMode != SharedData.CellRenderMethod.Pretty)
					{
						throw new NotImplementedException();
					}
					waterSurfaceInternal2D.Add(Task.Factory.StartNew(capturedLocals6._Lambda_0024__5));
				}
				if (ReportTimeAtWaterSurface)
				{
					waterSurfaceInternal2D.Add(Task.Factory.StartNew(capturedLocals6._Lambda_0024__6));
				}
			}
		}
		return waterSurfaceInternal2D;
	}

	protected void ComputeWS2DSloped(SlopingMeshMap slopedArea, float[] cellWaterSurface, float[] output)
	{
		if (slopedArea == null || slopedArea.Cells.Count == 0)
		{
			return;
		}
		FaceValues[] faceWS = null;
		float[][] facePointValues = null;
		SlopingFactors.ComputeSlopingWSFPValues(_d2FlowArea, slopedArea, cellWaterSurface, ref faceWS, ref facePointValues);
		if (SharedData.CellStencilMode == SharedData.CellStencilMethod.WithFaces)
		{
			Render2D_NodeDonatedWeights_WaterSurface(slopedArea, faceWS, facePointValues, cellWaterSurface, output);
			return;
		}
		if (SharedData.CellStencilMode == SharedData.CellStencilMethod.JustFacepoints)
		{
			ComputeFromFacepointValues_SpecialWaterSurfaceMode(slopedArea, cellWaterSurface, faceWS, facePointValues, output);
			return;
		}
		throw new NotImplementedException();
	}

	private void ValidateProfileIndex(int pfIndex)
	{
		if (pfIndex != int.MaxValue && pfIndex != int.MinValue && (pfIndex < 0 || pfIndex >= _result.ProfileCount))
		{
			throw new Exception("Invalid profile index: " + pfIndex);
		}
	}
}
