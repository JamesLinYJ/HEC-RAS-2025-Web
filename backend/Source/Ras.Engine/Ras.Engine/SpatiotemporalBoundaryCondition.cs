using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Geospatial;
using Utility;

namespace Ras.Engine;

public class SpatiotemporalBoundaryCondition
{
	public enum TimeConvention
	{
		PeriodAverage,
		Instantaneous
	}

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void CachedScalarForTimestepDelegate(nint buffer, int sourceTimeIdx);

	public readonly TimeConvention InputConvention;

	public readonly TimeConvention OutputConvention;

	public JaggedWeights<float> InterpWeights;

	public double[] SourceTimesteps;

	public Func<int, float[]> GetScalarForTimestep;

	public Func<int, Vector2[]> GetVectorForTimestep;

	private int _sourceCount = -1;

	private readonly int _cacheSize = 2;

	public float NoDataValue;

	private const float _noData = -9999f;

	private const float _eps = 0.0001f;

	private readonly List<Tuple<int, float[]>> _scalarCache;

	private readonly List<Tuple<int, Vector2[]>> _vectorCache;

	public CellWaterSourceId WaterSourceId { get; internal set; } = CellWaterSourceId.Invalid;

	public CachedScalarForTimestepDelegate CachedScalarForTimestepPtr { get; private set; }

	public SpatiotemporalBoundaryCondition(double[] sourceTimes, Func<int, float[]> getScalarData, float scaleFactor, JaggedWeights<float> scalarWeights, TimeConvention outputConvention = TimeConvention.PeriodAverage, float noDataValue = 0f)
	{
		InterpWeights = scalarWeights;
		InterpWeights.ScaleElements(scaleFactor);
		if (InputConvention != TimeConvention.PeriodAverage)
		{
			throw new NotImplementedException();
		}
		OutputConvention = outputConvention;
		NoDataValue = noDataValue;
		SourceTimesteps = sourceTimes;
		GetScalarForTimestep = getScalarData;
		_scalarCache = new List<Tuple<int, float[]>>();
		CachedScalarForTimestepPtr = CachedScalarForTimestep;
	}

	public SpatiotemporalBoundaryCondition(double[] sourceTimes, Func<int, Vector2[]> getVectorData, float scaleFactor, JaggedWeights<float> vectorWeights, float noDataValue = 0f)
	{
		InterpWeights = vectorWeights;
		InterpWeights.ScaleElements(scaleFactor);
		InputConvention = TimeConvention.Instantaneous;
		OutputConvention = TimeConvention.Instantaneous;
		SourceTimesteps = sourceTimes;
		GetVectorForTimestep = getVectorData;
		NoDataValue = noDataValue;
		_vectorCache = new List<Tuple<int, Vector2[]>>();
	}

	public void UpdateScalar(double[] elemScalar, double time)
	{
		int timeToIndex = TimeToIndex(time);
		int timeToIndexOffset = timeToIndex + 1;
		int sourceTimestepLength = SourceTimesteps.Length;
		if (timeToIndexOffset >= sourceTimestepLength)
		{
			float[] cachedScalarForTimestep = CachedScalarForTimestep(timeToIndex);
			for (int i = 0; i < cachedScalarForTimestep.Length; i++)
			{
				elemScalar[i] = cachedScalarForTimestep[i];
			}
			return;
		}
		double sourceTimestep = SourceTimesteps[timeToIndex];
		double sourceTimestep2 = SourceTimesteps[timeToIndexOffset];
		double submergenceRatio = (time - sourceTimestep) / (sourceTimestep2 - sourceTimestep);
		double computed = 1.0 - submergenceRatio;
		float[] cachedScalarForTimestepArray = CachedScalarForTimestep(timeToIndex);
		float[] cachedScalarForTimestep2 = CachedScalarForTimestep(timeToIndexOffset);
		for (int j = 0; j < cachedScalarForTimestep2.Length; j++)
		{
			elemScalar[j] = submergenceRatio * (double)cachedScalarForTimestep2[j] + computed * (double)cachedScalarForTimestepArray[j];
		}
	}

	public void UpdateScalar(double[] elemScalar, double time, double timestep)
	{
		int timeToIndex = TimeToIndex(time);
		int timeToIndex2 = TimeToIndex(time + timestep, toLeft: true);
		float[] cachedScalarForTimestep = CachedScalarForTimestep(TimeToIndex(time));
		if (timeToIndex == timeToIndex2)
		{
			for (int i = 0; i < cachedScalarForTimestep.Length; i++)
			{
				elemScalar[i] = cachedScalarForTimestep[i];
			}
			return;
		}
		double timeToIndex2TimeTerm = (SourceTimesteps[timeToIndex + 1] - time) / timestep;
		for (int j = 0; j < cachedScalarForTimestep.Length; j++)
		{
			elemScalar[j] = timeToIndex2TimeTerm * (double)cachedScalarForTimestep[j];
		}
		for (int k = timeToIndex + 1; k <= timeToIndex2; k++)
		{
			float[] cachedScalarForTimestepArray = CachedScalarForTimestep(k);
			timeToIndex2TimeTerm = ((k >= timeToIndex2) ? ((time + timestep - SourceTimesteps[k]) / timestep) : ((SourceTimesteps[k + 1] - SourceTimesteps[k]) / timestep));
			for (int l = 0; l < cachedScalarForTimestepArray.Length; l++)
			{
				elemScalar[l] += timeToIndex2TimeTerm * (double)cachedScalarForTimestepArray[l];
			}
		}
	}

