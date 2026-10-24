namespace Geospatial.Rendering.TiledRenderingSystem;

public interface IRenderTask
{
	bool RenderSuccess { get; set; }

	bool Render();
}
