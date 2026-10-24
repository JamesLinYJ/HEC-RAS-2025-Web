namespace RasMapperLib;

public class EmptyProfileInfo : IProfileInfo
{
	public int Count { get; }

	public float FirstStation { get; }

	public float LastStation { get; }

	public bool ShouldAdjustToZeroStationing => true;

	public EmptyProfileInfo()
	{
		Count = 0;
		FirstStation = 0f;
		LastStation = 0f;
	}

	public void SetStationingOffset(float offsetValue)
	{
	}

	void IProfileInfo.SetStationingOffset(float offsetValue)
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetStationingOffset
		this.SetStationingOffset(offsetValue);
	}

	public Profile GetProfile()
	{
		return new Profile(SharedData.LengthUnits, SharedData.LengthUnits);
	}

	Profile IProfileInfo.GetProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetProfile
		return this.GetProfile();
	}

	public override string ToString()
	{
		return "(" + Count + " Points)";
	}
}
