using System;
using System.Collections.Generic;
using Ras.Process.RasCommands.Explorer.FeatureTable;
using Ras.Process.RasCommands.Explorer.GeometryEditor;
using Ras.Process.RasCommands.Explorer.PlanEditor;

namespace Ras.Process.RasCommands.Explorer;

public class RasExplorerState
{
	public ExplorerMode Mode { get; set; }

	public RasTreeNode Root { get; set; }

	public List<RasTreeNode> VisibleNodes { get; set; } = new List<RasTreeNode>();

	public int CursorIndex { get; set; }

	public int ScrollOffset { get; set; }

	public FeatureTableState FeatureTableState { get; set; }

	public PlanEditorState PlanEditorState { get; set; }

	public GeometryEditorState GeometryEditorState { get; set; }

	public InfoPanelMode InfoPanelMode { get; set; } = InfoPanelMode.PropertiesAndPreview;

	public bool ShowInfoPanel => InfoPanelMode != InfoPanelMode.None;

	public bool FocusOnInfoPanel { get; set; }

	public int SelectedPropertyIndex { get; set; }

	public int InfoPanelScrollOffset { get; set; }

	public RasNodeInfoData SelectedNodeInfo { get; set; }

	public string ErrorMessage { get; set; }

	public RasTreeNode SelectedNode
	{
		get
		{
			if (CursorIndex < 0 || CursorIndex >= VisibleNodes.Count)
			{
				return null;
			}
			return VisibleNodes[CursorIndex];
		}
	}

	public void RefreshVisibleNodes()
	{
		if (Root != null)
		{
			VisibleNodes = RasTreeNode.GetVisibleNodes(Root);
		}
	}

	public void MoveCursor(int delta, int visibleHeight)
	{
		CursorIndex = Math.Clamp(CursorIndex + delta, 0, Math.Max(0, VisibleNodes.Count - 1));
		AdjustScroll(visibleHeight);
	}

	private void AdjustScroll(int visibleHeight)
	{
		if (CursorIndex < ScrollOffset)
		{
			ScrollOffset = CursorIndex;
		}
		if (CursorIndex >= ScrollOffset + visibleHeight)
		{
			ScrollOffset = CursorIndex - visibleHeight + 1;
		}
	}

	public void MoveInfoPanelCursor(int delta, int visibleHeight, int propertyCount)
	{
		SelectedPropertyIndex = Math.Clamp(SelectedPropertyIndex + delta, 0, Math.Max(0, propertyCount - 1));
		AdjustInfoPanelScroll(visibleHeight);
	}

	public void AdjustInfoPanelScroll(int visibleHeight)
	{
		if (SelectedPropertyIndex < InfoPanelScrollOffset)
		{
			InfoPanelScrollOffset = SelectedPropertyIndex;
		}
		if (SelectedPropertyIndex >= InfoPanelScrollOffset + visibleHeight)
		{
			InfoPanelScrollOffset = SelectedPropertyIndex - visibleHeight + 1;
		}
	}
}
