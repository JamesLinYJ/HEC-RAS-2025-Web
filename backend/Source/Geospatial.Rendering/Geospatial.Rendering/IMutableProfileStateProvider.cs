using Geospatial.Rendering.Layers;

namespace Geospatial.Rendering;

public interface IMutableProfileStateProvider : IProfileStateProvider
{
	public delegate void ProfileStateChangedEvent(RenderLayer sender, IProfileState newVal);

	event ProfileStateChangedEvent ProfileStateChanged;
}
