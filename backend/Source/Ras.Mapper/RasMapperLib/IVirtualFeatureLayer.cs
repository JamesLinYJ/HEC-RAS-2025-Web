namespace RasMapperLib;

public interface IVirtualFeatureLayer
{
	FeatureLayer Layer { get; }

	FeatureLayer SourceLayer { get; }

	string CountAttributeName { get; }
}
