using System;
using System.Collections.Generic;
using Ras.Layers;
using Utility.Filtering;

namespace Ras.Process.GeometryCommands;

internal sealed class GeometryFields : ISelectorFields
{
	private readonly Geometry _geometry;

	public IReadOnlyList<string> FieldNames => new string[2] { "Name", "Description" };

	public GeometryFields(Geometry geometry)
	{
		_geometry = geometry;
	}

	public bool TryGetField(string normalizedName, out Type type, out bool settable)
	{
		if (!(normalizedName == "name"))
		{
			if (normalizedName == "description")
			{
				type = typeof(string);
				settable = true;
				return true;
			}
			type = null;
			settable = false;
			return false;
		}
		type = typeof(string);
		settable = false;
		return true;
	}

	public bool TryGetValue(string normalizedName, out object localValue)
	{
		localValue = null;
		if (_geometry == null)
		{
			return false;
		}
		if (!(normalizedName == "name"))
		{
			if (normalizedName == "description")
			{
				localValue = _geometry.Description;
				return true;
			}
			return false;
		}
		localValue = _geometry.Name;
		return true;
	}

	public bool TrySetValue(string normalizedName, object description, out string error)
	{
		error = null;
		if (_geometry == null)
		{
			error = "no geometry loaded.";
			return false;
		}
		if (!(normalizedName == "description"))
		{
			if (normalizedName == "name")
			{
				error = "'Name' is read-only for an existing file.";
				return false;
			}
			error = "Unknown field '" + normalizedName + "'.";
			return false;
		}
		_geometry.Description = (string)description;
		return true;
	}
}
