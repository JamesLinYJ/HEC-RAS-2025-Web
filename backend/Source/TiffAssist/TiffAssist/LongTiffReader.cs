namespace TiffAssist;

public class LongTiffReader : TiffReader<long>
{
	public LongTiffReader(string filename)
		: base(filename)
	{
	}
}
