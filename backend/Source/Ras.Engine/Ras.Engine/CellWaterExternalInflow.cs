namespace Ras.Engine;

public readonly struct CellWaterExternalInflow
{
	public CellWaterSourceId SourceId { get; }

	public int Cell { get; }

	public double Flow { get; }

	internal CellWaterExternalInflow(CellWaterSourceId sourceId, int cell, double flow)
	{
		SourceId = sourceId;
		Cell = cell;
		Flow = flow;
	}
}
