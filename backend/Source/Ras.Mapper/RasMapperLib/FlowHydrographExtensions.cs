using System;
using System.Runtime.CompilerServices;
using Geospatial.PairedData;
using Microsoft.VisualBasic.CompilerServices;
using Utility.Extensions;

namespace RasMapperLib;

[StandardModule]
public static class FlowHydrographExtensions
{
	[CompilerGenerated]
private sealed class _Closure_0024__1_002D0
	{
		public float[] retn;

		public _Closure_0024__1_002D0(_Closure_0024__1_002D0 other)
		{
			if (other != null)
			{
				retn = other.retn;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(FunctionalPairedDataF.Shape shape)
		{
			retn[shape.NextPointIdx] += shape.Area * (float)Math.Sign(shape.CenterOfMass.Y);
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__2_002D0
	{
		public double ifTemp;

		public double ofTemp;

		public _Closure_0024__2_002D0(_Closure_0024__2_002D0 other)
		{
			if (other != null)
			{
				ifTemp = other.ifTemp;
				ofTemp = other.ofTemp;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(FunctionalPairedDataF.Shape shape)
		{
			if (shape.CenterOfMass.Y > 0f)
			{
				ifTemp += shape.Area;
			}
			else
			{
				ofTemp += shape.Area;
			}
		}
	}

	public static float[] ReadAggregatedVolumeAccumulationHydrograph(this IFlowHydrograph lyr, int fid, string outputBlockName = "")
	{
		float[] aggregatedFlowHydrograph = lyr.ReadAggregatedFlowHydrograph(fid, outputBlockName);
		if (aggregatedFlowHydrograph.IsNullOrEmpty())
		{
			return null;
		}
		checked
		{
			float[] floatValues = new float[aggregatedFlowHydrograph.Length - 1 + 1];
			floatValues[0] = 0f;
			float outputTimestepInSeconds = lyr.GetOutputTimestepInSeconds(outputBlockName);
			int count = floatValues.Length - 1;
			for (int i = 1; i <= count; i++)
			{
				float aggregatedFlowHydrograph2 = aggregatedFlowHydrograph[i - 1];
				float aggregatedFlowHydrograph3 = aggregatedFlowHydrograph[i];
				float aggregatedFlowHydrograph4;
				float aggregatedFlowHydrograph5;
				if (aggregatedFlowHydrograph2 > aggregatedFlowHydrograph3)
				{
					aggregatedFlowHydrograph4 = aggregatedFlowHydrograph3;
					aggregatedFlowHydrograph5 = aggregatedFlowHydrograph2;
				}
				else
				{
					aggregatedFlowHydrograph4 = aggregatedFlowHydrograph2;
					aggregatedFlowHydrograph5 = aggregatedFlowHydrograph3;
				}
				floatValues[i] = floatValues[i - 1] + (float)((double)(aggregatedFlowHydrograph4 * outputTimestepInSeconds) + 0.5 * (double)outputTimestepInSeconds * (double)(aggregatedFlowHydrograph5 - aggregatedFlowHydrograph4));
			}
			return floatValues;
		}
	}

	public static float[] ReadAggregatedVolumePerTimestepHydrograph(this IFlowHydrograph lyr, int fid, string outputBlockName = "")
	{
		_Closure_0024__1_002D0 capturedLocals = default;
		_Closure_0024__1_002D0 capturedLocals2 = new _Closure_0024__1_002D0(capturedLocals);
		float[] aggregatedFlowHydrograph = lyr.ReadAggregatedFlowHydrograph(fid, outputBlockName);
		if (aggregatedFlowHydrograph.IsNullOrEmpty())
		{
			return null;
		}
		checked
		{
			capturedLocals2.retn = new float[aggregatedFlowHydrograph.Length - 1 + 1];
			float outputTimestepInSeconds = lyr.GetOutputTimestepInSeconds(outputBlockName);
			float[] floatValues = new float[aggregatedFlowHydrograph.Length - 1 + 1];
			int count = aggregatedFlowHydrograph.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				floatValues[i] = (float)i * outputTimestepInSeconds;
			}
			new FunctionalPairedDataF(aggregatedFlowHydrograph, floatValues).ComputeForShape([SpecialName] (FunctionalPairedDataF.Shape shape) =>
			{
				capturedLocals2.retn[shape.NextPointIdx] += shape.Area * (float)Math.Sign(shape.CenterOfMass.Y);
			});
			return capturedLocals2.retn;
		}
	}

	public static void ComputeNetInflowOutflow(this IFlowHydrograph lyr, int fid, ref double inflow, ref double outflow, string outputBlockName = "")
	{
		_Closure_0024__2_002D0 capturedLocals = default;
		_Closure_0024__2_002D0 capturedLocals2 = new _Closure_0024__2_002D0(capturedLocals);
		float[] aggregatedFlowHydrograph = lyr.ReadAggregatedFlowHydrograph(fid, outputBlockName);
		if (aggregatedFlowHydrograph.IsNullOrEmpty())
		{
			return;
		}
		float outputTimestepInSeconds = lyr.GetOutputTimestepInSeconds(outputBlockName);
		checked
		{
			float[] floatValues = new float[aggregatedFlowHydrograph.Length - 1 + 1];
			int count = aggregatedFlowHydrograph.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				floatValues[i] = (float)i * outputTimestepInSeconds;
			}
			capturedLocals2.ifTemp = 0.0;
			capturedLocals2.ofTemp = 0.0;
			new FunctionalPairedDataF(floatValues, aggregatedFlowHydrograph).ComputeForShape([SpecialName] (FunctionalPairedDataF.Shape shape) =>
			{
				if (shape.CenterOfMass.Y > 0f)
				{
					capturedLocals2.ifTemp += shape.Area;
				}
				else
				{
					capturedLocals2.ofTemp += shape.Area;
				}
			});
			inflow = capturedLocals2.ifTemp;
			outflow = capturedLocals2.ofTemp;
		}
	}
}
