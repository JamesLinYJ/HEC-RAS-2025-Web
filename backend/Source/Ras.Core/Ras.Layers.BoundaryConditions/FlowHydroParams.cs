using Utility.Hashing;
using Utility.ORM;

namespace Ras.Layers.BoundaryConditions;

public class FlowHydroParams : IDataHash
{
	[HashContributor]
	[Metadata("InitialWS")]
	public float InitialWS { get; set; }

	[HashContributor]
	[Metadata("QMin")]
	public float QMin { get; set; }

	[HashContributor]
	[Metadata("QMult")]
	public float QMult { get; set; } = 1f;

	[HashContributor]
	[Metadata("QSlope")]
	public float QSlope { get; set; } = 0.001f;

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}
}
