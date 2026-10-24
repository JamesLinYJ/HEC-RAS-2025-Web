namespace RasMapperLib;

public interface IExtent
{
	double MaxX { get; }

	double MinX { get; }

	double MaxY { get; }

	double MinY { get; }

	double MaxZ { get; }

	double MinZ { get; }

	double MaxM { get; }

	double MinM { get; }

	bool MaxMinXYDefined { get; }
}
