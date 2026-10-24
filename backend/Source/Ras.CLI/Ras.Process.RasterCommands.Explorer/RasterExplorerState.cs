using System;
using System.Collections.Generic;

namespace Ras.Process.RasterCommands.Explorer;

public class RasterExplorerState
{
	public RasterTreeNode Root { get; set; }

	public List<RasterTreeNode> VisibleNodes { get; set; } = new List<RasterTreeNode>();

	public int CursorIndex { get; set; }

	public int ScrollOffset { get; set; }

	public bool ShowInfoPanel { get; set; } = true;

	public bool IsEditing { get; set; }

	public string EditBuffer { get; set; } = "";

	public string EditError { get; set; }

	public string EditHint { get; set; }

	public string ErrorMessage { get; set; }

	public string StatusMessage { get; set; }

	public TilePreview TilePreview { get; set; }

	public string TilePreviewError { get; set; }

	public bool TilePreviewTried { get; set; }

	public RasterTreeNode SelectedNode
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
			VisibleNodes = RasterTreeNode.GetVisibleNodes(Root);
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
}
