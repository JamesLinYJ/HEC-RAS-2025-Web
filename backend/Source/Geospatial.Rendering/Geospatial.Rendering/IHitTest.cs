namespace Geospatial.Rendering;

public interface IHitTest<T>
{
	bool HitTest(HitTestArgs args, out T hit);
}
