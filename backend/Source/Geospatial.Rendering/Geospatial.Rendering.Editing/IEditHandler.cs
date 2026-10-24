namespace Geospatial.Rendering.Editing;

public interface IEditHandler
{
	bool CanHandle(CommandType type, EditContext context);

	HandlerResult Handle(EditCommand command, EditContext context);

	bool Preview(HitTestArgs hit, IEditModifiers modifiers, EditContext context);
}
