using Geospatial.Vectors;

namespace Geospatial.Rendering.Systems;

public class LabelDefinition : LabelDefinitionBase
{
	public override string Text { get; }

	public LabelDefinition()
	{
	}

	public LabelDefinition(string stringValue)
	{
		Text = stringValue;
	}

	public LabelDefinition(string stringValue, Point location)
	{
		Text = stringValue;
		PointLocation = location;
	}
}
