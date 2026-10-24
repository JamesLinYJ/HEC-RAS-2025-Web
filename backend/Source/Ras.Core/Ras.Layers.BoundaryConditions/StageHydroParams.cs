using Utility.Hashing;
using Utility.ORM;

namespace Ras.Layers.BoundaryConditions;

public class StageHydroParams : IDataHash
{
	[HashContributor]
	[Metadata("Use Initial Stage")]
	public bool UseInitialStage { get; set; }

	[HashContributor]
	[Metadata("Tailwater Check")]
	public bool TailwaterCheck { get; set; }

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}
}
