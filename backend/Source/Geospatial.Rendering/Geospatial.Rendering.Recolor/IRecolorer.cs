using System;

namespace Geospatial.Rendering.Recolor;

public interface IRecolorer
{
	void RecolorARGB(Memory<int> input, Memory<int> output);

	void RecolorBrightness(Memory<byte> input, Memory<int> output);
}
