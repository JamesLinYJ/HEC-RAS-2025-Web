using System;
using System.Linq;
using System.Runtime.CompilerServices;
using H5Assist.Caching.VB;
using Utility;

namespace RasMapperLib.Render;

public class DepthRenderer
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__5_002D0
	{
		public Func<RASGeometryMapPoints, float[], float[]> wsrWorker;

		public DepthRenderer depthRenderer;

		public _Closure_0024__5_002D0(_Closure_0024__5_002D0 other)
		{
			if (other != null)
			{
				wsrWorker = other.wsrWorker;
			}
		}

		[SpecialName]
		internal float[] _Lambda_0024__0(RASGeometryMapPoints mps, float[] terrain)
		{
			_Closure_0024__5_002D1 localValue = default;
			_Closure_0024__5_002D1 capturedLocals = new _Closure_0024__5_002D1(localValue);
			capturedLocals.terrain = terrain;
			capturedLocals.output = wsrWorker(mps, capturedLocals.terrain);
			Parallel.SmartFor(capturedLocals.output.Count(), [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i = checked(i + 1))
				{
					float output = capturedLocals.output[i];
					float terrain2 = capturedLocals.terrain[i];
					if (output != -9999f && terrain2 != -9999f)
					{
						capturedLocals.output[i] = output - terrain2;
					}
				}
			});
			return capturedLocals.output;
		}

		[SpecialName]
		internal float[] _Lambda_0024__2(RASGeometryMapPoints mps, float[] terrain)
		{
			_Closure_0024__5_002D2 localValue = default;
			_Closure_0024__5_002D2 capturedLocals = new _Closure_0024__5_002D2(localValue);
			capturedLocals.outerClosure = this;
			capturedLocals.terrain = terrain;
			capturedLocals.output = wsrWorker(mps, capturedLocals.terrain);
			Parallel.SmartFor(capturedLocals.output.Count(), [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i = checked(i + 1))
				{
					float output = capturedLocals.output[i];
					float terrain2 = capturedLocals.terrain[i];
					if (output == -9999f || output < terrain2 + capturedLocals.outerClosure.depthRenderer.MinWSPlotTolerance || terrain2 == -9999f)
					{
						output = -9999f;
					}
					else
					{
						capturedLocals.output[i] = output - terrain2;
					}
				}
			});
			return capturedLocals.output;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__5_002D1
	{
		public float[] output;

		public float[] terrain;

		public _Closure_0024__5_002D1(_Closure_0024__5_002D1 other)
		{
			if (other != null)
			{
				output = other.output;
				terrain = other.terrain;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				float output = this.output[i];
				float terrain = this.terrain[i];
				if (output != -9999f && terrain != -9999f)
				{
					this.output[i] = output - terrain;
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__5_002D2
	{
		public float[] output;

		public float[] terrain;

		public _Closure_0024__5_002D0 outerClosure;

		public _Closure_0024__5_002D2(_Closure_0024__5_002D2 other)
		{
			if (other != null)
			{
				output = other.output;
				terrain = other.terrain;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__3(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				float output = this.output[i];
				float terrain = this.terrain[i];
				if (output == -9999f || output < terrain + outerClosure.depthRenderer.MinWSPlotTolerance || terrain == -9999f)
				{
					output = -9999f;
				}
				else
				{
					this.output[i] = output - terrain;
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__7_002D0
	{
		public float[] lambdaWS;

		public float[] terrain;

		public float[] lambdaDepth;

		public DepthRenderer depthRenderer;

		public _Closure_0024__7_002D0(_Closure_0024__7_002D0 other)
		{
			if (other != null)
			{
				lambdaWS = other.lambdaWS;
				terrain = other.terrain;
				lambdaDepth = other.lambdaDepth;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				if (lambdaWS[i] != -9999f && terrain[i] != -9999f)
				{
					lambdaDepth[i] = lambdaWS[i] - terrain[i];
				}
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				float lambdaWS = this.lambdaWS[i];
				float terrain = this.terrain[i];
				if (lambdaWS != -9999f && !(lambdaWS < terrain + depthRenderer.MinWSPlotTolerance) && terrain != -9999f)
				{
					lambdaDepth[i] = lambdaWS - terrain;
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__9_002D0
	{
		public float[] outWS;

		public float[] terrain;

		public float[] lDepths;

		public DepthRenderer depthRenderer;

		public _Closure_0024__9_002D0(_Closure_0024__9_002D0 other)
		{
			if (other != null)
			{
				outWS = other.outWS;
				terrain = other.terrain;
				lDepths = other.lDepths;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				float outWS = this.outWS[i];
				float terrain = this.terrain[i];
				if (outWS != -9999f && !(outWS < terrain + depthRenderer.MinWSPlotTolerance) && terrain != -9999f)
				{
					lDepths[i] = outWS - terrain;
				}
			}
		}
	}

	private WaterSurfaceRenderer _wsr;

	private float MinWSPlotTolerance;

	public static void Compute(RASResults res, int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] depths, CacheCollection cache = null, bool allowNegativeDepths = false, double wsDepthThreshold = 0.0)
	{
		new DepthRenderer(res, cache, wsDepthThreshold).Compute(profile, mps, terrain, ref depths, allowNegativeDepths);
	}

	public DepthRenderer(RASResults res, CacheCollection cache = null, double wsDepthThreshold = 0.0)
	{
		MinWSPlotTolerance = RASResults.MinWSPlotTolerance;
		_wsr = new WaterSurfaceRenderer(res, cache, wsDepthThreshold);
	}

	public static Func<RASGeometryMapPoints, float[], float[]> GetComputer(RASResults res, int profile, CacheCollection cache, Func<float[]> GetReusableBuffer, Action<float[]> ReturnReusableBuffer, bool AllowNegativeDepths = false)
	{
		return new DepthRenderer(res, cache).GetComputer(profile, GetReusableBuffer, ReturnReusableBuffer, AllowNegativeDepths);
	}

	public Func<RASGeometryMapPoints, float[], float[]> GetComputer(int profile, Func<float[]> GetReusableBuffer, Action<float[]> ReturnReusableBuffer, bool AllowNegativeDepths = false)
	{
		_Closure_0024__5_002D0 capturedLocals = default;
		_Closure_0024__5_002D0 capturedLocals2 = new _Closure_0024__5_002D0(capturedLocals);
		capturedLocals2.depthRenderer = this;
		capturedLocals2.wsrWorker = _wsr.GetComputer(profile, AllowNegativeDepths, GetReusableBuffer, ReturnReusableBuffer);
		checked
		{
			if (AllowNegativeDepths)
			{
				return [SpecialName] (RASGeometryMapPoints mps, float[] terrain) =>
				{
					_Closure_0024__5_002D1 capturedLocals3 = default;
					_Closure_0024__5_002D1 capturedLocals4 = new _Closure_0024__5_002D1(capturedLocals3);
					capturedLocals4.terrain = terrain;
					capturedLocals4.output = capturedLocals2.wsrWorker(mps, capturedLocals4.terrain);
					Parallel.SmartFor(capturedLocals4.output.Count(), [SpecialName] (int start, int finish) =>
					{
						for (int i = start; i <= finish; i++)
						{
							float output = capturedLocals4.output[i];
							float terrain2 = capturedLocals4.terrain[i];
							if (output != -9999f && terrain2 != -9999f)
							{
								capturedLocals4.output[i] = output - terrain2;
							}
						}
					});
					return capturedLocals4.output;
				};
			}
			return [SpecialName] (RASGeometryMapPoints mps, float[] terrain) =>
			{
				_Closure_0024__5_002D2 capturedLocals5 = default;
				_Closure_0024__5_002D2 capturedLocals6 = new _Closure_0024__5_002D2(capturedLocals5);
				capturedLocals6.outerClosure = capturedLocals2;
				capturedLocals6.terrain = terrain;
				capturedLocals6.output = capturedLocals2.wsrWorker(mps, capturedLocals6.terrain);
				Parallel.SmartFor(capturedLocals6.output.Count(), [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						float computed = capturedLocals6.output[i];
						float terrain3 = capturedLocals6.terrain[i];
						if (computed == -9999f || computed < terrain3 + capturedLocals6.outerClosure.depthRenderer.MinWSPlotTolerance || terrain3 == -9999f)
						{
							computed = -9999f;
						}
						else
						{
							capturedLocals6.output[i] = computed - terrain3;
						}
					}
				});
				return capturedLocals6.output;
			};
		}
	}

	public float[] Compute(int profile, RASGeometryMapPoints mps, float[] terrain, bool allowNegativeDepths = false)
	{
		float[] depths = null;
		Compute(profile, mps, terrain, ref depths, allowNegativeDepths);
		return depths;
	}

	public void Compute(int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] depths, bool allowNegativeDepths = false)
	{
		_Closure_0024__7_002D0 capturedLocals = default;
		_Closure_0024__7_002D0 capturedLocals2 = new _Closure_0024__7_002D0(capturedLocals);
		capturedLocals2.depthRenderer = this;
		capturedLocals2.terrain = terrain;
		float[] waterSurfaces = null;
		WaterSurfaceRenderer wsr = _wsr;
		float[] outputTimes = null;
		wsr.Compute(profile, mps, ref waterSurfaces, null, ref outputTimes);
		Buffers.ArraySetAllVB(ref depths, -9999f, mps.InputCount);
		capturedLocals2.lambdaWS = waterSurfaces;
		capturedLocals2.lambdaDepth = depths;
		checked
		{
			if (allowNegativeDepths)
			{
				Parallel.SmartFor(waterSurfaces.Count(), [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						if (capturedLocals2.lambdaWS[i] != -9999f && capturedLocals2.terrain[i] != -9999f)
						{
							capturedLocals2.lambdaDepth[i] = capturedLocals2.lambdaWS[i] - capturedLocals2.terrain[i];
						}
					}
				});
				return;
			}
			Parallel.SmartFor(waterSurfaces.Count(), [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					float lambdaWS = capturedLocals2.lambdaWS[i];
					float terrain2 = capturedLocals2.terrain[i];
					if (lambdaWS != -9999f && !(lambdaWS < terrain2 + capturedLocals2.depthRenderer.MinWSPlotTolerance) && terrain2 != -9999f)
					{
						capturedLocals2.lambdaDepth[i] = lambdaWS - terrain2;
					}
				}
			});
		}
	}

	public static void Compute1D(RASResults res, int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] depths, CacheCollection cache = null)
	{
		new DepthRenderer(res, cache).Compute1D(profile, mps, terrain, ref depths);
	}

	public void Compute1D(int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] depths)
	{
		_Closure_0024__9_002D0 capturedLocals = default;
		_Closure_0024__9_002D0 capturedLocals2 = new _Closure_0024__9_002D0(capturedLocals);
		capturedLocals2.depthRenderer = this;
		capturedLocals2.terrain = terrain;
		capturedLocals2.outWS = null;
		_wsr.ComputeWaterSurface1D(mps, profile, ref capturedLocals2.outWS);
		Buffers.ArraySetAllVB(ref depths, -9999f, mps.InputCount);
		capturedLocals2.lDepths = depths;
		Parallel.SmartFor(capturedLocals2.outWS.Count(), [SpecialName] (int start, int finish) =>
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				float outWS = capturedLocals2.outWS[i];
				float terrain2 = capturedLocals2.terrain[i];
				if (outWS != -9999f && !(outWS < terrain2 + capturedLocals2.depthRenderer.MinWSPlotTolerance) && terrain2 != -9999f)
				{
					capturedLocals2.lDepths[i] = outWS - terrain2;
				}
			}
		});
	}
}
