using System.Diagnostics;

namespace Geospatial.Rendering.Systems.Vector;

public class VectorAction
{
	public VectorActionType Type { get; }

	public StackTrace StackTrace { get; }

	public long CurrentVersion { get; }

	public VectorAction(VectorActionType type, long curVersion)
	{
		Type = type;
		StackTrace = new StackTrace();
		CurrentVersion = curVersion;
	}
}
