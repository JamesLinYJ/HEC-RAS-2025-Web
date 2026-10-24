using Utility.ORM;

namespace Ras.Layers.BoundaryConditions;

public class RatingCurveDefinitionEntity
{
	[Metadata("Input Variable")]
	public string InputVariable { get; set; }

	[Metadata("Start (X)")]
	public int StartX { get; set; }

	[Metadata("Start (Y)")]
	public int StartY { get; set; }

	[Metadata("Count")]
	public int RowCount { get; set; }
}
