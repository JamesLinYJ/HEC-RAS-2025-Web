using System;
using System.Drawing;
using System.Threading.Tasks;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.TiledRenderingSystem;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.DataStructures;
using Utility.Logging;
using Utility.Systems;
using Utility.Systems.Gestures;

namespace Geospatial.Rendering.Systems.Tools;

public class MapTool
{
	public delegate void ActionInitiatedEvent(MapTool sender);

	public delegate void ActionFinalizedEvent(MapTool sender);

	protected static readonly ILogger _logger = LogHelper.GetLogger("MapTool");

	private const int HoldStartPixelThreshold = 5;

	private const int ClickStallMS = 150;

	private readonly ToolOrchestrator _staticOrchestrator;

	private readonly Func<ToolOrchestrator> _orchestratorProvider;

	private DoubleInteractionGesture _clickGestures;

	private bool _isCurrentRightClick;

	protected PyramidedTileManager Map { get; }

	public ToolOrchestrator Orchestrator => _orchestratorProvider?.Invoke() ?? _staticOrchestrator;

	public MapToolCursorKind CursorKind { get; }

	public Geospatial.Vectors.Point MouseDownPoint { get; protected set; }

	public Geospatial.Vectors.Point MouseUpPoint { get; protected set; }

	public Geospatial.Vectors.Point CurrentPoint { get; protected set; }

	public bool IsActive { get; private set; }

	public bool IsMouseDown { get; protected set; }

	public bool AllowInertia { get; private set; }

	public bool IsIsolatedAction { get; }

	public Func<KeyCode, bool> IsKeyPressedFunc { get; private init; }

	public event ActionInitiatedEvent ActionInitiated;

	public event ActionFinalizedEvent ActionFinalized;

	public MapTool(PyramidedTileManager map, ToolOrchestrator orchestrator, bool isIsolatedAction, Func<KeyCode, bool> isKeyPressedFunc, bool allowInertia = false, bool segregatedActions = true, MapToolCursorKind cursorKind = MapToolCursorKind.Default)
	{
		Map = map;
		_staticOrchestrator = orchestrator;
		AllowInertia = allowInertia;
		IsIsolatedAction = isIsolatedAction;
		IsKeyPressedFunc = isKeyPressedFunc;
		CursorKind = cursorKind;
		_clickGestures = new DoubleInteractionGesture(150, segregatedActions, 10.0);
	}

	public MapTool(PyramidedTileManager map, Func<ToolOrchestrator> orchestratorProvider, bool isIsolatedAction, Func<KeyCode, bool> isKeyPressedFunc, bool allowInertia = false, bool segregatedActions = true, MapToolCursorKind cursorKind = MapToolCursorKind.Default)
		: this(map, (ToolOrchestrator)null, isIsolatedAction, isKeyPressedFunc, allowInertia, segregatedActions, cursorKind)
	{
		_orchestratorProvider = orchestratorProvider;
	}

	protected virtual void MouseClicked(Geospatial.Vectors.Point screenPosition, bool isRightClick)
	{
		SubmitCommand(CommandType.Click, screenPosition, isRightClick);
	}

	public void ForceMouseClicked(Geospatial.Vectors.Point screenPosition, bool isRightClick)
	{
		MouseClicked(screenPosition, isRightClick);
	}

	protected virtual void MouseDoubleClicked(Geospatial.Vectors.Point screenPosition, bool isRightClick)
	{
		SubmitCommand(CommandType.DoubleClick, screenPosition, isRightClick);
	}

	public void ForceMouseDoubleClicked(Geospatial.Vectors.Point screenPosition, bool isRightClick)
	{
		MouseDoubleClicked(screenPosition, isRightClick);
	}

	protected virtual void MouseHoldStarted(Geospatial.Vectors.Point screenPosition, bool isRightClick)
	{
		SubmitCommand(CommandType.BeginDragging, screenPosition, isRightClick, allowNullHitTest: true);
	}

	public void ForceMouseHoldStarted(Geospatial.Vectors.Point screenPosition, bool isRightClick)
	{
		MouseHoldStarted(screenPosition, isRightClick);
	}

