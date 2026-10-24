using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Spectre.Console;
using Spectre.Console.Rendering;
using Ras.Process.Platform;

namespace Ras.Process.RasterCommands.Explorer;

public class RasterExplorer
{
	private IRasterMetadataSource _source;

	private readonly Stack<IRasterMetadataSource> _sourceStack = new Stack<IRasterMetadataSource>();

	private readonly RasterExplorerState _state = new RasterExplorerState();

	private readonly RasterTreeRenderer _renderer = new RasterTreeRenderer();

	private static int VisibleHeight => Math.Max(1, Console.WindowHeight - 6);

	public RasterExplorer(IRasterMetadataSource source)
	{
		_source = source;
	}

	public bool Run()
	{
		Console.CursorVisible = false;
		try
		{
			BuildTree();
			AnsiConsole.Live(Render()).AutoClear(enabled: false).Start((LiveDisplayContext ctx) =>
			{
				ctx.Refresh();
				while (true)
				{
					ConsoleKeyInfo key = Console.ReadKey(intercept: true);
					if (!ProcessInput(key))
					{
						break;
					}
					ctx.UpdateTarget(Render());
					ctx.Refresh();
				}
			});
			return true;
		}
		finally
		{
			while (_sourceStack.Count > 0)
			{
				_sourceStack.Pop()?.Dispose();
			}
			_source?.Dispose();
			Console.CursorVisible = true;
			AnsiConsole.Clear();
		}
	}

	private IRenderable Render()
	{
		EnsureTilePreview();
		return _renderer.Render(_state, _source.DisplayName, _source.DriverLabel, Console.WindowWidth, Console.WindowHeight);
	}

	private void EnsureTilePreview()
	{
		RasterTreeNode selectedNode = _state.SelectedNode;
		if (selectedNode != null && selectedNode.ShowsTilePreview && !_state.TilePreviewTried)
		{
			_state.TilePreviewTried = true;
			if (_source.TryGetTilePreview(out var preview, out var error))
			{
				_state.TilePreview = preview;
			}
			else
			{
				_state.TilePreviewError = error;
			}
		}
	}

	private void ResetTilePreview()
	{
		_state.TilePreview = null;
		_state.TilePreviewError = null;
		_state.TilePreviewTried = false;
	}

