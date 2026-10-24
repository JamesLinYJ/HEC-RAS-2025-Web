using System.Diagnostics;
using Geospatial;
using Geospatial.Vectors;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Hydraulics.Classic;

[DebuggerDisplay("{Name}")]
public class GateGroup : IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent
{
	public long VersionXY => 1L;

	[HashContributor]
	[ComplexMetadata]
	public StructureIdentifier StructureID { get; set; } = new StructureIdentifier();

	public Extent Extent => Extent.NullExtent;

	[HashContributor]
	[Metadata("Struct ID")]
	public int StructueIndex { get; }

	[HashContributor]
	[Metadata("Name")]
	public string Name { get; set; }

	[HashContributor]
	[Metadata("Width")]
	public float Width { get; set; }

	[HashContributor]
	[Metadata("Height")]
	public float Height { get; set; }

	[HashContributor]
	[Metadata("Invert")]
	public float Invert { get; set; }

	[HashContributor]
	[Metadata("Method")]
	public GateMethod Method { get; set; }

	[HashContributor]
	[Metadata("Sluice Coef")]
	public float SluiceCoefficient { get; set; }

	[HashContributor]
	[Metadata("Radial Coef")]
	public float RadialCoefficient { get; set; }

	[HashContributor]
	[Metadata("Exp Trunion")]
	public float ExpTrunion { get; set; }

	[HashContributor]
	[Metadata("Exp Open")]
	public float ExpOpen { get; set; }

	[HashContributor]
	[Metadata("Exp Head")]
	public float ExpHead { get; set; }

	[HashContributor]
	[Metadata("Trunion Height")]
	public float TrunionHeight { get; set; }

	[HashContributor]
	[Metadata("Orifice Coef")]
	public float OrificeCoefficient { get; set; }

	[HashContributor]
	[Metadata("Head Ref")]
	public int HeadRef { get; set; }

	[HashContributor]
	[Metadata("Use Ogee")]
	public bool UseOgee { get; set; }

	[HashContributor]
	[Metadata("Use Sharp")]
	public bool UseSharp { get; set; }

	[HashContributor]
	[Metadata("Weir Coef")]
	public float WeirCoefficient { get; set; }

	[HashContributor]
	[Metadata("Design Head")]
	public float DesignHead { get; set; }

	[HashContributor]
	[Metadata("Use Rehbok")]
	public bool UseRehbok { get; set; }

	[HashContributor]
	[Metadata("Use KVC")]
	public bool UseKVC { get; set; }

	[HashContributor]
	[Metadata("KVC Method")]
	public int KVCMethod { get; set; }

	[HashContributor]
	[Metadata("User Curve Set")]
	public string UserCurveSet { get; set; }

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}
}
