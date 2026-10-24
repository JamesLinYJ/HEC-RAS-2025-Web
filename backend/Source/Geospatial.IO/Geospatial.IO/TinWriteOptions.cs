using Utility.Memory;

namespace Geospatial.IO;

public class TinWriteOptions
{
	public Table AttributeTable { get; set; }

	public bool IncludeDeletedTriangles { get; set; }

	public bool IncludeIndices { get; set; } = true;

	public bool IncludeIsDeleted { get; set; } = true;

	public bool IncludeArea { get; set; } = true;

	public bool IncludePerimeterEdges { get; set; } = true;

	public bool IncludeVertexZ { get; set; } = true;
}
