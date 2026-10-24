namespace Geospatial.Systems.Shapes;

public interface ISegmentProvider
{
	ISegment Acquire();

	void Return(IShape shape);
}
