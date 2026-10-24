using System;
using Geospatial.Rendering.Systems.Vector;

namespace Geospatial.Rendering.Editing;

public abstract class EditableVector<T> : EditableVector
{
	public virtual T Vector { get; protected init; }

	public EditableVector(T vector, IVectorManager vectorManager, VectorType vectorType, Func<EditOperation, bool, bool> executeEditOperation, OverlayChannels overlayChannels)
		: base(vectorManager, vectorType, executeEditOperation, overlayChannels)
	{
		Vector = vector;
	}
}
public abstract class EditableVector
{
	protected VectorID _openFeatureSegmentsVectorId;

	protected VectorID _openFeaturePointsVectorId;

	protected readonly OverlayChannels _overlay;

	protected readonly string _previewSegmentChannel = "Edit_PreviewSeg";

	protected readonly string _previewSegmentBackgroundChannel = "Edit_PreviewSegBg";

	protected readonly string _previewPointChannel = "Edit_PreviewPt";

	protected IVectorManager VectorManager { get; private init; }

	protected Func<EditOperation, bool, bool> ExecuteEditOperation { get; private init; }

	protected VectorType VectorType { get; private init; }

	public OverlayChannels GlobalOverlay => _overlay;

	public EditableVector(IVectorManager vectorManager, VectorType vectorType, Func<EditOperation, bool, bool> executeEditOperation, OverlayChannels overlayChannels)
	{
		ExecuteEditOperation = executeEditOperation;
		VectorManager = vectorManager;
		VectorType = vectorType;
		_overlay = overlayChannels ?? throw new ArgumentNullException("overlayChannels");
	}

	public abstract bool IsValid();

	public virtual void ClearAllVectors()
	{
		VectorManager.TryDeleteVector(ref _openFeaturePointsVectorId);
		VectorManager.TryDeleteVector(ref _openFeatureSegmentsVectorId);
	}
}
