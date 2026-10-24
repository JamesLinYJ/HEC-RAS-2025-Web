using System.Collections.Generic;

namespace Ras.Process.RasterCommands.Explorer;

public record TilePreview(int TilesWide, int TilesTall, int Cols, int Rows, IReadOnlyList<double> DataFractions, bool HasNoData, bool Sampled);
