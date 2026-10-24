using System;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public sealed class AddNewLineFeatureHandler<T, K> : IEditHandler where T : Polyline, new() where K : GenericEditableLine<T>
{
	private readonly IVectorCollection<T> _features;

	private readonly SelectedSet _selected;

	private Func<int, IVectorManager, EditContext, K> _createFeature;

	private Action<EditHandlerRouter, K> _registerCapturedChildHandlers;

	public AddNewLineFeatureHandler(IVectorCollection<T> features, SelectedSet selected, Func<int, IVectorManager, EditContext, K> createFeature, Action<EditHandlerRouter, K> registerCapturedChildHandlers = null)
	{
		_features = features;
		_selected = selected;
		_createFeature = createFeature;
		_registerCapturedChildHandlers = registerCapturedChildHandlers;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		return type == CommandType.DoubleClick;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		Point firstPoint = cmd.Location.QueryPoint;
		NewFeatureOperation<T> op = new NewFeatureOperation<T>(_features, _selected, () => new T { firstPoint });
		ctx.ExecuteEditOperation(op);
		int fid = _features.Count - 1;
		return new HandlerResult
		{
			Handled = true,
			TrackCaptureStateChanged = true,
			GetCapture = GetNewLineEditCapture(fid, ctx, _createFeature, _registerCapturedChildHandlers)
		};
	}

	public static Func<IEditHandler> GetNewLineEditCapture(int fid, EditContext ctx, Func<int, IVectorManager, EditContext, K> createFeature, Action<EditHandlerRouter, K> registerCapturedChildHandlers = null)
	{
		return () =>
		{
			K editable = createFeature(fid, ctx.VectorManager, ctx);
			return new EditLineFeatureHandler<T, K>(editable, fid, (EditHandlerRouter child) =>
			{
				if (registerCapturedChildHandlers != null)
				{
					registerCapturedChildHandlers(child, editable);
				}
				else
				{
					child.Register(new AppendLineFeaturePointHandler<T, K>(editable), 30);
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
		string channel = "addline";
		ctx.Preview.TryGetVector(channel, out var id);
		ctx.VectorManager.AddOrUpdateVector(ref id, hit.QueryPoint, DefaultEditSymbology.AppendPointSymbology);
		ctx.Preview.RegisterVector(channel, id);
		return true;
	}
}
