namespace TiffAssist;

public class ByteTiffReader : TiffReader<byte>
{
	public ByteTiffReader(string filename)
		: base(filename)
	{
	}
}
