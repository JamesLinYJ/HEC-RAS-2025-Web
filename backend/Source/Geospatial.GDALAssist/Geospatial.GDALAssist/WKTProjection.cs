namespace Geospatial.GDALAssist;

public class WKTProjection : Projection
{
	public WKTProjection(string wellKnownText)
	{
		_cmdLineProjection = "WKT\"" + wellKnownText + "\"";
		_srs.ImportFromWkt(ref wellKnownText);
		if (Projection.UseGlobalStaticSRSList)
		{
			Projection.GlobalSRSLookup(ref _srs);
		}
	}
}