	private void BuildTree()
	{
		RasterTreeNode rasterTreeNode = new RasterTreeNode(NodeKind.Section, "root")
		{
			Depth = -1
		};
		RasterSummary summary = _source.GetSummary();
		RasterTreeNode rasterTreeNode2 = rasterTreeNode.AddChild(new RasterTreeNode(NodeKind.Section, "Raster")
		{
			IsExpanded = true
		});
		rasterTreeNode2.AddChild(new RasterTreeNode(NodeKind.InfoRow, "Dimensions", $"{summary.Width} x {summary.Height}"));
		rasterTreeNode2.AddChild(new RasterTreeNode(NodeKind.InfoRow, "Bands", summary.Bands.ToString()));
		rasterTreeNode2.AddChild(new RasterTreeNode(NodeKind.InfoRow, "Data Type", summary.DataType));
		rasterTreeNode2.AddChild(new RasterTreeNode(NodeKind.InfoRow, "Driver", _source.DriverLabel));
		if (_source.SupportsTilePreview)
		{
			rasterTreeNode.AddChild(new RasterTreeNode(NodeKind.Section, "Image Preview")
			{
				ShowsTilePreview = true
			});
		}
		RasterTreeNode rasterTreeNode3 = rasterTreeNode.AddChild(new RasterTreeNode(NodeKind.Section, "Sub-datasets"));
		IReadOnlyList<SubdatasetEntry> subdatasets = _source.GetSubdatasets();
		if (subdatasets.Count == 0)
		{
			rasterTreeNode3.AddChild(new RasterTreeNode(NodeKind.InfoRow, "(none)"));
		}
		else
		{
			foreach (SubdatasetEntry subdataset in subdatasets)
			{
				rasterTreeNode3.AddChild(new RasterTreeNode(NodeKind.SubdatasetItem, $"[{subdataset.Index}] {subdataset.Name}", subdataset.Description)
				{
					Subdataset = subdataset,
					Detail = subdataset.ConnectionString
				});
			}
		}
		GeoreferenceInfo georeference = _source.GetGeoreference();
		RasterTreeNode rasterTreeNode4 = rasterTreeNode.AddChild(new RasterTreeNode(NodeKind.Section, "Georeferencing")
		{
			IsExpanded = true
		});
		if (!georeference.HasGeoreference)
		{
			rasterTreeNode4.AddChild(new RasterTreeNode(NodeKind.InfoRow, "(not georeferenced)"));
		}
		else
		{
			bool canEditGeoreference = _source.CanEditGeoreference;
			rasterTreeNode4.AddChild(new RasterTreeNode(NodeKind.GerefField, "Origin X", FormatNum(georeference.OriginX))
			{
				Georef = GerefField.OriginX,
				IsEditable = canEditGeoreference
			});
			rasterTreeNode4.AddChild(new RasterTreeNode(NodeKind.GerefField, "Origin Y", FormatNum(georeference.OriginY))
			{
				Georef = GerefField.OriginY,
				IsEditable = canEditGeoreference
			});
			rasterTreeNode4.AddChild(new RasterTreeNode(NodeKind.GerefField, "Cell Size", FormatNum(georeference.CellSize))
			{
				Georef = GerefField.CellSize,
				IsEditable = canEditGeoreference
			});
		}
		ProjectionInfo projection = _source.GetProjection();
		RasterTreeNode rasterTreeNode5 = rasterTreeNode.AddChild(new RasterTreeNode(NodeKind.Section, "Projection")
		{
			IsExpanded = true
		});
		if (!projection.HasProjection)
		{
			rasterTreeNode5.AddChild(new RasterTreeNode(NodeKind.InfoRow, "(none)"));
		}
		else
		{
			if (!string.IsNullOrEmpty(projection.Name))
			{
				rasterTreeNode5.AddChild(new RasterTreeNode(NodeKind.ProjectionField, "Name", projection.Name));
			}
			if (projection.Epsg > 0)
			{
				rasterTreeNode5.AddChild(new RasterTreeNode(NodeKind.ProjectionField, "EPSG", projection.Epsg.ToString()));
			}
			if (!string.IsNullOrEmpty(projection.Units))
			{
				rasterTreeNode5.AddChild(new RasterTreeNode(NodeKind.ProjectionField, "Units", projection.Units));
			}
			if (!string.IsNullOrEmpty(projection.ExtraDetail))
			{
				rasterTreeNode5.AddChild(new RasterTreeNode(NodeKind.ProjectionField, "GeoTIFF", projection.ExtraDetail));
			}
			if (!string.IsNullOrEmpty(projection.Wkt))
			{
				rasterTreeNode5.AddChild(new RasterTreeNode(NodeKind.ProjectionField, "WKT", "(see info panel)")
				{
					Detail = projection.Wkt
				});
			}
		}
		RasterTreeNode rasterTreeNode6 = rasterTreeNode.AddChild(new RasterTreeNode(NodeKind.Section, "Metadata")
		{
			IsExpanded = true
		});
		IReadOnlyList<MetadataEntry> metadata = _source.GetMetadata();
		if (metadata.Count == 0)
		{
			rasterTreeNode6.AddChild(new RasterTreeNode(NodeKind.InfoRow, "(none)"));
		}
		else
		{
			foreach (MetadataEntry metadata2 in metadata)
			{
				rasterTreeNode6.AddChild(new RasterTreeNode(NodeKind.MetadataItem, metadata2.Key, metadata2.Value)
				{
					MetadataKey = metadata2.Key,
					IsEditable = metadata2.IsEditable,
					Detail = metadata2.Value
				});
			}
		}
		RasterTreeNode rasterTreeNode7 = rasterTreeNode.AddChild(new RasterTreeNode(NodeKind.Section, "Pyramid Levels"));
		IReadOnlyList<PyramidLevelInfo> pyramidLevels = _source.GetPyramidLevels();
		if (pyramidLevels.Count == 0)
		{
			rasterTreeNode7.AddChild(new RasterTreeNode(NodeKind.InfoRow, "(none)"));
		}
		else
		{
			foreach (PyramidLevelInfo pyramidLevel in pyramidLevels)
			{
				string formatNum = (double.IsNaN(pyramidLevel.CellSize) ? "?" : FormatNum(pyramidLevel.CellSize));
				string label = $"Level {pyramidLevel.Index}  ({pyramidLevel.Cols} x {pyramidLevel.Rows} @ {formatNum})";
				if (pyramidLevel.Tags.Count > 0)
				{
					RasterTreeNode rasterTreeNode8 = rasterTreeNode7.AddChild(new RasterTreeNode(NodeKind.Section, label));
					foreach (TiffTagEntry tag in pyramidLevel.Tags)
					{
						rasterTreeNode8.AddChild(new RasterTreeNode(NodeKind.TiffTag, tag.Name, tag.Summary)
						{
							Detail = tag.FullValue,
							Description = tag.Description,
							IsEditable = tag.IsEditable,
							TiffTagId = tag.TagId,
							LevelIndex = pyramidLevel.Index,
							EditHint = tag.EditHint
						});
					}
				}
				else
				{
					rasterTreeNode7.AddChild(new RasterTreeNode(NodeKind.PyramidLevel, label));
				}
			}
		}
		_state.Root = rasterTreeNode;
		_state.RefreshVisibleNodes();
		if (_state.CursorIndex >= _state.VisibleNodes.Count)
		{
			_state.CursorIndex = 0;
		}
	}

