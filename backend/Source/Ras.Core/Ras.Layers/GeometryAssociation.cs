namespace Ras.Layers;

public class GeometryAssociation
{
	private Terrain _terr;

	private NValueLayer _nv;

	public Project Project { get; }

	public string TerrainName { get; private set; }

	public Terrain Terrain
	{
		get
		{
			return _terr;
		}
		set
		{
			_terr = value;
			TerrainName = _terr?.Name ?? "";
		}
	}

	public string NValueLayerName { get; private set; }

	public NValueLayer NValueLayer
	{
		get
		{
			return _nv;
		}
		set
		{
			_nv = value;
			NValueLayerName = _nv?.Name ?? "";
		}
	}

	public GeometryAssociation(Project prj, string terrName, string nvName)
	{
		Project = prj;
		TerrainName = terrName;
		NValueLayerName = nvName;
		if (!string.IsNullOrEmpty(terrName))
		{
			_terr = Project.FindTerrain(terrName);
		}
		if (!string.IsNullOrEmpty(nvName))
		{
			_nv = Project.FindNValueLayer(nvName);
		}
	}

	public bool IsDefault()
	{
		if (string.IsNullOrEmpty(TerrainName))
		{
			return string.IsNullOrEmpty(NValueLayerName);
		}
		return false;
	}

	public static GeometryAssociation CreateNew(Project prj)
	{
		return new GeometryAssociation(prj, "", "");
	}

	public void AlertTerrainRenamed(string name)
	{
		TerrainName = name;
	}

	public void AlertNValueRenamed(string name)
	{
		NValueLayerName = name;
	}
}
