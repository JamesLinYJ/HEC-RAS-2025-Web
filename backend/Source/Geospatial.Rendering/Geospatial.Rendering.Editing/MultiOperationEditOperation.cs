using System.Collections.Generic;
using System.Linq;

namespace Geospatial.Rendering.Editing;

public class MultiOperationEditOperation : EditOperation
{
	private IEnumerable<EditOperation> _operations;

	private bool _hasApplied;

	public override string Description => "Multi-Operation";

	public MultiOperationEditOperation(params EditOperation[] operations)
		: base(null)
	{
		_operations = operations;
	}

	public MultiOperationEditOperation(IEnumerable<EditOperation> operations)
		: base(null)
	{
		_operations = operations;
	}

	protected override bool ApplyInternal()
	{
		if (_operations == null)
		{
			return false;
		}
		foreach (EditOperation operation in _operations)
		{
			if (_hasApplied)
			{
				if (!operation.Redo())
				{
					return false;
				}
			}
			else if (!operation.Apply())
			{
				return false;
			}
		}
		_hasApplied = true;
		return true;
	}

	protected override bool UndoInternal()
	{
		if (_operations == null)
		{
			return false;
		}
		foreach (EditOperation reverse in _operations.Reverse())
		{
			if (!reverse.Undo())
			{
				return false;
			}
		}
		return true;
	}
}
