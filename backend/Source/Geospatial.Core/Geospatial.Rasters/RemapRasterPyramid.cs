using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

public class RemapRasterPyramid : IRasterPyramid<int>, IRaster<int>, IResample<int>, ISurface<int>, IRasterDefinition, IPyramidLevel
{
	private readonly IRasterPyramid<int> _baseRes;

	private readonly Dictionary<int, int> _valueMap;

	public Extent ValidExtent => _baseRes.ValidExtent;

	public int? NoDataValue => _baseRes.NoDataValue;

	public RasterDefinition RasterDefinition => _baseRes.RasterDefinition;

	public int PyramidCount => _baseRes.PyramidCount;

	public float PyramidSelectionRatio
	{
		get
		{
			return _baseRes.PyramidSelectionRatio;
		}
		set
		{
			_baseRes.PyramidSelectionRatio = value;
		}
	}

	public RemapRasterPyramid(IRasterPyramid<int> baseRes, Dictionary<int, int> map)
	{
		_baseRes = baseRes;
		_valueMap = map;
	}

	public void Resample(ResampleArgs args, Memory<int> outData)
	{
		_baseRes.Resample(args, outData);
		Convert(outData.Span);
	}

	public void SamplePoints(Memory<Point> points, Memory<int> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		_baseRes.SamplePoints(points, outData, sampleParams);
		Convert(outData.Span);
	}

	private void Convert(Span<int> sp)
	{
		if (_valueMap == null || _valueMap.Count == 0)
		{
			return;
		}
		int valueOrDefault = NoDataValue.GetValueOrDefault();
		int minValue = int.MinValue;
		int minValue2 = int.MinValue;
		for (int i = 0; i < sp.Length; i++)
		{
			int sp2 = sp[i];
			if (sp2 != valueOrDefault)
			{
				int sp3;
				if (sp2 == minValue)
				{
					sp[i] = minValue2;
				}
				else if (_valueMap.TryGetValue(sp2, out sp3))
				{
					sp[i] = sp3;
					minValue = sp2;
					minValue2 = sp3;
				}
			}
		}
	}

	public IRaster<int> GetPyramidLevel(int lvl)
	{
		return new RemapRaster(_baseRes.GetPyramidLevel(lvl), _valueMap);
	}

	public void GetRange(PixelRange range, Memory<int> outData)
	{
		_baseRes.GetRange(range, outData);
		Convert(outData.Span);
	}
}
