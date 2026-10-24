namespace Ras.Migration;

public class MigrationParams
{
	public bool ForceSerialProcessing { get; set; }

	public bool MigrateTerrainTiffsAsPathRelSymlinks { get; set; }

	public bool CopyExternalConnectionsLocal { get; set; }

	public Generate1DLinesMode Generate1DLines { get; set; }

	public bool AsClassic { get; set; }

	public static MigrationParams Default()
	{
		return new MigrationParams
		{
			MigrateTerrainTiffsAsPathRelSymlinks = false,
			CopyExternalConnectionsLocal = false,
			ForceSerialProcessing = false,
			Generate1DLines = Generate1DLinesMode.No
		};
	}
}
