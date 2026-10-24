using System;
using System.Buffers;
using System.Collections.Generic;
using Geospatial.Vectors;
using Utility;
using Utility.Memory;

namespace Geospatial.Rasters;

internal class CalculatorDynamicResampler : IMultiBandResampler<float>, ISurface<float>, IDiscreteTimesteps
{
	private readonly RasterCalculatorInput[] _inputs;

	private readonly Delegate _evaluate;

	private readonly int _inputCount;

	private readonly int[][] _profileMaps;

	private readonly CalculatorNdvBehavior _ndvBehavior;

	public int ProfileCount { get; }

	public Extent ValidExtent { get; }

	public float? NoDataValue => -9999f;

	public IReadOnlyList<DateTime> Times { get; }

	public CalculatorDynamicResampler(int profileCount, Extent validExtent, RasterCalculatorInput[] inputs, Delegate evaluateExpression, IReadOnlyList<DateTime> outputTimes, int[][] profileMaps, CalculatorNdvBehavior ndvBehavior = CalculatorNdvBehavior.WriteNDV)
	{
		ProfileCount = profileCount;
		ValidExtent = validExtent;
		_inputs = inputs;
		_evaluate = evaluateExpression;
		_inputCount = inputs.Length;
		_profileMaps = profileMaps;
		_ndvBehavior = ndvBehavior;
		Times = outputTimes;
	}

	private int MapProfile(int inputIndex, int outputProfileIndex)
	{
		int[][] profileMaps = _profileMaps;
		int[] profileMap = ((profileMaps != null) ? profileMaps[inputIndex] : null);
		if (profileMap == null)
		{
			return outputProfileIndex;
		}
		return profileMap[outputProfileIndex];
	}

	private ResampleCache[] GetInputCaches(ResampleCache cache)
	{
		if (cache == null)
		{
			return null;
		}
		if (cache.SpatialCache is ResampleCache[] result)
		{
			return result;
		}
		ResampleCache[] spatialCache = new ResampleCache[_inputs.Length];
		for (int i = 0; i < _inputs.Length; i++)
		{
			if (_inputs[i].IsDynamic)
			{
				spatialCache[i] = new ResampleCache();
				cache.TrackDisposable(spatialCache[i]);
			}
		}
		cache.SpatialCache = spatialCache;
		return spatialCache;
	}

	public void Resample(ResampleArgs args, int profileIndex, Memory<float> outData, ResampleCache cache = null)
	{
		args.EnsureBuffer(outData, NoDataValue);
		RasterDefinition raster = args.Raster;
		if (raster.GetPixelRange(ValidExtent, PixelRangeOptions.CenterPixelCovered).CellCount == 0)
		{
			return;
		}
		int cellCount = raster.CellCount;
		ResampleCache[] inputCaches = GetInputCaches(cache);
		IMemoryOwner<float>[] iMemoryOwners = new IMemoryOwner<float>[_inputs.Length];
		Memory<float>[] floatSpanArray = new Memory<float>[_inputs.Length];
		try
		{
			for (int i = 0; i < _inputs.Length; i++)
			{
				iMemoryOwners[i] = SharedMemoryPool<float>.Rent(cellCount, out floatSpanArray[i]);
				if (_inputs[i].IsDynamic)
				{
					int mapProfile = MapProfile(i, profileIndex);
					if (mapProfile < 0)
					{
						floatSpanArray[i].Span.Fill(-9999f);
						continue;
					}
					if (_inputs[i].NoDataValue.HasValue)
					{
						floatSpanArray[i].Span.Fill(_inputs[i].NoDataValue.Value);
					}
					_inputs[i].DynamicInput.Resample(args, mapProfile, floatSpanArray[i], (inputCaches != null) ? inputCaches[i] : null);
				}
				else
				{
					if (_inputs[i].NoDataValue.HasValue)
					{
						floatSpanArray[i].Span.Fill(_inputs[i].NoDataValue.Value);
					}
					_inputs[i].StaticInput.Resample(args, floatSpanArray[i]);
				}
			}
			EvaluatePixels(floatSpanArray, outData, cellCount);
		}
		finally
		{
			for (int j = 0; j < iMemoryOwners.Length; j++)
			{
				iMemoryOwners[j]?.Dispose();
			}
		}
	}

