using System;
using Geospatial;
using Geospatial.Vectors;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Layers.HydraulicLayers;

public class StructureProperties : IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent, ICloneable
{
	public long VersionXY => 1L;

	public Extent Extent => Extent.NullExtent;

	[HashContributor]
	[Metadata("Type")]
	public WeirTypes WeirType { get; set; }

	[HashContributor]
	[Metadata("Name")]
	public string Name { get; set; }

	[HashContributor]
	[Metadata("Weir Coefficient")]
	public float WeirCoefficient { get; set; }

	public bool IsValid()
	{
		return true;
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}

	public object Clone()
	{
		return new StructureProperties
		{
			Name = Name,
			WeirCoefficient = WeirCoefficient,
			WeirType = WeirType
		};
	}
}
