namespace RasMapperLib.Mapping;

public class StoredMapStatus
{
	private string _description;

	private string _longDescription;

	private bool _allowRecompute;

	public static readonly StoredMapStatus NotCreatedYet = new StoredMapStatus("Map not created", "Compute the map to visualize results.", allowRecomp: true);

	public static readonly StoredMapStatus ResultsDontExist = new StoredMapStatus("Results do not exist", "Recompute the RAS results to view this map.", allowRecomp: false);

	public static readonly StoredMapStatus TerrainNotFound = new StoredMapStatus("Terrain not found", "Associate a valid terrain with this map to visualize results.", allowRecomp: false);

	public static readonly StoredMapStatus FilesOutOfDate = new StoredMapStatus("Map files are out of date", "Recompute the map to ensure it reflects the most recent results.", allowRecomp: true);

	public static readonly StoredMapStatus FilesUpToDate = new StoredMapStatus("Map files up to date.", "Map files reflect the most recent results stored on disk.", allowRecomp: true);

	public static readonly StoredMapStatus DifferentParameters = new StoredMapStatus("Map file parameters differ", "Map files indicate different parameters than the current parameters", allowRecomp: true);

	public static readonly StoredMapStatus NotApplicable = new StoredMapStatus("N/A", "Not a stored map type.", allowRecomp: false);

	public string Description => _description;

	public string LongDescription => _longDescription;

	public bool AllowRecompute => _allowRecompute;

	private StoredMapStatus(string desc, string longDesc, bool allowRecomp)
	{
		_description = desc;
		_longDescription = longDesc;
		_allowRecompute = allowRecomp;
	}

	public override bool Equals(object obj) => ReferenceEquals(this, obj);

	public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

	public static bool operator ==(StoredMapStatus a, StoredMapStatus b)
	{
		if ((object)a == null || (object)b == null)
		{
			return false;
		}
		return a.Equals(b);
	}

	public static bool operator !=(StoredMapStatus a, StoredMapStatus b)
	{
		return !(a == b);
	}
}
