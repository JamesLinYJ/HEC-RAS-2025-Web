using System.IO;

namespace Geospatial.GDALAssist;

public class ESRIProjection : Projection
{
	public string Filename { get; }

	public ESRIProjection(string file)
	{
		Filename = file;
		_srs.ImportFromESRI(File.ReadAllLines(Filename));
		if (Projection.UseGlobalStaticSRSList)
		{
			Projection.GlobalSRSLookup(ref _srs);
		}
		_cmdLineProjection = "ESRI::\"" + Filename + "\"";
	}
}
