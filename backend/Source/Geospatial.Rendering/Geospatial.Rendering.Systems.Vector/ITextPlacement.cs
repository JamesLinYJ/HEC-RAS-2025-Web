using Geospatial.Vectors;

namespace Geospatial.Rendering.Systems.Vector;

public interface ITextPlacement
{
	bool TryPlace(Point desiredPosition, float textWidth, float textHeight, out Point actualPosition);

	void Reset();
}
