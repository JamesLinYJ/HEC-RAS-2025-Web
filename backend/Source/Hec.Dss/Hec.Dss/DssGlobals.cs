namespace Hec.Dss;

public static class DssGlobals
{
	public static int SetProgramName(string programName)
	{
		return DssNative.hec_dss_set_string("PROG", programName);
	}

	public static void SetMessageLevel(int level)
	{
		DssNative.hec_dss_set_value("mlvl", level);
	}
}
