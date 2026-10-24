using System;
using System.Collections.Generic;
using Ras.Layers;

namespace Ras.Process.RasCommands.Explorer.GeometryEditor;

public class GeometryEditorState
{
	public const int AssociationRowCount = 2;

	public Geometry Geometry { get; set; }

	public Project Project { get; set; }

	public List<GeometryPropertyInfo> Properties { get; set; } = new List<GeometryPropertyInfo>();

	public int CursorRow { get; set; }

	public int ScrollOffset { get; set; }

	public bool IsEditingCell { get; set; }

	public string EditBuffer { get; set; } = "";

	public string EditError { get; set; }

	public Dictionary<string, object> PendingChanges { get; } = new Dictionary<string, object>();

	public Terrain PendingTerrain { get; set; }

	public bool HasPendingTerrainChange { get; set; }

	public NValueLayer PendingNValue { get; set; }

	public bool HasPendingNValueChange { get; set; }

	public bool HasUnsavedChanges
	{
		get
		{
			if (PendingChanges.Count <= 0 && !HasPendingTerrainChange)
			{
				return HasPendingNValueChange;
			}
			return true;
		}
	}

	public bool HasUnsavedAssociationChanges
	{
		get
		{
			if (!HasPendingTerrainChange)
			{
				return HasPendingNValueChange;
			}
			return true;
		}
	}

	public bool ShowingSavePrompt { get; set; }

	public bool ShowingSaveAsPrompt { get; set; }

	public string SaveAsName { get; set; } = "";

	public string StatusMessage { get; set; }

	public string ErrorMessage { get; set; }

	public bool NeedsSelectionPrompt { get; set; }

	public int TotalRowCount => 2 + Properties.Count;

	public bool IsOnAssociationRow => CursorRow < 2;

	public bool IsOnTerrainRow => CursorRow == 0;

	public bool IsOnNValueRow => CursorRow == 1;

	public GeometryPropertyInfo SelectedProperty
	{
		get
		{
			if (IsOnAssociationRow)
			{
				return null;
			}
			int cursorRowOffset = CursorRow - 2;
			if (cursorRowOffset < 0 || cursorRowOffset >= Properties.Count)
			{
				return null;
			}
			return Properties[cursorRowOffset];
		}
	}

	public void MoveCursor(int delta, int visibleHeight)
	{
		CursorRow = Math.Clamp(CursorRow + delta, 0, Math.Max(0, TotalRowCount - 1));
		AdjustScroll(visibleHeight);
	}

	private void AdjustScroll(int visibleHeight)
	{
		if (CursorRow < ScrollOffset)
		{
			ScrollOffset = CursorRow;
		}
		if (CursorRow >= ScrollOffset + visibleHeight)
		{
			ScrollOffset = CursorRow - visibleHeight + 1;
		}
	}

	public object GetCurrentValue(GeometryPropertyInfo prop)
	{
		if (PendingChanges.TryGetValue(prop.PropertyName, out var localValue))
		{
			return localValue;
		}
		return prop.ReflectionInfo.GetValue(Geometry);
	}

	public void SetPendingChange(GeometryPropertyInfo prop, object pendingChange)
	{
		PendingChanges[prop.PropertyName] = pendingChange;
		ClearMessages();
	}

	public void ClearPendingChanges()
	{
		PendingChanges.Clear();
		PendingTerrain = null;
		HasPendingTerrainChange = false;
		PendingNValue = null;
		HasPendingNValueChange = false;
	}

	public void StartEditing()
	{
		if (SelectedProperty != null && !SelectedProperty.IsReadOnly)
		{
			IsEditingCell = true;
			EditBuffer = GetCurrentValue(SelectedProperty)?.ToString() ?? "";
			EditError = null;
		}
	}

	public void CancelEditing()
	{
		IsEditingCell = false;
		EditBuffer = "";
		EditError = null;
	}

	public void ClearMessages()
	{
		StatusMessage = null;
		ErrorMessage = null;
	}

	public Terrain GetCurrentTerrain()
	{
		if (HasPendingTerrainChange)
		{
			return PendingTerrain;
		}
		return Geometry?.AssociatedTerrain;
	}

	public NValueLayer GetCurrentNValue()
	{
		if (HasPendingNValueChange)
		{
			return PendingNValue;
		}
		return Geometry?.AssociatedNValueLayer;
	}

	public void SetPendingTerrain(Terrain terrain)
	{
		PendingTerrain = terrain;
		HasPendingTerrainChange = true;
		ClearMessages();
	}

	public void SetPendingNValue(NValueLayer nvalue)
	{
		PendingNValue = nvalue;
		HasPendingNValueChange = true;
		ClearMessages();
	}
}
