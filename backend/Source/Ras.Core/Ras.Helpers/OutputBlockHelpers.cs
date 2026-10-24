using System.Collections.Generic;
using Geospatial.Rendering;
using Ras.Output;

namespace Ras.Helpers;

public static class OutputBlockHelpers
{
	public static ProfileState CreateTrackedProfileState(this IHorizontalOutputBlock ob)
	{
		ProfileState pf = new ProfileState(ob.ProfileNames);
		pf.CanSetMax = true;
		ob.TimestepAdded += () =>
		{
			ProfileState profileState = pf;
			IList<string> profileNames = ob.ProfileNames;
			profileState.AddProfile(profileNames[profileNames.Count - 1]);
		};
		return pf;
	}
}
