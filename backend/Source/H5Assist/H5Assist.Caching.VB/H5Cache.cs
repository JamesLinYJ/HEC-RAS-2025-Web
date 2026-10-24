using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using H5Assist.VB;
using Microsoft.Extensions.Logging;
using Utility.Extensions;
using Utility.Logging;

namespace H5Assist.Caching.VB;

public abstract class H5Cache<T, ChunkType> : IH5Cache where T : struct where ChunkType : Chunk<T>
{
	protected readonly ILogger _logger = LogHelper.GetLogger("H5Cache");

	public const float CACHE_EVICTION_FRAC = 0.5f;

	protected readonly int SizeOfT = Marshal.SizeOf<T>(default);

	protected string _filename;

	protected H5Assist.VB.H5Reader _h5r;

	protected bool _openFileOnDemand;

	protected DateTime _filedate;

	protected List<DatasetParameters> _datasetParameters;

	protected List<ConcurrentDictionary<int, ChunkType>> Cache;

	protected List<List<object>> CacheChunkLocks;

	public float CacheSizePerDataset { get; set; } = 25f;

	public string Filename => _filename;

	public List<string> Datasets
	{
		get
		{
			lock (_datasetParameters)
			{
				return _datasetParameters.Select((DatasetParameters param) => param.Name).ToList();
			}
		}
	}

	public List<long[]> DatasetChunkDimensions
	{
		get
		{
			lock (_datasetParameters)
			{
				return _datasetParameters.Select((DatasetParameters param) => param.ChunkDimensions).ToList();
			}
		}
	}

	public List<long[]> DatasetDimensions
	{
		get
		{
			lock (_datasetParameters)
			{
				return _datasetParameters.Select((DatasetParameters param) => param.Dimensions).ToList();
			}
		}
	}

	protected H5Cache(H5Assist.VB.H5Reader h5r)
	{
		_h5r = h5r;
		_filename = h5r.Filename;
		_filedate = File.GetLastWriteTime(_filename);
		_openFileOnDemand = false;
		_datasetParameters = new List<DatasetParameters>();
		Cache = new List<ConcurrentDictionary<int, ChunkType>>();
		CacheChunkLocks = new List<List<object>>();
	}

	protected H5Cache(string filename)
	{
		_filename = filename;
		_filedate = File.GetLastWriteTime(_filename);
		_openFileOnDemand = true;
		_datasetParameters = new List<DatasetParameters>();
		Cache = new List<ConcurrentDictionary<int, ChunkType>>();
		CacheChunkLocks = new List<List<object>>();
	}

	protected abstract int GetNumDimensionsExpected();

	protected abstract int ComputeBytesPerChunk(long[] chunkDims);

	protected int GetNumChunksAllowed(int dsetIndex)
	{
		if (_datasetParameters[dsetIndex].Pinned)
		{
			return int.MaxValue;
		}
		return Math.Max(1, Convert.ToInt32(Math.Ceiling(CacheSizePerDataset * 1024f * 1024f / (float)_datasetParameters[dsetIndex].BytesPerChunk)));
	}

	public abstract void EnsureDataset(string dset, bool pinned = false);

	protected void AddFileInternal(H5Assist.VB.H5Reader hr, string dset, bool chunkedByRows, bool pinned = false)
	{
		if (!hr.ExistsAsDataset(dset))
		{
			throw new ArgumentException(dset + " does not exist in " + hr.Filename.InDoubleQuotes());
		}
		DatasetParameters datasetParameters = new DatasetParameters();
		datasetParameters.Name = dset;
		datasetParameters.ChunkDimensions = hr.GetChunkSize(dset);
		datasetParameters.Dimensions = hr.GetDatasetDimensions(dset);
		if (!ValidateDatasetDimensions(datasetParameters))
		{
			throw new Exception("Dataset " + dset + " had incorrect dimensions.");
		}
		datasetParameters.BytesPerChunk = ComputeBytesPerChunk(datasetParameters.ChunkDimensions);
		datasetParameters.Pinned = pinned;
		lock (_datasetParameters)
		{
			_datasetParameters.Add(datasetParameters);
		}
		Cache.Add(new ConcurrentDictionary<int, ChunkType>());
		CacheChunkLocks.Add(new List<object>());
		if (chunkedByRows)
		{
			for (int i = 0; (double)i < Math.Ceiling((double)datasetParameters.Dimensions[0] / (double)datasetParameters.ChunkDimensions[0]); i++)
			{
				CacheChunkLocks[CacheChunkLocks.Count - 1].Add(new object());
			}
		}
		else
		{
			for (int j = 0; (double)j < Math.Ceiling((double)datasetParameters.Dimensions[1] / (double)datasetParameters.ChunkDimensions[1]); j++)
			{
				CacheChunkLocks[CacheChunkLocks.Count - 1].Add(new object());
			}
		}
	}

	protected bool ValidateDatasetDimensions(DatasetParameters param)
	{
		if (param.Dimensions.Length != GetNumDimensionsExpected())
		{
			return false;
		}
		if (param.ChunkDimensions == null || param.ChunkDimensions.Length != GetNumDimensionsExpected() || param.ChunkDimensions.Any((long v) => v == 0))
		{
			param.ChunkDimensions = param.Dimensions;
		}
		return true;
	}

	public virtual int GetIndex(string dataset)
	{
		for (int i = 0; i < _datasetParameters.Count; i++)
		{
			if (_datasetParameters[i].Name == dataset)
			{
				return i;
			}
		}
		throw new ArgumentException("Dataset " + dataset + " was not found.");
	}

	protected void CleanIfNeeded(int dsetIndex)
	{
		int count = Cache[dsetIndex].Count;
		int numChunksAllowed = GetNumChunksAllowed(dsetIndex);
		bool tryEnter = false;
		try
		{
			if (!(tryEnter = Monitor.TryEnter(Cache[dsetIndex]) && count > numChunksAllowed) || count <= numChunksAllowed)
			{
				return;
			}
			_datasetParameters[dsetIndex].EvictionActive = true;
			if (_datasetParameters[dsetIndex].ActiveThreadCount != 0)
			{
				return;
			}
			int countTerm = Convert.ToInt32((float)count * 0.5f);
			countTerm = Math.Min(count, Math.Max(1, countTerm));
			foreach (int ordinal in from kvp in (from s in Cache[dsetIndex].ToArray()
					orderby s.Value.GetLastAccessedDate()
					select s).Take(countTerm)
				select kvp.Key)
			{
				lock (CacheChunkLocks[dsetIndex][ordinal])
				{
					Cache[dsetIndex].TryRemove(ordinal, out var _);
				}
			}
			_datasetParameters[dsetIndex].EvictionActive = false;
		}
		finally
		{
			if (tryEnter)
			{
				Monitor.Exit(Cache[dsetIndex]);
			}
		}
	}

	public void VerifyFileDate()
	{
		DateTime lastWriteTime = File.GetLastWriteTime(_filename);
		if (lastWriteTime != _filedate)
		{
			Clear();
			_filedate = lastWriteTime;
		}
	}

	public void Clear()
	{
		Cache.Clear();
		CacheChunkLocks.Clear();
		lock (_datasetParameters)
		{
			_datasetParameters.Clear();
		}
		_filedate = File.GetLastWriteTime(_filename);
	}
}
