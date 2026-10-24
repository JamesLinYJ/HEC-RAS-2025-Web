namespace Ras.Layers;

public struct GeometryClipOptions
{
	public bool FullyContainedGeometry;

	public static GeometryClipOptions Default()
	{
		return new GeometryClipOptions
		{
			FullyContainedGeometry = true
		};
	}
}