	public void Resample(ResampleArgs args, int startBandIndex, int bandCount, Memory<float>[] outData)
	{
		int cellCount = args.Raster.CellCount;
		IMemoryOwner<float>[] iMemoryOwners = new IMemoryOwner<float>[_inputs.Length];
		Memory<float>[] floatSpanArray = new Memory<float>[_inputs.Length];
		ResampleCache[] resampleCaches = new ResampleCache[_inputs.Length];
		try
		{
			for (int i = 0; i < _inputs.Length; i++)
			{
				if (!_inputs[i].IsDynamic)
				{
					iMemoryOwners[i] = SharedMemoryPool<float>.Rent(cellCount, out floatSpanArray[i]);
					if (_inputs[i].NoDataValue.HasValue)
					{
						floatSpanArray[i].Span.Fill(_inputs[i].NoDataValue.Value);
					}
					_inputs[i].StaticInput.Resample(args, floatSpanArray[i]);
				}
				else
				{
					resampleCaches[i] = new ResampleCache();
				}
			}
			for (int j = 0; j < bandCount; j++)
			{
				int outputProfileIndex = startBandIndex + j;
				args.EnsureBuffer(outData[j], NoDataValue);
				IMemoryOwner<float>[] iMemoryOwnersArray = new IMemoryOwner<float>[_inputs.Length];
				Memory<float>[] floatSpanArraySpan = new Memory<float>[_inputs.Length];
				try
				{
					for (int k = 0; k < _inputs.Length; k++)
					{
						if (_inputs[k].IsDynamic)
						{
							iMemoryOwnersArray[k] = SharedMemoryPool<float>.Rent(cellCount, out floatSpanArraySpan[k]);
							int mapProfile = MapProfile(k, outputProfileIndex);
							if (mapProfile < 0)
							{
								floatSpanArraySpan[k].Span.Fill(-9999f);
								continue;
							}
							if (_inputs[k].NoDataValue.HasValue)
							{
								floatSpanArraySpan[k].Span.Fill(_inputs[k].NoDataValue.Value);
							}
							_inputs[k].DynamicInput.Resample(args, mapProfile, floatSpanArraySpan[k], resampleCaches[k]);
						}
						else
						{
							floatSpanArraySpan[k] = floatSpanArray[k];
						}
					}
					EvaluatePixels(floatSpanArraySpan, outData[j], cellCount);
				}
				finally
				{
					for (int l = 0; l < iMemoryOwnersArray.Length; l++)
					{
						iMemoryOwnersArray[l]?.Dispose();
					}
				}
			}
		}
		finally
		{
			for (int m = 0; m < iMemoryOwners.Length; m++)
			{
				iMemoryOwners[m]?.Dispose();
			}
			for (int n = 0; n < resampleCaches.Length; n++)
			{
				resampleCaches[n]?.Dispose();
			}
		}
	}

	public void SamplePoints(Memory<Point> points, int profileIndex, Memory<float> outData)
	{
		int length = points.Length;
		float valueOrDefault = NoDataValue.GetValueOrDefault();
		outData.Span.Fill(valueOrDefault);
		IMemoryOwner<float>[] iMemoryOwners = new IMemoryOwner<float>[_inputs.Length];
		Memory<float>[] floatSpanArray = new Memory<float>[_inputs.Length];
		try
		{
			for (int i = 0; i < _inputs.Length; i++)
			{
				iMemoryOwners[i] = SharedMemoryPool<float>.Rent(length, out floatSpanArray[i]);
				if (_inputs[i].IsDynamic)
				{
					int mapProfile = MapProfile(i, profileIndex);
					if (mapProfile < 0)
					{
						floatSpanArray[i].Span.Fill(valueOrDefault);
					}
					else
					{
						_inputs[i].DynamicInput.SamplePoints(points, mapProfile, floatSpanArray[i]);
					}
				}
				else
				{
					_inputs[i].StaticInput.SamplePoints(points, floatSpanArray[i]);
				}
			}
			EvaluatePixels(floatSpanArray, outData, length);
		}
		finally
		{
			for (int j = 0; j < iMemoryOwners.Length; j++)
			{
				iMemoryOwners[j]?.Dispose();
			}
		}
	}

