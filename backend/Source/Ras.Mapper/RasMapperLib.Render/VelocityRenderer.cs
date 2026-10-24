using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using H5Assist.Caching.VB;
using RasMapperLib.Mapping;
using RasMapperLib.Mesh;
using Utility;
using Utility.Extensions;

namespace RasMapperLib.Render;

public class VelocityRenderer : Renderer
{
	private class VelocityComputerHelper
	{
		public float[][] MeshFaceVels;

		public float[][] MeshCellWS;

		public FaceValues[][] MeshFaceWSValues;

		public Tuple<Vector2D, Vector2D>[][] MeshFaceTrueVels;

		public FacePointVels[][] MeshFPVels;

		public Func<RASGeometryMapPoints, float[], float[]> DepthComputer;

		public float[] FlowXS;

		public float[] WsXS;

		public VelocityComputerHelper()
		{
			MeshFaceVels = null;
			MeshCellWS = null;
			MeshFaceWSValues = null;
			MeshFaceTrueVels = null;
			MeshFPVels = null;
			FlowXS = null;
			WsXS = null;
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__11_002D0
	{
		public Func<float[]> getReusableBuffer;

		public Func<RASGeometryMapPoints, float[], float[]> drComputer;

		public int profile;

		public string vel1D;

		public float[][] xsSubsectionVels;

		public string flow1D;

		public string ws1D;

		public float[] flowXS;

		public float[] wsXS;

		public List<string> vel2D;

		public List<string> ws2D;

		public float[][] meshFaceVels;

		public float[][] meshCellWS;

		public FaceValues[][] meshFaceWSValues;

		public Tuple<Vector2D, Vector2D>[][] meshFaceTrueVels;

		public FacePointVels[][] meshFPVels;

		public VelocityRenderer velocityRenderer;

		public _Closure_0024__11_002D0(_Closure_0024__11_002D0 other)
		{
			if (other != null)
			{
				getReusableBuffer = other.getReusableBuffer;
				drComputer = other.drComputer;
				profile = other.profile;
				vel1D = other.vel1D;
				xsSubsectionVels = other.xsSubsectionVels;
				flow1D = other.flow1D;
				ws1D = other.ws1D;
				flowXS = other.flowXS;
				wsXS = other.wsXS;
				vel2D = other.vel2D;
				ws2D = other.ws2D;
				meshFaceVels = other.meshFaceVels;
				meshCellWS = other.meshCellWS;
				meshFaceWSValues = other.meshFaceWSValues;
				meshFaceTrueVels = other.meshFaceTrueVels;
				meshFPVels = other.meshFPVels;
			}
		}

		[SpecialName]
		internal float[][] _Lambda_0024__0(RASGeometryMapPoints mps, float[] terrain)
		{
			_Closure_0024__11_002D1 localValue = default;
			_Closure_0024__11_002D1 capturedLocals = new _Closure_0024__11_002D1(localValue);
			capturedLocals.outerClosure = this;
			capturedLocals.mps = mps;
			capturedLocals.output = getReusableBuffer();
			capturedLocals.outX = getReusableBuffer();
			capturedLocals.outY = getReusableBuffer();
			Buffers.ArraySetAllVB(ref capturedLocals.output, -9999f, capturedLocals.mps.InputCount);
			checked
			{
				Buffer.BlockCopy(capturedLocals.output, 0, capturedLocals.outX, 0, capturedLocals.output.Count() * 4);
				Buffer.BlockCopy(capturedLocals.output, 0, capturedLocals.outY, 0, capturedLocals.output.Count() * 4);
				List<Task> tasks = new List<Task>();
				capturedLocals.outDepths = drComputer(capturedLocals.mps, terrain);
				if (profile == int.MaxValue || profile == int.MinValue)
				{
					if (vel1D != null && capturedLocals.mps.HasMapped1DAreas())
					{
						tasks.Add(Task.Factory.StartNew([SpecialName] () =>
						{
							capturedLocals.outerClosure.velocityRenderer.ComputeVelocityInternal1DFromVelocity(capturedLocals.outerClosure.xsSubsectionVels, capturedLocals.mps, capturedLocals.outDepths, capturedLocals.outX, capturedLocals.outY, capturedLocals.output);
						}));
					}
				}
				else if (flow1D != null && ws1D != null && capturedLocals.mps.HasMapped1DAreas())
				{
					tasks.Add(Task.Factory.StartNew([SpecialName] () =>
					{
						capturedLocals.outerClosure.velocityRenderer.ComputeVelocityInternal1DFromFlow(capturedLocals.outerClosure.flowXS, capturedLocals.outerClosure.wsXS, capturedLocals.mps, capturedLocals.outDepths, capturedLocals.outX, capturedLocals.outY, capturedLocals.output);
					}));
				}
				if (capturedLocals.mps.HasMappedStorageAreas())
				{
					tasks.Add(Task.Factory.StartNew([SpecialName] () =>
					{
						using List<SAMap>.Enumerator mappedSAs = capturedLocals.mps.MappedSAs.GetEnumerator();
						_Closure_0024__11_002D2 localValue2 = default;
						while (mappedSAs.MoveNext())
						{
							localValue2 = new _Closure_0024__11_002D2(localValue2);
							localValue2.outerClosure = capturedLocals;
							localValue2.sa = mappedSAs.Current;
							Utility.Parallel.SmartFor(localValue2.sa.MapPoints.Count, localValue2._Lambda_0024__4);
						}
					}));
				}
				if (!vel2D.IsNullOrEmpty() && !ws2D.IsNullOrEmpty() && capturedLocals.mps.HasMapped2DCells())
				{
					tasks.Add(Task.Factory.StartNew([SpecialName] () =>
					{
						capturedLocals.outerClosure.velocityRenderer.ComputeVelocity2D(capturedLocals.outerClosure.meshFaceVels, capturedLocals.outerClosure.meshCellWS, capturedLocals.mps, capturedLocals.outDepths, capturedLocals.outX, capturedLocals.outY, capturedLocals.output, capturedLocals.outerClosure.meshFaceWSValues, capturedLocals.outerClosure.meshFaceTrueVels, capturedLocals.outerClosure.meshFPVels);
					}));
				}
				Task.WaitAll(tasks.ToArray());
				return new float[5][] { capturedLocals.outX, capturedLocals.outY, capturedLocals.output, capturedLocals.outDepths, null };
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__11_002D1
	{
		public RASGeometryMapPoints mps;

		public float[] outDepths;

		public float[] outX;

		public float[] outY;

		public float[] output;

		public _Closure_0024__11_002D0 outerClosure;

		public _Closure_0024__11_002D1(_Closure_0024__11_002D1 other)
		{
			if (other != null)
			{
				mps = other.mps;
				outDepths = other.outDepths;
				outX = other.outX;
				outY = other.outY;
				output = other.output;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1()
		{
			outerClosure.velocityRenderer.ComputeVelocityInternal1DFromVelocity(outerClosure.xsSubsectionVels, mps, outDepths, outX, outY, output);
		}

		[SpecialName]
		internal void _Lambda_0024__2()
		{
			outerClosure.velocityRenderer.ComputeVelocityInternal1DFromFlow(outerClosure.flowXS, outerClosure.wsXS, mps, outDepths, outX, outY, output);
		}

		[SpecialName]
		internal void _Lambda_0024__3()
		{
			using List<SAMap>.Enumerator mappedSAs = mps.MappedSAs.GetEnumerator();
			_Closure_0024__11_002D2 localValue = default;
			while (mappedSAs.MoveNext())
			{
				localValue = new _Closure_0024__11_002D2(localValue);
				localValue.outerClosure = this;
				localValue.sa = mappedSAs.Current;
				Utility.Parallel.SmartFor(localValue.sa.MapPoints.Count, localValue._Lambda_0024__4);
			}
		}

		[SpecialName]
		internal void _Lambda_0024__5()
		{
			outerClosure.velocityRenderer.ComputeVelocity2D(outerClosure.meshFaceVels, outerClosure.meshCellWS, mps, outDepths, outX, outY, output, outerClosure.meshFaceWSValues, outerClosure.meshFaceTrueVels, outerClosure.meshFPVels);
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__11_002D2
	{
		public SAMap sa;

		public _Closure_0024__11_002D1 outerClosure;

		public _Closure_0024__11_002D2(_Closure_0024__11_002D2 other)
		{
			if (other != null)
			{
				sa = other.sa;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__4(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				int mapPoint = sa.MapPoints[i];
				if (outerClosure.outDepths[mapPoint] > 0f)
				{
					outerClosure.output[mapPoint] = 0f;
				}
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__13_002D0
	{
		public Tuple<Vector2D, Vector2D>[] toReturn;

		public float[] item1x;

		public float[] item1y;

		public float[] item2x;

		public float[] item2y;

		public _Closure_0024__13_002D0(_Closure_0024__13_002D0 other)
		{
			if (other != null)
			{
				toReturn = other.toReturn;
				item1x = other.item1x;
				item1y = other.item1y;
				item2x = other.item2x;
				item2y = other.item2y;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				toReturn[i] = new Tuple<Vector2D, Vector2D>(new Vector2D(item1x[i], item1y[i]), new Vector2D(item2x[i], item2y[i]));
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__14_002D0
	{
		public Func<float[]> getReusableBuffer;

		public Func<RASGeometryMapPoints, float[], float[]> drComputer;

		public int profile;

		public string vel1D;

		public float[][] xsSubsectionVels;

		public string flow1D;

		public string ws1D;

		public float[] flowXS;

		public float[] wsXS;

		public List<string> vel2D;

		public List<string> ws2D;

		public float[][] meshFaceVels;

		public float[][] meshCellWS;

		public FaceValues[][] meshFaceWSValues;

		public Tuple<Vector2D, Vector2D>[][] meshFaceTrueVels;

		public FacePointVels[][] meshFPVels;

		public Action<float[]> returnReusableBuffer;

		public VelocityRenderer velocityRenderer;

		public _Closure_0024__14_002D0(_Closure_0024__14_002D0 other)
		{
			if (other != null)
			{
				getReusableBuffer = other.getReusableBuffer;
				drComputer = other.drComputer;
				profile = other.profile;
				vel1D = other.vel1D;
				xsSubsectionVels = other.xsSubsectionVels;
				flow1D = other.flow1D;
				ws1D = other.ws1D;
				flowXS = other.flowXS;
				wsXS = other.wsXS;
				vel2D = other.vel2D;
				ws2D = other.ws2D;
				meshFaceVels = other.meshFaceVels;
				meshCellWS = other.meshCellWS;
				meshFaceWSValues = other.meshFaceWSValues;
				meshFaceTrueVels = other.meshFaceTrueVels;
				meshFPVels = other.meshFPVels;
				returnReusableBuffer = other.returnReusableBuffer;
			}
		}

		[SpecialName]
		internal float[] _Lambda_0024__0(RASGeometryMapPoints mps, float[] terrain)
		{
			_Closure_0024__14_002D1 localValue = default;
			_Closure_0024__14_002D1 capturedLocals = new _Closure_0024__14_002D1(localValue);
			capturedLocals.outerClosure = this;
			capturedLocals.mps = mps;
			capturedLocals.output = getReusableBuffer();
			capturedLocals.outX = getReusableBuffer();
			capturedLocals.outY = getReusableBuffer();
			Buffers.ArraySetAllVB(ref capturedLocals.output, -9999f, capturedLocals.mps.InputCount);
			checked
			{
				Buffer.BlockCopy(capturedLocals.output, 0, capturedLocals.outX, 0, capturedLocals.output.Count() * 4);
				Buffer.BlockCopy(capturedLocals.output, 0, capturedLocals.outY, 0, capturedLocals.output.Count() * 4);
				List<Task> tasks = new List<Task>();
				capturedLocals.outDepths = drComputer(capturedLocals.mps, terrain);
				if (profile == int.MaxValue || profile == int.MinValue)
				{
					if (vel1D != null && capturedLocals.mps.HasMapped1DAreas())
					{
						tasks.Add(Task.Factory.StartNew([SpecialName] () =>
						{
							capturedLocals.outerClosure.velocityRenderer.ComputeVelocityInternal1DFromVelocity(capturedLocals.outerClosure.xsSubsectionVels, capturedLocals.mps, capturedLocals.outDepths, capturedLocals.outX, capturedLocals.outY, capturedLocals.output);
						}));
					}
				}
				else if (flow1D != null && ws1D != null && capturedLocals.mps.HasMapped1DAreas())
				{
					tasks.Add(Task.Factory.StartNew([SpecialName] () =>
					{
						capturedLocals.outerClosure.velocityRenderer.ComputeVelocityInternal1DFromFlow(capturedLocals.outerClosure.flowXS, capturedLocals.outerClosure.wsXS, capturedLocals.mps, capturedLocals.outDepths, capturedLocals.outX, capturedLocals.outY, capturedLocals.output);
					}));
				}
				if (capturedLocals.mps.HasMappedStorageAreas())
				{
					tasks.Add(Task.Factory.StartNew([SpecialName] () =>
					{
						using List<SAMap>.Enumerator mappedSAs = capturedLocals.mps.MappedSAs.GetEnumerator();
						_Closure_0024__14_002D2 localValue2 = default;
						while (mappedSAs.MoveNext())
						{
							localValue2 = new _Closure_0024__14_002D2(localValue2);
							localValue2.outerClosure = capturedLocals;
							localValue2.sa = mappedSAs.Current;
							Utility.Parallel.SmartFor(localValue2.sa.MapPoints.Count, localValue2._Lambda_0024__4);
						}
					}));
				}
				if (!vel2D.IsNullOrEmpty() && !ws2D.IsNullOrEmpty() && capturedLocals.mps.HasMapped2DCells())
				{
					tasks.Add(Task.Factory.StartNew([SpecialName] () =>
					{
						capturedLocals.outerClosure.velocityRenderer.ComputeVelocity2D(capturedLocals.outerClosure.meshFaceVels, capturedLocals.outerClosure.meshCellWS, capturedLocals.mps, capturedLocals.outDepths, capturedLocals.outX, capturedLocals.outY, capturedLocals.output, capturedLocals.outerClosure.meshFaceWSValues, capturedLocals.outerClosure.meshFaceTrueVels, capturedLocals.outerClosure.meshFPVels);
					}));
				}
				Task.WaitAll(tasks.ToArray());
				returnReusableBuffer(capturedLocals.outX);
				returnReusableBuffer(capturedLocals.outY);
				returnReusableBuffer(capturedLocals.outDepths);
				return capturedLocals.output;
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__14_002D1
	{
		public RASGeometryMapPoints mps;

		public float[] outDepths;

		public float[] outX;

		public float[] outY;

		public float[] output;

		public _Closure_0024__14_002D0 outerClosure;

		public _Closure_0024__14_002D1(_Closure_0024__14_002D1 other)
		{
			if (other != null)
			{
				mps = other.mps;
				outDepths = other.outDepths;
				outX = other.outX;
				outY = other.outY;
				output = other.output;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1()
		{
			outerClosure.velocityRenderer.ComputeVelocityInternal1DFromVelocity(outerClosure.xsSubsectionVels, mps, outDepths, outX, outY, output);
		}

		[SpecialName]
		internal void _Lambda_0024__2()
		{
			outerClosure.velocityRenderer.ComputeVelocityInternal1DFromFlow(outerClosure.flowXS, outerClosure.wsXS, mps, outDepths, outX, outY, output);
		}

		[SpecialName]
		internal void _Lambda_0024__3()
		{
			using List<SAMap>.Enumerator mappedSAs = mps.MappedSAs.GetEnumerator();
			_Closure_0024__14_002D2 localValue = default;
			while (mappedSAs.MoveNext())
			{
				localValue = new _Closure_0024__14_002D2(localValue);
				localValue.outerClosure = this;
				localValue.sa = mappedSAs.Current;
				Utility.Parallel.SmartFor(localValue.sa.MapPoints.Count, localValue._Lambda_0024__4);
			}
		}

		[SpecialName]
		internal void _Lambda_0024__5()
		{
			outerClosure.velocityRenderer.ComputeVelocity2D(outerClosure.meshFaceVels, outerClosure.meshCellWS, mps, outDepths, outX, outY, output, outerClosure.meshFaceWSValues, outerClosure.meshFaceTrueVels, outerClosure.meshFPVels);
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__14_002D2
	{
		public SAMap sa;

		public _Closure_0024__14_002D1 outerClosure;

		public _Closure_0024__14_002D2(_Closure_0024__14_002D2 other)
		{
			if (other != null)
			{
				sa = other.sa;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__4(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				int mapPoint = sa.MapPoints[i];
				if (outerClosure.outDepths[mapPoint] > 0f)
				{
					outerClosure.output[mapPoint] = 0f;
				}
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__17_002D0
	{
		public Func<float[]> getReusableBuffer;

		public VelocityComputerHelper vch;

		public Action<float[]> returnReusableBuffer;

		public DepthVelocityMethod dvMethod;

		public VelocityRenderer velocityRenderer;

		public _Closure_0024__17_002D0(_Closure_0024__17_002D0 other)
		{
			if (other != null)
			{
				getReusableBuffer = other.getReusableBuffer;
				vch = other.vch;
				returnReusableBuffer = other.returnReusableBuffer;
				dvMethod = other.dvMethod;
			}
		}

		[SpecialName]
		internal float[] _Lambda_0024__0(RASGeometryMapPoints mps, float[] terrain)
		{
			_Closure_0024__17_002D1 localValue = default;
			_Closure_0024__17_002D1 capturedLocals = new _Closure_0024__17_002D1(localValue);
			capturedLocals.outerClosure = this;
			capturedLocals.mps = mps;
			capturedLocals.output = getReusableBuffer();
			capturedLocals.outX = getReusableBuffer();
			capturedLocals.outY = getReusableBuffer();
			Buffers.ArraySetAllVB(ref capturedLocals.output, -9999f, capturedLocals.mps.InputCount);
			checked
			{
				Buffer.BlockCopy(capturedLocals.output, 0, capturedLocals.outX, 0, capturedLocals.output.Count() * 4);
				Buffer.BlockCopy(capturedLocals.output, 0, capturedLocals.outY, 0, capturedLocals.output.Count() * 4);
				List<Task> tasks = new List<Task>();
				capturedLocals.outDepths = vch.DepthComputer(capturedLocals.mps, terrain);
				if (vch.FlowXS != null && vch.WsXS != null && capturedLocals.mps.HasMapped1DAreas())
				{
					tasks.Add(Task.Factory.StartNew([SpecialName] () =>
					{
						capturedLocals.outerClosure.velocityRenderer.ComputeVelocityInternal1DFromFlow(capturedLocals.outerClosure.vch.FlowXS, capturedLocals.outerClosure.vch.WsXS, capturedLocals.mps, capturedLocals.outDepths, capturedLocals.outX, capturedLocals.outY, capturedLocals.output);
					}));
				}
				if (capturedLocals.mps.HasMappedStorageAreas())
				{
					tasks.Add(Task.Factory.StartNew([SpecialName] () =>
					{
						using List<SAMap>.Enumerator mappedSAs = capturedLocals.mps.MappedSAs.GetEnumerator();
						_Closure_0024__17_002D2 localValue2 = default;
						while (mappedSAs.MoveNext())
						{
							localValue2 = new _Closure_0024__17_002D2(localValue2);
							localValue2.outerClosure = capturedLocals;
							localValue2.sa = mappedSAs.Current;
							Utility.Parallel.SmartFor(localValue2.sa.MapPoints.Count, localValue2._Lambda_0024__3);
						}
					}));
				}
				if (vch.MeshCellWS != null && vch.MeshFaceVels != null && capturedLocals.mps.HasMapped2DCells())
				{
					tasks.Add(Task.Factory.StartNew([SpecialName] () =>
					{
						capturedLocals.outerClosure.velocityRenderer.ComputeVelocity2D(capturedLocals.outerClosure.vch.MeshFaceVels, capturedLocals.outerClosure.vch.MeshCellWS, capturedLocals.mps, capturedLocals.outDepths, capturedLocals.outX, capturedLocals.outY, capturedLocals.output, capturedLocals.outerClosure.vch.MeshFaceWSValues, capturedLocals.outerClosure.vch.MeshFaceTrueVels, capturedLocals.outerClosure.vch.MeshFPVels);
					}));
				}
				Task.WaitAll(tasks.ToArray());
				returnReusableBuffer(capturedLocals.outX);
				returnReusableBuffer(capturedLocals.outY);
				if (dvMethod == DepthVelocityMethod.DepthTimesVelocity)
				{
					Utility.Parallel.SmartFor(capturedLocals.outDepths.Length, [SpecialName] (int start, int fin) =>
					{
						for (int i = start; i <= fin; i++)
						{
							float output = capturedLocals.output[i];
							float outDepth = capturedLocals.outDepths[i];
							if (output != -9999f && outDepth != -9999f)
							{
								capturedLocals.output[i] = outDepth * output;
							}
							else
							{
								capturedLocals.output[i] = -9999f;
							}
						}
					}, 256);
				}
				else if (dvMethod == DepthVelocityMethod.DepthTimesVelocitySquared)
				{
					Utility.Parallel.SmartFor(capturedLocals.outDepths.Length, [SpecialName] (int start, int fin) =>
					{
						for (int i = start; i <= fin; i++)
						{
							float output2 = capturedLocals.output[i];
							float outDepth2 = capturedLocals.outDepths[i];
							if (output2 != -9999f && outDepth2 != -9999f)
							{
								capturedLocals.output[i] = outDepth2 * output2 * output2;
							}
							else
							{
								capturedLocals.output[i] = -9999f;
							}
						}
					}, 256);
				}
				else if (dvMethod == DepthVelocityMethod.Froude)
				{
					_Closure_0024__17_002D3 localValue3 = default;
					_Closure_0024__17_002D3 capturedLocals2 = new _Closure_0024__17_002D3(localValue3);
					capturedLocals2.outerClosure = capturedLocals;
					capturedLocals2.gravity = SharedData.Gravity;
					Utility.Parallel.SmartFor(capturedLocals2.outerClosure.outDepths.Length, [SpecialName] (int start, int fin) =>
					{
						for (int i = start; i <= fin; i++)
						{
							float output3 = capturedLocals2.outerClosure.output[i];
							float outDepth3 = capturedLocals2.outerClosure.outDepths[i];
							if (output3 != -9999f && outDepth3 != -9999f)
							{
								capturedLocals2.outerClosure.output[i] = (float)((double)output3 / Math.Sqrt(capturedLocals2.gravity * outDepth3));
							}
							else
							{
								capturedLocals2.outerClosure.output[i] = -9999f;
							}
						}
					}, 256);
				}
				else if (dvMethod == DepthVelocityMethod.Froude)
				{
					_Closure_0024__17_002D4 localValue4 = default;
					_Closure_0024__17_002D4 capturedLocals3 = new _Closure_0024__17_002D4(localValue4);
					capturedLocals3.outerClosure = capturedLocals;
					capturedLocals3.gravity = SharedData.Gravity;
					Utility.Parallel.SmartFor(capturedLocals3.outerClosure.outDepths.Length, [SpecialName] (int start, int fin) =>
					{
						for (int i = start; i <= fin; i++)
						{
							float output4 = capturedLocals3.outerClosure.output[i];
							float outDepth4 = capturedLocals3.outerClosure.outDepths[i];
							if (output4 != -9999f && outDepth4 != -9999f)
							{
								capturedLocals3.outerClosure.output[i] = outDepth4 + output4 * output4 / (2f * capturedLocals3.gravity);
							}
							else
							{
								capturedLocals3.outerClosure.output[i] = -9999f;
							}
						}
					}, 256);
				}
				returnReusableBuffer(capturedLocals.outDepths);
				return capturedLocals.output;
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__17_002D1
	{
		public RASGeometryMapPoints mps;

		public float[] outDepths;

		public float[] outX;

		public float[] outY;

		public float[] output;

		public _Closure_0024__17_002D0 outerClosure;

		public _Closure_0024__17_002D1(_Closure_0024__17_002D1 other)
		{
			if (other != null)
			{
				mps = other.mps;
				outDepths = other.outDepths;
				outX = other.outX;
				outY = other.outY;
				output = other.output;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1()
		{
			outerClosure.velocityRenderer.ComputeVelocityInternal1DFromFlow(outerClosure.vch.FlowXS, outerClosure.vch.WsXS, mps, outDepths, outX, outY, output);
		}

		[SpecialName]
		internal void _Lambda_0024__2()
		{
			using List<SAMap>.Enumerator mappedSAs = mps.MappedSAs.GetEnumerator();
			_Closure_0024__17_002D2 localValue = default;
			while (mappedSAs.MoveNext())
			{
				localValue = new _Closure_0024__17_002D2(localValue);
				localValue.outerClosure = this;
				localValue.sa = mappedSAs.Current;
				Utility.Parallel.SmartFor(localValue.sa.MapPoints.Count, localValue._Lambda_0024__3);
			}
		}

		[SpecialName]
		internal void _Lambda_0024__4()
		{
			outerClosure.velocityRenderer.ComputeVelocity2D(outerClosure.vch.MeshFaceVels, outerClosure.vch.MeshCellWS, mps, outDepths, outX, outY, output, outerClosure.vch.MeshFaceWSValues, outerClosure.vch.MeshFaceTrueVels, outerClosure.vch.MeshFPVels);
		}

		[SpecialName]
		internal void _Lambda_0024__5(int start, int fin)
		{
			for (int i = start; i <= fin; i = checked(i + 1))
			{
				float output = this.output[i];
				float outDepth = outDepths[i];
				if (output != -9999f && outDepth != -9999f)
				{
					this.output[i] = outDepth * output;
				}
				else
				{
					this.output[i] = -9999f;
				}
			}
		}

		[SpecialName]
		internal void _Lambda_0024__6(int start, int fin)
		{
			for (int i = start; i <= fin; i = checked(i + 1))
			{
				float output = this.output[i];
				float outDepth = outDepths[i];
				if (output != -9999f && outDepth != -9999f)
				{
					this.output[i] = outDepth * output * output;
				}
				else
				{
					this.output[i] = -9999f;
				}
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__17_002D2
	{
		public SAMap sa;

		public _Closure_0024__17_002D1 outerClosure;

		public _Closure_0024__17_002D2(_Closure_0024__17_002D2 other)
		{
			if (other != null)
			{
				sa = other.sa;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__3(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				int mapPoint = sa.MapPoints[i];
				if (outerClosure.outDepths[mapPoint] > 0f)
				{
					outerClosure.output[mapPoint] = 0f;
				}
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__17_002D3
	{
		public float gravity;

		public _Closure_0024__17_002D1 outerClosure;

		public _Closure_0024__17_002D3(_Closure_0024__17_002D3 other)
		{
			if (other != null)
			{
				gravity = other.gravity;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__7(int start, int fin)
		{
			for (int i = start; i <= fin; i = checked(i + 1))
			{
				float output = outerClosure.output[i];
				float outDepth = outerClosure.outDepths[i];
				if (output != -9999f && outDepth != -9999f)
				{
					outerClosure.output[i] = (float)((double)output / Math.Sqrt(gravity * outDepth));
				}
				else
				{
					outerClosure.output[i] = -9999f;
				}
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__17_002D4
	{
		public float gravity;

		public _Closure_0024__17_002D1 outerClosure;

		public _Closure_0024__17_002D4(_Closure_0024__17_002D4 other)
		{
			if (other != null)
			{
				gravity = other.gravity;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__8(int start, int fin)
		{
			for (int i = start; i <= fin; i = checked(i + 1))
			{
				float output = outerClosure.output[i];
				float outDepth = outerClosure.outDepths[i];
				if (output != -9999f && outDepth != -9999f)
				{
					outerClosure.output[i] = outDepth + output * output / (2f * gravity);
				}
				else
				{
					outerClosure.output[i] = -9999f;
				}
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__19_002D0
	{
		public Dictionary<int, float[]> xsSubsectionVels;

		public _Closure_0024__19_002D1 outerClosure;

		public _Closure_0024__19_002D0(_Closure_0024__19_002D0 other)
		{
			if (other != null)
			{
				xsSubsectionVels = other.xsSubsectionVels;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0()
		{
			outerClosure.velocityRenderer.ComputeVelocityInternal1DFromVelocity(xsSubsectionVels, outerClosure.mps, outerClosure.lOD, outerClosure.lOVX, outerClosure.lOVY, outerClosure.lOVM);
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__19_002D1
	{
		public RASGeometryMapPoints mps;

		public float[] lOD;

		public float[] lOVX;

		public float[] lOVY;

		public float[] lOVM;

		public VelocityRenderer velocityRenderer;

		public _Closure_0024__19_002D1(_Closure_0024__19_002D1 other)
		{
			if (other != null)
			{
				mps = other.mps;
				lOD = other.lOD;
				lOVX = other.lOVX;
				lOVY = other.lOVY;
				lOVM = other.lOVM;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__2()
		{
			using List<SAMap>.Enumerator mappedSAs = mps.MappedSAs.GetEnumerator();
			_Closure_0024__19_002D3 localValue = default;
			while (mappedSAs.MoveNext())
			{
				localValue = new _Closure_0024__19_002D3(localValue);
				localValue.outerClosure = this;
				localValue.sa = mappedSAs.Current;
				Utility.Parallel.SmartFor(localValue.sa.MapPoints.Count, localValue._Lambda_0024__3);
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__19_002D2
	{
		public float[] flowXS;

		public float[] wsXS;

		public _Closure_0024__19_002D1 outerClosure;

		public _Closure_0024__19_002D2(_Closure_0024__19_002D2 other)
		{
			if (other != null)
			{
				flowXS = other.flowXS;
				wsXS = other.wsXS;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1()
		{
			outerClosure.velocityRenderer.ComputeVelocityInternal1DFromFlow(flowXS, wsXS, outerClosure.mps, outerClosure.lOD, outerClosure.lOVX, outerClosure.lOVY, outerClosure.lOVM);
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__19_002D3
	{
		public SAMap sa;

		public _Closure_0024__19_002D1 outerClosure;

		public _Closure_0024__19_002D3(_Closure_0024__19_002D3 other)
		{
			if (other != null)
			{
				sa = other.sa;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__3(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				int mapPoint = sa.MapPoints[i];
				if (outerClosure.lOD[mapPoint] > 0f)
				{
					outerClosure.lOVX[mapPoint] = 0f;
					outerClosure.lOVY[mapPoint] = 0f;
					outerClosure.lOVM[mapPoint] = 0f;
				}
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__19_002D4
	{
		public float[][] meshFaceVels;

		public float[][] meshCellWS;

		public _Closure_0024__19_002D1 outerClosure;

		public _Closure_0024__19_002D4(_Closure_0024__19_002D4 other)
		{
			if (other != null)
			{
				meshFaceVels = other.meshFaceVels;
				meshCellWS = other.meshCellWS;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__4()
		{
			outerClosure.velocityRenderer.ComputeVelocity2D(meshFaceVels, meshCellWS, outerClosure.mps, outerClosure.lOD, outerClosure.lOVX, outerClosure.lOVY, outerClosure.lOVM);
		}
	}

	[CompilerGenerated]
private new sealed class _Closure_0024__21_002D0
	{
		public RASGeometryMapPoints mps;

		public float[] lOD;

		public float[] lOVX;

		public float[] lOVY;

		public float[] lOVM;

		public VelocityRenderer velocityRenderer;

		public _Closure_0024__21_002D0(_Closure_0024__21_002D0 other)
		{
			if (other != null)
			{
				mps = other.mps;
				lOD = other.lOD;
				lOVX = other.lOVX;
				lOVY = other.lOVY;
				lOVM = other.lOVM;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0()
		{
			using List<SAMap>.Enumerator mappedSAs = mps.MappedSAs.GetEnumerator();
			_Closure_0024__21_002D1 localValue = default;
			while (mappedSAs.MoveNext())
			{
				localValue = new _Closure_0024__21_002D1(localValue);
				localValue.outerClosure = this;
				localValue.sa = mappedSAs.Current;
				Utility.Parallel.SmartFor(localValue.sa.MapPoints.Count, localValue._Lambda_0024__1);
			}
		}
	}

	[CompilerGenerated]
private new sealed class _Closure_0024__21_002D1
	{
		public SAMap sa;

		public _Closure_0024__21_002D0 outerClosure;

		public _Closure_0024__21_002D1(_Closure_0024__21_002D1 other)
		{
			if (other != null)
			{
				sa = other.sa;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				int mapPoint = sa.MapPoints[i];
				if (outerClosure.lOD[mapPoint] > 0f)
				{
					outerClosure.lOVX[mapPoint] = 0f;
					outerClosure.lOVY[mapPoint] = 0f;
					outerClosure.lOVM[mapPoint] = 0f;
				}
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__21_002D2
	{
		public float[][] meshFaceVels;

		public float[][] meshCellWS;

		public _Closure_0024__21_002D0 outerClosure;

		public _Closure_0024__21_002D2(_Closure_0024__21_002D2 other)
		{
			if (other != null)
			{
				meshFaceVels = other.meshFaceVels;
				meshCellWS = other.meshCellWS;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__2()
		{
			outerClosure.velocityRenderer.ComputeVelocity2D(meshFaceVels, meshCellWS, outerClosure.mps, outerClosure.lOD, outerClosure.lOVX, outerClosure.lOVY, outerClosure.lOVM);
		}
	}

	[CompilerGenerated]
private new sealed class _Closure_0024__26_002D0
	{
		public Func<int, float[]> xsVelSubsections;

		public float[] clipDepths;

		public float[] velX;

		public float[] velY;

		public float[] velMag;

		public VelocityRenderer velocityRenderer;

		public _Closure_0024__26_002D0(_Closure_0024__26_002D0 other)
		{
			if (other != null)
			{
				xsVelSubsections = other.xsVelSubsections;
				clipDepths = other.clipDepths;
				velX = other.velX;
				velY = other.velY;
				velMag = other.velMag;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(MappedXSIS interpSurface)
		{
			_Closure_0024__26_002D1 localValue = default;
			_Closure_0024__26_002D1 capturedLocals = new _Closure_0024__26_002D1(localValue);
			capturedLocals.outerClosure = this;
			capturedLocals.interpSurface = interpSurface;
			capturedLocals.usXS = velocityRenderer._XSIS.GetUpstreamXSID(capturedLocals.interpSurface.ID);
			capturedLocals.dsXS = velocityRenderer._XSIS.GetDownstreamXSID(capturedLocals.interpSurface.ID);
			capturedLocals.usXSVels = xsVelSubsections(capturedLocals.usXS);
			capturedLocals.dsXSVels = xsVelSubsections(capturedLocals.dsXS);
			capturedLocals.usStationComputer = velocityRenderer._XS.GetStationComputer(capturedLocals.usXS);
			capturedLocals.dsStationComputer = velocityRenderer._XS.GetStationComputer(capturedLocals.dsXS);
			Utility.Parallel.SmartFor(capturedLocals.interpSurface.MapPoints.Count, [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i = checked(i + 1))
				{
					XSISMapPoint xSISMapPoint = capturedLocals.interpSurface.MapPoints[i];
					if (!(capturedLocals.outerClosure.clipDepths[xSISMapPoint.Index] <= 0f))
					{
						float averagedMValue = capturedLocals.outerClosure.velocityRenderer._flowDistribution.AveragedMValue(capturedLocals.usXS, capturedLocals.usStationComputer(xSISMapPoint.M), capturedLocals.usXSVels);
						float averagedMValue2 = capturedLocals.outerClosure.velocityRenderer._flowDistribution.AveragedMValue(capturedLocals.dsXS, capturedLocals.dsStationComputer(xSISMapPoint.M), capturedLocals.dsXSVels);
						float xSISMapPointTerm = averagedMValue * xSISMapPoint.Z + averagedMValue2 * (1f - xSISMapPoint.Z);
						float streamNXXSISMapPointTerm = xSISMapPointTerm * xSISMapPoint.StreamNX;
						float streamNYXSISMapPointTerm = xSISMapPointTerm * xSISMapPoint.StreamNY;
						capturedLocals.outerClosure.velX[xSISMapPoint.Index] = streamNXXSISMapPointTerm;
						capturedLocals.outerClosure.velY[xSISMapPoint.Index] = streamNYXSISMapPointTerm;
						capturedLocals.outerClosure.velMag[xSISMapPoint.Index] = xSISMapPointTerm;
					}
				}
			});
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__26_002D1
	{
		public MappedXSIS interpSurface;

		public int usXS;

		public Func<float, float> usStationComputer;

		public float[] usXSVels;

		public int dsXS;

		public Func<float, float> dsStationComputer;

		public float[] dsXSVels;

		public _Closure_0024__26_002D0 outerClosure;

		public _Closure_0024__26_002D1(_Closure_0024__26_002D1 other)
		{
			if (other != null)
			{
				interpSurface = other.interpSurface;
				usXS = other.usXS;
				usStationComputer = other.usStationComputer;
				usXSVels = other.usXSVels;
				dsXS = other.dsXS;
				dsStationComputer = other.dsStationComputer;
				dsXSVels = other.dsXSVels;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				XSISMapPoint xSISMapPoint = interpSurface.MapPoints[i];
				if (!(outerClosure.clipDepths[xSISMapPoint.Index] <= 0f))
				{
					float averagedMValue = outerClosure.velocityRenderer._flowDistribution.AveragedMValue(usXS, usStationComputer(xSISMapPoint.M), usXSVels);
					float averagedMValue2 = outerClosure.velocityRenderer._flowDistribution.AveragedMValue(dsXS, dsStationComputer(xSISMapPoint.M), dsXSVels);
					float xSISMapPointTerm = averagedMValue * xSISMapPoint.Z + averagedMValue2 * (1f - xSISMapPoint.Z);
					float streamNXXSISMapPointTerm = xSISMapPointTerm * xSISMapPoint.StreamNX;
					float streamNYXSISMapPointTerm = xSISMapPointTerm * xSISMapPoint.StreamNY;
					outerClosure.velX[xSISMapPoint.Index] = streamNXXSISMapPointTerm;
					outerClosure.velY[xSISMapPoint.Index] = streamNYXSISMapPointTerm;
					outerClosure.velMag[xSISMapPoint.Index] = xSISMapPointTerm;
				}
			}
		}
	}

	private readonly Names _velNames;

	private readonly Names _wsNames;

	private readonly Names _flowNames;

	private readonly H5RowCache<float> _rowCache;

	private readonly DepthRenderer _depthRenderer;

	public VelocityRenderer(RASResults res, CacheCollection cache = null)
		: base(res, cache, needsVelocityFlowData: true, needsPostProcessFile: false)
	{
		GetVelocityDatasetNames(ref _velNames, ref _flowNames, ref _wsNames);
		_velNames.Validate(_hdfFilename);
		_flowNames.Validate(_hdfFilename);
		_wsNames.Validate(_hdfFilename);
		_rowCache = _cache.GetRowCache<float>(_hdfFilename);
		_depthRenderer = new DepthRenderer(res, cache);
	}

	public void ForceLoadProfileDatasets()
	{
		if (!string.IsNullOrEmpty(_velNames.Profile1D))
		{
			_rowCache.PinDataset(_velNames.Profile1D);
		}
		if (!string.IsNullOrEmpty(_velNames.ProfileSA))
		{
			_rowCache.PinDataset(_velNames.ProfileSA);
		}
		foreach (string profile2D in _velNames.Profile2D)
		{
			if (!string.IsNullOrEmpty(profile2D))
			{
				_rowCache.PinDataset(profile2D);
			}
		}
		if (!string.IsNullOrEmpty(_wsNames.Profile1D))
		{
			_rowCache.PinDataset(_wsNames.Profile1D);
		}
		if (!string.IsNullOrEmpty(_wsNames.ProfileSA))
		{
			_rowCache.PinDataset(_wsNames.ProfileSA);
		}
		foreach (string profile2D2 in _wsNames.Profile2D)
		{
			if (!string.IsNullOrEmpty(profile2D2))
			{
				_rowCache.PinDataset(profile2D2);
			}
		}
		if (!string.IsNullOrEmpty(_flowNames.Profile1D))
		{
			_rowCache.PinDataset(_flowNames.Profile1D);
		}
	}

	public static void Compute(RASResults res, int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] outVelocityX, ref float[] outVelocityY, ref float[] outVelocityMag, [Optional][DefaultParameterValue(null)] CacheCollection cache, [Optional][DefaultParameterValue(null)] ref float[] outDepths)
	{
		new VelocityRenderer(res, cache).Compute(profile, mps, terrain, ref outVelocityX, ref outVelocityY, ref outVelocityMag, ref outDepths);
	}

	public static Func<RASGeometryMapPoints, float[], float[]> GetComputer(RASResults res, int profile, CacheCollection cache, Func<float[]> GetReusableBuffer, Action<float[]> ReturnReusableBuffer)
	{
		return new VelocityRenderer(res, cache).GetComputer(profile, GetReusableBuffer, ReturnReusableBuffer);
	}

	public static Func<RASGeometryMapPoints, float[], float[][]> GetComputerAllValues(RASResults res, int profile, CacheCollection cache, Func<float[]> GetReusableBuffer, Action<float[]> ReturnReusableBuffer, bool TrueFaceVelocitiesPreprocessed = false)
	{
		return new VelocityRenderer(res, cache).GetComputerAllValues(profile, GetReusableBuffer, ReturnReusableBuffer, TrueFaceVelocitiesPreprocessed);
	}

	private void DoFaceComputations(List<string> vel2D, List<string> ws2D, float[][] meshFaceVels, float[][] meshCellWS, ref FaceValues[][] meshFaceWSValues, ref Tuple<Vector2D, Vector2D>[][] meshFaceTrueVels, ref FacePointVels[][] meshFPVels, bool consoleWrite = false)
	{
		Stopwatch stopwatch = new Stopwatch();
		if (vel2D.IsNullOrEmpty() || ws2D.IsNullOrEmpty())
		{
			return;
		}
		int meshCount = _d2FlowArea.MeshCount;
		checked
		{
			meshFaceWSValues = new FaceValues[meshCount - 1 + 1][];
			meshFPVels = new FacePointVels[meshCount - 1 + 1][];
			int count = meshCount - 1;
			for (int i = 0; i <= count; i++)
			{
				MeshFV2D meshFV2D = _d2FlowArea.Meshes().ElementAtOrDefault(i);
				stopwatch.Restart();
				meshFaceWSValues[i] = SlopingFactors.ComputeSlopingWSFaceFactors(meshFV2D, meshCellWS[i]);
				if (consoleWrite)
				{
					Console.WriteLine("It took " + stopwatch.ElapsedMilliseconds + " ms to STEP 1: Compute face WS values (Hydraulic Connectivity)");
				}
				stopwatch.Restart();
				if (meshFaceTrueVels == null || meshFaceTrueVels[i] == null)
				{
					meshFaceTrueVels = new Tuple<Vector2D, Vector2D>[meshCount - 1 + 1][];
					if (meshFaceTrueVels[i] == null)
					{
						meshFaceTrueVels[i] = meshFV2D.ComputeFaceTrueVelocities_CStencilAveraged(meshFaceWSValues[i], meshFaceVels[i]);
					}
				}
				if (consoleWrite)
				{
					Console.WriteLine("It took " + stopwatch.ElapsedMilliseconds + " ms to STEP 2: Compute face true velocities");
				}
				stopwatch.Restart();
				meshFPVels[i] = meshFV2D.ComputeSlopingFacepointVelocities_AvgTrueVels(meshFaceTrueVels[i], meshFaceWSValues[i]);
				if (consoleWrite)
				{
					Console.WriteLine("It took " + stopwatch.ElapsedMilliseconds + " ms to STEP 3: Compute facepoint true velocities");
				}
				stopwatch.Restart();
			}
		}
	}

	public Func<RASGeometryMapPoints, float[], float[][]> GetComputerAllValues(int profile, Func<float[]> GetReusableBuffer, Action<float[]> ReturnReusableBuffer, bool faceTrueVelocityPreprocessed = false)
	{
		_Closure_0024__11_002D0 capturedLocals = default;
		_Closure_0024__11_002D0 capturedLocals2 = new _Closure_0024__11_002D0(capturedLocals);
		capturedLocals2.velocityRenderer = this;
		capturedLocals2.profile = profile;
		capturedLocals2.getReusableBuffer = GetReusableBuffer;
		Stopwatch stopwatch = Stopwatch.StartNew();
		capturedLocals2.drComputer = DepthRenderer.GetComputer(_result, capturedLocals2.profile, _cache, capturedLocals2.getReusableBuffer, ReturnReusableBuffer);
		stopwatch.Restart();
		capturedLocals2.ws1D = "";
		capturedLocals2.vel1D = null;
		capturedLocals2.vel2D = null;
		capturedLocals2.ws2D = null;
		capturedLocals2.flow1D = "";
		Names velNames = _velNames;
		int pfIndex = capturedLocals2.profile;
		ref string dataset = ref capturedLocals2.vel1D;
		int row = 0;
		velNames.Get1D(pfIndex, ref dataset, ref row);
		int row2 = default;
		_velNames.Get2D(capturedLocals2.profile, ref capturedLocals2.vel2D, ref row2);
		int row3 = default;
		_flowNames.Get1D(capturedLocals2.profile, ref capturedLocals2.flow1D, ref row3);
		int row4 = default;
		_wsNames.Get1D(capturedLocals2.profile, ref capturedLocals2.ws1D, ref row4);
		int row5 = default;
		_wsNames.Get2D(capturedLocals2.profile, ref capturedLocals2.ws2D, ref row5);
		capturedLocals2.xsSubsectionVels = null;
		if ((capturedLocals2.profile == int.MaxValue || capturedLocals2.profile == int.MinValue) && capturedLocals2.vel1D != null)
		{
			capturedLocals2.xsSubsectionVels = ReadMappedXSProfile(capturedLocals2.vel1D, _rowCache);
		}
		capturedLocals2.flowXS = null;
		capturedLocals2.wsXS = null;
		if (capturedLocals2.profile >= 0 && capturedLocals2.profile < int.MaxValue && capturedLocals2.flow1D != null && capturedLocals2.ws1D != null)
		{
			capturedLocals2.flowXS = _rowCache.EnsureGetRow(capturedLocals2.flow1D, row3);
			capturedLocals2.wsXS = _rowCache.EnsureGetRow(capturedLocals2.ws1D, row4);
		}
		capturedLocals2.meshFaceVels = null;
		capturedLocals2.meshCellWS = null;
		bool condition = false;
		capturedLocals2.meshFaceWSValues = null;
		capturedLocals2.meshFaceTrueVels = null;
		capturedLocals2.meshFPVels = null;
		checked
		{
			if (!capturedLocals2.vel2D.IsNullOrEmpty() && !capturedLocals2.ws2D.IsNullOrEmpty())
			{
				capturedLocals2.meshCellWS = ReadMappedMeshProfile(capturedLocals2.ws2D, row5, _rowCache);
				capturedLocals2.meshFaceVels = ReadMappedMeshProfile(capturedLocals2.vel2D, row2, _rowCache);
				int meshCount = _d2FlowArea.MeshCount;
				capturedLocals2.meshFaceWSValues = new FaceValues[meshCount - 1 + 1][];
				capturedLocals2.meshFaceTrueVels = new Tuple<Vector2D, Vector2D>[meshCount - 1 + 1][];
				capturedLocals2.meshFPVels = new FacePointVels[meshCount - 1 + 1][];
				stopwatch.Restart();
				row = meshCount - 1;
				for (int i = 0; i <= row; i++)
				{
					MeshFV2D meshFV2D = _d2FlowArea.Meshes().ElementAtOrDefault(i);
					capturedLocals2.meshFaceWSValues[i] = SlopingFactors.ComputeSlopingWSFaceFactors(meshFV2D, capturedLocals2.meshCellWS[i]);
					if (condition)
					{
						Console.WriteLine("It took " + stopwatch.ElapsedMilliseconds + " ms to STEP 1: Compute face WS values (Hydraulic Connectivity)");
					}
					stopwatch.Restart();
					if (!faceTrueVelocityPreprocessed)
					{
						capturedLocals2.meshFaceTrueVels[i] = meshFV2D.ComputeFaceTrueVelocities_CStencilAveraged(capturedLocals2.meshFaceWSValues[i], capturedLocals2.meshFaceVels[i]);
					}
					else
					{
						capturedLocals2.meshFaceTrueVels[i] = ReadFaceTrueVelocitiesForProfile("Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/2D Flow Areas/" + _d2FlowAreaNames[i] + "/Face True Velocity", capturedLocals2.profile, capturedLocals2.meshFaceVels[i].Count());
					}
					if (condition)
					{
						Console.WriteLine("It took " + stopwatch.ElapsedMilliseconds + " ms to STEP 2: Compute face true velocities");
					}
					stopwatch.Restart();
					capturedLocals2.meshFPVels[i] = meshFV2D.ComputeSlopingFacepointVelocities_AvgTrueVels(capturedLocals2.meshFaceTrueVels[i], capturedLocals2.meshFaceWSValues[i]);
					if (condition)
					{
						Console.WriteLine("It took " + stopwatch.ElapsedMilliseconds + " ms to STEP 3: Compute facepoint true velocities");
					}
					stopwatch.Restart();
				}
			}
			return [SpecialName] (RASGeometryMapPoints mps, float[] terrain) =>
			{
				_Closure_0024__11_002D1 capturedLocals3 = default;
				_Closure_0024__11_002D1 capturedLocals4 = new _Closure_0024__11_002D1(capturedLocals3);
				capturedLocals4.outerClosure = capturedLocals2;
				capturedLocals4.mps = mps;
				capturedLocals4.output = capturedLocals2.getReusableBuffer();
				capturedLocals4.outX = capturedLocals2.getReusableBuffer();
				capturedLocals4.outY = capturedLocals2.getReusableBuffer();
				Buffers.ArraySetAllVB(ref capturedLocals4.output, -9999f, capturedLocals4.mps.InputCount);
				Buffer.BlockCopy(capturedLocals4.output, 0, capturedLocals4.outX, 0, capturedLocals4.output.Count() * 4);
				Buffer.BlockCopy(capturedLocals4.output, 0, capturedLocals4.outY, 0, capturedLocals4.output.Count() * 4);
				List<Task> tasks = new List<Task>();
				capturedLocals4.outDepths = capturedLocals2.drComputer(capturedLocals4.mps, terrain);
				if (capturedLocals2.profile == int.MaxValue || capturedLocals2.profile == int.MinValue)
				{
					if (capturedLocals2.vel1D != null && capturedLocals4.mps.HasMapped1DAreas())
					{
						tasks.Add(Task.Factory.StartNew([SpecialName] () =>
						{
							capturedLocals4.outerClosure.velocityRenderer.ComputeVelocityInternal1DFromVelocity(capturedLocals4.outerClosure.xsSubsectionVels, capturedLocals4.mps, capturedLocals4.outDepths, capturedLocals4.outX, capturedLocals4.outY, capturedLocals4.output);
						}));
					}
				}
				else if (capturedLocals2.flow1D != null && capturedLocals2.ws1D != null && capturedLocals4.mps.HasMapped1DAreas())
				{
					tasks.Add(Task.Factory.StartNew([SpecialName] () =>
					{
						capturedLocals4.outerClosure.velocityRenderer.ComputeVelocityInternal1DFromFlow(capturedLocals4.outerClosure.flowXS, capturedLocals4.outerClosure.wsXS, capturedLocals4.mps, capturedLocals4.outDepths, capturedLocals4.outX, capturedLocals4.outY, capturedLocals4.output);
					}));
				}
				if (capturedLocals4.mps.HasMappedStorageAreas())
				{
					tasks.Add(Task.Factory.StartNew([SpecialName] () =>
					{
						using List<SAMap>.Enumerator mappedSAs = capturedLocals4.mps.MappedSAs.GetEnumerator();
						_Closure_0024__11_002D2 capturedLocals5 = default;
						while (mappedSAs.MoveNext())
						{
							capturedLocals5 = new _Closure_0024__11_002D2(capturedLocals5);
							capturedLocals5.outerClosure = capturedLocals4;
							capturedLocals5.sa = mappedSAs.Current;
							Utility.Parallel.SmartFor(capturedLocals5.sa.MapPoints.Count, capturedLocals5._Lambda_0024__4);
						}
					}));
				}
				if (!capturedLocals2.vel2D.IsNullOrEmpty() && !capturedLocals2.ws2D.IsNullOrEmpty() && capturedLocals4.mps.HasMapped2DCells())
				{
					tasks.Add(Task.Factory.StartNew([SpecialName] () =>
					{
						capturedLocals4.outerClosure.velocityRenderer.ComputeVelocity2D(capturedLocals4.outerClosure.meshFaceVels, capturedLocals4.outerClosure.meshCellWS, capturedLocals4.mps, capturedLocals4.outDepths, capturedLocals4.outX, capturedLocals4.outY, capturedLocals4.output, capturedLocals4.outerClosure.meshFaceWSValues, capturedLocals4.outerClosure.meshFaceTrueVels, capturedLocals4.outerClosure.meshFPVels);
					}));
				}
				Task.WaitAll(tasks.ToArray());
				return new float[5][] { capturedLocals4.outX, capturedLocals4.outY, capturedLocals4.output, capturedLocals4.outDepths, null };
			};
		}
	}

	public long GetNumBytesForComputer()
	{
		RASGeometry geometry = _result.Geometry;
		int profileCount = _result.ProfileCount;
		int featureCount = geometry.XS.FeatureCount();
		int meshCount = geometry.D2FlowArea.MeshCount;
		int cellCountSum = 0;
		int faceCountSum = 0;
		int facePointCountSum = 0;
		checked
		{
			int count = geometry.D2FlowArea.MeshCount - 1;
			for (int i = 0; i <= count; i++)
			{
				cellCountSum += geometry.D2FlowArea.Mesh(i).CellCount;
				faceCountSum += geometry.D2FlowArea.Mesh(i).FaceCount;
				facePointCountSum += geometry.D2FlowArea.Mesh(i).FacePointCount;
			}
			int numBytesForComputer = 0;
			int count2 = featureCount - 1;
			for (int j = 0; j <= count2; j++)
			{
				numBytesForComputer += profileCount * Marshal.SizeOf(typeof(float));
				numBytesForComputer += Marshal.SizeOf(typeof(int));
			}
			numBytesForComputer += featureCount * Marshal.SizeOf(typeof(float));
			numBytesForComputer += featureCount * Marshal.SizeOf(typeof(float));
			int count3 = meshCount - 1;
			for (int k = 0; k <= count3; k++)
			{
				numBytesForComputer += faceCountSum * Marshal.SizeOf(typeof(float));
				numBytesForComputer += cellCountSum * Marshal.SizeOf(typeof(float));
				numBytesForComputer += faceCountSum * (Marshal.SizeOf(typeof(float)) * 2 + Marshal.SizeOf(typeof(byte)));
				numBytesForComputer += faceCountSum * Marshal.SizeOf(typeof(float)) * 4;
				numBytesForComputer += facePointCountSum * Marshal.SizeOf(typeof(float)) * 8;
			}
			return numBytesForComputer;
		}
	}

	private Tuple<Vector2D, Vector2D>[] ReadFaceTrueVelocitiesForProfile(string faceTrueVelocityDatasetPath, int profile, int numFaces)
	{
		_Closure_0024__13_002D0 capturedLocals = default;
		_Closure_0024__13_002D0 capturedLocals2 = new _Closure_0024__13_002D0(capturedLocals);
		checked
		{
			capturedLocals2.toReturn = new Tuple<Vector2D, Vector2D>[numFaces + 1];
			Stopwatch.StartNew();
			capturedLocals2.item1x = _rowCache.EnsureGetRow(faceTrueVelocityDatasetPath, profile * 4);
			capturedLocals2.item1y = _rowCache.EnsureGetRow(faceTrueVelocityDatasetPath, profile * 4 + 1);
			capturedLocals2.item2x = _rowCache.EnsureGetRow(faceTrueVelocityDatasetPath, profile * 4 + 2);
			capturedLocals2.item2y = _rowCache.EnsureGetRow(faceTrueVelocityDatasetPath, profile * 4 + 3);
			Utility.Parallel.SmartFor(numFaces, [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					capturedLocals2.toReturn[i] = new Tuple<Vector2D, Vector2D>(new Vector2D(capturedLocals2.item1x[i], capturedLocals2.item1y[i]), new Vector2D(capturedLocals2.item2x[i], capturedLocals2.item2y[i]));
				}
			});
			return capturedLocals2.toReturn;
		}
	}

	public Func<RASGeometryMapPoints, float[], float[]> GetComputer(int profile, Func<float[]> GetReusableBuffer, Action<float[]> ReturnReusableBuffer)
	{
		_Closure_0024__14_002D0 capturedLocals = default;
		_Closure_0024__14_002D0 capturedLocals2 = new _Closure_0024__14_002D0(capturedLocals);
		capturedLocals2.velocityRenderer = this;
		capturedLocals2.profile = profile;
		capturedLocals2.getReusableBuffer = GetReusableBuffer;
		capturedLocals2.returnReusableBuffer = ReturnReusableBuffer;
		capturedLocals2.drComputer = DepthRenderer.GetComputer(_result, capturedLocals2.profile, _cache, capturedLocals2.getReusableBuffer, capturedLocals2.returnReusableBuffer);
		capturedLocals2.ws1D = "";
		capturedLocals2.vel1D = null;
		capturedLocals2.vel2D = null;
		capturedLocals2.ws2D = null;
		capturedLocals2.flow1D = "";
		Names velNames = _velNames;
		int pfIndex = capturedLocals2.profile;
		ref string dataset = ref capturedLocals2.vel1D;
		int row = 0;
		velNames.Get1D(pfIndex, ref dataset, ref row);
		int row2 = default;
		_velNames.Get2D(capturedLocals2.profile, ref capturedLocals2.vel2D, ref row2);
		int row3 = default;
		_flowNames.Get1D(capturedLocals2.profile, ref capturedLocals2.flow1D, ref row3);
		int row4 = default;
		_wsNames.Get1D(capturedLocals2.profile, ref capturedLocals2.ws1D, ref row4);
		int row5 = default;
		_wsNames.Get2D(capturedLocals2.profile, ref capturedLocals2.ws2D, ref row5);
		capturedLocals2.xsSubsectionVels = null;
		if ((capturedLocals2.profile == int.MaxValue || capturedLocals2.profile == int.MinValue) && capturedLocals2.vel1D != null)
		{
			capturedLocals2.xsSubsectionVels = ReadMappedXSProfile(capturedLocals2.vel1D, _rowCache);
		}
		capturedLocals2.flowXS = null;
		capturedLocals2.wsXS = null;
		if (capturedLocals2.profile >= 0 && capturedLocals2.profile < int.MaxValue && capturedLocals2.flow1D != null && capturedLocals2.ws1D != null)
		{
			capturedLocals2.flowXS = _rowCache.EnsureGetRow(capturedLocals2.flow1D, row3);
			capturedLocals2.wsXS = _rowCache.EnsureGetRow(capturedLocals2.ws1D, row4);
		}
		capturedLocals2.meshFaceVels = null;
		capturedLocals2.meshCellWS = null;
		capturedLocals2.meshFaceWSValues = null;
		capturedLocals2.meshFaceTrueVels = null;
		capturedLocals2.meshFPVels = null;
		checked
		{
			if (!capturedLocals2.vel2D.IsNullOrEmpty() && !capturedLocals2.ws2D.IsNullOrEmpty())
			{
				capturedLocals2.meshFaceVels = ReadMappedMeshProfile(capturedLocals2.vel2D, row2, _rowCache);
				capturedLocals2.meshCellWS = ReadMappedMeshProfile(capturedLocals2.ws2D, row5, _rowCache);
				int meshCount = _d2FlowArea.MeshCount;
				capturedLocals2.meshFaceWSValues = new FaceValues[meshCount - 1 + 1][];
				capturedLocals2.meshFaceTrueVels = new Tuple<Vector2D, Vector2D>[meshCount - 1 + 1][];
				capturedLocals2.meshFPVels = new FacePointVels[meshCount - 1 + 1][];
				row = meshCount - 1;
				for (int i = 0; i <= row; i++)
				{
					MeshFV2D meshFV2D = _d2FlowArea.Meshes().ElementAtOrDefault(i);
					capturedLocals2.meshFaceWSValues[i] = SlopingFactors.ComputeSlopingWSFaceFactors(meshFV2D, capturedLocals2.meshCellWS[i]);
					capturedLocals2.meshFaceTrueVels[i] = meshFV2D.ComputeFaceTrueVelocities_CStencilAveraged(capturedLocals2.meshFaceWSValues[i], capturedLocals2.meshFaceVels[i]);
					capturedLocals2.meshFPVels[i] = meshFV2D.ComputeSlopingFacepointVelocities_AvgTrueVels(capturedLocals2.meshFaceTrueVels[i], capturedLocals2.meshFaceWSValues[i]);
				}
			}
			return [SpecialName] (RASGeometryMapPoints mps, float[] terrain) =>
			{
				_Closure_0024__14_002D1 capturedLocals3 = default;
				_Closure_0024__14_002D1 capturedLocals4 = new _Closure_0024__14_002D1(capturedLocals3);
				capturedLocals4.outerClosure = capturedLocals2;
				capturedLocals4.mps = mps;
				capturedLocals4.output = capturedLocals2.getReusableBuffer();
				capturedLocals4.outX = capturedLocals2.getReusableBuffer();
				capturedLocals4.outY = capturedLocals2.getReusableBuffer();
				Buffers.ArraySetAllVB(ref capturedLocals4.output, -9999f, capturedLocals4.mps.InputCount);
				Buffer.BlockCopy(capturedLocals4.output, 0, capturedLocals4.outX, 0, capturedLocals4.output.Count() * 4);
				Buffer.BlockCopy(capturedLocals4.output, 0, capturedLocals4.outY, 0, capturedLocals4.output.Count() * 4);
				List<Task> tasks = new List<Task>();
				capturedLocals4.outDepths = capturedLocals2.drComputer(capturedLocals4.mps, terrain);
				if (capturedLocals2.profile == int.MaxValue || capturedLocals2.profile == int.MinValue)
				{
					if (capturedLocals2.vel1D != null && capturedLocals4.mps.HasMapped1DAreas())
					{
						tasks.Add(Task.Factory.StartNew([SpecialName] () =>
						{
							capturedLocals4.outerClosure.velocityRenderer.ComputeVelocityInternal1DFromVelocity(capturedLocals4.outerClosure.xsSubsectionVels, capturedLocals4.mps, capturedLocals4.outDepths, capturedLocals4.outX, capturedLocals4.outY, capturedLocals4.output);
						}));
					}
				}
				else if (capturedLocals2.flow1D != null && capturedLocals2.ws1D != null && capturedLocals4.mps.HasMapped1DAreas())
				{
					tasks.Add(Task.Factory.StartNew([SpecialName] () =>
					{
						capturedLocals4.outerClosure.velocityRenderer.ComputeVelocityInternal1DFromFlow(capturedLocals4.outerClosure.flowXS, capturedLocals4.outerClosure.wsXS, capturedLocals4.mps, capturedLocals4.outDepths, capturedLocals4.outX, capturedLocals4.outY, capturedLocals4.output);
					}));
				}
				if (capturedLocals4.mps.HasMappedStorageAreas())
				{
					tasks.Add(Task.Factory.StartNew([SpecialName] () =>
					{
						using List<SAMap>.Enumerator mappedSAs = capturedLocals4.mps.MappedSAs.GetEnumerator();
						_Closure_0024__14_002D2 capturedLocals5 = default;
						while (mappedSAs.MoveNext())
						{
							capturedLocals5 = new _Closure_0024__14_002D2(capturedLocals5);
							capturedLocals5.outerClosure = capturedLocals4;
							capturedLocals5.sa = mappedSAs.Current;
							Utility.Parallel.SmartFor(capturedLocals5.sa.MapPoints.Count, capturedLocals5._Lambda_0024__4);
						}
					}));
				}
				if (!capturedLocals2.vel2D.IsNullOrEmpty() && !capturedLocals2.ws2D.IsNullOrEmpty() && capturedLocals4.mps.HasMapped2DCells())
				{
					tasks.Add(Task.Factory.StartNew([SpecialName] () =>
					{
						capturedLocals4.outerClosure.velocityRenderer.ComputeVelocity2D(capturedLocals4.outerClosure.meshFaceVels, capturedLocals4.outerClosure.meshCellWS, capturedLocals4.mps, capturedLocals4.outDepths, capturedLocals4.outX, capturedLocals4.outY, capturedLocals4.output, capturedLocals4.outerClosure.meshFaceWSValues, capturedLocals4.outerClosure.meshFaceTrueVels, capturedLocals4.outerClosure.meshFPVels);
					}));
				}
				Task.WaitAll(tasks.ToArray());
				capturedLocals2.returnReusableBuffer(capturedLocals4.outX);
				capturedLocals2.returnReusableBuffer(capturedLocals4.outY);
				capturedLocals2.returnReusableBuffer(capturedLocals4.outDepths);
				return capturedLocals4.output;
			};
		}
	}

	public static Func<RASGeometryMapPoints, float[], float[]> GetDVProfileComputer(RASResults res, int profile, DepthVelocityMethod dvMethod, CacheCollection cache, Func<float[]> GetReusableBuffer, Action<float[]> ReturnReusableBuffer)
	{
		return new VelocityRenderer(res, cache).GetDVProfileComputer(profile, dvMethod, GetReusableBuffer, ReturnReusableBuffer);
	}

	public Func<RASGeometryMapPoints, float[], float[]> GetDVProfileComputer(int profile, DepthVelocityMethod dvMethod, Func<float[]> GetReusableBuffer, Action<float[]> ReturnReusableBuffer)
	{
		_Closure_0024__17_002D0 capturedLocals = default;
		_Closure_0024__17_002D0 capturedLocals2 = new _Closure_0024__17_002D0(capturedLocals);
		capturedLocals2.velocityRenderer = this;
		capturedLocals2.dvMethod = dvMethod;
		capturedLocals2.getReusableBuffer = GetReusableBuffer;
		capturedLocals2.returnReusableBuffer = ReturnReusableBuffer;
		if (profile == int.MaxValue || profile < 0)
		{
			throw new NotImplementedException();
		}
		capturedLocals2.vch = PreprocessComputer(profile, capturedLocals2.getReusableBuffer, capturedLocals2.returnReusableBuffer);
		return checked([SpecialName] (RASGeometryMapPoints mps, float[] terrain) =>
		{
			_Closure_0024__17_002D1 capturedLocals3 = default;
			_Closure_0024__17_002D1 capturedLocals4 = new _Closure_0024__17_002D1(capturedLocals3);
			capturedLocals4.outerClosure = capturedLocals2;
			capturedLocals4.mps = mps;
			capturedLocals4.output = capturedLocals2.getReusableBuffer();
			capturedLocals4.outX = capturedLocals2.getReusableBuffer();
			capturedLocals4.outY = capturedLocals2.getReusableBuffer();
			Buffers.ArraySetAllVB(ref capturedLocals4.output, -9999f, capturedLocals4.mps.InputCount);
			Buffer.BlockCopy(capturedLocals4.output, 0, capturedLocals4.outX, 0, capturedLocals4.output.Count() * 4);
			Buffer.BlockCopy(capturedLocals4.output, 0, capturedLocals4.outY, 0, capturedLocals4.output.Count() * 4);
			List<Task> tasks = new List<Task>();
			capturedLocals4.outDepths = capturedLocals2.vch.DepthComputer(capturedLocals4.mps, terrain);
			if (capturedLocals2.vch.FlowXS != null && capturedLocals2.vch.WsXS != null && capturedLocals4.mps.HasMapped1DAreas())
			{
				tasks.Add(Task.Factory.StartNew([SpecialName] () =>
				{
					capturedLocals4.outerClosure.velocityRenderer.ComputeVelocityInternal1DFromFlow(capturedLocals4.outerClosure.vch.FlowXS, capturedLocals4.outerClosure.vch.WsXS, capturedLocals4.mps, capturedLocals4.outDepths, capturedLocals4.outX, capturedLocals4.outY, capturedLocals4.output);
				}));
			}
			if (capturedLocals4.mps.HasMappedStorageAreas())
			{
				tasks.Add(Task.Factory.StartNew([SpecialName] () =>
				{
					using List<SAMap>.Enumerator mappedSAs = capturedLocals4.mps.MappedSAs.GetEnumerator();
					_Closure_0024__17_002D2 capturedLocals5 = default;
					while (mappedSAs.MoveNext())
					{
						capturedLocals5 = new _Closure_0024__17_002D2(capturedLocals5);
						capturedLocals5.outerClosure = capturedLocals4;
						capturedLocals5.sa = mappedSAs.Current;
						Utility.Parallel.SmartFor(capturedLocals5.sa.MapPoints.Count, capturedLocals5._Lambda_0024__3);
					}
				}));
			}
			if (capturedLocals2.vch.MeshCellWS != null && capturedLocals2.vch.MeshFaceVels != null && capturedLocals4.mps.HasMapped2DCells())
			{
				tasks.Add(Task.Factory.StartNew([SpecialName] () =>
				{
					capturedLocals4.outerClosure.velocityRenderer.ComputeVelocity2D(capturedLocals4.outerClosure.vch.MeshFaceVels, capturedLocals4.outerClosure.vch.MeshCellWS, capturedLocals4.mps, capturedLocals4.outDepths, capturedLocals4.outX, capturedLocals4.outY, capturedLocals4.output, capturedLocals4.outerClosure.vch.MeshFaceWSValues, capturedLocals4.outerClosure.vch.MeshFaceTrueVels, capturedLocals4.outerClosure.vch.MeshFPVels);
				}));
			}
			Task.WaitAll(tasks.ToArray());
			capturedLocals2.returnReusableBuffer(capturedLocals4.outX);
			capturedLocals2.returnReusableBuffer(capturedLocals4.outY);
			if (capturedLocals2.dvMethod == DepthVelocityMethod.DepthTimesVelocity)
			{
				Utility.Parallel.SmartFor(capturedLocals4.outDepths.Length, [SpecialName] (int start, int fin) =>
				{
					for (int i = start; i <= fin; i++)
					{
						float output = capturedLocals4.output[i];
						float outDepth = capturedLocals4.outDepths[i];
						if (output != -9999f && outDepth != -9999f)
						{
							capturedLocals4.output[i] = outDepth * output;
						}
						else
						{
							capturedLocals4.output[i] = -9999f;
						}
					}
				}, 256);
			}
			else if (capturedLocals2.dvMethod == DepthVelocityMethod.DepthTimesVelocitySquared)
			{
				Utility.Parallel.SmartFor(capturedLocals4.outDepths.Length, [SpecialName] (int start, int fin) =>
				{
					for (int i = start; i <= fin; i++)
					{
						float output2 = capturedLocals4.output[i];
						float outDepth2 = capturedLocals4.outDepths[i];
						if (output2 != -9999f && outDepth2 != -9999f)
						{
							capturedLocals4.output[i] = outDepth2 * output2 * output2;
						}
						else
						{
							capturedLocals4.output[i] = -9999f;
						}
					}
				}, 256);
			}
			else if (capturedLocals2.dvMethod == DepthVelocityMethod.Froude)
			{
				_Closure_0024__17_002D3 capturedLocals6 = default;
				_Closure_0024__17_002D3 capturedLocals7 = new _Closure_0024__17_002D3(capturedLocals6);
				capturedLocals7.outerClosure = capturedLocals4;
				capturedLocals7.gravity = SharedData.Gravity;
				Utility.Parallel.SmartFor(capturedLocals7.outerClosure.outDepths.Length, [SpecialName] (int start, int fin) =>
				{
					for (int i = start; i <= fin; i++)
					{
						float output3 = capturedLocals7.outerClosure.output[i];
						float outDepth3 = capturedLocals7.outerClosure.outDepths[i];
						if (output3 != -9999f && outDepth3 != -9999f)
						{
							capturedLocals7.outerClosure.output[i] = (float)((double)output3 / Math.Sqrt(capturedLocals7.gravity * outDepth3));
						}
						else
						{
							capturedLocals7.outerClosure.output[i] = -9999f;
						}
					}
				}, 256);
			}
			else if (capturedLocals2.dvMethod == DepthVelocityMethod.Froude)
			{
				_Closure_0024__17_002D4 capturedLocals8 = default;
				_Closure_0024__17_002D4 capturedLocals9 = new _Closure_0024__17_002D4(capturedLocals8);
				capturedLocals9.outerClosure = capturedLocals4;
				capturedLocals9.gravity = SharedData.Gravity;
				Utility.Parallel.SmartFor(capturedLocals9.outerClosure.outDepths.Length, [SpecialName] (int start, int fin) =>
				{
					for (int i = start; i <= fin; i++)
					{
						float output4 = capturedLocals9.outerClosure.output[i];
						float outDepth4 = capturedLocals9.outerClosure.outDepths[i];
						if (output4 != -9999f && outDepth4 != -9999f)
						{
							capturedLocals9.outerClosure.output[i] = outDepth4 + output4 * output4 / (2f * capturedLocals9.gravity);
						}
						else
						{
							capturedLocals9.outerClosure.output[i] = -9999f;
						}
					}
				}, 256);
			}
			capturedLocals2.returnReusableBuffer(capturedLocals4.outDepths);
			return capturedLocals4.output;
		});
	}

	private VelocityComputerHelper PreprocessComputer(int profile, Func<float[]> GetReusableBuffer, Action<float[]> ReturnReusableBuffer)
	{
		VelocityComputerHelper velocityComputerHelper = new VelocityComputerHelper();
		velocityComputerHelper.DepthComputer = DepthRenderer.GetComputer(_result, profile, _cache, GetReusableBuffer, ReturnReusableBuffer);
		string dataset = "";
		string dataset2 = null;
		List<string> datasets = null;
		List<string> datasets2 = null;
		string dataset3 = "";
		Names velNames = _velNames;
		int row = 0;
		velNames.Get1D(profile, ref dataset2, ref row);
		int row2 = default;
		_velNames.Get2D(profile, ref datasets, ref row2);
		int row3 = default;
		_flowNames.Get1D(profile, ref dataset3, ref row3);
		int row4 = default;
		_wsNames.Get1D(profile, ref dataset, ref row4);
		int row5 = default;
		_wsNames.Get2D(profile, ref datasets2, ref row5);
		if ((profile == int.MaxValue || profile == int.MinValue) && dataset2 != null)
		{
			ReadMappedXSProfile(dataset2, _rowCache);
		}
		if (profile >= 0 && profile < int.MaxValue && dataset3 != null && dataset != null)
		{
			velocityComputerHelper.FlowXS = _rowCache.EnsureGetRow(dataset3, row3);
			velocityComputerHelper.WsXS = _rowCache.EnsureGetRow(dataset, row4);
		}
		checked
		{
			if (!datasets.IsNullOrEmpty() && !datasets2.IsNullOrEmpty())
			{
				velocityComputerHelper.MeshFaceVels = ReadMappedMeshProfile(datasets, row2, _rowCache);
				velocityComputerHelper.MeshCellWS = ReadMappedMeshProfile(datasets2, row5, _rowCache);
				int meshCount = _d2FlowArea.MeshCount;
				velocityComputerHelper.MeshFaceWSValues = new FaceValues[meshCount - 1 + 1][];
				velocityComputerHelper.MeshFaceTrueVels = new Tuple<Vector2D, Vector2D>[meshCount - 1 + 1][];
				velocityComputerHelper.MeshFPVels = new FacePointVels[meshCount - 1 + 1][];
				row = meshCount - 1;
				for (int i = 0; i <= row; i++)
				{
					MeshFV2D meshFV2D = _d2FlowArea.Meshes().ElementAtOrDefault(i);
					velocityComputerHelper.MeshFaceWSValues[i] = SlopingFactors.ComputeSlopingWSFaceFactors(meshFV2D, velocityComputerHelper.MeshCellWS[i]);
					velocityComputerHelper.MeshFaceTrueVels[i] = meshFV2D.ComputeFaceTrueVelocities_CStencilAveraged(velocityComputerHelper.MeshFaceWSValues[i], velocityComputerHelper.MeshFaceVels[i]);
					velocityComputerHelper.MeshFPVels[i] = meshFV2D.ComputeSlopingFacepointVelocities_AvgTrueVels(velocityComputerHelper.MeshFaceTrueVels[i], velocityComputerHelper.MeshFaceWSValues[i]);
				}
			}
			return velocityComputerHelper;
		}
	}

	public void Compute(int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] outVelocityX, ref float[] outVelocityY, ref float[] outVelocityMag, [Optional][DefaultParameterValue(null)] ref float[] outDepths)
	{
		_Closure_0024__19_002D1 capturedLocals = default;
		_Closure_0024__19_002D1 capturedLocals2 = new _Closure_0024__19_002D1(capturedLocals);
		capturedLocals2.velocityRenderer = this;
		capturedLocals2.mps = mps;
		Buffers.ArraySetAllVB(ref outVelocityMag, -9999f, capturedLocals2.mps.InputCount);
		Buffers.ArraySetAllVB(ref outVelocityX, -9999f, capturedLocals2.mps.InputCount);
		Buffers.ArraySetAllVB(ref outVelocityY, -9999f, capturedLocals2.mps.InputCount);
		_depthRenderer.Compute(profile, capturedLocals2.mps, terrain, ref outDepths);
		string dataset = "";
		string dataset2 = null;
		List<string> datasets = null;
		List<string> datasets2 = null;
		string dataset3 = "";
		Names velNames = _velNames;
		int row = 0;
		velNames.Get1D(profile, ref dataset2, ref row);
		int row2 = default;
		_velNames.Get2D(profile, ref datasets, ref row2);
		int row3 = default;
		_flowNames.Get1D(profile, ref dataset3, ref row3);
		int row4 = default;
		_wsNames.Get1D(profile, ref dataset, ref row4);
		int row5 = default;
		_wsNames.Get2D(profile, ref datasets2, ref row5);
		List<Task> tasks = new List<Task>();
		capturedLocals2.lOVM = outVelocityMag;
		capturedLocals2.lOVX = outVelocityX;
		capturedLocals2.lOVY = outVelocityY;
		capturedLocals2.lOD = outDepths;
		if (profile == int.MaxValue || profile == int.MinValue)
		{
			if (dataset2 != null && capturedLocals2.mps.HasMapped1DAreas())
			{
				_Closure_0024__19_002D0 capturedLocals3 = default;
				_Closure_0024__19_002D0 capturedLocals4 = new _Closure_0024__19_002D0(capturedLocals3);
				capturedLocals4.outerClosure = capturedLocals2;
				capturedLocals4.xsSubsectionVels = ReadMappedXSProfile(capturedLocals4.outerClosure.mps, dataset2, _rowCache);
				tasks.Add(Task.Factory.StartNew([SpecialName] () =>
				{
					capturedLocals4.outerClosure.velocityRenderer.ComputeVelocityInternal1DFromVelocity(capturedLocals4.xsSubsectionVels, capturedLocals4.outerClosure.mps, capturedLocals4.outerClosure.lOD, capturedLocals4.outerClosure.lOVX, capturedLocals4.outerClosure.lOVY, capturedLocals4.outerClosure.lOVM);
				}));
			}
		}
		else if (dataset3 != null && dataset != null && capturedLocals2.mps.HasMapped1DAreas())
		{
			_Closure_0024__19_002D2 capturedLocals5 = default;
			_Closure_0024__19_002D2 capturedLocals6 = new _Closure_0024__19_002D2(capturedLocals5);
			capturedLocals6.outerClosure = capturedLocals2;
			capturedLocals6.flowXS = _rowCache.EnsureGetRow(dataset3, row3);
			capturedLocals6.wsXS = _rowCache.EnsureGetRow(dataset, row4);
			tasks.Add(Task.Factory.StartNew([SpecialName] () =>
			{
				capturedLocals6.outerClosure.velocityRenderer.ComputeVelocityInternal1DFromFlow(capturedLocals6.flowXS, capturedLocals6.wsXS, capturedLocals6.outerClosure.mps, capturedLocals6.outerClosure.lOD, capturedLocals6.outerClosure.lOVX, capturedLocals6.outerClosure.lOVY, capturedLocals6.outerClosure.lOVM);
			}));
		}
		if (capturedLocals2.mps.HasMappedStorageAreas())
		{
			tasks.Add(Task.Factory.StartNew([SpecialName] () =>
			{
				using List<SAMap>.Enumerator mappedSAs = capturedLocals2.mps.MappedSAs.GetEnumerator();
				_Closure_0024__19_002D3 capturedLocals7 = default;
				while (mappedSAs.MoveNext())
				{
					capturedLocals7 = new _Closure_0024__19_002D3(capturedLocals7);
					capturedLocals7.outerClosure = capturedLocals2;
					capturedLocals7.sa = mappedSAs.Current;
					Utility.Parallel.SmartFor(capturedLocals7.sa.MapPoints.Count, capturedLocals7._Lambda_0024__3);
				}
			}));
		}
		if (!datasets.IsNullOrEmpty() && !datasets2.IsNullOrEmpty() && capturedLocals2.mps.HasMapped2DCells())
		{
			_Closure_0024__19_002D4 capturedLocals8 = default;
			_Closure_0024__19_002D4 capturedLocals9 = new _Closure_0024__19_002D4(capturedLocals8);
			capturedLocals9.outerClosure = capturedLocals2;
			capturedLocals9.meshFaceVels = ReadMappedMeshProfile(datasets, row2, _rowCache, capturedLocals9.outerClosure.mps);
			capturedLocals9.meshCellWS = ReadMappedMeshProfile(datasets2, row5, _rowCache, capturedLocals9.outerClosure.mps);
			tasks.Add(Task.Factory.StartNew([SpecialName] () =>
			{
				capturedLocals9.outerClosure.velocityRenderer.ComputeVelocity2D(capturedLocals9.meshFaceVels, capturedLocals9.meshCellWS, capturedLocals9.outerClosure.mps, capturedLocals9.outerClosure.lOD, capturedLocals9.outerClosure.lOVX, capturedLocals9.outerClosure.lOVY, capturedLocals9.outerClosure.lOVM);
			}));
		}
		Task.WaitAll(tasks.ToArray());
	}

	public void Compute2DWithKnownDepths(int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] outVelocityX, ref float[] outVelocityY, ref float[] outVelocityMag, float[] inDepths = null)
	{
		float[] outDepths = null;
		Compute2DInternal(profile, mps, terrain, ref outVelocityX, ref outVelocityY, ref outVelocityMag, inDepths, ref outDepths);
	}

	protected void Compute2DInternal(int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] outVelocityX, ref float[] outVelocityY, ref float[] outVelocityMag, float[] inDepths, ref float[] outDepths)
	{
		_Closure_0024__21_002D0 capturedLocals = default;
		_Closure_0024__21_002D0 capturedLocals2 = new _Closure_0024__21_002D0(capturedLocals);
		capturedLocals2.velocityRenderer = this;
		capturedLocals2.mps = mps;
		Buffers.ArraySetAllVB(ref outVelocityMag, -9999f, capturedLocals2.mps.InputCount);
		Buffers.ArraySetAllVB(ref outVelocityX, -9999f, capturedLocals2.mps.InputCount);
		Buffers.ArraySetAllVB(ref outVelocityY, -9999f, capturedLocals2.mps.InputCount);
		if (inDepths != null)
		{
			outDepths = inDepths;
		}
		else
		{
			_depthRenderer.Compute(profile, capturedLocals2.mps, terrain, ref outDepths);
		}
		List<string> datasets = null;
		List<string> datasets2 = null;
		int row = default;
		_velNames.Get2D(profile, ref datasets, ref row);
		int row2 = default;
		_wsNames.Get2D(profile, ref datasets2, ref row2);
		List<Task> tasks = new List<Task>();
		capturedLocals2.lOVM = outVelocityMag;
		capturedLocals2.lOVX = outVelocityX;
		capturedLocals2.lOVY = outVelocityY;
		capturedLocals2.lOD = outDepths;
		if (capturedLocals2.mps.HasMappedStorageAreas())
		{
			tasks.Add(Task.Factory.StartNew([SpecialName] () =>
			{
				using List<SAMap>.Enumerator mappedSAs = capturedLocals2.mps.MappedSAs.GetEnumerator();
				_Closure_0024__21_002D1 capturedLocals3 = default;
				while (mappedSAs.MoveNext())
				{
					capturedLocals3 = new _Closure_0024__21_002D1(capturedLocals3);
					capturedLocals3.outerClosure = capturedLocals2;
					capturedLocals3.sa = mappedSAs.Current;
					Utility.Parallel.SmartFor(capturedLocals3.sa.MapPoints.Count, capturedLocals3._Lambda_0024__1);
				}
			}));
		}
		if (!datasets.IsNullOrEmpty() && !datasets2.IsNullOrEmpty() && capturedLocals2.mps.HasMapped2DCells())
		{
			_Closure_0024__21_002D2 capturedLocals4 = default;
			_Closure_0024__21_002D2 capturedLocals5 = new _Closure_0024__21_002D2(capturedLocals4);
			capturedLocals5.outerClosure = capturedLocals2;
			capturedLocals5.meshFaceVels = ReadMappedMeshProfile(datasets, row, _rowCache, capturedLocals5.outerClosure.mps);
			capturedLocals5.meshCellWS = ReadMappedMeshProfile(datasets2, row2, _rowCache, capturedLocals5.outerClosure.mps);
			tasks.Add(Task.Factory.StartNew([SpecialName] () =>
			{
				capturedLocals5.outerClosure.velocityRenderer.ComputeVelocity2D(capturedLocals5.meshFaceVels, capturedLocals5.meshCellWS, capturedLocals5.outerClosure.mps, capturedLocals5.outerClosure.lOD, capturedLocals5.outerClosure.lOVX, capturedLocals5.outerClosure.lOVY, capturedLocals5.outerClosure.lOVM);
			}));
		}
		Task.WaitAll(tasks.ToArray());
	}

	protected void ComputeVelocity2D(float[][] meshFaceVectors, float[][] meshCellWS, RASGeometryMapPoints mps, float[] clipPixelDepths, float[] valueX, float[] valueY, float[] valueMagnitude, FaceValues[][] meshFaceWSValues = null, Tuple<Vector2D, Vector2D>[][] meshFaceTrueVels = null, FacePointVels[][] meshFPVels = null)
	{
		foreach (KeyValuePair<int, Tuple<FlatMeshMap, SlopingMeshMap>> joinedMappedMeshe in mps.JoinedMappedMeshes())
		{
			int key = joinedMappedMeshe.Key;
			FlatMeshMap flatMeshMap = joinedMappedMeshe.Value.Item1;
			SlopingMeshMap slopingMeshMap = joinedMappedMeshe.Value.Item2;
			float[] meshCellWSArray = meshCellWS[key];
			float[] facePerpendicularValues = meshFaceVectors[key];
			ComputeFromFacePerpValues(flatMeshMap, facePerpendicularValues, valueX, valueY, valueMagnitude, meshCellWSArray, clipPixelDepths);
			FaceValues[] faceWSValues = null;
			if (meshFaceWSValues != null)
			{
				faceWSValues = meshFaceWSValues[key];
			}
			Tuple<Vector2D, Vector2D>[] faceTrueVels = null;
			if (meshFaceTrueVels != null)
			{
				faceTrueVels = meshFaceTrueVels[key];
			}
			FacePointVels[] fpVels = null;
			if (meshFPVels != null)
			{
				fpVels = meshFPVels[key];
			}
			Render2D_8Stencil(slopingMeshMap, facePerpendicularValues, valueX, valueY, valueMagnitude, meshCellWSArray, clipPixelDepths, faceWSValues, faceTrueVels, fpVels);
		}
	}

	private void ComputeVelocityInternal1DFromFlow(float[] xsFlows, float[] xsWaterSurfaces, RASGeometryMapPoints mps, float[] clipDepths, float[] velX, float[] velY, float[] velMag)
	{
		if (_flowDistribution != null)
		{
			System.Threading.Tasks.Parallel.ForEach(mps.MappedXSISs, [SpecialName] (MappedXSIS interpSurface) =>
			{
				VelocityComputer1D velocityComputer1D = new VelocityComputer1D(_flowDistribution, _XS, _XSIS);
				velocityComputer1D.UpdateXSIS(interpSurface.ID, xsWaterSurfaces, xsFlows);
				velocityComputer1D.ComputeVelocity(interpSurface.MapPoints, clipDepths, velMag, velX, velY);
			});
		}
	}

	private void ComputeVelocityInternal1DFromVelocity(Dictionary<int, float[]> xsVelSubsections, RASGeometryMapPoints mps, float[] clipDepths, float[] velX, float[] velY, float[] velMag)
	{
		ComputeVelocityInternal1DFromVelocity([SpecialName] (int idx) => xsVelSubsections[idx], mps, clipDepths, velX, velY, velMag);
	}

	private void ComputeVelocityInternal1DFromVelocity(float[][] xsVelSubsections, RASGeometryMapPoints mps, float[] clipDepths, float[] velX, float[] velY, float[] velMag)
	{
		ComputeVelocityInternal1DFromVelocity([SpecialName] (int idx) => xsVelSubsections[idx], mps, clipDepths, velX, velY, velMag);
	}

	private void ComputeVelocityInternal1DFromVelocity(Func<int, float[]> xsVelSubsections, RASGeometryMapPoints mps, float[] clipDepths, float[] velX, float[] velY, float[] velMag)
	{
		_Closure_0024__26_002D0 capturedLocals = default;
		_Closure_0024__26_002D0 capturedLocals2 = new _Closure_0024__26_002D0(capturedLocals);
		capturedLocals2.velocityRenderer = this;
		capturedLocals2.xsVelSubsections = xsVelSubsections;
		capturedLocals2.clipDepths = clipDepths;
		capturedLocals2.velX = velX;
		capturedLocals2.velY = velY;
		capturedLocals2.velMag = velMag;
		System.Threading.Tasks.Parallel.ForEach(mps.MappedXSISs, [SpecialName] (MappedXSIS interpSurface) =>
		{
			_Closure_0024__26_002D1 capturedLocals3 = default;
			_Closure_0024__26_002D1 capturedLocals4 = new _Closure_0024__26_002D1(capturedLocals3);
			capturedLocals4.outerClosure = capturedLocals2;
			capturedLocals4.interpSurface = interpSurface;
			capturedLocals4.usXS = capturedLocals2.velocityRenderer._XSIS.GetUpstreamXSID(capturedLocals4.interpSurface.ID);
			capturedLocals4.dsXS = capturedLocals2.velocityRenderer._XSIS.GetDownstreamXSID(capturedLocals4.interpSurface.ID);
			capturedLocals4.usXSVels = capturedLocals2.xsVelSubsections(capturedLocals4.usXS);
			capturedLocals4.dsXSVels = capturedLocals2.xsVelSubsections(capturedLocals4.dsXS);
			capturedLocals4.usStationComputer = capturedLocals2.velocityRenderer._XS.GetStationComputer(capturedLocals4.usXS);
			capturedLocals4.dsStationComputer = capturedLocals2.velocityRenderer._XS.GetStationComputer(capturedLocals4.dsXS);
			Utility.Parallel.SmartFor(capturedLocals4.interpSurface.MapPoints.Count, [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i = checked(i + 1))
				{
					XSISMapPoint xSISMapPoint = capturedLocals4.interpSurface.MapPoints[i];
					if (!(capturedLocals4.outerClosure.clipDepths[xSISMapPoint.Index] <= 0f))
					{
						float averagedMValue = capturedLocals4.outerClosure.velocityRenderer._flowDistribution.AveragedMValue(capturedLocals4.usXS, capturedLocals4.usStationComputer(xSISMapPoint.M), capturedLocals4.usXSVels);
						float averagedMValue2 = capturedLocals4.outerClosure.velocityRenderer._flowDistribution.AveragedMValue(capturedLocals4.dsXS, capturedLocals4.dsStationComputer(xSISMapPoint.M), capturedLocals4.dsXSVels);
						float averagedMValueXSISMapPointTerm = averagedMValue * xSISMapPoint.Z + averagedMValue2 * (1f - xSISMapPoint.Z);
						float streamNXXSISMapPointTerm = averagedMValueXSISMapPointTerm * xSISMapPoint.StreamNX;
						float streamNYXSISMapPointTerm = averagedMValueXSISMapPointTerm * xSISMapPoint.StreamNY;
						capturedLocals4.outerClosure.velX[xSISMapPoint.Index] = streamNXXSISMapPointTerm;
						capturedLocals4.outerClosure.velY[xSISMapPoint.Index] = streamNYXSISMapPointTerm;
						capturedLocals4.outerClosure.velMag[xSISMapPoint.Index] = averagedMValueXSISMapPointTerm;
					}
				}
			});
		});
	}
}
