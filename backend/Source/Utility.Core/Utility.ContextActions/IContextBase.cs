using System.Drawing;

namespace Utility.ContextActions;

public interface IContextBase
{
	string Name { get; }

	string IconResource { get; }

	Color IconColor { get; }

	bool Enabled { get; }
}
