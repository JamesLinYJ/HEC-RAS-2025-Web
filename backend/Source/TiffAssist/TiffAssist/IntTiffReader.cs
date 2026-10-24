namespace TiffAssist;

public class IntTiffReader : TiffReader<int>
{
	public IntTiffReader(string TiffFileName)
		: base(TiffFileName)
	{
	}
}