	public void SamplePoints(Memory<Point> points, int startBandIndex, int bandCount, Memory<float>[] outData)
	{
		this.SamplePoints_NaiiveLoop(points, startBandIndex, bandCount, outData);
	}

	private static float HandleNoData(CalculatorNdvBehavior behavior, float ndv)
	{
		return behavior switch
		{
			CalculatorNdvBehavior.WriteNDV => ndv, 
			CalculatorNdvBehavior.WriteZero => 0f, 
			_ => ndv, 
		};
	}

	private void EvaluatePixels(Memory<float>[] inputs, Memory<float> output, int count)
	{
		float ndv = -9999f;
		switch (_inputCount)
		{
		case 1:
			EvaluatePixels1((Func<float, float>)_evaluate, inputs, output, count, ndv, _ndvBehavior);
			break;
		case 2:
			EvaluatePixels2((Func<float, float, float>)_evaluate, inputs, output, count, ndv, _ndvBehavior);
			break;
		case 3:
			EvaluatePixels3((Func<float, float, float, float>)_evaluate, inputs, output, count, ndv, _ndvBehavior);
			break;
		case 4:
			EvaluatePixels4((Func<float, float, float, float, float>)_evaluate, inputs, output, count, ndv, _ndvBehavior);
			break;
		case 5:
			EvaluatePixels5((Func<float, float, float, float, float, float>)_evaluate, inputs, output, count, ndv, _ndvBehavior);
			break;
		case 6:
			EvaluatePixels6((Func<float, float, float, float, float, float, float>)_evaluate, inputs, output, count, ndv, _ndvBehavior);
			break;
		case 7:
			EvaluatePixels7((Func<float, float, float, float, float, float, float, float>)_evaluate, inputs, output, count, ndv, _ndvBehavior);
			break;
		case 8:
			EvaluatePixels8((Func<float, float, float, float, float, float, float, float, float>)_evaluate, inputs, output, count, ndv, _ndvBehavior);
			break;
		default:
			EvaluatePixelsDynamic(_evaluate, inputs, output, count, ndv, _inputCount, _ndvBehavior);
			break;
		}
	}

	private static void EvaluatePixels1(Func<float, float> func, Memory<float>[] inputs, Memory<float> output, int count, float ndv, CalculatorNdvBehavior behavior)
	{
		float ndvOut = HandleNoData(behavior, ndv);
		bool checkNdv = behavior != CalculatorNdvBehavior.Allow;
		Parallel.SmartFor(count, (int start, int end) =>
		{
			Span<float> input = inputs[0].Span;
			Span<float> outputSpan = output.Span;
			for (int i = start; i <= end; i++)
			{
				float input2 = input[i];
				if (checkNdv && input2 == ndv)
				{
					outputSpan[i] = ndvOut;
				}
				else
				{
					outputSpan[i] = func(input2);
				}
			}
		});
	}

	private static void EvaluatePixels2(Func<float, float, float> func, Memory<float>[] inputs, Memory<float> output, int count, float ndv, CalculatorNdvBehavior behavior)
	{
		float ndvOut = HandleNoData(behavior, ndv);
		bool checkNdv = behavior != CalculatorNdvBehavior.Allow;
		Parallel.SmartFor(count, (int start, int end) =>
		{
			Span<float> input = inputs[0].Span;
			Span<float> inputSpan = inputs[1].Span;
			Span<float> outputSpan = output.Span;
			for (int i = start; i <= end; i++)
			{
				float input2 = input[i];
				float input3 = inputSpan[i];
				if (checkNdv && (input2 == ndv || input3 == ndv))
				{
					outputSpan[i] = ndvOut;
				}
				else
				{
					outputSpan[i] = func(input2, input3);
				}
			}
		});
	}

