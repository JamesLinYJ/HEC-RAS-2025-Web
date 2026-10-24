namespace RasMapperLib;

public interface IProfileInfo
{
	int Count { get; }

	float FirstStation { get; }

	float LastStation { get; }

	bool ShouldAdjustToZeroStationing { get; }

	Profile GetProfile();

	void SetStationingOffset(float offsetValue);
}
