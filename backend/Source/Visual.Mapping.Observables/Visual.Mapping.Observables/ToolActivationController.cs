using System;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Systems.Tools;
using Geospatial.Rendering.TiledRenderingSystem;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Systems;

namespace Visual.Mapping.Observables;

public sealed class ToolActivationController : IDisposable
{
	private static readonly ILogger _logger = LogHelper.GetLogger("ToolActivationController");

	private readonly MapTool _noTool;

	private MapTool _primary;

	private MapTool _secondary;

	private MapTool _middleMouse;

	private bool _secondaryHotkeyHeld;

	private bool _middleMouseHeld;

	private MapTool _activeTool;

	private bool _isTransientOverrideActive;

	private EditingSession _lastActiveOrchestrator;

	private bool _disposed;

	public MapTool PrimaryTool => _primary;

	public MapTool SecondaryTool => _secondary;

	public MapTool MiddleMouseTool => _middleMouse;

	public bool SecondaryHotkeyHeld => _secondaryHotkeyHeld;

	public bool MiddleMouseHeld => _middleMouseHeld;

	public MapTool ActiveTool => _activeTool ?? _noTool;

	public bool IsTransientOverrideActive => _isTransientOverrideActive;

	internal EditingSession LastActiveOrchestrator => _lastActiveOrchestrator;

	public event Action<MapTool, MapTool> ActiveToolChanged;

	public event Action<MapTool> ActiveToolActionInitiated;

	public ToolActivationController()
	{
		_noTool = new MapTool((PyramidedTileManager)null, (ToolOrchestrator)null, false, (Func<KeyCode, bool>)null, false, true, MapToolCursorKind.Default);
	}

	internal ToolActivationController(MapTool noTool)
	{
		_noTool = noTool ?? throw new ArgumentNullException("noTool");
	}

	public void SetPrimaryTool(MapTool tool)
	{
		MapTool primary = _primary;
		if (primary != tool)
		{
			_primary = tool;
			if (primary != null && primary != _activeTool)
			{
				primary.ActionInitiated -= OnActiveToolActionInitiatedRaw;
				TryDeactivate(primary);
			}
			RecomputeActive();
		}
	}

	public void SetSecondaryTool(MapTool tool)
	{
		if (_secondary != tool)
		{
			_secondary = tool;
			RecomputeActive();
		}
	}

	public void SetMiddleMouseTool(MapTool tool)
	{
		if (_middleMouse != tool)
		{
			_middleMouse = tool;
			RecomputeActive();
		}
	}

	public void SetSecondaryHotkeyHeld(bool held)
	{
		if (_secondaryHotkeyHeld != held)
		{
			_secondaryHotkeyHeld = held;
			RecomputeActive();
		}
	}

	public void SetMiddleMouseHeld(bool held)
	{
		if (_middleMouseHeld != held)
		{
			_middleMouseHeld = held;
			RecomputeActive();
		}
	}

	public void RefreshActiveOrchestrator()
	{
		NotifyActiveOrchestratorChanged();
	}

	private MapTool ResolveDesired()
	{
		MapTool mapTool;
		if (_middleMouseHeld)
		{
			mapTool = _middleMouse;
		}
		else
		{
			mapTool = ((!_secondaryHotkeyHeld) ? _primary : _secondary);
		}
		if (mapTool == null)
		{
			mapTool = _primary;
		}
		return mapTool;
	}

	private void RecomputeActive()
	{
		MapTool mapTool = ResolveDesired();
		if (mapTool != _activeTool)
		{
			bool isTransientOverride = IsTempToolOverrideTransition(_activeTool, mapTool);
			SwapActiveTool(mapTool, isTransientOverride);
		}
	}

	private void SwapActiveTool(MapTool newValue, bool isTransientOverride)
	{
		MapTool activeTool = _activeTool;
		if (activeTool == newValue)
		{
			return;
		}
		if (activeTool != null && activeTool.IsActive)
		{
			_logger.LogDebug("Active Tool Cancelling. TODO: Track task.");
			activeTool.Cancel();
		}
		if (activeTool != null)
		{
			activeTool.ActionInitiated -= OnActiveToolActionInitiatedRaw;
			if (!isTransientOverride || activeTool != _primary)
			{
				TryDeactivate(activeTool);
			}
		}
		_activeTool = newValue;
		MapTool primary = _primary;
		_isTransientOverrideActive = primary != null && newValue != null && newValue != primary && (newValue == _secondary || newValue == _middleMouse);
		if (newValue != null)
		{
			newValue.ActionInitiated += OnActiveToolActionInitiatedRaw;
		}
		NotifyActiveOrchestratorChanged();
		ActiveToolChanged?.Invoke(activeTool ?? _noTool, newValue ?? _noTool);
	}

	private bool IsTempToolOverrideTransition(MapTool oldTool, MapTool newTool)
	{
		MapTool primary = _primary;
		if (primary == null)
		{
			return false;
		}
		if (oldTool == primary && IsTempSlot(newTool))
		{
			return true;
		}
		if (newTool == primary && IsTempSlot(oldTool))
		{
			return true;
		}
		return false;
		bool IsTempSlot(MapTool t)
		{
			if (_secondary == null || t != _secondary)
			{
				if (_middleMouse != null)
				{
					return t == _middleMouse;
				}
				return false;
			}
			return true;
		}
	}

	private void NotifyActiveOrchestratorChanged()
	{
		EditingSession editingSession = _activeTool?.Orchestrator;
		if (!_isTransientOverrideActive && editingSession != _lastActiveOrchestrator)
		{
			_lastActiveOrchestrator?.NotifyBecameInactiveTool();
			_lastActiveOrchestrator = editingSession;
			editingSession?.NotifyBecameActiveTool();
		}
	}

	private void OnActiveToolActionInitiatedRaw(MapTool sender)
	{
		ActiveToolActionInitiated?.Invoke(sender);
	}

	private void TryDeactivate(MapTool tool)
	{
		try
		{
			tool.OnDeactivated();
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Error deactivating previous tool.");
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			_lastActiveOrchestrator?.NotifyBecameInactiveTool();
			_lastActiveOrchestrator = null;
			if (_activeTool != null)
			{
				_activeTool.ActionInitiated -= OnActiveToolActionInitiatedRaw;
			}
		}
	}
}
