namespace Geospatial.GDALAssist;

public class BandIdentifier
{
	public DatasetIdentifier Dataset { get; set; }

	public int BandIndex { get; set; }

	public BandIdentifier(DatasetIdentifier ds, int bandIdx)
	{
		Dataset = ds;
		BandIndex = bandIdx;
	}
}
