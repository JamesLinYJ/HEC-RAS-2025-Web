using System;
using System.Diagnostics.CodeAnalysis;

namespace Geospatial.Rendering.Editing;

public abstract class EditOperation
{
	// Programmatic edits, including retained control-point repairs, have no
	// pointer hit-test location. This metadata is optional for those operations.
	[AllowNull, MaybeNull]
	protected HitTestArgs _location;

	private bool _isApplied;

	public virtual bool IsNoOp => false;

	public abstract string Description { get; }

	public Func<bool> OnApplyingCallback { get; set; }

	public Func<bool> OnAppliedCallback { get; set; }

	public Func<bool> OnUndoingCallback { get; set; }

	public Func<bool> OnUndoneCallback { get; set; }

	public Func<bool> OnRedoingCallback { get; set; }

	public Func<bool> OnRedoneCallback { get; set; }

	[MaybeNull]
	public HitTestArgs Location => _location;

	public EditOperation([AllowNull] HitTestArgs args)
	{
		_location = args;
	}

	protected abstract bool ApplyInternal();

	protected abstract bool UndoInternal();

	public bool Apply()
	{
		if (_isApplied)
		{
			throw new InvalidOperationException("This action has already been applied. Please undo / redo.");
		}
		_isApplied = true;
		TryExecuteApplyingCallback();
		bool success = ApplyInternal();
		TryThrowEditStackFailure(success);
		TryExecuteAppliedCallback();
		return true;
	}

	public bool Redo()
	{
		if (!_isApplied)
		{
			throw new InvalidOperationException("This action has not been executed yet, please Apply first.");
		}
		_isApplied = true;
		if (!TryExecuteRedoingCallback())
		{
			TryExecuteApplyingCallback();
		}
		bool success = ApplyInternal();
		TryThrowEditStackFailure(success);
		if (!TryExecuteRedoneCallback())
		{
			TryExecuteAppliedCallback();
		}
		return true;
	}

	public bool Undo()
	{
		if (!_isApplied)
		{
			throw new InvalidOperationException("This action has not been executed yet, please Apply first.");
		}
		TryExecuteUndoingCallback();
		bool success = UndoInternal();
		TryThrowEditStackFailure(success);
		TryExecuteUndoneCallback();
		return true;
	}

	private bool TryExecuteRedoingCallback()
	{
		if (OnRedoingCallback == null)
		{
			return false;
		}
		bool success = OnRedoingCallback();
		TryThrowEditStackFailure(success);
		return true;
	}

	private bool TryExecuteRedoneCallback()
	{
		if (OnRedoneCallback == null)
		{
			return false;
		}
		bool success = OnRedoneCallback?.Invoke() ?? true;
		TryThrowEditStackFailure(success);
		return true;
	}

	private bool TryExecuteUndoingCallback()
	{
		if (OnUndoingCallback == null)
		{
			return false;
		}
		bool success = OnUndoingCallback();
		TryThrowEditStackFailure(success);
		return true;
	}

	private bool TryExecuteUndoneCallback()
	{
		if (OnUndoneCallback == null)
		{
			return false;
		}
		bool success = OnUndoneCallback();
		TryThrowEditStackFailure(success);
		return true;
	}

	private bool TryExecuteApplyingCallback()
	{
		if (OnApplyingCallback == null)
		{
			return false;
		}
		bool success = OnApplyingCallback();
		TryThrowEditStackFailure(success);
		return true;
	}

	private bool TryExecuteAppliedCallback()
	{
		if (OnAppliedCallback == null)
		{
			return false;
		}
		bool success = OnAppliedCallback?.Invoke() ?? true;
		TryThrowEditStackFailure(success);
		return true;
	}

	private void TryThrowEditStackFailure(bool success)
	{
		if (success)
		{
			return;
		}
		throw new InvalidOperationException("Not sure what to do with a apply/undo failure...");
	}

	public override string ToString()
	{
		return GetType().Name;
	}
}
