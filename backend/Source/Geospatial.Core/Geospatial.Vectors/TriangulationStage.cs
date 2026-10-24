using System;

namespace Geospatial.Vectors;

public abstract class TriangulationStage
{
	protected Tin _tin;

	protected TriangulationParameters _triParams;

	protected Vector _normalizedOffset;

	protected double _nearPointTolerance;

	protected Action<Tin> _diagnosticAction;

	public TriangulationStage(Tin tin, TriangulationParameters tParams = null)
	{
		_tin = tin;
		if (tParams == null)
		{
			_triParams = new TriangulationParameters();
		}
		else
		{
			_triParams = tParams;
		}
		if (_triParams.NormalizedOffset.HasValue)
		{
			_normalizedOffset = _triParams.NormalizedOffset.Value;
		}
		else
		{
			_normalizedOffset = default;
		}
	}

	public TriangulationStage(TinFactory tf)
	{
		_tin = tf.Tin;
		_triParams = tf.TriangulationParameters;
		_normalizedOffset = tf.ActualNormalizedOffset;
	}

	public void TrackDiagnosticSteps(Action<Tin> newTinAction)
	{
		_diagnosticAction = newTinAction;
	}
}
