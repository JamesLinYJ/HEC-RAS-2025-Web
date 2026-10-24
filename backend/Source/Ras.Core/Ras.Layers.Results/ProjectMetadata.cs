using Utility.ORM;

namespace Ras.Layers.Results;

public class ProjectMetadata
{
	[Metadata("Name")]
	public string Name { get; set; }

	[Metadata("Description")]
	public string Description { get; set; }

	[Metadata("Projection")]
	public string Projection { get; set; }

	[Metadata("Units")]
	public string Units { get; set; }
}
