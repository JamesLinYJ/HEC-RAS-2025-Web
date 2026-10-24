namespace RasMapperLib;

public interface IEnforceBreaklineLayer
{
	string NearSpacingColumnName { get; }

	string NearSpacingDiskName { get; }

	string FarSpacingColumnName { get; }

	string FarSpacingDiskName { get; }

	string NearRepeatsColumnName { get; }

	string NearRepeatsDiskName { get; }
}
