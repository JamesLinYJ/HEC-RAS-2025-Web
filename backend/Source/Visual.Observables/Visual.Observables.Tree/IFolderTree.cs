namespace Visual.Observables.Tree;

public interface IFolderTree
{
	TreeNode AddFolder(FolderVM fc, TreeNode rootNode);

	TreeNode AddFolder(FolderVM fc, FolderVM parentFolder);
}
