namespace Visual.Observables.Tree;

public interface ITree
{
	public delegate void SelectedItemChangedEvent(ITree mode, TreeNode treeNode);

	TreeNode SelectedItem { get; set; }

	event SelectedItemChangedEvent SelectedItemChanged;
}
