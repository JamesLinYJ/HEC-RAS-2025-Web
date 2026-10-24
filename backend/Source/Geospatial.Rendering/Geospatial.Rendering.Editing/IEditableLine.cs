using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public interface IEditableLine<Line> : IEditablePointCollection where Line : IPointCollection
{
}
