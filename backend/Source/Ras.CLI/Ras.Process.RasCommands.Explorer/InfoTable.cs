using System.Collections.Generic;

namespace Ras.Process.RasCommands.Explorer;

public class InfoTable
{
	public List<string> Columns { get; set; } = new List<string>();

	public List<List<string>> Rows { get; set; } = new List<List<string>>();

	public void AddColumn(string name)
	{
		Columns.Add(name);
	}

	public void AddRow(params string[] values)
	{
		Rows.Add(new List<string>(values));
	}
}
