using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RasMapperLib;

[ComVisible(false)]
public interface IAnimatingLayer
{
	bool CanProvideMinMax { get; }

	bool CanAnimate { get; }

	bool IsProfile { get; }

	bool IsMinProfile { get; }

	bool IsMaxProfile { get; }

	bool IsNullProfile { get; }

	int CurrentProfileIndex { get; set; }

	string CurrentProfileName { get; }

	IList<string> AvailableProfiles { get; }

	void TrySetProfile(string profile);

	void SetToMaxProfile();

	void SetToMinProfile();
}
