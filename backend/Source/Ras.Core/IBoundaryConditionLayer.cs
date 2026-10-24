using Ras.Layers;

public interface IBoundaryConditionLayer
{
	BoundaryCondition BoundaryCondition { get; }

	bool IsReadOnly => BoundaryCondition.IsResult;
}
