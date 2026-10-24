using System.Drawing;

namespace Utility.ContextActions;

public abstract class ContextBase : IContextBase
{
	public string Name { get; init; }

	public string IconResource { get; init; }

	public Color IconColor { get; init; }

	public bool Enabled { get; set; }

	public ContextBase(string name, bool enabled)
	{
		Name = name;
		Enabled = enabled;
	}
}
