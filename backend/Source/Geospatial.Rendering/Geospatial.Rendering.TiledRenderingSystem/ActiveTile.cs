using System.Diagnostics;

namespace Geospatial.Rendering.TiledRenderingSystem;

[DebuggerDisplay("{DebuggerDisplayString()}")]
public class ActiveTile
{
	public readonly RenderTask RenderTask;

	public readonly RenderTile RenderTile;

	public PyramidLevel PyramidLevel => RenderTile.PyramidLevel;

	public bool IsNull
	{
		get
		{
			if (RenderTask == null)
			{
				return RenderTile == null;
			}
			return false;
		}
	}

	public ActiveTile(RenderTask rtask, RenderTile rtile)
	{
		RenderTask = rtask;
		RenderTile = rtile;
	}

	private string DebuggerDisplayString()
	{
		return $"PyramidLevel: {PyramidLevel.Level}, TileIndex: {RenderTask.TileIndex}, RenderState: {RenderTask.GlobalRenderState} ";
	}

	public override int GetHashCode()
	{
		return new TileKey(RenderTask).GetHashCode();
	}
}
