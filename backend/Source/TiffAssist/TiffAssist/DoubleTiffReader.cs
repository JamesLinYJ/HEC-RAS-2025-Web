namespace TiffAssist;

public class DoubleTiffReader : TiffReader<double>
{
	public DoubleTiffReader(string filename)
		: base(filename)
	{
	}
}
