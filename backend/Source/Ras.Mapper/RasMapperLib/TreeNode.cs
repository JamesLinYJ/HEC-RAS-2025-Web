namespace RasMapperLib;

public abstract class TreeNode
{
	public TreeNodeCollection Nodes;

	public TreeNode Parent;

	public bool IsVisible;

	public bool Checked;

	public bool IsExpanded;

	public bool IsSelected;

	public Bounds Bounds;

	public string Text;

	public string ToolTipText;

	public TreeView TreeView;

	public string Name { get; set; }

	protected TreeNode()
	{
		Nodes = new TreeNodeCollection();
	}

	public bool IsCheckedAllTheWayUp()
	{
		return true;
	}

	public virtual object Clone()
	{
		return null;
	}

	public virtual object GetFeatureEditor()
	{
		return null;
	}

	public virtual void AfterStoppedEditing()
	{
	}

	public virtual void StopEditing(bool save)
	{
	}

	public virtual void OnStartEditSession()
	{
	}

	public void Expand()
	{
	}

	public virtual void DrawNode()
	{
	}

	public void Remove()
	{
	}
}
