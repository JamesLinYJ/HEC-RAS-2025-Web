using System.IO;

namespace Geospatial.Rasters.TileProcessing;

public interface ITileStateSerializer<TState>
{
	void Write(Stream stream, TState state);

	TState Read(Stream stream);
}
