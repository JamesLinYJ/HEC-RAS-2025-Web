using System.Diagnostics;

namespace Geospatial.Rasters;

[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
public struct BlockSize
{
	public static readonly BlockSize NullBlockSize = new BlockSize(-1, -1);

	public readonly int Width;

	public readonly int Height;

	public int PixelCount => Width * Height;

	public static bool IsNull(BlockSize b)
	{
		if (b.Width == -1)
		{
			return b.Height == -1;
		}
		return false;
	}

	public bool IsNull()
	{
		return IsNull(this);
	}

	public BlockSize(int heightRowCount, int widthColCount)
	{
		Height = heightRowCount;
		Width = widthColCount;
	}

	public bool IsSquare()
	{
		return Width == Height;
	}

	private string GetDebuggerDisplay()
	{
		return "[" + Width + ", " + Height + "]";
	}
}