	private static void EvaluatePixels3(Func<float, float, float, float> func, Memory<float>[] inputs, Memory<float> output, int count, float ndv, CalculatorNdvBehavior behavior)
	{
		float ndvOut = HandleNoData(behavior, ndv);
		bool checkNdv = behavior != CalculatorNdvBehavior.Allow;
		Parallel.SmartFor(count, (int start, int end) =>
		{
			Span<float> input = inputs[0].Span;
			Span<float> inputSpan = inputs[1].Span;
			Span<float> input2 = inputs[2].Span;
			Span<float> outputSpan = output.Span;
			for (int i = start; i <= end; i++)
			{
				float input3 = input[i];
				float input4 = inputSpan[i];
				float input5 = input2[i];
				if (checkNdv && (input3 == ndv || input4 == ndv || input5 == ndv))
				{
					outputSpan[i] = ndvOut;
				}
				else
				{
					outputSpan[i] = func(input3, input4, input5);
				}
			}
		});
	}

	private static void EvaluatePixels4(Func<float, float, float, float, float> func, Memory<float>[] inputs, Memory<float> output, int count, float ndv, CalculatorNdvBehavior behavior)
	{
		float ndvOut = HandleNoData(behavior, ndv);
		bool checkNdv = behavior != CalculatorNdvBehavior.Allow;
		Parallel.SmartFor(count, (int start, int end) =>
		{
			Span<float> input = inputs[0].Span;
			Span<float> inputSpan = inputs[1].Span;
			Span<float> input2 = inputs[2].Span;
			Span<float> input3 = inputs[3].Span;
			Span<float> outputSpan = output.Span;
			for (int i = start; i <= end; i++)
			{
				float input4 = input[i];
				float input5 = inputSpan[i];
				float input6 = input2[i];
				float input7 = input3[i];
				if (checkNdv && (input4 == ndv || input5 == ndv || input6 == ndv || input7 == ndv))
				{
					outputSpan[i] = ndvOut;
				}
				else
				{
					outputSpan[i] = func(input4, input5, input6, input7);
				}
			}
		});
	}

	private static void EvaluatePixels5(Func<float, float, float, float, float, float> func, Memory<float>[] inputs, Memory<float> output, int count, float ndv, CalculatorNdvBehavior behavior)
	{
		float ndvOut = HandleNoData(behavior, ndv);
		bool checkNdv = behavior != CalculatorNdvBehavior.Allow;
		Parallel.SmartFor(count, (int start, int end) =>
		{
			Span<float> input = inputs[0].Span;
			Span<float> inputSpan = inputs[1].Span;
			Span<float> input2 = inputs[2].Span;
			Span<float> input3 = inputs[3].Span;
			Span<float> input4 = inputs[4].Span;
			Span<float> outputSpan = output.Span;
			for (int i = start; i <= end; i++)
			{
				float input5 = input[i];
				float input6 = inputSpan[i];
				float input7 = input2[i];
				float input8 = input3[i];
				float input9 = input4[i];
				if (checkNdv && (input5 == ndv || input6 == ndv || input7 == ndv || input8 == ndv || input9 == ndv))
				{
					outputSpan[i] = ndvOut;
				}
				else
				{
					outputSpan[i] = func(input5, input6, input7, input8, input9);
				}
			}
		});
	}

	private static void EvaluatePixels6(Func<float, float, float, float, float, float, float> func, Memory<float>[] inputs, Memory<float> output, int count, float ndv, CalculatorNdvBehavior behavior)
	{
		float ndvOut = HandleNoData(behavior, ndv);
		bool checkNdv = behavior != CalculatorNdvBehavior.Allow;
		Parallel.SmartFor(count, (int start, int end) =>
		{
			Span<float> input = inputs[0].Span;
			Span<float> inputSpan = inputs[1].Span;
			Span<float> input2 = inputs[2].Span;
			Span<float> input3 = inputs[3].Span;
			Span<float> input4 = inputs[4].Span;
			Span<float> input5 = inputs[5].Span;
			Span<float> outputSpan = output.Span;
			for (int i = start; i <= end; i++)
			{
				float input6 = input[i];
				float input7 = inputSpan[i];
				float input8 = input2[i];
				float input9 = input3[i];
				float input10 = input4[i];
				float input11 = input5[i];
				if (checkNdv && (input6 == ndv || input7 == ndv || input8 == ndv || input9 == ndv || input10 == ndv || input11 == ndv))
				{
					outputSpan[i] = ndvOut;
				}
				else
				{
					outputSpan[i] = func(input6, input7, input8, input9, input10, input11);
				}
			}
		});
	}

