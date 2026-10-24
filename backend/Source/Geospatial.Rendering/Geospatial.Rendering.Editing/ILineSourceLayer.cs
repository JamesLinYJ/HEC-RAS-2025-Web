using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public interface ILineSourceLayer
{
	bool TryGetLineSource(out ILineSegmentSource source);
}
