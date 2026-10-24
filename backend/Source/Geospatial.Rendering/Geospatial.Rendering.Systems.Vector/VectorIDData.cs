using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Systems.Shapes;

namespace Geospatial.Rendering.Systems.Vector;

public class VectorIDData<Shape, VectorGroup> where Shape : class, IShape where VectorGroup : class, IVectorCanvas
{
	private Func<object> _getDynamicData;

	private object _dataCache;

	private long _renderState;

	public bool IsDisposed;

	private const int _logSize = 16;

	private int _curLogIdx;

	private VectorAction[] _mostRecentActions;

	internal VectorID ID { get; set; }

	internal VectorSymbology Symbology { get; set; }

	internal VectorSymbology QueueSymbology { get; set; }

	internal Shape[] OriginalShapeCache { get; set; }

	internal Memory<Shape> CurrentlyUsedShapeCache { get; set; } = Memory<Shape>.Empty;

	internal VectorGroup Group { get; set; }

	internal VectorType Type { get; set; }

	internal int ZIndex { get; set; }

	internal object DataCache
	{
		get
		{
			if (!UsingDynamicDataSource)
			{
				return _dataCache;
			}
			return _getDynamicData();
		}
		set
		{
			if (!UsingDynamicDataSource)
			{
				_dataCache = value;
			}
		}
	}

	internal bool UsingDynamicDataSource => _getDynamicData != null;

	public long RenderState => _renderState;

	public bool QueuedForDelete { get; private set; }

	internal VectorIDData(VectorID id, VectorSymbology symbology, VectorGroup group, VectorType type, int zIndex = 1)
	{
		ID = id;
		Symbology = symbology;
		Group = group;
		Type = type;
		ZIndex = zIndex;
	}

	internal void QueueForDelete()
	{
		QueuedForDelete = true;
		IncrementRenderState(VectorActionType.Delete);
	}

	internal void SetDynamicDataSource(Func<object> getDynamicDataFunc)
	{
		_dataCache = null;
		_getDynamicData = getDynamicDataFunc;
	}

	internal void IncrementRenderState()
	{
		_renderState++;
	}

	internal void IncrementRenderState(VectorActionType action)
	{
		_renderState++;
		LogAction(action);
	}

	internal void LogAction(VectorActionType action)
	{
		if (VectorManagerOptions.DebugEnableGroupLogging)
		{
			if (_mostRecentActions == null)
			{
				_mostRecentActions = new VectorAction[16];
			}
			_mostRecentActions[_curLogIdx % 16] = new VectorAction(action, _renderState);
			_curLogIdx++;
		}
	}

	internal VectorAction[] GetLoggedActionsOrdered()
	{
		if (!VectorManagerOptions.DebugEnableGroupLogging)
		{
			return null;
		}
		VectorAction[] vectorActions = new VectorAction[16];
		for (int i = 0; i < 16; i++)
		{
			vectorActions[i] = _mostRecentActions[(_curLogIdx + i) % 16];
		}
		return vectorActions;
	}

	internal IEnumerable<string> GetLoggedActionsOrderedAsStr()
	{
		if (!VectorManagerOptions.DebugEnableGroupLogging)
		{
			return null;
		}
		return from item in GetLoggedActionsOrdered()
			select (item != null) ? (item.Type.ToString() + "(" + item.CurrentVersion + ")") : "-";
	}
}
