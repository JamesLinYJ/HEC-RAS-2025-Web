namespace Geospatial;

public interface ITileTraversal<TTile, TBoundary>
{
	bool CanTraverse(TTile tile, int fromLocalIndex, int toLocalIndex);

	TBoundary GetBoundaryContext(TTile tile, int boundaryLocalIndex);

	bool CanCross(TBoundary sourceContext, TTile targetTile, int targetLocalIndex, bool diagonal);
}
