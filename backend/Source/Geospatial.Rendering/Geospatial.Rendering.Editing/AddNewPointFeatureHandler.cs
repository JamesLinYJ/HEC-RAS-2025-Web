using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public sealed class AddNewPointFeatureHandler : IEditHandler
{
	private readonly IPointCollection _features;

	private readonly SelectedSet _selected;

	public AddNewPointFeatureHandler(IPointCollection features, SelectedSet selected)
	{
		_features = features;
		_selected = selected;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		return type == CommandType.Click;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		NewFeatureOperation<Point> op = new NewFeatureOperation<Point>(_features, _selected, () => cmd.Location.QueryPoint);
		ctx.ExecuteEditOperation(op);
		int idx = _features.Count - 1;
		if (!cmd.Modifiers.IsCtrlDown())
		{
			_selected.Clear();
		}
		_selected.Add(idx);
		return HandlerResult.WasHandled;
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (hit == null)
		{
			return false;
		}
		string channel = "addpoint";
		ctx.Preview.TryGetVector(channel, out var id);
		ctx.VectorManager.AddOrUpdateVector(ref id, hit.QueryPoint, DefaultEditSymbology.AppendPointSymbology);
		ctx.Preview.RegisterVector(channel, id);
		return true;
	}
}
