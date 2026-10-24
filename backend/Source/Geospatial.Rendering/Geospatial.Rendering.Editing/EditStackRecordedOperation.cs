using System.Diagnostics;

namespace Geospatial.Rendering.Editing;

[DebuggerDisplay("{DebugString(),nq}")]
public struct EditStackRecordedOperation
{
	public EditStackOperation Operation { get; init; }

	public EditExecutionMode Mode { get; init; }

	public EditStackRecordedOperation(EditStackOperation operation, EditExecutionMode mode)
	{
		Operation = operation;
		Mode = mode;
	}

	public string DebugString()
	{
		if (!Operation.IsSilent)
		{
			return $"[{Mode}] {Operation.Operation}";
		}
		return $"[{Mode}] {Operation.Operation} (Silent)";
	}
}
