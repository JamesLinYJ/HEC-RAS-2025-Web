using System.Collections.Generic;
using System.Linq;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public sealed class DeleteFeatureHandler<T> : IEditHandler
{
	private readonly IVectorCollection<T> _features;

	private readonly SelectedSet _selected;

	private bool _silently;

	public DeleteFeatureHandler(IVectorCollection<T> features, SelectedSet selected, bool silently = false)
	{
		_silently = silently;
		_features = features;
		_selected = selected;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		return type == CommandType.Delete;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		List<EditOperation> editOperations = new List<EditOperation>();
		foreach (int orderByDescending in _selected.Elements.OrderByDescending((int x) => x))
		{
			editOperations.Add(new DeleteFeatureOperation<T>(_features, _selected, orderByDescending));
		}
		MultiOperationEditOperation op = new MultiOperationEditOperation(editOperations);
		ctx.ExecuteEditOperation(op, _silently);
		return new HandlerResult
		{
			Handled = true
		};
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		return false;
	}
}
