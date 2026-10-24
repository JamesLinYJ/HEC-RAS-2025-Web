using System;
using System.Collections;
using System.Collections.Generic;
using Geospatial;
using Geospatial.IO.ORM;
using H5Assist;
using Microsoft.Extensions.Logging;
using Utility.Hashing;
using Utility.Logging;

namespace Ras.Layers;

public abstract class TableLayerBase<T> : LayerBase, ISpatialVersion, IReadOnlyList<T>, IEnumerable<T>, IEnumerable, IReadOnlyCollection<T> where T : ITableRow, new()
{
	private static readonly ILogger _logger = LogHelper.GetLogger("TableLayerBase");

	protected readonly Func<H5Reader, List<T>> _readAction;

	private CollectionVersionTracker _tracker;

	protected List<T> _loadedFeatures;

	private int _featureLoadHash = int.MinValue;

	private int _peekCount = -1;

	private Freezer _freezer;

	public virtual long VersionXY
	{
		get
		{
			if (_freezer.IsFrozen)
			{
				return _freezer.FrozenVersionXY;
			}
			if (!IsDataLoaded)
			{
				return -1L;
			}
			return _tracker.GetVersion(_loadedFeatures);
		}
	}

	public bool IsDataLoaded => _loadedFeatures != null;

	public abstract bool IsReadOnly { get; }

	public int Count => CheckLoadData().Count;

	public T this[int index] => CheckLoadData()[index];

	public IReadOnlyList<T> Features => CheckLoadData();

	public TableLayerBase()
	{
		_readAction = (H5Reader h5r) => FeatureIO.Read<T>(h5r, HDF5Group, _attrDatasetName);
	}

	public override bool NeedsSave()
	{
		if (base.NeedsSave())
		{
			return true;
		}
		if (IsDataLoaded && _featureLoadHash != GetFeatureHash())
		{
			return true;
		}
		return false;
	}

	private List<T> CheckLoadData()
	{
		return _loadedFeatures ?? LoadFeatures();
	}

	private List<T> LoadFeatures()
	{
		List<T> loadFeaturesInternal = LoadFeaturesInternal();
		PostLoadFeatures(loadFeaturesInternal);
		_featureLoadHash = GetFeatureHash();
		return loadFeaturesInternal;
	}

	protected abstract List<T> LoadFeaturesInternal();

	protected virtual void PostLoadFeatures(List<T> features)
	{
	}

	public void Move(int oldIndex, int newIndex)
	{
		T oldIndexTerm = this[oldIndex];
		RemoveAt(oldIndex);
		Insert(newIndex, oldIndexTerm);
	}

	public int PeekCount()
	{
		if (IsDataLoaded)
		{
			return Count;
		}
		if (_peekCount >= 0)
		{
			return _peekCount;
		}
		_peekCount = PeekCountInternal();
		return _peekCount;
	}

	protected abstract int PeekCountInternal();

	public int PeekCount(H5Reader h5r)
	{
		if (IsDataLoaded)
		{
			return Count;
		}
		if (_peekCount >= 0)
		{
			return _peekCount;
		}
		string hdF5GroupAttrDatasetNameTerm = HDF5Group + "/" + _attrDatasetName;
		if (h5r.ExistsAsDataset(hdF5GroupAttrDatasetNameTerm))
		{
			long[] datasetDimensions = h5r.GetDatasetDimensions(hdF5GroupAttrDatasetNameTerm);
			_peekCount = (int)datasetDimensions[0];
		}
		else
		{
			_peekCount = 0;
		}
		return _peekCount;
	}

	protected virtual void PreSaveInternal(H5Writer hw)
	{
	}

	protected virtual void PostSaveInternal(H5Writer hw)
	{
	}

	protected virtual void SaveFeaturesInternal(H5Writer hw, string hdfGroup, List<T> features)
	{
		if (features == null || features.Count == 0)
		{
			_logger.LogDebug("Saving 0 features - cleaning group {GroupName}", hdfGroup);
			hw.CleanGroup(hdfGroup, deleteAttrs: false);
		}
		else
		{
			FeatureIO.Write(hw, HDF5Group, features);
		}
	}

	public void Save(H5Writer hw, bool forceSaveIfFeaturesUnloaded = true)
	{
		Save(hw, forceSaveIfFeaturesUnloaded, refreshLoadHashes: true);
	}

	public void Save(H5Writer hw, bool forceSaveIfFeaturesUnloaded, bool refreshLoadHashes)
	{
		PreSaveInternal(hw);
		GeospatialH5Metadata.Write(this, hw, HDF5Group);
		if (IsDataLoaded | forceSaveIfFeaturesUnloaded)
		{
			List<T> features = CheckLoadData();
			SaveFeaturesInternal(hw, HDF5Group, features);
		}
		PostSaveInternal(hw);
		if (refreshLoadHashes)
		{
			UpdateInternalMetadataHash();
			_featureLoadHash = GetFeatureHash();
		}
	}

	public override int GetDataHash()
	{
		int dataHash = base.GetDataHash();
		if (IsDataLoaded && Count > 0)
		{
			dataHash = StableHash.Combine(dataHash, GetFeatureHash());
		}
		return dataHash;
	}

	protected virtual int GetFeatureHash()
	{
		int combine = 0;
		if (IsDataLoaded)
		{
			for (int i = 0; i < Count; i++)
			{
				combine = StableHash.Combine(combine, Features[i].GetDataHash());
			}
		}
		return combine;
	}

	public void FreezeOne()
	{
		_freezer.FreezeOne(this);
	}

	public void ThawOne()
	{
		_freezer.ThawOne();
	}

	public void EvictMemory()
	{
		_loadedFeatures = null;
		_peekCount = -1;
	}

	public void AddRange(IEnumerable<T> items)
	{
		CheckLoadData().AddRange(items);
	}

	public virtual void Add(T localValue)
	{
		CheckLoadData().Add(localValue);
	}

	public virtual void Insert(int index, T localValue)
	{
		CheckLoadData().Insert(index, localValue);
	}

	public virtual void RemoveAt(int index)
	{
		_tracker.OnDeleted(Features[index]);
		CheckLoadData().RemoveAt(index);
	}

	public void Clear()
	{
		_tracker.OnDeleted<T>(Features);
		_loadedFeatures?.Clear();
	}

	public IEnumerator<T> GetEnumerator()
	{
		return CheckLoadData().GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return CheckLoadData().GetEnumerator();
	}
}
