using System.Collections.Generic;

namespace Ras.Process.RasterCommands.Explorer;

public record PyramidLevelInfo(int Index, int Cols, int Rows, double CellSize, IReadOnlyList<TiffTagEntry> Tags);
