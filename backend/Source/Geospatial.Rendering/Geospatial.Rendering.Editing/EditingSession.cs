using System;
using System.Collections.Generic;
using System.Threading;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Annotations;
using Geospatial.Rendering.Systems.Vector;
using Utility;

namespace Geospatial.Rendering.Editing;

public abstract class EditingSession : IDisposable
{
	public delegate void ElementAddedEvent(int idx);

	public delegate void ElementRemovedEvent(int idx);

	public delegate void ElementInsertedEvent(int idx);

	public delegate void ElementsAddedEvent(int appendedCount);

	public delegate void ElementsRemovedEvent(IEnumerable<int> idx);

	public delegate void ElementsInsertedEvent(IEnumerable<int> idx);

	protected readonly EditStack _editStack;

	private readonly List<EditCommand> _executionList = new List<EditCommand>();

	protected IVectorManager _vectorManager;

	protected ISynchronizationContext _mapSyncContext;

	protected ISynchronizationContext _uiSyncContext;

	protected AnnotationSystem _annotations;

	protected long _version;

	private bool _disposed;

	public EditStack EditStack => _editStack;

	public IReadOnlyList<EditCommand> ExecutionList => _executionList;

	public SelectedSet SelectedFeatures { get; }

	public event Action EditOperationInvoked;

	public event Action BecameActive;

	public event Action BecameInactive;

	public void NotifyBecameActiveTool()
	{
		BecameActive?.Invoke();
	}

	public void NotifyBecameInactiveTool()
	{
		BecameInactive?.Invoke();
	}

	protected EditingSession(SelectedSet selected)
	{
		SelectedFeatures = selected ?? new SelectedSet();
		_editStack = new EditStack(EditStackExecutionWrapper, OnEditStackInvalidated);
	}

	protected EditingSession(SelectedSet selected, IVectorManager vectorManager, ISynchronizationContext mapSynchronizationContext, ISynchronizationContext uiSynchronizationContext)
		: this(selected)
	{
		if (mapSynchronizationContext == null)
		{
			throw new ArgumentNullException("mapSynchronizationContext");
		}
		if (uiSynchronizationContext == null)
		{
			throw new ArgumentNullException("uiSynchronizationContext");
		}
		_vectorManager = vectorManager;
		_mapSyncContext = mapSynchronizationContext;
		_uiSyncContext = uiSynchronizationContext;
	}

	protected EditingSession(IVectorManager vectorManager, ISynchronizationContext mapSynchronizationContext, ISynchronizationContext uiSynchronizationContext)
		: this(new SelectedSet(), vectorManager, mapSynchronizationContext, uiSynchronizationContext)
	{
	}

	public void ActivateAnnotations()
	{
		if (_annotations == null)
		{
			_annotations = new AnnotationSystem(_uiSyncContext);
			RegisterAnnotations();
			_annotations.Invalidate(null);
		}
	}

	public virtual void DeactivateAnnotations()
	{
		if (_annotations != null)
		{
			_annotations.Dispose();
			_annotations = null;
		}
	}

	protected virtual bool EditStackExecutionWrapper(Func<bool> toExecute)
	{
		bool result = toExecute();
		Invalidate();
		return result;
	}

	protected virtual void OnEditStackInvalidated()
	{
		EditOperationInvoked?.Invoke();
	}

	public virtual bool TryUndo()
	{
		return _editStack.TryUndo();
	}

	public virtual bool TryRedo()
	{
		return _editStack.TryRedo();
	}

	public virtual void Invalidate()
	{
		Interlocked.Increment(ref _version);
		InvalidateAnnotations(null);
	}

	public void InvalidateAnnotations()
	{
		InvalidateAnnotations(null);
	}

	protected void InvalidateAnnotations(HitTestArgs args)
	{
		_annotations?.Invalidate(args);
	}

	public void PreviewRender(HitTestArgs interaction, IEditModifiers modifiers)
	{
		if (PreviewInternal(interaction, modifiers))
		{
			_vectorManager?.Canvas.Invalidate();
		}
	}

	public bool Execute(EditCommand cmd)
	{
		_executionList.Add(cmd);
		bool result = ExecuteInternal(cmd);
		InvalidateAnnotations(cmd.Location);
		return result;
	}

	protected abstract bool PreviewInternal(HitTestArgs interaction, IEditModifiers modifiers);

	protected abstract bool ExecuteInternal(EditCommand cmd);

	protected abstract void RegisterAnnotations();

	public virtual void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			DeactivateAnnotations();
		}
	}
}
