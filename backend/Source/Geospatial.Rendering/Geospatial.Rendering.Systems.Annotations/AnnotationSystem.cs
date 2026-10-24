using System;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.DataStructures;
using Utility.Logging;

namespace Geospatial.Rendering.Systems.Annotations;

public class AnnotationSystem : IDisposable
{
	private static readonly ILogger _logger = LogHelper.GetLogger("AnnotationSystem");

	private bool _isDisposed;

	private HitTestArgs _latestAnnotationHitTestArgs;

	private bool _waitingForAnnotationInvalidation;

	private AdvancedCollection<IMapAnnotation> Annotations { get; }

	public ISynchronizationContext SynchronizationContext { get; }

	public AnnotationSystem(ISynchronizationContext syncContext)
	{
		SynchronizationContext = syncContext;
		Annotations = new AdvancedCollection<IMapAnnotation>();
	}

	public void Invalidate()
	{
		Invalidate(null);
	}

	public void Invalidate(HitTestArgs args)
	{
		if (args != null)
		{
			_latestAnnotationHitTestArgs = args;
		}
		if (!_waitingForAnnotationInvalidation)
		{
			_waitingForAnnotationInvalidation = true;
			UpdateAnnotations(_latestAnnotationHitTestArgs);
			_latestAnnotationHitTestArgs = null;
			_waitingForAnnotationInvalidation = false;
		}
	}

	private void UpdateAnnotations(HitTestArgs args)
	{
		foreach (IMapAnnotation annotation in Annotations)
		{
			try
			{
				annotation.Update(args);
			}
			catch (Exception exception)
			{
				_logger.LogError(exception, "Annotation error.");
			}
		}
	}

	public void AddAnnotation(IMapAnnotation annotation)
	{
		Annotations.Add(annotation);
	}

	public void Dispose()
	{
		Dispose(isDisposing: true);
		GC.SuppressFinalize(this);
	}

	public void Dispose(bool isDisposing)
	{
		if (!isDisposing)
		{
			_logger.LogWarning("Failed to dispose AnnotationSystem when finished.");
		}
		if (_isDisposed)
		{
			throw new ObjectDisposedException("AnnotationSystem");
		}
		if (_isDisposed)
		{
			return;
		}
		foreach (IMapAnnotation annotation in Annotations)
		{
			annotation.Dispose();
		}
		_isDisposed = true;
	}
}
