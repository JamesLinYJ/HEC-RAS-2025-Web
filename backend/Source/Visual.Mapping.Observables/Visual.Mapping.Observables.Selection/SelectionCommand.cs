namespace Visual.Mapping.Observables.Selection;

public struct SelectionCommand
{
	public int FeatureID { get; }

	public SelectionCommandType Type { get; }

	public SelectionCommand(int featureId, SelectionCommandType type)
	{
		FeatureID = featureId;
		Type = type;
	}
}
