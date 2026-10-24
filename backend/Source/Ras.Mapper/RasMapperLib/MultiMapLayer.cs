namespace RasMapperLib;

public class MultiMapLayer : InterpolatedLayer
{
	public RASResultsMap Map1;

	public RASResultsMap Map2;

	public MultiMapLayer(RASResultsMap m1, RASResultsMap m2)
	{
		Map1 = m1;
		Map2 = m2;
	}
}
