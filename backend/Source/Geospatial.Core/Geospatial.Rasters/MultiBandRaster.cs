using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Geospatial.Vectors;
using Utility;
using Utility.DataStructures;

namespace Geospatial.Rasters;

public class MultiBandRaster<T> : IMultiBandRaster<T>, IMultiBandResampler<T>, ISurface<T>, IRasterDefinition where T : unmanaged, IEquatable<T>
{
	protected readonly List<Any<T[]>> _data;

	private readonly int _sizeOfT;

	public int ProfileCount => _data.Count;

	public Extent ValidExtent => RasterDefinition.Extent;

	public RasterDefinition RasterDefinition { get; }

	public T? NoDataValue { get; set; }

	public List<Any<T[]>> Bands => _data;

	public MultiBandRaster(RasterDefinition raster, IList<Any<T[]>> data, T? noDataValue = null)
	{
		if (raster.IsNull() || data == null)
		{
			throw new ArgumentNullException();
		}
		RasterDefinition = raster;
		_data = data.ToList();
		_sizeOfT = Marshal.SizeOf<T>();
		NoDataValue = noDataValue;
		for (int i = 0; i < _data.Count; i++)
		{
			T[] valueIfAlreadyEvaluated = _data[i].ValueIfAlreadyEvaluated;
			if (valueIfAlreadyEvaluated != null && valueIfAlreadyEvaluated.Length != raster.CellCount)
			{
				throw new ArgumentException("Data provided doesn't match raster length.");
			}
		}
	}

	public MultiBandRaster(RasterDefinition raster, T[][] data, T? noDataValue = null)
	{
		if (raster.IsNull() || data == null)
		{
			throw new ArgumentNullException();
		}
		RasterDefinition = raster;
		_data = new List<Any<T[]>>();
		for (int i = 0; i < data.Length; i++)
		{
			_data.Add(data[i]);
		}
		_sizeOfT = Marshal.SizeOf<T>();
		NoDataValue = noDataValue;
		for (int j = 0; j < _data.Count; j++)
		{
			T[] valueIfAlreadyEvaluated = _data[j].ValueIfAlreadyEvaluated;
			if (valueIfAlreadyEvaluated != null && valueIfAlreadyEvaluated.Length != raster.CellCount)
			{
				throw new ArgumentException("Data provided doesn't match raster length.");
			}
		}
	}

	public void Resample(ResampleArgs args, int bandIndex, Memory<T> result, ResampleCache cache = null)
	{
		if (bandIndex < 0 || bandIndex >= ProfileCount)
		{
			throw new ArgumentOutOfRangeException("bandIndex");
		}
		T[] bandIndexTerm = _data[bandIndex].Value;
		RasterOperations.Resample(RasterDefinition, bandIndexTerm, args, result, NoDataValue.GetValueOrDefault());
	}

	public void Resample(ResampleArgs args, int bandStart, int bandCount, Memory<T>[] result)
	{
		this.Resample_NaiiveLoop(args, bandStart, bandCount, result);
	}

	public void GetRange(PixelRange range, int bandIndex, Memory<T> result)
	{
		if (bandIndex < 0 || bandIndex >= ProfileCount)
		{
			throw new ArgumentOutOfRangeException("bandIndex");
		}
		RasterOperations.CopyRange(RasterDefinition, _data[bandIndex].Value, range, result);
	}

	public void SamplePoints(Memory<Point> points, int bandStart, int bandCount, Memory<T>[] result)
	{
		this.SamplePoints_NaiiveLoop(points, bandStart, bandCount, result);
	}

	public void SamplePoints(Memory<Point> points, int bandIndex, Memory<T> result)
	{
		if (bandIndex < 0 || bandIndex >= ProfileCount)
		{
			throw new ArgumentOutOfRangeException("bandIndex");
		}
		T[] bandIndexTerm = _data[bandIndex].Value;
		RasterData<T>.SamplePoints(RasterDefinition, bandIndexTerm, NoDataValue.GetValueOrDefault(), points, result);
	}

	public void GetBand(int profileIndex, ref T[] outData)
	{
		T[] profileIndexTerm = _data[profileIndex].Value;
		Buffers.EnsureArraySize(ref outData, profileIndexTerm.Length);
		Buffer.BlockCopy(profileIndexTerm, 0, outData, 0, profileIndexTerm.Length * _sizeOfT);
	}
}
