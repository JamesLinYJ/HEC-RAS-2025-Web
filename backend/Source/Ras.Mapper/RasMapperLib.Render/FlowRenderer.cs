using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using H5Assist.Caching.VB;
using RasMapperLib.Mapping;
using Utility;

namespace RasMapperLib.Render;

public class FlowRenderer : Renderer
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__7_002D0
	{
		public float[] perXSFlow;

		public float[] clipPixelDepths;

		public float[] outFlows;

		public FlowRenderer flowRenderer;

		public _Closure_0024__7_002D0(_Closure_0024__7_002D0 other)
		{
			if (other != null)
			{
				perXSFlow = other.perXSFlow;
				clipPixelDepths = other.clipPixelDepths;
				outFlows = other.outFlows;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(MappedXSIS interpSurface)
		{
			_Closure_0024__7_002D1 localValue = default;
			_Closure_0024__7_002D1 capturedLocals = new _Closure_0024__7_002D1(localValue);
			capturedLocals.outerClosure = this;
			int iD = interpSurface.ID;
			int upstreamXSID = flowRenderer._XSIS.GetUpstreamXSID(iD);
			int downstreamXSID = flowRenderer._XSIS.GetDownstreamXSID(iD);
			capturedLocals.upstreamFlow = perXSFlow[upstreamXSID];
			capturedLocals.downstreamFlow = perXSFlow[downstreamXSID];
			capturedLocals.mapPts = interpSurface.MapPoints;
			Utility.Parallel.SmartFor(capturedLocals.mapPts.Count, [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i = checked(i + 1))
				{
					XSISMapPoint xSISMapPoint = capturedLocals.mapPts[i];
					if (capturedLocals.outerClosure.clipPixelDepths[xSISMapPoint.Index] > 0f)
					{
						capturedLocals.outerClosure.outFlows[xSISMapPoint.Index] = capturedLocals.upstreamFlow * xSISMapPoint.Z + capturedLocals.downstreamFlow * (1f - xSISMapPoint.Z);
					}
				}
			});
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__7_002D1
	{
		public List<XSISMapPoint> mapPts;

		public float upstreamFlow;

		public float downstreamFlow;

		public _Closure_0024__7_002D0 outerClosure;

		public _Closure_0024__7_002D1(_Closure_0024__7_002D1 other)
		{
			if (other != null)
			{
				mapPts = other.mapPts;
				upstreamFlow = other.upstreamFlow;
				downstreamFlow = other.downstreamFlow;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				XSISMapPoint xSISMapPoint = mapPts[i];
				if (outerClosure.clipPixelDepths[xSISMapPoint.Index] > 0f)
				{
					outerClosure.outFlows[xSISMapPoint.Index] = upstreamFlow * xSISMapPoint.Z + downstreamFlow * (1f - xSISMapPoint.Z);
				}
			}
		}
	}

	private Names _flowNames;

	private DepthRenderer _depthRenderer;

	public static void Compute(RASResults res, int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] outFlows, CacheCollection cache = null)
	{
		new FlowRenderer(res, cache).Compute(profile, mps, terrain, ref outFlows);
	}

	public FlowRenderer(RASResults res, CacheCollection cache = null)
		: base(res, cache, needsVelocityFlowData: true, needsPostProcessFile: false)
	{
		ref Names flowNames = ref _flowNames;
		Names wsNames = null;
		GetFlowDatasetNames(ref flowNames, ref wsNames);
		_flowNames.Validate(_hdfFilename);
		_depthRenderer = new DepthRenderer(res, cache);
	}

	public static float[] Read1DTimeSeries(RASResults res, int xsidx)
	{
		return new FlowRenderer(res).Read1DTimeSeries(xsidx);
	}

	public float[] Read1DTimeSeries(int xsIdx)
	{
		_ = new float[checked(_result.ProfileCount - 1 + 1)];
		H5ColumnCache<float> columnCache = _cache.GetColumnCache<float>(_postProcessFilename);
		string dataset = null;
		Names flowNames = _flowNames;
		int row = 0;
		flowNames.Get1D(0, ref dataset, ref row);
		return columnCache.EnsureGetColumn(dataset, xsIdx);
	}

	public void Compute(int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] outFlows)
	{
		float[] depths = null;
		_depthRenderer.Compute1D(profile, mps, terrain, ref depths);
		H5RowCache<float> rowCache = _cache.GetRowCache<float>(_hdfFilename);
		Buffers.ArraySetAllVB(ref outFlows, -9999f, mps.InputCount);
		string dataset = null;
		int row = 0;
		_flowNames.Get1D(profile, ref dataset, ref row);
		if (dataset != null)
		{
			float[] perXSFlow = rowCache.EnsureGetRow(dataset, row);
			ComputeFlowInternal1D(perXSFlow, mps, outFlows, depths);
		}
	}

	private void ComputeFlowInternal1D(float[] perXSFlow, RASGeometryMapPoints mps, float[] outFlows, float[] clipPixelDepths)
	{
		_Closure_0024__7_002D0 capturedLocals = default;
		_Closure_0024__7_002D0 capturedLocals2 = new _Closure_0024__7_002D0(capturedLocals);
		capturedLocals2.flowRenderer = this;
		capturedLocals2.perXSFlow = perXSFlow;
		capturedLocals2.outFlows = outFlows;
		capturedLocals2.clipPixelDepths = clipPixelDepths;
		System.Threading.Tasks.Parallel.ForEach(mps.MappedXSISs, [SpecialName] (MappedXSIS interpSurface) =>
		{
			_Closure_0024__7_002D1 capturedLocals3 = default;
			_Closure_0024__7_002D1 capturedLocals4 = new _Closure_0024__7_002D1(capturedLocals3);
			capturedLocals4.outerClosure = capturedLocals2;
			int iD = interpSurface.ID;
			int upstreamXSID = capturedLocals2.flowRenderer._XSIS.GetUpstreamXSID(iD);
			int downstreamXSID = capturedLocals2.flowRenderer._XSIS.GetDownstreamXSID(iD);
			capturedLocals4.upstreamFlow = capturedLocals2.perXSFlow[upstreamXSID];
			capturedLocals4.downstreamFlow = capturedLocals2.perXSFlow[downstreamXSID];
			capturedLocals4.mapPts = interpSurface.MapPoints;
			Utility.Parallel.SmartFor(capturedLocals4.mapPts.Count, [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i = checked(i + 1))
				{
					XSISMapPoint xSISMapPoint = capturedLocals4.mapPts[i];
					if (capturedLocals4.outerClosure.clipPixelDepths[xSISMapPoint.Index] > 0f)
					{
						capturedLocals4.outerClosure.outFlows[xSISMapPoint.Index] = capturedLocals4.upstreamFlow * xSISMapPoint.Z + capturedLocals4.downstreamFlow * (1f - xSISMapPoint.Z);
					}
				}
			});
		});
	}
}
