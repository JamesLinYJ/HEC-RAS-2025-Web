namespace Ras.Engine;

public sealed class CellWaterSourceInfo
{
	public CellWaterSourceId Id { get; }

	public string Name { get; }

	internal CellWaterSourceInfo(CellWaterSourceId id, string name)
	{
		Id = id;
		Name = name;
	}
}
