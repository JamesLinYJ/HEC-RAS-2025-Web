using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Geospatial.Rendering.Editing;

public class EditStack
{
	private static readonly ILogger _logger = LogHelper.GetLogger("EditStack");

	private List<EditStackRecordedOperation> _editStackRecord = new List<EditStackRecordedOperation>();

	private Stack<EditStackOperation> _undoStack = new Stack<EditStackOperation>();

	private Stack<EditStackOperation> _redoStack = new Stack<EditStackOperation>();

	private Action _invalidateLayer;

	private Func<Func<bool>, bool> _executionWrapper;

	private int _version;

	private bool _isUndoing;

	public IReadOnlyList<EditStackRecordedOperation> EditStackRecord => _editStackRecord;

	public int UndoStackCount => _undoStack.Count;

	public int RedoStackCount => _redoStack.Count;

	public int Version => _version;

	public EditStack(Func<Func<bool>, bool> executionWrapper = null, Action invalidateLayer = null)
	{
		_invalidateLayer = invalidateLayer;
		_executionWrapper = executionWrapper;
	}

	public void Reset()
	{
		_undoStack.Clear();
		_redoStack.Clear();
	}

	public void ClearRedoStack()
	{
		_redoStack.Clear();
	}

	private bool ExecuteWithExecutionWrapper(Func<bool> action)
	{
		if (_executionWrapper == null)
		{
			return action();
		}
		return _executionWrapper(action);
	}

	public bool Execute(EditOperation editOperation, bool isSilent = false)
	{
		if (_isUndoing)
		{
			throw new InvalidOperationException("Cannot execute a new edit operation while mid-undo.");
		}
		_version++;
		EditStackOperation editStackOperation = new EditStackOperation(editOperation, isSilent);
		LogEditStackEvent(EditExecutionMode.Execute, editStackOperation);
		_undoStack.Push(editStackOperation);
		if (!ExecuteWithExecutionWrapper(editStackOperation.Operation.Apply))
		{
			_undoStack.Pop();
			_editStackRecord.RemoveAt(_editStackRecord.Count - 1);
			return false;
		}
		_invalidateLayer?.Invoke();
		_redoStack.Clear();
		return true;
	}

	public bool DeleteTopOfUndoStack()
	{
		_version++;
		if (_undoStack.Count == 0)
		{
			return false;
		}
		EditStackOperation op = _undoStack.Pop();
		LogEditStackEvent(EditExecutionMode.Debug_ForceDelete_UndoStack, op);
		return true;
	}

	public bool DeleteTopOfRedoStack()
	{
		_version++;
		if (_redoStack.Count == 0)
		{
			return false;
		}
		EditStackOperation op = _redoStack.Pop();
		LogEditStackEvent(EditExecutionMode.Debug_ForceDelete_RedoStack, op);
		return true;
	}

	public bool TryUndo()
	{
		IReadOnlyList<EditOperation> undoneOperations;
		return TryUndo(out undoneOperations);
	}

	public bool TryUndo(out IReadOnlyList<EditOperation> undoneOperations)
	{
		List<EditOperation> editOperations = (List<EditOperation>)(undoneOperations = new List<EditOperation>());
		_version++;
		if (_undoStack.Count == 0)
		{
			return false;
		}
		_isUndoing = true;
		EditStackOperation editStackOperation = _undoStack.Pop();
		LogEditStackEvent(EditExecutionMode.Undo, editStackOperation);
		editOperations.Add(editStackOperation.Operation);
		_redoStack.Push(editStackOperation);
		if (!ExecuteWithExecutionWrapper(editStackOperation.Operation.Undo))
		{
			_isUndoing = false;
			_redoStack.Pop();
			_undoStack.Push(editStackOperation);
			_editStackRecord.RemoveAt(_editStackRecord.Count - 1);
			return false;
		}
		_invalidateLayer?.Invoke();
		if (editStackOperation.IsSilent)
		{
			if (!TryUndo(out var undoneOperations2))
			{
				_isUndoing = false;
				return false;
			}
			editOperations.AddRange(undoneOperations2);
		}
		_isUndoing = false;
		return true;
	}

	public bool TryRedo()
	{
		IReadOnlyList<EditOperation> redoneOperations;
		return TryRedo(out redoneOperations);
	}

	public bool TryRedo(out IReadOnlyList<EditOperation> redoneOperations)
	{
		List<EditOperation> editOperations = (List<EditOperation>)(redoneOperations = new List<EditOperation>());
		_version++;
		if (_redoStack.Count == 0)
		{
			return false;
		}
		EditStackOperation editStackOperation = _redoStack.Pop();
		LogEditStackEvent(EditExecutionMode.Redo, editStackOperation);
		editOperations.Add(editStackOperation.Operation);
		_undoStack.Push(editStackOperation);
		if (!ExecuteWithExecutionWrapper(editStackOperation.Operation.Redo))
		{
			_undoStack.Pop();
			_redoStack.Push(editStackOperation);
			_editStackRecord.RemoveAt(_editStackRecord.Count - 1);
			return false;
		}
		_invalidateLayer?.Invoke();
		if (_redoStack.TryPeek(out var result) && result.IsSilent)
		{
			if (!TryRedo(out var redoneOperations2))
			{
				return false;
			}
			editOperations.AddRange(redoneOperations2);
		}
		return true;
	}

	private void LogEditStackEvent(EditExecutionMode mode, EditStackOperation op)
	{
		EditStackRecordedOperation editStackRecordedOperation = new EditStackRecordedOperation(op, mode);
		_editStackRecord.Add(editStackRecordedOperation);
		_logger.LogInformation(editStackRecordedOperation.DebugString());
	}

	public bool TryPeekUndoStack(out EditOperation op, bool allowSilentOperations = false, bool allowNoOps = false)
	{
		if (!_undoStack.TryPeek(out var result))
		{
			op = null;
			return false;
		}
		bool startsWith = result.Operation.GetType().Name.StartsWith("Assert");
		if ((!startsWith && result.Operation.IsNoOp) & allowNoOps)
		{
			op = result.Operation;
			return true;
		}
		if (((result.Operation.IsNoOp && !allowNoOps) || (result.IsSilent && !allowSilentOperations)) | startsWith)
		{
			EditStackOperation pop = _undoStack.Pop();
			bool result2 = TryPeekUndoStack(out var op2, allowSilentOperations, allowNoOps);
			_undoStack.Push(pop);
			op = op2;
			return result2;
		}
		op = result.Operation;
		return true;
	}
}
