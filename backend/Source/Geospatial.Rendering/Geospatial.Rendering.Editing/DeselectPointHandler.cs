using Geospatial.Rendering.Layers;

namespace Geospatial.Rendering.Editing;

public sealed class DeselectPointHandler : IEditHandler
{
	private readonly SelectedSet _selected;

	public DeselectPointHandler(SelectedSet selected)
	{
		_selected = selected;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		return type == CommandType.Close;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		_selected.Clear();
		return HandlerResult.WasHandled;
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		return false;
	}
}
