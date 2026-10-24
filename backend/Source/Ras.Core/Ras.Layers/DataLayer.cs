using Geospatial.IO.ORM;
using H5Assist;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Layers;

public abstract class DataLayer : IDataHash
{
	public string HDFGroupName { get; protected set; }

	[Metadata("Description")]
	public string Description { get; set; }

	public void Save(H5Writer h5w)
	{
		GeospatialH5Metadata.Write(this, h5w, HDFGroupName);
	}

	public void Load(H5Reader hr)
	{
		GeospatialH5Metadata.Read(this, hr, HDFGroupName);
	}

	public virtual int GetDataHash()
	{
		return GetMetadataHash();
	}

	private int GetMetadataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}
}
