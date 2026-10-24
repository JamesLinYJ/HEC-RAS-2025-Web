using Geospatial.IO.ORM;
using H5Assist;
using Microsoft.Extensions.Logging;
using Utility.Hashing;
using Utility.Logging;

namespace Ras.Layers;

public abstract class LayerBase : IDataHash
{
	protected const int HashNDV = int.MinValue;

	private static readonly ILogger _logger = LogHelper.GetLogger("LayerBase");

	protected readonly string _attrDatasetName = "Attributes";

	protected int _metadataLoadHash = int.MinValue;

	protected abstract string HDF5Group { get; }

	protected LayerBase()
	{
		_metadataLoadHash = GetMetadataHash();
	}

	public virtual bool NeedsSave()
	{
		return _metadataLoadHash != GetMetadataHash();
	}

	public void LoadMetadata(H5Reader hr)
	{
		LoadMetadataInternal(hr);
		_metadataLoadHash = GetMetadataHash();
	}

	protected virtual void LoadMetadataInternal(H5Reader hr)
	{
		GeospatialH5Metadata.Read(this, hr, HDF5Group);
	}

	public virtual int GetDataHash()
	{
		return GetMetadataHash();
	}

	protected int GetMetadataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}

	protected void UpdateInternalMetadataHash()
	{
		_metadataLoadHash = GetMetadataHash();
	}
}
