using Geospatial.Vectors;
using Utility.Logging;

namespace Ras.Engine.Structures;

public static class StructureMessageCodes
{
	public static readonly MessageClass StructEndPtError = new MessageClass("Structures_StructEndPtError", MessageSeverity.Error, "Structure initialization failed: Could not find mesh cell for structure {name}, endpoint ({pt.X}, {pt.Y})");

	public static readonly MessageClass StructSingleCellError = new MessageClass("Structures_StructSingleCellError", MessageSeverity.Error, "Structure initialization failed: Structure {name} is contained within a single computational cell");

	public static readonly MessageClass StructOnBoundaryError = new MessageClass("Structures_StructOnBoundaryError", MessageSeverity.Error, "Structure initialization failed: Structure {name} is located on global mesh boundary");

	public static Message GetStructEndPtError(string name, Point pt)
	{
		return StructEndPtError.Create(name, pt);
	}

	public static Message GetStructSingleCellError(string name)
	{
		return StructSingleCellError.Create(name);
	}

	public static Message GetStructOnBoundaryError(string name)
	{
		return StructOnBoundaryError.Create(name);
	}
}
