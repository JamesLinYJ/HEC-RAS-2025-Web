using System;

namespace Geospatial.Rendering.Systems.Vector;

public class VectorManagerOptions
{
	private static double _renderScaling = 1.0;

	public static bool DebugEnableGroupLogging { get; set; } = false;

	public static bool USE_VECTOR_POOLING { get; set; } = false;

	public static double RenderScaling
	{
		get
		{
			return _renderScaling;
		}
		set
		{
			if (_renderScaling != value)
			{
				_renderScaling = value;
				RenderScalingChanged?.Invoke();
			}
		}
	}

	public static event Action RenderScalingChanged;
}