	public void UpdateVector(Vector2[] elemVector, double time)
	{
		int timeToIndex = TimeToIndex(time);
		int timeToIndexOffset = timeToIndex + 1;
		int sourceTimestepLength = SourceTimesteps.Length;
		if (timeToIndexOffset < sourceTimestepLength)
		{
			double sourceTimestep = SourceTimesteps[timeToIndex];
			double sourceTimestep2 = SourceTimesteps[timeToIndexOffset];
			double submergenceRatio = (time - sourceTimestep) / (sourceTimestep2 - sourceTimestep);
			double computed = 1.0 - submergenceRatio;
			Vector2[] cachedVectorForTimestep = CachedVectorForTimestep(timeToIndex);
			Vector2[] cachedVectorForTimestepArray = CachedVectorForTimestep(timeToIndexOffset);
			float submergenceRatio2 = (float)submergenceRatio;
			float computed2 = (float)computed;
			for (int i = 0; i < cachedVectorForTimestepArray.Length; i++)
			{
				elemVector[i] = submergenceRatio2 * cachedVectorForTimestepArray[i] + computed2 * cachedVectorForTimestep[i];
			}
		}
	}

	private float[] CachedScalarForTimestep(int sourceTimeIdx)
	{
		float[] floatValues = null;
		if (_sourceCount < 0)
		{
			float[] scalarForTimestep = GetScalarForTimestep(sourceTimeIdx);
			ReplaceScalarNoDataValues(scalarForTimestep);
			_sourceCount = InterpWeights.Count;
			floatValues = new float[_sourceCount];
			InterpWeights.ApplyWeights(scalarForTimestep, floatValues);
			_scalarCache.Add(new Tuple<int, float[]>(sourceTimeIdx, floatValues));
			return floatValues;
		}
		foreach (Tuple<int, float[]> scalarCache in _scalarCache)
		{
			if (scalarCache.Item1 == sourceTimeIdx)
			{
				return scalarCache.Item2;
			}
		}
		if (_scalarCache.Count >= _cacheSize)
		{
			_scalarCache.RemoveRange(_cacheSize, _scalarCache.Count - _cacheSize);
		}
		float[] scalarForTimestepArray = GetScalarForTimestep(sourceTimeIdx);
		ReplaceScalarNoDataValues(scalarForTimestepArray);
		_sourceCount = InterpWeights.Count;
		floatValues = new float[_sourceCount];
		InterpWeights.ApplyWeights(scalarForTimestepArray, floatValues);
		_scalarCache.Insert(0, new Tuple<int, float[]>(sourceTimeIdx, floatValues));
		return floatValues;
	}

	private void ReplaceScalarNoDataValues(float[] data)
	{
		for (int i = 0; i < data.Length; i++)
		{
			if (MathF.Abs(data[i] - -9999f) <= 0.0001f)
			{
				data[i] = NoDataValue;
			}
		}
	}

	private void CachedScalarForTimestep(nint buffer, int sourceTimeIdx)
	{
		float[] cachedScalarForTimestep = CachedScalarForTimestep(sourceTimeIdx);
		Marshal.Copy(cachedScalarForTimestep, 0, buffer, cachedScalarForTimestep.Length);
	}

	private Vector2[] CachedVectorForTimestep(int sourceTimeIdx)
	{
		Vector2[] vector2s = null;
		if (_sourceCount < 0)
		{
			Vector2[] vectorForTimestep = GetVectorForTimestep(sourceTimeIdx);
			ReplaceVectorNoDataValues(vectorForTimestep);
			_sourceCount = InterpWeights.Count;
			vector2s = new Vector2[_sourceCount];
			InterpWeights.ApplyWeights(vectorForTimestep, vector2s);
			_vectorCache.Add(new Tuple<int, Vector2[]>(sourceTimeIdx, vector2s));
			return vector2s;
		}
		foreach (Tuple<int, Vector2[]> vectorCache in _vectorCache)
		{
			if (vectorCache.Item1 == sourceTimeIdx)
			{
				return vectorCache.Item2;
			}
		}
		if (_vectorCache.Count >= _cacheSize)
		{
			_vectorCache.RemoveRange(_cacheSize, _vectorCache.Count - _cacheSize);
		}
		Vector2[] vectorForTimestepArray = GetVectorForTimestep(sourceTimeIdx);
		ReplaceVectorNoDataValues(vectorForTimestepArray);
		_sourceCount = InterpWeights.Count;
		vector2s = new Vector2[_sourceCount];
		InterpWeights.ApplyWeights(vectorForTimestepArray, vector2s);
		_vectorCache.Insert(0, new Tuple<int, Vector2[]>(sourceTimeIdx, vector2s));
		return vector2s;
	}

	private void ReplaceVectorNoDataValues(Vector2[] data)
	{
		for (int i = 0; i < data.Length; i++)
		{
			if (MathF.Abs(data[i].X - -9999f) <= 0.0001f)
			{
				data[i].X = NoDataValue;
			}
			if (MathF.Abs(data[i].Y - -9999f) <= 0.0001f)
			{
				data[i].Y = NoDataValue;
			}
		}
	}

	private int TimeToIndex(double time, bool toLeft = false)
	{
		int binarySearch = MathHelpers.BinarySearch((IReadOnlyList<double>)SourceTimesteps, time);
		if (binarySearch < 0)
		{
			binarySearch = ~binarySearch - 1;
		}
		else if (toLeft && binarySearch > 0)
		{
			binarySearch--;
		}
		return binarySearch;
	}
}
