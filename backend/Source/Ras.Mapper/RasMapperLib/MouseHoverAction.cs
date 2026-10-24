namespace RasMapperLib;

public class MouseHoverAction
{
	public MouseHoverState State;

	public SelectedFeature HoveredFeature;

	public SegmentM InsertSegment;

	public PointM InitialPoint;

	public bool HasControl;

	public MouseHoverAction()
	{
		State = MouseHoverState.None;
		InitialPoint = default;
		InsertSegment = default;
		HoveredFeature = null;
		HasControl = false;
	}
}
