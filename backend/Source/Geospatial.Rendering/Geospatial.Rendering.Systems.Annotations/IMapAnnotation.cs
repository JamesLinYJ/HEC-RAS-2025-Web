using System;

namespace Geospatial.Rendering.Systems.Annotations;

public interface IMapAnnotation
{
	bool IsDisposed { get; }

	void Update(HitTestArgs mousePt);

	void Dispose();

	void ValidateNotDisposed()
	{
		if (IsDisposed)
		{
			throw new InvalidOperationException("This annotation has already been disposed of.");
		}
	}
}