	private void Rebuild()
	{
		HashSet<string> stringSet = new HashSet<string>();
		foreach (RasterTreeNode child in _state.Root.Children)
		{
			if (child.Kind == NodeKind.Section && child.IsExpanded)
			{
				stringSet.Add(child.Label);
			}
		}
		int cursorIndex = _state.CursorIndex;
		int scrollOffset = _state.ScrollOffset;
		BuildTree();
		foreach (RasterTreeNode child2 in _state.Root.Children)
		{
			if (child2.Kind == NodeKind.Section)
			{
				child2.IsExpanded = stringSet.Contains(child2.Label);
			}
		}
		_state.RefreshVisibleNodes();
		_state.CursorIndex = Math.Clamp(cursorIndex, 0, Math.Max(0, _state.VisibleNodes.Count - 1));
		_state.ScrollOffset = Math.Min(scrollOffset, Math.Max(0, _state.VisibleNodes.Count - 1));
	}

	private static string FormatNum(double v)
	{
		if (!double.IsNaN(v))
		{
			return v.ToString("0.######", CultureInfo.InvariantCulture);
		}
		return "?";
	}

	private bool ProcessInput(ConsoleKeyInfo key)
	{
		if (_state.IsEditing)
		{
			return ProcessEditInput(key);
		}
		_state.ErrorMessage = null;
		_state.StatusMessage = null;
		switch (key.Key)
		{
		case ConsoleKey.UpArrow:
			_state.MoveCursor(-1, VisibleHeight);
			break;
		case ConsoleKey.DownArrow:
			_state.MoveCursor(1, VisibleHeight);
			break;
		case ConsoleKey.PageUp:
			_state.MoveCursor(-VisibleHeight, VisibleHeight);
			break;
		case ConsoleKey.PageDown:
			_state.MoveCursor(VisibleHeight, VisibleHeight);
			break;
		case ConsoleKey.Home:
			_state.CursorIndex = 0;
			_state.ScrollOffset = 0;
			break;
		case ConsoleKey.End:
			_state.CursorIndex = _state.VisibleNodes.Count - 1;
			_state.MoveCursor(0, VisibleHeight);
			break;
		case ConsoleKey.RightArrow:
			ExpandOrActivate();
			break;
		case ConsoleKey.Enter:
			ExpandOrActivate();
			break;
		case ConsoleKey.LeftArrow:
			CollapseOrParent();
			break;
		case ConsoleKey.Backspace:
			GoBack();
			break;
		case ConsoleKey.A:
			_state.ShowInfoPanel = !_state.ShowInfoPanel;
			break;
		case ConsoleKey.C:
			CopySelection();
			break;
		case ConsoleKey.Escape:
		case ConsoleKey.Q:
			return false;
		}
		return true;
	}

	private void ExpandOrActivate()
	{
		RasterTreeNode selectedNode = _state.SelectedNode;
		if (selectedNode == null)
		{
			return;
		}
		if (selectedNode.Kind == NodeKind.Section)
		{
			if (selectedNode.Children.Count > 0)
			{
				selectedNode.IsExpanded = !selectedNode.IsExpanded;
				_state.RefreshVisibleNodes();
			}
		}
		else if (selectedNode.Kind == NodeKind.SubdatasetItem)
		{
			OpenSubdataset(selectedNode);
		}
		else if (selectedNode.IsEditable)
		{
			StartEdit(selectedNode);
		}
	}

	private void CollapseOrParent()
	{
		RasterTreeNode selectedNode = _state.SelectedNode;
		if (selectedNode == null)
		{
			return;
		}
		if (selectedNode.Kind == NodeKind.Section && selectedNode.IsExpanded)
		{
			selectedNode.IsExpanded = false;
			_state.RefreshVisibleNodes();
		}
		else if (selectedNode.Parent != null && selectedNode.Parent != _state.Root)
		{
			int cursorIndex = _state.VisibleNodes.IndexOf(selectedNode.Parent);
			if (cursorIndex >= 0)
			{
				_state.CursorIndex = cursorIndex;
				_state.MoveCursor(0, VisibleHeight);
			}
		}
	}

