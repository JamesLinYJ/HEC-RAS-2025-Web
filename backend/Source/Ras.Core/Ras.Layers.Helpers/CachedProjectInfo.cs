using Geospatial.GDALAssist;
using Utility.Units;

namespace Ras.Layers.Helpers;

public class CachedProjectInfo
{
	public string Name { get; set; }

	public UnitSystem UnitSystem { get; set; }

	public ProjectionDefinitionInfo Projection { get; set; }

	public CachedProjectInfo()
	{
	}

	public CachedProjectInfo(string projName, UnitSystem units, Projection proj)
	{
		Name = projName;
		UnitSystem = units;
		if (proj != null)
		{
			string wKT = proj.GetWKT();
			Projection = new ProjectionDefinitionInfo
			{
				Type = ProjectionType.WKT,
				Value = wKT
			};
		}
		else
		{
			Projection = new ProjectionDefinitionInfo
			{
				Type = ProjectionType.Unknown,
				Value = ""
			};
		}
	}
}
