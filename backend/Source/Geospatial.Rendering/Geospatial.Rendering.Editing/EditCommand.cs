namespace Geospatial.Rendering.Editing;

public struct EditCommand(CommandType cmd, HitTestArgs loc, IEditModifiers modifiers = null)
{
	public ToolMode ToolMode = ToolMode.Default;

	public CommandType Command = cmd;

	public HitTestArgs Location = loc;

	public IEditModifiers Modifiers = (IEditModifiers)(modifiers ?? ((object)default(EditModifiers)));
}
