using System;
using Geospatial.Rendering.Editing.Options;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Annotations;
using Geospatial.Rendering.Systems.Vector;
using Utility;

namespace Geospatial.Rendering.Editing;

public class ToolOrchestrator : EditingSession
{
	private readonly OverlayChannels _globalOverlay;

	public EditHandlerRouter Router { get; set; } = new EditHandlerRouter();

	public OptionStore Options { get; set; }

	public IVectorManager VectorManager => _vectorManager;

	public OverlayChannels GlobalOverlay => _globalOverlay;

	public Func<EditOperation, bool, bool> OverrideExecuteOp { get; set; }

	public Action<AnnotationSystem> AdditionalAnnotationsRegistration { get; set; }

	public ToolOrchestrator(SelectedSet selected)
		: base(selected)
	{
	}

	public ToolOrchestrator(SelectedSet selected, IVectorManager vectorManager, ISynchronizationContext mapSynchronizationContext, ISynchronizationContext uiSynchronizationContext)
		: base(selected, vectorManager, mapSynchronizationContext, uiSynchronizationContext)
	{
		_globalOverlay = new OverlayChannels(vectorManager);
	}

	public ToolOrchestrator(IVectorManager vectorManager, ISynchronizationContext mapSynchronizationContext, ISynchronizationContext uiSynchronizationContext)
		: base(vectorManager, mapSynchronizationContext, uiSynchronizationContext)
	{
		_globalOverlay = new OverlayChannels(vectorManager);
	}

	public override bool TryRedo()
	{
		EditContext context = BuildContext();
		RouterResult routerResult = Router.Route(new EditCommand(CommandType.Redo, null), context);
		if ((bool)routerResult)
		{
			Invalidate();
			Router.Preview(null, default(EditModifiers), context);
		}
		return routerResult;
	}

	public override bool TryUndo()
	{
		EditContext context = BuildContext();
		RouterResult routerResult = Router.Route(new EditCommand(CommandType.Undo, null), context);
		if ((bool)routerResult)
		{
			Invalidate();
			Router.Preview(null, default(EditModifiers), context);
		}
		return routerResult;
	}

	protected override void RegisterAnnotations()
	{
		AdditionalAnnotationsRegistration?.Invoke(_annotations);
	}

	public override void DeactivateAnnotations()
	{
		base.DeactivateAnnotations();
		_globalOverlay?.ClearAll();
	}

	protected override bool ExecuteInternal(EditCommand cmd)
	{
		EditContext context = BuildContext();
		bool route = Router.Route(cmd, context);
		if (route)
		{
			Invalidate();
		}
		Router.Preview(cmd.Location, cmd.Modifiers, context);
		return route;
	}

	private EditContext BuildContext()
	{
		return new EditContext
		{
			Stack = EditStack,
			VectorManager = _vectorManager,
			Preview = _globalOverlay,
			ExecuteEditOperationOverride = OverrideExecuteOp,
			Options = Options
		};
	}

	public void ReleaseRouterCapture()
	{
		int ordinal = 1000;
		int ordinal2 = 0;
		while (Router.HasCapture)
		{
			EditContext context = BuildContext();
			Router.Route(new EditCommand(CommandType.Close, null), context);
			GlobalOverlay.ClearAll();
			ordinal2++;
			if (ordinal2 >= ordinal)
			{
				throw new InvalidOperationException("Failed to release router capture.");
			}
		}
	}

	protected override bool PreviewInternal(HitTestArgs hit, IEditModifiers modifiers)
	{
		return Router.Preview(hit, modifiers, BuildContext());
	}

	public override void Dispose()
	{
		ReleaseRouterCapture();
		GlobalOverlay?.ClearAll();
		base.Dispose();
	}
}
