using System;
using System.Diagnostics;
using Geospatial.IO.ORM;
using Geospatial.IO.Vectors;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using H5Assist;
using Microsoft.Extensions.Logging;
using Utility.Hashing;
using Utility.Logging;

namespace Ras.Layers;

[DebuggerDisplay("{Topology}")]
public class TopologyLayer : IDataHash
{
	private static readonly ILogger _logger = LogHelper.GetLogger("TopologyLayer");

	public static readonly string HDF5GroupName = "/Geometry/Mesh Topology";

	private const int HashNDV = int.MinValue;

	private int _featureLoadHash = int.MinValue;

	private int _metadataLoadHash = int.MinValue;

	private Topology _topo;

	private readonly Func<TopologyLayer, string> _getParentNameForLogging;

	public Topology Topology
	{
		get
		{
			return _topo;
		}
		set
		{
			if (value != null)
			{
				EnsureMetadataColumns(value);
				LoadFailed = false;
			}
			_topo = value;
		}
	}

	public bool IsDataLoaded { get; private set; }

	public bool LoadFailed { get; private set; }

	public Extent Extent => Topology.Extent;

	internal TopologyLayer(Func<TopologyLayer, string> getParentNameForLogging = null)
	{
		_getParentNameForLogging = getParentNameForLogging;
		Topology = new Topology();
	}

	public virtual void EnsureMetadataColumns(Topology topo)
	{
	}

	public void LoadMetadata(H5Reader hr)
	{
		GeospatialH5Metadata.Read(this, hr, HDF5GroupName);
		_metadataLoadHash = GetMetadataHash();
		LoadTopo(hr);
		if (!LoadFailed)
		{
			IsDataLoaded = true;
			_featureLoadHash = GetFeatureHash();
		}
	}

	private void LoadTopo(H5Reader hr)
	{
		try
		{
			Topology = TopologyReader.Read(hr, HDF5GroupName);
			EnsureMetadataColumns(Topology);
			if (Topology != null)
			{
				try
				{
					Topology.Complete();
				}
				catch
				{
				}
			}
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Unable to load Topology for '{GeometryName}'; keeping existing file data and skipping save until a successful load.", _getParentNameForLogging?.Invoke(this) ?? "unknown");
			Topology = new Topology();
			LoadFailed = true;
		}
		EnsureMetadataColumns(Topology);
	}

	public void EvictMemory()
	{
		Topology = null;
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
		int result = 0;
		if (IsDataLoaded)
		{
			return Topology.GetDataHash();
		}
		return result;
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
		if (Topology != null && !LoadFailed)
		{
			TopologyReader.Write(hw, HDF5GroupName, Topology);
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
		throw new NotSupportedException("TODO: topology isn't really a feature layer, doesnt have features. zach fix Thx");
	}
}
