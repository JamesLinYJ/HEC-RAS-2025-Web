using System;
using System.Diagnostics;
using Geospatial.IO.ORM;
using Geospatial.IO.Vectors;
using Geospatial.Vectors;
using Geospatial.Vectors.Graph;
using H5Assist;
using Microsoft.Extensions.Logging;
using Utility.Hashing;
using Utility.Logging;

namespace Ras.Layers;

[DebuggerDisplay("{Graph}")]
public class GraphLayer : IDataHash
{
	private static readonly ILogger _logger = LogHelper.GetLogger("GraphLayer");

	private const int HashNDV = int.MinValue;

	private int _featureLoadHash = int.MinValue;

	private int _metadataLoadHash = int.MinValue;

	private readonly string _hdf5GroupName;

	private Graph _graph;

	private readonly Func<GraphLayer, string> _getParentNameForLogging;

	public Graph Graph
	{
		get
		{
			return _graph;
		}
		set
		{
			if (value != null)
			{
				EnsureMetadataColumns(value);
				LoadFailed = false;
			}
			_graph = value;
		}
	}

	public bool IsDataLoaded { get; private set; }

	public bool LoadFailed { get; private set; }

	public Extent Extent => Graph.Extent;

	internal GraphLayer(string hdf5GroupName, Func<GraphLayer, string> getParentNameForLogging = null)
	{
		_hdf5GroupName = hdf5GroupName;
		_getParentNameForLogging = getParentNameForLogging;
		Graph = new Graph();
	}

	public virtual void EnsureMetadataColumns(Graph graph)
	{
	}

	public void LoadMetadata(H5Reader hr)
	{
		GeospatialH5Metadata.Read(this, hr, _hdf5GroupName);
		_metadataLoadHash = GetMetadataHash();
		LoadGraph(hr);
		if (!LoadFailed)
		{
			IsDataLoaded = true;
			_featureLoadHash = GetFeatureHash();
		}
	}

	private void LoadGraph(H5Reader hr)
	{
		try
		{
			Graph = GraphReader.Read(hr, _hdf5GroupName) ?? new Graph();
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Unable to load Graph for '{GeometryName}'; keeping existing file data and skipping save until a successful load.", _getParentNameForLogging?.Invoke(this) ?? "unknown");
			Graph = new Graph();
			LoadFailed = true;
		}
		EnsureMetadataColumns(Graph);
	}

	public void EvictMemory()
	{
		Graph = null;
		IsDataLoaded = false;
	}

	public int GetDataHash()
	{
		int metadataHash = GetMetadataHash();
		if (IsDataLoaded)
		{
			metadataHash = HashCode.Combine(metadataHash, GetFeatureHash());
		}
		return metadataHash;
	}

	public int GetMetadataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}

	public bool NeedsSave()
	{
		if (LoadFailed)
		{
			return false;
		}
		if (_metadataLoadHash != GetMetadataHash())
		{
			return true;
		}
		if (IsDataLoaded && _featureLoadHash != GetFeatureHash())
		{
			return true;
		}
		return false;
	}

	private int GetFeatureHash()
	{
		if (!IsDataLoaded)
		{
			return 0;
		}
		return Graph.GetDataHash();
	}

	public int PeekCount(H5Reader hr)
	{
		return 1;
	}

	public void Save(H5Writer hw, bool forceSaveIfFeaturesUnloaded = true)
	{
		Save(hw, forceSaveIfFeaturesUnloaded, refreshLoadHashes: true);
	}

	public void Save(H5Writer hw, bool forceSaveIfFeaturesUnloaded, bool refreshLoadHashes)
	{
		if (Graph != null && !LoadFailed)
		{
			GraphReader.Write(hw, _hdf5GroupName, Graph);
			IsDataLoaded = true;
			if (refreshLoadHashes)
			{
				_metadataLoadHash = GetMetadataHash();
				_featureLoadHash = GetFeatureHash();
			}
		}
	}

	public void RemoveAt(int index)
	{
		throw new NotSupportedException("Graph isn't a feature layer; it has no removable features.");
	}
}