	private static void EvaluatePixels7(Func<float, float, float, float, float, float, float, float> func, Memory<float>[] inputs, Memory<float> output, int count, float ndv, CalculatorNdvBehavior behavior)
	{
		float ndvOut = HandleNoData(behavior, ndv);
		bool checkNdv = behavior != CalculatorNdvBehavior.Allow;
		Parallel.SmartFor(count, (int start, int end) =>
		{
			Span<float> input = inputs[0].Span;
			Span<float> inputSpan = inputs[1].Span;
			Span<float> input2 = inputs[2].Span;
			Span<float> input3 = inputs[3].Span;
			Span<float> input4 = inputs[4].Span;
			Span<float> input5 = inputs[5].Span;
			Span<float> input6 = inputs[6].Span;
			Span<float> outputSpan = output.Span;
			for (int i = start; i <= end; i++)
			{
				float input7 = input[i];
				float input8 = inputSpan[i];
				float input9 = input2[i];
				float input10 = input3[i];
				float input11 = input4[i];
				float input12 = input5[i];
				float input13 = input6[i];
				if (checkNdv && (input7 == ndv || input8 == ndv || input9 == ndv || input10 == ndv || input11 == ndv || input12 == ndv || input13 == ndv))
				{
					outputSpan[i] = ndvOut;
				}
				else
				{
					outputSpan[i] = func(input7, input8, input9, input10, input11, input12, input13);
				}
			}
		});
	}

	private static void EvaluatePixels8(Func<float, float, float, float, float, float, float, float, float> func, Memory<float>[] inputs, Memory<float> output, int count, float ndv, CalculatorNdvBehavior behavior)
	{
		float ndvOut = HandleNoData(behavior, ndv);
		bool checkNdv = behavior != CalculatorNdvBehavior.Allow;
		Parallel.SmartFor(count, (int start, int end) =>
		{
			Span<float> input = inputs[0].Span;
			Span<float> inputSpan = inputs[1].Span;
			Span<float> input2 = inputs[2].Span;
			Span<float> input3 = inputs[3].Span;
			Span<float> input4 = inputs[4].Span;
			Span<float> input5 = inputs[5].Span;
			Span<float> input6 = inputs[6].Span;
			Span<float> input7 = inputs[7].Span;
			Span<float> outputSpan = output.Span;
			for (int i = start; i <= end; i++)
			{
				float input8 = input[i];
				float input9 = inputSpan[i];
				float input10 = input2[i];
				float input11 = input3[i];
				float input12 = input4[i];
				float input13 = input5[i];
				float input14 = input6[i];
				float input15 = input7[i];
				if (checkNdv && (input8 == ndv || input9 == ndv || input10 == ndv || input11 == ndv || input12 == ndv || input13 == ndv || input14 == ndv || input15 == ndv))
				{
					outputSpan[i] = ndvOut;
				}
				else
				{
					outputSpan[i] = func(input8, input9, input10, input11, input12, input13, input14, input15);
				}
			}
		});
	}

	private static void EvaluatePixelsDynamic(Delegate func, Memory<float>[] inputs, Memory<float> output, int count, float ndv, int inputCount, CalculatorNdvBehavior behavior)
	{
		float handleNoData = HandleNoData(behavior, ndv);
		bool behaviorAllowTerm = behavior != CalculatorNdvBehavior.Allow;
		Span<float> outputSpan = output.Span;
		object[] objectValues = new object[inputCount];
		for (int i = 0; i < count; i++)
		{
			bool condition = false;
			for (int j = 0; j < inputCount; j++)
			{
				float input = inputs[j].Span[i];
				if (behaviorAllowTerm && input == ndv)
				{
					condition = true;
					break;
				}
				objectValues[j] = input;
			}
			if (condition)
			{
				outputSpan[i] = handleNoData;
			}
			else
			{
				outputSpan[i] = (float)func.DynamicInvoke(objectValues);
			}
		}
	}
}