	private void OpenSubdataset(RasterTreeNode node)
	{
		if (_source.TryOpenSubdataset(node.Subdataset, out var sub, out var error))
		{
			_sourceStack.Push(_source);
			_source = sub;
			_state.CursorIndex = 0;
			_state.ScrollOffset = 0;
			ResetTilePreview();
			BuildTree();
			_state.StatusMessage = "Opened sub-dataset: " + node.Subdataset.Name;
		}
		else
		{
			_state.ErrorMessage = error;
		}
	}

	private void GoBack()
	{
		if (_sourceStack.Count != 0)
		{
			_source.Dispose();
			_source = _sourceStack.Pop();
			_state.CursorIndex = 0;
			_state.ScrollOffset = 0;
			ResetTilePreview();
			BuildTree();
		}
	}

	private void StartEdit(RasterTreeNode node)
	{
		_state.IsEditing = true;
		_state.EditError = null;
		_state.EditHint = node.EditHint;
		string editBuffer = node.Value ?? "";
		if (node.Kind == NodeKind.TiffTag)
		{
			int indexOf = editBuffer.IndexOf(" (");
			if (indexOf >= 0)
			{
				editBuffer = editBuffer.Substring(0, indexOf);
			}
		}
		_state.EditBuffer = editBuffer;
	}

	private bool ProcessEditInput(ConsoleKeyInfo key)
	{
		switch (key.Key)
		{
		case ConsoleKey.Enter:
			CommitEdit();
			break;
		case ConsoleKey.Escape:
			_state.IsEditing = false;
			_state.EditBuffer = "";
			_state.EditError = null;
			_state.EditHint = null;
			break;
		case ConsoleKey.Backspace:
			if (_state.EditBuffer.Length > 0)
			{
				RasterExplorerState state = _state;
				string editBuffer = _state.EditBuffer;
				state.EditBuffer = editBuffer.Substring(0, editBuffer.Length - 1);
			}
			_state.EditError = null;
			break;
		default:
			if (!char.IsControl(key.KeyChar))
			{
				_state.EditBuffer += key.KeyChar;
				_state.EditError = null;
			}
			break;
		}
		return true;
	}

	private void CommitEdit()
	{
		RasterTreeNode selectedNode = _state.SelectedNode;
		if (selectedNode == null)
		{
			_state.IsEditing = false;
		}
		else
		{
			bool commitGeorefSelectedNodeTerm;
			string error;
			if (selectedNode.Kind == NodeKind.GerefField)
			{
				commitGeorefSelectedNodeTerm = CommitGeoref(selectedNode, out error);
			}
			else
			{
				commitGeorefSelectedNodeTerm = ((selectedNode.Kind != NodeKind.TiffTag) ? _source.TrySetMetadataItem(selectedNode.MetadataKey, _state.EditBuffer, out error) : _source.TrySetTiffTag(selectedNode.LevelIndex, selectedNode.TiffTagId, _state.EditBuffer, out error));
			}
			if (commitGeorefSelectedNodeTerm)
			{
				_state.IsEditing = false;
				_state.EditError = null;
				_state.EditHint = null;
				_state.StatusMessage = "Saved " + selectedNode.Label + ".";
				Rebuild();
			}
			else
			{
				_state.EditError = error;
			}
		}
	}

	private bool CommitGeoref(RasterTreeNode node, out string error)
	{
		error = null;
		if (!double.TryParse(_state.EditBuffer, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			error = "'" + _state.EditBuffer + "' is not a valid number.";
			return false;
		}
		GeoreferenceInfo georeference = _source.GetGeoreference();
		double originX = georeference.OriginX;
		double originY = georeference.OriginY;
		double cellSize = georeference.CellSize;
		if (node.Georef == GerefField.OriginX)
		{
			originX = result;
		}
		else if (node.Georef == GerefField.OriginY)
		{
			originY = result;
		}
		else if (node.Georef == GerefField.CellSize)
		{
			cellSize = result;
		}
		return _source.TrySetGeoreference(originX, originY, cellSize, out error);
	}

	private void CopySelection()
	{
		RasterTreeNode selectedNode = _state.SelectedNode;
		if (selectedNode != null)
		{
			string detail = ((!string.IsNullOrEmpty(selectedNode.Detail)) ? selectedNode.Detail : selectedNode.Value);
			if (string.IsNullOrEmpty(detail))
			{
				detail = selectedNode.Label;
			}
			CopyToClipboard(detail);
			_state.StatusMessage = "Copied to clipboard.";
		}
	}

	private static void CopyToClipboard(string textValue)
	{
		if (!DesktopClipboard.TryCopy(textValue)) Console.Error.WriteLine("Clipboard is unavailable.");
	}
}
