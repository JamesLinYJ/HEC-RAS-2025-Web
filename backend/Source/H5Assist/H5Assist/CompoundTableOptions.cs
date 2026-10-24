namespace H5Assist;

public class CompoundTableOptions
{
	public bool CompressDataset { get; set; }

	public bool EncodeEnumsAsStrings { get; set; }

	public static CompoundTableOptions Default()
	{
		return new CompoundTableOptions
		{
			CompressDataset = true,
			EncodeEnumsAsStrings = false
		};
	}
}
