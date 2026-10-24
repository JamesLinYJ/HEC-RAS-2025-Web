namespace Ras.Layers;

public class ResultAssociation
{
	private Terrain _overrideTerrain;

	public Project Project { get; }

	public string OverrideTerrainName { get; private set; }

	public Terrain OverrideTerrain
	{
		get
		{
			return _overrideTerrain;
		}
		set
		{
			_overrideTerrain = value;
			OverrideTerrainName = _overrideTerrain?.Name ?? "";
		}
	}

	public bool HasOverride => !string.IsNullOrEmpty(OverrideTerrainName);

	private ResultAssociation(Project prj)
	{
		Project = prj;
		OverrideTerrainName = "";
	}

	public ResultAssociation(Project prj, string overrideTerrainName)
	{
		Project = prj;
		OverrideTerrainName = overrideTerrainName ?? "";
		if (!string.IsNullOrEmpty(overrideTerrainName))
		{
			_overrideTerrain = Project.FindTerrain(overrideTerrainName);
		}
	}

	public ResultAssociation(Terrain overrideTerrain)
	{
		OverrideTerrain = overrideTerrain;
	}

	public bool IsDefault()
	{
		return string.IsNullOrEmpty(OverrideTerrainName);
	}

	public void AlertTerrainRenamed(string name)
	{
		OverrideTerrainName = name;
	}

	public static ResultAssociation CreateNew(Project prj)
	{
		return new ResultAssociation(prj);
	}
}
