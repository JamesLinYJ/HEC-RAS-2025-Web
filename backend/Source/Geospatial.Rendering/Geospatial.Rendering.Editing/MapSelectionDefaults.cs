using Geospatial.Rendering.Layers;

namespace Geospatial.Rendering.Editing;

public static class MapSelectionDefaults
{
	public const SelectMode DefaultSelectMode = SelectMode.Replace;

	public const SelectMode DefaultControlPressedSelectMode = SelectMode.Toggle;

	public const SelectMode DefaultControlPressedSelectExtentMode = SelectMode.Toggle;
}
