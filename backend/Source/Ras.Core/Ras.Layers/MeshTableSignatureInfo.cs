namespace Ras.Layers;

public sealed class MeshTableSignatureInfo
{
	public string FlowAreaName { get; }

	public bool HasTables { get; }

	public MeshPropertyTableSignature Signature { get; }

	public MeshTableSignatureInfo(string flowAreaName, bool hasTables, MeshPropertyTableSignature signature)
	{
		FlowAreaName = flowAreaName;
		HasTables = hasTables;
		Signature = signature;
	}
}
