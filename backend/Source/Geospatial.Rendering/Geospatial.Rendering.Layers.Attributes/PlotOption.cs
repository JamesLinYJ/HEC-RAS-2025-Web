using System;

namespace Geospatial.Rendering.Layers.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class PlotOption : Attribute
{
	public string Name { get; set; }

	public string Label { get; set; }

	public string Description { get; set; }

	public bool IsDebug { get; set; }

	public string EnabledIf { get; set; }

	public PlotOption(string name)
		: this(name, name)
	{
	}

	public PlotOption(string name, string label)
		: this(name, label, name)
	{
	}

	public PlotOption(string name, string label, string description)
		: this(name, label, description, isDebug: false)
	{
	}

	public PlotOption(string name, string label, string description, bool isDebug)
	{
		Name = name;
		Label = label;
		Description = description;
		IsDebug = isDebug;
	}
}
