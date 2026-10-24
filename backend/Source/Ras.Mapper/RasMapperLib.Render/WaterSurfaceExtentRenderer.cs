using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using H5Assist.Caching.VB;
using RasMapperLib.Mapping;
using Utility;

namespace RasMapperLib.Render;

public class WaterSurfaceExtentRenderer : WaterSurfaceRenderer
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__2_002D0
	{
		public RASGeometryMapPoints mps;

		public float[] lWaterSurfaces;

		public float[] clipTerrain;

		public WaterSurfaceExtentRenderer waterSurfaceExtentRenderer;

		public _Closure_0024__2_002D0(_Closure_0024__2_002D0 other)
		{
			if (other != null)
			{
				mps = other.mps;
				lWaterSurfaces = other.lWaterSurfaces;
				clipTerrain = other.clipTerrain;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__2(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				if (lWaterSurfaces[i] < clipTerrain[i] + waterSurfaceExtentRenderer.MinWSPlotTolerance || clipTerrain[i] == -9999f)
				{
					lWaterSurfaces[i] = -9999f;
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__2_002D1
	{
		public float[] xsWaterSurfaces;

		public Tuple<float, float>[] xsEncroachments;

		public _Closure_0024__2_002D0 outerClosure;

		public _Closure_0024__2_002D1(_Closure_0024__2_002D1 other)
		{
			if (other != null)
			{
				xsWaterSurfaces = other.xsWaterSurfaces;
				xsEncroachments = other.xsEncroachments;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0()
		{
			outerClosure.waterSurfaceExtentRenderer.ComputeWaterSurface1DInternal(outerClosure.mps, xsWaterSurfaces, xsEncroachments, outerClosure.lWaterSurfaces);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__2_002D2
	{
		public float[] ws;

		public _Closure_0024__2_002D0 outerClosure;

		public _Closure_0024__2_002D2(_Closure_0024__2_002D2 other)
		{
			if (other != null)
			{
				ws = other.ws;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1()
		{
			outerClosure.waterSurfaceExtentRenderer.ComputeWaterSurfaceInternalSA(outerClosure.mps, ws, outerClosure.lWaterSurfaces);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__3_002D0
	{
		public FlatMeshMap flatArea;

		public _Closure_0024__3_002D2 outerClosure;

		public _Closure_0024__3_002D0(_Closure_0024__3_002D0 other)
		{
			if (other != null)
			{
				flatArea = other.flatArea;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__3_002D1
	{
		public float[] cellWaterSurfaces;

		public _Closure_0024__3_002D0 outerClosure;

		public _Closure_0024__3_002D1(_Closure_0024__3_002D1 other)
		{
			if (other != null)
			{
				cellWaterSurfaces = other.cellWaterSurfaces;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0()
		{
			outerClosure.outerClosure.waterSurfaceExtentRenderer.ComputeCellValuesFlat(outerClosure.flatArea, cellWaterSurfaces, outerClosure.outerClosure.outWaterSurfaces, null, cellWaterSurfaces);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__3_002D2
	{
		public float[] outWaterSurfaces;

		public WaterSurfaceExtentRenderer waterSurfaceExtentRenderer;

		public _Closure_0024__3_002D2(_Closure_0024__3_002D2 other)
		{
			if (other != null)
			{
				outWaterSurfaces = other.outWaterSurfaces;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__3_002D3
	{
		public SlopingMeshMap slopedArea;

		public _Closure_0024__3_002D2 outerClosure;

		public _Closure_0024__3_002D3(_Closure_0024__3_002D3 other)
		{
			if (other != null)
			{
				slopedArea = other.slopedArea;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__3_002D4
	{
		public float[] cellWaterSurfaces;

		public _Closure_0024__3_002D3 outerClosure;

		public _Closure_0024__3_002D4(_Closure_0024__3_002D4 other)
		{
			if (other != null)
			{
				cellWaterSurfaces = other.cellWaterSurfaces;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1()
		{
			outerClosure.outerClosure.waterSurfaceExtentRenderer.ComputeCellValuesFlat(outerClosure.slopedArea, cellWaterSurfaces, outerClosure.outerClosure.outWaterSurfaces, null, cellWaterSurfaces);
		}

		[SpecialName]
		internal void _Lambda_0024__2()
		{
			outerClosure.outerClosure.waterSurfaceExtentRenderer.ComputeWS2DSloped(outerClosure.slopedArea, cellWaterSurfaces, outerClosure.outerClosure.outWaterSurfaces);
		}
	}

	public new static void Compute(RASResults res, int profile, RASGeometryMapPoints mps, ref float[] waterSurfaces, float[] clipTerrain = null, CacheCollection cache = null)
	{
		WaterSurfaceExtentRenderer waterSurfaceExtentRenderer = new WaterSurfaceExtentRenderer(res, cache);
		float[] outputTimes = null;
		waterSurfaceExtentRenderer.Compute(profile, mps, ref waterSurfaces, clipTerrain, ref outputTimes);
	}

	public WaterSurfaceExtentRenderer(RASResults res, CacheCollection cache = null)
		: base(res, cache)
	{
	}

	public override void Compute(int profile, RASGeometryMapPoints mps, ref float[] waterSurfaces, [Optional][DefaultParameterValue(null)] float[] clipTerrain, [Optional][DefaultParameterValue(null)] ref float[] outputTimes)
	{
		_Closure_0024__2_002D0 capturedLocals = default;
		_Closure_0024__2_002D0 capturedLocals2 = new _Closure_0024__2_002D0(capturedLocals);
		capturedLocals2.waterSurfaceExtentRenderer = this;
		capturedLocals2.mps = mps;
		capturedLocals2.clipTerrain = clipTerrain;
		Buffers.ArraySetAllVB(ref waterSurfaces, -9999f, capturedLocals2.mps.InputCount);
		List<Task> tasks = new List<Task>();
		capturedLocals2.lWaterSurfaces = waterSurfaces;
		string dataset = null;
		int row = default;
		_names.Get1D(profile, ref dataset, ref row);
		if (!string.IsNullOrEmpty(dataset) && capturedLocals2.mps.MappedXSISs.Count > 0)
		{
			_Closure_0024__2_002D1 capturedLocals3 = default;
			_Closure_0024__2_002D1 capturedLocals4 = new _Closure_0024__2_002D1(capturedLocals3);
			capturedLocals4.outerClosure = capturedLocals2;
			capturedLocals4.xsWaterSurfaces = _rowCache.EnsureGetRow(dataset, row);
			capturedLocals4.xsEncroachments = _result.ReadXSEncroachments(profile);
			tasks.Add(Task.Factory.StartNew([SpecialName] () =>
			{
				capturedLocals4.outerClosure.waterSurfaceExtentRenderer.ComputeWaterSurface1DInternal(capturedLocals4.outerClosure.mps, capturedLocals4.xsWaterSurfaces, capturedLocals4.xsEncroachments, capturedLocals4.outerClosure.lWaterSurfaces);
			}));
		}
		string dataset2 = null;
		int row2 = default;
		_names.GetSA(profile, ref dataset2, ref row2);
		if (!string.IsNullOrEmpty(dataset2) && capturedLocals2.mps.MappedSAs.Count > 0)
		{
			_Closure_0024__2_002D2 capturedLocals5 = default;
			_Closure_0024__2_002D2 capturedLocals6 = new _Closure_0024__2_002D2(capturedLocals5);
			capturedLocals6.outerClosure = capturedLocals2;
			capturedLocals6.ws = _rowCache.EnsureGetRow(dataset2, row2);
			tasks.Add(Task.Factory.StartNew([SpecialName] () =>
			{
				capturedLocals6.outerClosure.waterSurfaceExtentRenderer.ComputeWaterSurfaceInternalSA(capturedLocals6.outerClosure.mps, capturedLocals6.ws, capturedLocals6.outerClosure.lWaterSurfaces);
			}));
		}
		List<string> datasets = null;
		int row3 = default;
		_names.Get2D(profile, ref datasets, ref row3);
		tasks.AddRange(ComputeWaterSurfaceExtentInternal2D(capturedLocals2.mps, ReadMappedMeshProfile(datasets, row3, _rowCache, capturedLocals2.mps), waterSurfaces));
		if (tasks.Count == 0)
		{
			return;
		}
		Task.WaitAll(tasks.ToArray());
		if (capturedLocals2.clipTerrain == null)
		{
			return;
		}
		Utility.Parallel.SmartFor(waterSurfaces.Count(), [SpecialName] (int start, int finish) =>
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				if (capturedLocals2.lWaterSurfaces[i] < capturedLocals2.clipTerrain[i] + capturedLocals2.waterSurfaceExtentRenderer.MinWSPlotTolerance || capturedLocals2.clipTerrain[i] == -9999f)
				{
					capturedLocals2.lWaterSurfaces[i] = -9999f;
				}
			}
		});
	}

	protected List<Task> ComputeWaterSurfaceExtentInternal2D(RASGeometryMapPoints mps, float[][] meshCellWaterSurfaces, float[] outWaterSurfaces)
	{
		_Closure_0024__3_002D2 capturedLocals = new _Closure_0024__3_002D2(null);
		capturedLocals.waterSurfaceExtentRenderer = this;
		capturedLocals.outWaterSurfaces = outWaterSurfaces;
		List<Task> waterSurfaceExtentInternal2D = new List<Task>();
		if (meshCellWaterSurfaces == null)
		{
			return waterSurfaceExtentInternal2D;
		}
		using (List<FlatMeshMap>.Enumerator flatMapped2DAreas = mps.FlatMapped2DAreas().GetEnumerator())
		{
			_Closure_0024__3_002D0 capturedLocals2 = default;
			_Closure_0024__3_002D1 capturedLocals3 = default;
			while (flatMapped2DAreas.MoveNext())
			{
				capturedLocals2 = new _Closure_0024__3_002D0(capturedLocals2);
				capturedLocals2.outerClosure = capturedLocals;
				capturedLocals2.flatArea = flatMapped2DAreas.Current;
				capturedLocals3 = new _Closure_0024__3_002D1(capturedLocals3);
				capturedLocals3.outerClosure = capturedLocals2;
				capturedLocals3.cellWaterSurfaces = meshCellWaterSurfaces[capturedLocals3.outerClosure.flatArea.MeshIndex];
				waterSurfaceExtentInternal2D.Add(Task.Factory.StartNew(capturedLocals3._Lambda_0024__0));
			}
		}
		using (List<SlopingMeshMap>.Enumerator slopingMapped2DAreas = mps.SlopingMapped2DAreas().GetEnumerator())
		{
			_Closure_0024__3_002D3 capturedLocals4 = default;
			_Closure_0024__3_002D4 capturedLocals5 = default;
			while (slopingMapped2DAreas.MoveNext())
			{
				capturedLocals4 = new _Closure_0024__3_002D3(capturedLocals4);
				capturedLocals4.outerClosure = capturedLocals;
				capturedLocals4.slopedArea = slopingMapped2DAreas.Current;
				capturedLocals5 = new _Closure_0024__3_002D4(capturedLocals5);
				capturedLocals5.outerClosure = capturedLocals4;
				capturedLocals5.cellWaterSurfaces = meshCellWaterSurfaces[capturedLocals5.outerClosure.slopedArea.MeshIndex];
				if (SharedData.CellRenderMode == SharedData.CellRenderMethod.AsComputed)
				{
					waterSurfaceExtentInternal2D.Add(Task.Factory.StartNew(capturedLocals5._Lambda_0024__1));
					continue;
				}
				if (SharedData.CellRenderMode == SharedData.CellRenderMethod.Pretty)
				{
					waterSurfaceExtentInternal2D.Add(Task.Factory.StartNew(capturedLocals5._Lambda_0024__2));
					continue;
				}
				throw new NotImplementedException();
			}
		}
		return waterSurfaceExtentInternal2D;
	}
}