	protected virtual void MouseHoldEnded(Geospatial.Vectors.Point screenPosition, bool isRightClick)
	{
		SubmitCommand(CommandType.FinishDragging, screenPosition, isRightClick, allowNullHitTest: true);
	}

	public void ForceMouseHoldEnded(Geospatial.Vectors.Point screenPosition, bool isRightClick)
	{
		MouseHoldEnded(screenPosition, isRightClick);
	}

	protected virtual void MousePositionChanged(Geospatial.Vectors.Point screenPosition)
	{
		SubmitCommand(CommandType.Dragging, screenPosition, _isCurrentRightClick, allowNullHitTest: true);
	}

	public void ForceMousePositionChanged(Geospatial.Vectors.Point screenPosition)
	{
		MousePositionChanged(screenPosition);
	}

	protected virtual ValueTask Update()
	{
		return ValueTask.CompletedTask;
	}

	private bool SubmitCommand(CommandType type, Geospatial.Vectors.Point screenPosition, bool isRightClick, bool allowNullHitTest = false)
	{
		ToolOrchestrator orchestrator = Orchestrator;
		if (orchestrator == null)
		{
			return false;
		}
		HitTestArgs loc = null;
		if (Map != null)
		{
			loc = Map.GetHitTestArgs(screenPosition);
		}
		else if (!allowNullHitTest)
		{
			return false;
		}
		EditModifiers editModifiers = BuildModifiers(isRightClick);
		bool execute = orchestrator.Execute(new EditCommand(type, loc, editModifiers));
		if (execute)
		{
			PyramidedTileManager map = Map;
			if (map == null)
			{
				return execute;
			}
			map.QueueRenderTiles();
		}
		return execute;
	}

	public virtual void OnDeactivated()
	{
		ToolOrchestrator orchestrator = Orchestrator;
		if (orchestrator != null)
		{
			orchestrator.ReleaseRouterCapture();
			if (_staticOrchestrator != null)
			{
				orchestrator.GlobalOverlay?.ClearAll();
			}
		}
	}

	private EditModifiers BuildModifiers(bool isRightClick)
	{
		if (IsKeyPressedFunc == null)
		{
			return new EditModifiers(isCtrlDown: false, isShiftDown: false, isAltDown: false, isRightClick);
		}
		bool isCtrlDown = IsKeyPressed(KeyCode.Control);
		bool isShiftDown = IsKeyPressed(KeyCode.Shift);
		bool isAltDown = IsKeyPressed(KeyCode.Menu);
		CompactList<KeyCode> pressedKeys = default;
		Capture(KeyCode.A);
		Capture(KeyCode.B);
		Capture(KeyCode.C);
		Capture(KeyCode.D);
		Capture(KeyCode.E);
		Capture(KeyCode.F);
		Capture(KeyCode.G);
		Capture(KeyCode.H);
		Capture(KeyCode.I);
		Capture(KeyCode.J);
		Capture(KeyCode.K);
		Capture(KeyCode.L);
		Capture(KeyCode.M);
		Capture(KeyCode.N);
		Capture(KeyCode.O);
		Capture(KeyCode.P);
		Capture(KeyCode.Q);
		Capture(KeyCode.R);
		Capture(KeyCode.S);
		Capture(KeyCode.T);
		Capture(KeyCode.U);
		Capture(KeyCode.V);
		Capture(KeyCode.W);
		Capture(KeyCode.X);
		Capture(KeyCode.Y);
		Capture(KeyCode.Z);
		return new EditModifiers(isCtrlDown, isShiftDown, isAltDown, isRightClick, pressedKeys);
		void Capture(KeyCode code)
		{
			if (IsKeyPressed(code))
			{
				pressedKeys.Add(code);
			}
		}
	}

	private void HandleRestureResult(GestureResult result, Geospatial.Vectors.Point position, bool isRightClick)
	{
		if (result != GestureResult.Ignore)
		{
			_logger.LogDebug("Handled map tool gesture result {res}", result);
		}
		switch (result)
		{
		case GestureResult.Single:
			MouseClicked(position, isRightClick);
			break;
		case GestureResult.Double:
			MouseDoubleClicked(position, isRightClick);
			break;
		case GestureResult.HoldStart:
			MouseHoldStarted(position, isRightClick);
			break;
		case GestureResult.HoldEnd:
			MouseHoldEnded(position, isRightClick);
			break;
		case GestureResult.InterjectionIgnored:
			break;
		default:
			throw new NotImplementedException();
		case GestureResult.Ignore:
			break;
		}
	}

