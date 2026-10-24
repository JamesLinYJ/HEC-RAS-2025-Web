using System;
using System.IO;

namespace Ras.Process.RasCommands.Explorer.Actions.Parameters;

public class FilePathParameter : IActionParameter
{
	public string Name { get; }

	public string DisplayName { get; }

	public string Description { get; }

	public Type ValueType => typeof(string);

	public object DefaultValue { get; }

	public bool IsRequired { get; }

	public string Extension { get; }

	public string DefaultFilename { get; }

	public bool MustExist { get; }

	public bool MustNotExist { get; }

	public FilePathParameter(string name, string displayName, string description = null, bool isRequired = true, string defaultValue = null, string extension = null, string defaultFilename = null, bool mustExist = false, bool mustNotExist = false)
	{
		Name = name;
		DisplayName = displayName;
		Description = description;
		IsRequired = isRequired;
		DefaultValue = defaultValue;
		Extension = extension;
		DefaultFilename = defaultFilename ?? ((extension != null) ? ("exported" + extension) : "exported");
		MustExist = mustExist;
		MustNotExist = mustNotExist;
	}

	public string Validate(object localValue)
	{
		if (!(localValue is string textValue))
		{
			return "Value must be a string path";
		}
		if (string.IsNullOrWhiteSpace(textValue))
		{
			if (!IsRequired)
			{
				return null;
			}
			return DisplayName + " is required";
		}
		if (Extension != null && !textValue.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
		{
			return "File must have " + Extension + " extension";
		}
		if (MustExist && !File.Exists(textValue))
		{
			return "File does not exist";
		}
		if (MustNotExist && File.Exists(textValue))
		{
			return "File already exists";
		}
		if (!MustExist)
		{
			string directoryName = Path.GetDirectoryName(textValue);
			if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
			{
				return "Directory does not exist: " + directoryName;
			}
		}
		return null;
	}
}
