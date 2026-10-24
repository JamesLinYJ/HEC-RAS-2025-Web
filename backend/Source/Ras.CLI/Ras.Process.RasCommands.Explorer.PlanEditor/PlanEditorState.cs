using System;
using System.Collections.Generic;
using Ras.Layers;

namespace Ras.Process.RasCommands.Explorer.PlanEditor;

public class PlanEditorState
{
	public const int AssociationRowCount = 2;

	public Plan Plan { get; set; }

	public Project Project { get; set; }

	public List<PlanPropertyInfo> Properties { get; set; } = new List<PlanPropertyInfo>();

	public int CursorRow { get; set; }

	public int ScrollOffset { get; set; }

	public bool IsEditingCell { get; set; }

	public string EditBuffer { get; set; } = "";

	public string EditError { get; set; }

	public Dictionary<string, object> PendingChanges { get; } = new Dictionary<string, object>();

	public Geometry PendingGeometry { get; set; }

	public bool HasPendingGeometryChange { get; set; }

	public BoundaryCondition PendingBoundaryCondition { get; set; }

	public bool HasPendingBCChange { get; set; }

	public bool HasUnsavedChanges
	{
		get
		{
			if (PendingChanges.Count <= 0 && !HasPendingGeometryChange)
			{
				return HasPendingBCChange;
			}
			return true;
		}
	}

	public bool HasUnsavedAssociationChanges
	{
		get
		{
			if (!HasPendingGeometryChange)
			{
				return HasPendingBCChange;
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

	public bool IsOnGeometryRow => CursorRow == 0;

	public bool IsOnBCRow => CursorRow == 1;

	public PlanPropertyInfo SelectedProperty
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

	public object GetCurrentValue(PlanPropertyInfo prop)
	{
		if (PendingChanges.TryGetValue(prop.PropertyName, out var localValue))
		{
			return localValue;
		}
		return prop.ReflectionInfo.GetValue(Plan);
	}

	public void SetPendingChange(PlanPropertyInfo prop, object pendingChange)
	{
		PendingChanges[prop.PropertyName] = pendingChange;
		ClearMessages();
	}

	public void ClearPendingChanges()
	{
		PendingChanges.Clear();
		PendingGeometry = null;
		HasPendingGeometryChange = false;
		PendingBoundaryCondition = null;
		HasPendingBCChange = false;
	}

	public void StartEditing()
	{
		if (SelectedProperty != null && !SelectedProperty.IsReadOnly)
		{
			IsEditingCell = true;
			object currentValue = GetCurrentValue(SelectedProperty);
			EditBuffer = PlanValueEditor.FormatValue(currentValue, SelectedProperty.ValueType);
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

	public Geometry GetCurrentGeometry()
	{
		if (HasPendingGeometryChange)
		{
			return PendingGeometry;
		}
		return Project?.GetPlanAssociation(Plan)?.Geometry;
	}

	public BoundaryCondition GetCurrentBoundaryCondition()
	{
		if (HasPendingBCChange)
		{
			return PendingBoundaryCondition;
		}
		return Project?.GetPlanAssociation(Plan)?.BoundaryCondition;
	}

	public void SetPendingGeometry(Geometry geom)
	{
		PendingGeometry = geom;
		HasPendingGeometryChange = true;
		ClearMessages();
	}

	public void SetPendingBoundaryCondition(BoundaryCondition bc)
	{
		PendingBoundaryCondition = bc;
		HasPendingBCChange = true;
		ClearMessages();
	}
}