	public async ValueTask MouseDown(Geospatial.Vectors.Point mouseDown, bool isRightClick)
	{
		if (!IsMouseDown)
		{
			if (!IsActive)
			{
				IsActive = true;
				MouseDownPoint = mouseDown;
				CurrentPoint = mouseDown;
				IsMouseDown = true;
				_isCurrentRightClick = isRightClick;
			}
			HandleRestureResult(await _clickGestures.InteractionStart(new System.Drawing.Point((int)mouseDown.X, (int)mouseDown.Y)), mouseDown, isRightClick);
			await Update();
			ActionInitiated?.Invoke(this);
		}
	}

	public async ValueTask MouseUp(Geospatial.Vectors.Point position, bool isRightClick)
	{
		if (IsMouseDown)
		{
			IsMouseDown = false;
			MouseUpPoint = position;
			if (!AllowInertia)
			{
				await MouseUpFinalized(position, isRightClick);
			}
		}
	}

	public async ValueTask MouseUpFinalized(Geospatial.Vectors.Point position, bool isRightClick)
	{
		if (IsActive)
		{
			IsActive = false;
			HandleRestureResult(await _clickGestures.InteractionCompleted(), position, isRightClick);
			await Update();
			SubmitCommand(CommandType.FinishDragging, position, isRightClick, allowNullHitTest: true);
			_isCurrentRightClick = false;
			ActionFinalized?.Invoke(this);
		}
	}

	public async ValueTask MouseMoved(Geospatial.Vectors.Point position)
	{
		if (!AllowInertia)
		{
			await MouseManipulated(position);
		}
		else if (!IsMouseDown)
		{
			if (!IsActive)
			{
				CurrentPoint = position;
			}
			MousePositionChanged(position);
			await Update();
		}
	}

	public async ValueTask MouseManipulated(Geospatial.Vectors.Point position)
	{
		CurrentPoint = position;
		await TryInterjectAndBeginDrag(position);
		MousePositionChanged(position);
		await Update();
	}

	private async ValueTask TryInterjectAndBeginDrag(Geospatial.Vectors.Point position)
	{
		if (IsMouseDown)
		{
			double pixelDelta = MouseDownPoint.DistanceTo(position);
			if (_clickGestures.InteractionInterjection(pixelDelta) == GestureResult.InterjectionSucceeded)
			{
				HandleRestureResult(GestureResult.HoldStart, MouseDownPoint, isRightClick: false);
				await Update();
			}
		}
	}

	public async ValueTask MouseManipulated(double lastMoveDeltaX, double lastMoveDeltaY)
	{
		if (AllowInertia)
		{
			await MouseManipulated(new Geospatial.Vectors.Point(CurrentPoint.X + lastMoveDeltaX, CurrentPoint.Y + lastMoveDeltaY));
		}
	}

	public async ValueTask Cancel()
	{
		if (IsActive)
		{
			SubmitCommand(CommandType.CancelDragging, CurrentPoint, _isCurrentRightClick, allowNullHitTest: true);
			await MouseUpFinalized(CurrentPoint, isRightClick: false);
		}
	}

	public virtual async ValueTask Invalidate()
	{
		await Update();
	}

	protected bool IsMouseDownValid()
	{
		if (!double.IsNaN(MouseDownPoint.X))
		{
			return !double.IsNaN(MouseDownPoint.Y);
		}
		return false;
	}

	protected bool IsMapValid()
	{
		if (Map != null)
		{
			return Map.IsValid();
		}
		return false;
	}

	protected void QueueRender()
	{
		if (IsMapValid())
		{
			Map.QueueRenderTiles();
		}
	}

	public bool IsKeyPressed(KeyCode code)
	{
		if (IsKeyPressedFunc == null)
		{
			_logger.LogError("Tried to check 'key pressed' status, but no IsKeyPressedFunc was attached.");
			throw new InvalidOperationException("No IsKeyPressedFunc was attached.");
		}
		return IsKeyPressedFunc?.Invoke(code) ?? false;
	}
}
