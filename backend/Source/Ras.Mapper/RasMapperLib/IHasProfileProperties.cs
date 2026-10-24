namespace RasMapperLib;

public interface IHasProfileProperties
{
	Profile GetProfile(int xsid);

	void SetProfile(int xsid, Profile pf);

	IProfileInfo GetProfileProperties(int xsid);

	void SetProfileProperties(int xsid, IProfileInfo pfInfo);
}
