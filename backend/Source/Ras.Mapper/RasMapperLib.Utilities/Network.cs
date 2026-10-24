using System;
using Microsoft.VisualBasic.CompilerServices;
using Utility.Progress;

namespace RasMapperLib.Utilities;

public class Network
{
	public const string HECForwardLinkURL = "http://www.hec.usace.army.mil/fwlink/?";

	private static string MakeForwardLinkURL(int linkId)
	{
		return "http://www.hec.usace.army.mil/fwlink/?linkid=" + linkId + "&type=string";
	}

	public static string TryGetHecUrl(int linkId, ProgressReporter prog = null)
	{
		try
		{
			if (prog == null)
			{
				prog = ProgressReporter.None();
			}
			throw new NotImplementedException();
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			prog.ReportError("Server not found: " + ex2.Message.ToString());
			string result = "";
			ProjectData.ClearProjectError();
			return result;
		}
	}
}
