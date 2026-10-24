using System.Diagnostics;

namespace Geospatial.Rendering.Editing;

[DebuggerDisplay("{DebugString(),nq}")]
public struct EditStackOperation
{
	public EditOperation Operation;

	public bool IsSilent;

	internal EditStackOperation(EditOperation operation, bool isSilent)
	{
		Operation = operation;
		IsSilent = isSilent;
	}

	public string DebugString()
	{
		return Operation.ToString() + " " + (IsSilent ? "(Silent)" : string.Empty);
	}
}
