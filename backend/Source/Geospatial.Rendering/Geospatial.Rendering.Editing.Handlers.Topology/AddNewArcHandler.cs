using System;
using Geospatial.Vectors.Topology;
using Utility.DataStructures;

namespace Geospatial.Rendering.Editing.Handlers.Topology;

public sealed class AddNewArcHandler : IEditHandler
{
	private readonly NodeArcEditContext _context;

	private readonly Action<EditHandlerRouter, EditableArc> _registerCapturedChildHandlers;

	private int _startNodeFid = -1;

	public AddNewArcHandler(NodeArcEditContext context, int startNodeFid = -1, Action<EditHandlerRouter, EditableArc> registerCapturedChildHandlers = null)
	{
		_startNodeFid = startNodeFid;
		_context = context;
		_registerCapturedChildHandlers = registerCapturedChildHandlers;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		return type == CommandType.DoubleClick;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		ctx.ExecuteEditOperation(new NewArcOperation(_context, cmd.Location, _startNodeFid));
		int fid = _context.Network.ArcCount - 1;
		return new HandlerResult
		{
			Handled = true,
			GetCapture = GetNewArcEditCapture(fid, _context, ctx, _registerCapturedChildHandlers),
			BeforeAcquireCapture = (EditContext _) =>
			{
				_context.OpenFeatureSelectedSetCache.Clear();
			},
			TrackCaptureStateChanged = true
		};
	}

	public static Func<IEditHandler> GetNewArcEditCapture(int fid, NodeArcEditContext context, EditContext ctx)
	{
		return GetNewArcEditCapture(fid, context, ctx, null);
	}

	public static Func<IEditHandler> GetNewArcEditCapture(int fid, NodeArcEditContext context, EditContext ctx, Action<EditHandlerRouter, EditableArc> registerCapturedChildHandlers)
	{
		return () =>
		{
			BaseTopology network = context.Network;
			EditableArc arc = new EditableArc(fid, new Any<BaseTopology>(network), EditableArc.EditingMode.Adding, ctx.VectorManager, ctx.ExecuteEditOperation, context.OpenFeatureSelectedSetCache, ctx.Preview);
			return new CompleteableEditArcFeatureHandler(context, arc, (EditHandlerRouter child) =>
			{
				if (registerCapturedChildHandlers != null)
				{
					registerCapturedChildHandlers(child, arc);
				}
				else
				{
					child.Register(new AppendArcPointHandler(arc, context), 30);
				}
			});
		};
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (hit == null)
		{
			return false;
		}
		_context.EditableNetwork.PreviewAddArc(hit);
		return true;
	}
}
