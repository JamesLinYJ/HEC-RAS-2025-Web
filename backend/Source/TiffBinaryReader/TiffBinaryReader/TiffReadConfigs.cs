namespace TiffBinaryReader;

public struct TiffReadConfigs
{
	public static readonly TiffReadConfigs DefaultConfigs = new TiffReadConfigs
	{
		ParseOvrFile = true,
		ParsePyramidLevels = true
	};

	public bool ParsePyramidLevels;

	public bool ParseOvrFile;
}
