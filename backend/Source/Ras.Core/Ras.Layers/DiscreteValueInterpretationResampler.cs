using System;
using System.Collections.Generic;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Utility.Memory;

namespace Ras.Layers;

internal class DiscreteValueInterpretationResampler : IResample<float>, ISurface<float>
{
	private readonly IResample<int> _discreteRes;

	private readonly Dictionary<int, float> _table;

	public Extent ValidExtent => _discreteRes.ValidExtent;

	public float? NoDataValue { get; init; }

	public DiscreteValueInterpretationResampler(IResample<int> res, Dictionary<int, float> table, float ndv)
	{
		_discreteRes = res;
		_table = table;
		NoDataValue = ndv;
	}

	public void Resample(ResampleArgs args, Memory<float> outData)
	{
		Memory<int> mem;
		using (SharedMemoryPool<int>.Rent(args.Raster.CellCount, out mem))
		{
			_discreteRes.Resample(args, mem);
			ConvertData(mem, outData);
		}
	}

	public void SamplePoints(Memory<Point> points, Memory<float> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		Memory<int> mem;
		using (SharedMemoryPool<int>.Rent(points.Length, out mem))
		{
			_discreteRes.SamplePoints(points, mem, sampleParams);
			ConvertData(mem, outData);
		}
	}

	private void ConvertData(Memory<int> inClasses, Memory<float> outData)
	{
		Span<int> inClassesSpan = inClasses.Span;
		Span<float> outDataSpan = outData.Span;
		int length = inClassesSpan.Length;
		int valueOrDefault = _discreteRes.NoDataValue.GetValueOrDefault();
		float valueOrDefault2 = NoDataValue.GetValueOrDefault();
		int inClasse = inClassesSpan[0];
		float outDataSpan2;
		float computed;
		if (inClasse == valueOrDefault)
		{
			outDataSpan2 = valueOrDefault2;
		}
		else
		{
			outDataSpan2 = ((!_table.TryGetValue(inClasse, out computed)) ? valueOrDefault2 : computed);
		}
		for (int i = 0; i < length; i++)
		{
			int inClasse2 = inClassesSpan[i];
			if (inClasse2 == inClasse)
			{
				outDataSpan[i] = outDataSpan2;
			}
			else if (inClasse2 == valueOrDefault)
			{
				outDataSpan[i] = valueOrDefault2;
			}
			else if (_table.TryGetValue(inClasse2, out computed))
			{
				inClasse = inClasse2;
				outDataSpan2 = computed;
				outDataSpan[i] = outDataSpan2;
			}
			else
			{
				outDataSpan[i] = valueOrDefault2;
			}
		}
	}
}
