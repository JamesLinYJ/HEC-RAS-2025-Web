using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using H5Assist.Caching.VB;
using Utility;

namespace RasMapperLib.Render;

public class DepthAndVelocityRenderer : Renderer
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__5_002D0
	{
		public float[] outDepths;

		public float[] outVels;

		public float[] lOutput;

		public _Closure_0024__5_002D3 outerClosure;

		public _Closure_0024__5_002D0(_Closure_0024__5_002D0 other)
		{
			if (other != null)
			{
				outDepths = other.outDepths;
				outVels = other.outVels;
				lOutput = other.lOutput;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int fin)
		{
			for (int i = start; i <= fin; i = checked(i + 1))
			{
				float outDepth = outDepths[i];
				float outVel = outVels[i];
				if (outDepth != -9999f && outVel != -9999f)
				{
					lOutput[i] = outDepth * outVel;
				}
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int fin)
		{
			for (int i = start; i <= fin; i = checked(i + 1))
			{
				float outDepth = outDepths[i];
				float outVel = outVels[i];
				if (outDepth != -9999f && outVel != -9999f)
				{
					lOutput[i] = outDepth * outVel * outVel;
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__5_002D1
	{
		public float gravity;

		public _Closure_0024__5_002D0 outerClosure;

		public _Closure_0024__5_002D1(_Closure_0024__5_002D1 other)
		{
			if (other != null)
			{
				gravity = other.gravity;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__2(int start, int fin)
		{
			for (int i = start; i <= fin; i = checked(i + 1))
			{
				float outDepth = outerClosure.outDepths[i];
				float outVel = outerClosure.outVels[i];
				if (outDepth != -9999f && outVel != -9999f)
				{
					outerClosure.lOutput[i] = (float)((double)outVel / Math.Sqrt(gravity * outDepth));
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__5_002D2
	{
		public float gravity;

		public _Closure_0024__5_002D0 outerClosure;

		public _Closure_0024__5_002D2(_Closure_0024__5_002D2 other)
		{
			if (other != null)
			{
				gravity = other.gravity;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__3(int start, int fin)
		{
			for (int i = start; i <= fin; i = checked(i + 1))
			{
				float outDepth = outerClosure.outDepths[i];
				float outVel = outerClosure.outVels[i];
				if (outDepth != -9999f && outVel != -9999f)
				{
					outerClosure.lOutput[i] = outDepth + outVel * outVel / (2f * gravity);
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__5_002D3
	{
		public float[] terrain;

		public _Closure_0024__5_002D3(_Closure_0024__5_002D3 other)
		{
			if (other != null)
			{
				terrain = other.terrain;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__5_002D4
	{
		public float gravity;

		public _Closure_0024__5_002D0 outerClosure;

		public _Closure_0024__5_002D4(_Closure_0024__5_002D4 other)
		{
			if (other != null)
			{
				gravity = other.gravity;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__4(int start, int fin)
		{
			for (int i = start; i <= fin; i = checked(i + 1))
			{
				float outDepth = outerClosure.outDepths[i];
				float outVel = outerClosure.outVels[i];
				float terrain = outerClosure.outerClosure.terrain[i];
				if (outDepth != -9999f && outVel != -9999f)
				{
					outerClosure.lOutput[i] = outDepth + terrain + outVel * outVel / (2f * gravity);
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__7_002D0
	{
		public Func<float[]> getReusableBuffer;

		public Action<float[]> returnReusableBuffer;

		public _Closure_0024__7_002D0(_Closure_0024__7_002D0 other)
		{
			if (other != null)
			{
				getReusableBuffer = other.getReusableBuffer;
				returnReusableBuffer = other.returnReusableBuffer;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__7_002D1
	{
		public List<Func<RASGeometryMapPoints, float[], float[]>> computers;

		public _Closure_0024__7_002D0 outerClosure;

		public _Closure_0024__7_002D1(_Closure_0024__7_002D1 other)
		{
			if (other != null)
			{
				computers = other.computers;
			}
		}

		[SpecialName]
		internal float[] _Lambda_0024__0(RASGeometryMapPoints mps, float[] terrain)
		{
			float[] a = outerClosure.getReusableBuffer();
			Buffers.ArraySetAllVB(ref a, -9999f, mps.InputCount);
			checked
			{
				int countOffset = computers.Count - 1;
				for (int i = 0; i <= countOffset; i++)
				{
					float[] floatValues = computers[i](mps, terrain);
					int count = floatValues.Count() - 1;
					for (int j = 0; j <= count; j++)
					{
						float computed = a[j];
						float floatValue = floatValues[j];
						if (computed == -9999f)
						{
							a[j] = floatValue;
						}
						else if (floatValue != -9999f)
						{
							a[j] = Math.Min(computed, floatValue);
						}
					}
					outerClosure.returnReusableBuffer(floatValues);
				}
				return a;
			}
		}

		[SpecialName]
		internal float[] _Lambda_0024__1(RASGeometryMapPoints mps, float[] terrain)
		{
			float[] a = outerClosure.getReusableBuffer();
			Buffers.ArraySetAllVB(ref a, -9999f, mps.InputCount);
			checked
			{
				int countOffset = computers.Count - 1;
				for (int i = 0; i <= countOffset; i++)
				{
					float[] floatValues = computers[i](mps, terrain);
					int count = floatValues.Count() - 1;
					for (int j = 0; j <= count; j++)
					{
						float computed = a[j];
						float floatValue = floatValues[j];
						if (computed == -9999f)
						{
							a[j] = floatValue;
						}
						else if (floatValue != -9999f)
						{
							a[j] = Math.Max(computed, floatValue);
						}
					}
					outerClosure.returnReusableBuffer(floatValues);
				}
				return a;
			}
		}
	}

	private VelocityRenderer _velRenderer;

	private DepthVelocityMethod _dvMethod;

	private H5RowCache<float> _resRowCache;

	public static void Compute(RASResults res, int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] outputDVValues, DepthVelocityMethod dvMethod, CacheCollection cache = null)
	{
		new DepthAndVelocityRenderer(res, cache, dvMethod).Compute(profile, mps, terrain, ref outputDVValues);
	}

	protected DepthAndVelocityRenderer(RASResults res, CacheCollection cache, DepthVelocityMethod dvMethod)
		: base(res, cache, needsVelocityFlowData: true, needsPostProcessFile: false)
	{
		_dvMethod = dvMethod;
		_resRowCache = _cache.GetRowCache<float>(_hdfFilename);
		_velRenderer = new VelocityRenderer(_result, cache);
	}

	protected void Compute(int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] output)
	{
		_Closure_0024__5_002D3 capturedLocals = new _Closure_0024__5_002D3(null);
		capturedLocals.terrain = terrain;
		Buffers.ArraySetAllVB(ref output, -9999f, mps.InputCount);
		if (profile == int.MaxValue || profile == int.MinValue)
		{
			return;
		}
		_Closure_0024__5_002D0 capturedLocals2 = default;
		_Closure_0024__5_002D0 capturedLocals3 = new _Closure_0024__5_002D0(capturedLocals2);
		capturedLocals3.outerClosure = capturedLocals;
		capturedLocals3.outVels = null;
		capturedLocals3.lOutput = output;
		capturedLocals3.outDepths = null;
		VelocityRenderer velRenderer = _velRenderer;
		float[] terrain2 = capturedLocals3.outerClosure.terrain;
		float[] outVelocityX = null;
		float[] outVelocityY = null;
		velRenderer.Compute(profile, mps, terrain2, ref outVelocityX, ref outVelocityY, ref capturedLocals3.outVels, ref capturedLocals3.outDepths);
		checked
		{
			if (_dvMethod == DepthVelocityMethod.DepthTimesVelocity)
			{
				Parallel.SmartFor(capturedLocals3.outDepths.Length, [SpecialName] (int start, int fin) =>
				{
					for (int i = start; i <= fin; i++)
					{
						float outDepth = capturedLocals3.outDepths[i];
						float outVel = capturedLocals3.outVels[i];
						if (outDepth != -9999f && outVel != -9999f)
						{
							capturedLocals3.lOutput[i] = outDepth * outVel;
						}
					}
				});
			}
			else if (_dvMethod == DepthVelocityMethod.DepthTimesVelocitySquared)
			{
				Parallel.SmartFor(capturedLocals3.outDepths.Length, [SpecialName] (int start, int fin) =>
				{
					for (int i = start; i <= fin; i++)
					{
						float outDepth2 = capturedLocals3.outDepths[i];
						float outVel2 = capturedLocals3.outVels[i];
						if (outDepth2 != -9999f && outVel2 != -9999f)
						{
							capturedLocals3.lOutput[i] = outDepth2 * outVel2 * outVel2;
						}
					}
				});
			}
			else if (_dvMethod == DepthVelocityMethod.Froude)
			{
				_Closure_0024__5_002D1 capturedLocals4 = default;
				_Closure_0024__5_002D1 capturedLocals5 = new _Closure_0024__5_002D1(capturedLocals4);
				capturedLocals5.outerClosure = capturedLocals3;
				capturedLocals5.gravity = SharedData.Gravity;
				Parallel.SmartFor(capturedLocals5.outerClosure.outDepths.Length, [SpecialName] (int start, int fin) =>
				{
					for (int i = start; i <= fin; i++)
					{
						float outDepth3 = capturedLocals5.outerClosure.outDepths[i];
						float outVel3 = capturedLocals5.outerClosure.outVels[i];
						if (outDepth3 != -9999f && outVel3 != -9999f)
						{
							capturedLocals5.outerClosure.lOutput[i] = (float)((double)outVel3 / Math.Sqrt(capturedLocals5.gravity * outDepth3));
						}
					}
				});
			}
			else if (_dvMethod == DepthVelocityMethod.EnergyDepth)
			{
				_Closure_0024__5_002D2 capturedLocals6 = default;
				_Closure_0024__5_002D2 capturedLocals7 = new _Closure_0024__5_002D2(capturedLocals6);
				capturedLocals7.outerClosure = capturedLocals3;
				capturedLocals7.gravity = SharedData.Gravity;
				Parallel.SmartFor(capturedLocals7.outerClosure.outDepths.Length, [SpecialName] (int start, int fin) =>
				{
					for (int i = start; i <= fin; i++)
					{
						float outDepth4 = capturedLocals7.outerClosure.outDepths[i];
						float outVel4 = capturedLocals7.outerClosure.outVels[i];
						if (outDepth4 != -9999f && outVel4 != -9999f)
						{
							capturedLocals7.outerClosure.lOutput[i] = outDepth4 + outVel4 * outVel4 / (2f * capturedLocals7.gravity);
						}
					}
				});
			}
			else
			{
				if (_dvMethod != DepthVelocityMethod.EnergyElevation)
				{
					return;
				}
				_Closure_0024__5_002D4 capturedLocals8 = default;
				_Closure_0024__5_002D4 capturedLocals9 = new _Closure_0024__5_002D4(capturedLocals8);
				capturedLocals9.outerClosure = capturedLocals3;
				capturedLocals9.gravity = SharedData.Gravity;
				Parallel.SmartFor(capturedLocals9.outerClosure.outDepths.Length, [SpecialName] (int start, int fin) =>
				{
					for (int i = start; i <= fin; i++)
					{
						float outDepth5 = capturedLocals9.outerClosure.outDepths[i];
						float outVel5 = capturedLocals9.outerClosure.outVels[i];
						float terrain3 = capturedLocals9.outerClosure.outerClosure.terrain[i];
						if (outDepth5 != -9999f && outVel5 != -9999f)
						{
							capturedLocals9.outerClosure.lOutput[i] = outDepth5 + terrain3 + outVel5 * outVel5 / (2f * capturedLocals9.gravity);
						}
					}
				});
			}
		}
	}

	public static Func<RASGeometryMapPoints, float[], float[]> GetComputer(RASResults res, int profile, DepthVelocityMethod dvMethod, CacheCollection cache, Func<float[]> GetReusableBuffer, Action<float[]> ReturnReusableBuffer)
	{
		return new DepthAndVelocityRenderer(res, cache, dvMethod).GetDVComputer(profile, GetReusableBuffer, ReturnReusableBuffer);
	}

	public Func<RASGeometryMapPoints, float[], float[]> GetDVComputer(int profile, Func<float[]> GetReusableBuffer, Action<float[]> ReturnReusableBuffer)
	{
		_Closure_0024__7_002D0 capturedLocals = new _Closure_0024__7_002D0(null);
		capturedLocals.getReusableBuffer = GetReusableBuffer;
		capturedLocals.returnReusableBuffer = ReturnReusableBuffer;
		checked
		{
			if (profile == int.MaxValue || profile == int.MinValue)
			{
				_Closure_0024__7_002D1 capturedLocals2 = default;
				_Closure_0024__7_002D1 capturedLocals3 = new _Closure_0024__7_002D1(capturedLocals2);
				capturedLocals3.outerClosure = capturedLocals;
				_velRenderer.ForceLoadProfileDatasets();
				capturedLocals3.computers = new List<Func<RASGeometryMapPoints, float[], float[]>>();
				int count = _result.ProfileCount - 1;
				for (int i = 0; i <= count; i++)
				{
					capturedLocals3.computers.Add(VelocityRenderer.GetDVProfileComputer(_result, i, _dvMethod, _cache, capturedLocals3.outerClosure.getReusableBuffer, capturedLocals3.outerClosure.returnReusableBuffer));
				}
				if (profile == int.MinValue)
				{
					return [SpecialName] (RASGeometryMapPoints mps, float[] terrain) =>
					{
						float[] a = capturedLocals3.outerClosure.getReusableBuffer();
						Buffers.ArraySetAllVB(ref a, -9999f, mps.InputCount);
						int count2 = capturedLocals3.computers.Count - 1;
						for (int j = 0; j <= count2; j++)
						{
							float[] floatValues = capturedLocals3.computers[j](mps, terrain);
							int count3 = floatValues.Count() - 1;
							for (int k = 0; k <= count3; k++)
							{
								float computed = a[k];
								float computed2 = floatValues[k];
								if (computed == -9999f)
								{
									a[k] = computed2;
								}
								else if (computed2 != -9999f)
								{
									a[k] = Math.Min(computed, computed2);
								}
							}
							capturedLocals3.outerClosure.returnReusableBuffer(floatValues);
						}
						return a;
					};
				}
				return [SpecialName] (RASGeometryMapPoints mps, float[] terrain) =>
				{
					float[] a = capturedLocals3.outerClosure.getReusableBuffer();
					Buffers.ArraySetAllVB(ref a, -9999f, mps.InputCount);
					int count4 = capturedLocals3.computers.Count - 1;
					for (int j = 0; j <= count4; j++)
					{
						float[] floatValuesArray = capturedLocals3.computers[j](mps, terrain);
						int count5 = floatValuesArray.Count() - 1;
						for (int k = 0; k <= count5; k++)
						{
							float computed3 = a[k];
							float computed4 = floatValuesArray[k];
							if (computed3 == -9999f)
							{
								a[k] = computed4;
							}
							else if (computed4 != -9999f)
							{
								a[k] = Math.Max(computed3, computed4);
							}
						}
						capturedLocals3.outerClosure.returnReusableBuffer(floatValuesArray);
					}
					return a;
				};
			}
			return VelocityRenderer.GetDVProfileComputer(_result, profile, _dvMethod, _cache, capturedLocals.getReusableBuffer, capturedLocals.returnReusableBuffer);
		}
	}
}
