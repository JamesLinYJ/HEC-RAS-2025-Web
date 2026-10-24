using System;
using System.Text.RegularExpressions;

namespace Utility.Files;

public class InputParams
{
	public bool ExcludeHidden { get; set; }

	public bool DeleteFromDest { get; set; }

	public Regex[] ExcludeFiles { get; set; }

	public Regex[] ExcludeDirs { get; set; }

	public Regex[] IncludeFiles { get; set; }

	public Regex[] IncludeDirs { get; set; }

	public Regex[] DeleteExcludeFiles { get; set; }

	public Regex[] DeleteExcludeDirs { get; set; }

	public Func<string, bool> ShouldExcludeFile { get; set; }

	public bool AreSourceFilesFiltered
	{
		get
		{
			if (!ExcludeHidden && IncludeFiles == null && ExcludeFiles == null && IncludeDirs == null && ExcludeDirs == null)
			{
				return ShouldExcludeFile != null;
			}
			return true;
		}
	}
}
