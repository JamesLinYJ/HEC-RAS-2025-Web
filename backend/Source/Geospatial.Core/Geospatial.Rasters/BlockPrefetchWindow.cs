using System.Collections.Generic;

namespace Geospatial.Rasters;

public static class BlockPrefetchWindow
{
	public const int DefaultRadius = 8;

	public static List<BlockKey> GetWindow(BlockKey key, int profileCount, long blocksPerProfile, int radius = 8)
	{
		List<BlockKey> blockKeys = new List<BlockKey>();
		for (int i = 1; i <= radius; i++)
		{
			AddProfile(blockKeys, key.ProfileIndex + i, profileCount, blocksPerProfile);
			AddProfile(blockKeys, key.ProfileIndex - i, profileCount, blocksPerProfile);
		}
		return blockKeys;
	}

	private static void AddProfile(List<BlockKey> window, int profileIndex, int profileCount, long blocksPerProfile)
	{
		if (profileIndex >= 0 && profileIndex < profileCount)
		{
			for (long ordinal = 0L; ordinal < blocksPerProfile; ordinal++)
			{
				window.Add(new BlockKey(profileIndex, ordinal));
			}
		}
	}
}
