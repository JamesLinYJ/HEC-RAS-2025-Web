namespace Geospatial.GDALAssist;

public class Proj4Projection : Projection
{
	public Proj4Projection(string proj4String)
	{
		_srs.ImportFromProj4(proj4String);
		if (Projection.UseGlobalStaticSRSList)
		{
			Projection.GlobalSRSLookup(ref _srs);
		}
		_cmdLineProjection = "\"" + proj4String + "\"";
	}
}
