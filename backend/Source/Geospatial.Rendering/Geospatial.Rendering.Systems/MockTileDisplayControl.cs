using Geospatial.Rendering.TiledRenderingSystem;
using Utility;

namespace Geospatial.Rendering.Systems;

public static class MockTileDisplayControl
{
	public const int ImageTileSize = 256;

	public const int BitmapTileSize = 256;

	public static PyramidedTileManager CreatePyramidedTileManager(double width, double height)
	{
		MockCanvas mockCanvas = new MockCanvas(width, height);
		return new PyramidedTileManager(mockCanvas, mockCanvas, null, new MockImageProvider(256, 256), new MockBitmapProvider(256, 256), new MockLabelProvider(), new SynchronizationContext());
	}
}
