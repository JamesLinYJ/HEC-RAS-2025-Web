using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Utility.DataStructures;
using Visual.Observables.Linking;

namespace Visual.Observables.Tree;

[DebuggerDisplay("{ViewModel.Name} (Expanded: {IsExpanded}, Checked: {ViewModel.IsChecked}, Context: {Context})")]
public class TreeNode : ObservableObject, ITreeNodeV2<TreeNode>, IMutableTreeNodeChildV2<TreeNode>, ITreeNodeChildV2<TreeNode>, ISerializable
{
	public delegate void ParentChangedEvent(TreeNode sender, TreeNode oldValue, TreeNode newValue);

	public class NameComparer : IComparer<TreeNode>
	{
		public static NameComparer Instance = new NameComparer();

		private static readonly Regex _regex = new Regex("(\\d+)", RegexOptions.Compiled);

		public int Compare(TreeNode x, TreeNode y)
		{
			if (x == null && y == null)
			{
				return 0;
			}
			if (x == null)
			{
				return -1;
			}
			if (y == null)
			{
				return 1;
			}
			return CompareNames(x.ViewModel.Name, y.ViewModel.Name);
		}

		public static int CompareNames(string xName, string yName)
		{
			string[] parts = _regex.Split(xName ?? string.Empty);
			string[] partsArray = _regex.Split(yName ?? string.Empty);
			int count = Math.Min(parts.Length, partsArray.Length);
			for (int i = 0; i < count; i++)
			{
				int compare = 0;
				bool tryParse = int.TryParse(parts[i], out var result);
				bool tryParse2 = int.TryParse(partsArray[i], out var result2);
				compare = ((!(tryParse & tryParse2)) ? string.Compare(parts[i], partsArray[i], StringComparison.OrdinalIgnoreCase) : result.CompareTo(result2));
				if (compare != 0)
				{
					return compare;
				}
			}
			return parts.Length.CompareTo(partsArray.Length);
		}
	}

	public class ReverseNameComparer : IComparer<TreeNode>
	{
		public static ReverseNameComparer Instance = new ReverseNameComparer();

		public int Compare(TreeNode x, TreeNode y)
		{
			return NameComparer.Instance.Compare(y, x);
		}
	}

	private LinkFocus _link;

	protected readonly TreeNodeVM _viewModel;

	public static Type Type => typeof(TreeNode);

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsSelected
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("IsSelected");
				field = value;
				OnPropertyChanged("IsSelected");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsExpanded
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				bool oldValue = field;
				OnPropertyChanging("IsExpanded");
				field = value;
				OnIsExpandedChanged(oldValue, value);
				OnPropertyChanged("IsExpanded");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool CanBeMoved
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("CanBeMoved");
				field = value;
				OnPropertyChanged("CanBeMoved");
			}
		}
	} = true;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ChildrenIgnoreSortOrder
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("ChildrenIgnoreSortOrder");
				field = value;
				OnPropertyChanged("ChildrenIgnoreSortOrder");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsLeaf
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("IsLeaf");
				field = value;
				OnPropertyChanged("IsLeaf");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public TreeNode Parent
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<TreeNode>.Default.Equals(field, value))
			{
				TreeNode oldValue = field;
				OnPropertyChanging("Parent");
				field = value;
				OnParentChanged(oldValue, value);
				OnPropertyChanged("Parent");
			}
		}
	}

	ITreeNodeV2<TreeNode> IMutableTreeNodeChildV2<TreeNode>.Parent
	{
		get
		{
			return Parent;
		}
		set
		{
			Parent = value as TreeNode;
		}
	}

	public object Context => ViewModel.Context;

	public virtual bool IsRootNode => Parent == null;

	public LinkFocus Link
	{
		get
		{
			return _link;
		}
		private set
		{
			SetProperty(ref _link, value, "Link");
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public INotifyList<TreeNode> Children
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<INotifyList<TreeNode>>.Default.Equals(field, value))
			{
				OnPropertyChanging("Children");
				field = value;
				OnPropertyChanged("Children");
			}
		}
	} = new AdvancedCollection<TreeNode>();

	public int ChildrenCount => ((ICollection<TreeNode>)Children).Count;

	IList<TreeNode> ITreeNodeV2<TreeNode>.Children => Children;

	public TreeNodeVM ViewModel => _viewModel;

	public ITree ContainingTree { get; }

	public event ParentChangedEvent ParentChanged;

	public TreeNode(ITree containingTree)
	{
		ContainingTree = containingTree;
		_viewModel = new TreeNodeVM();
		Initialize(ViewModel);
	}

	public TreeNode(ITree containingTree, string name, bool isCheckable = true, bool isPinnable = true)
	{
		ContainingTree = containingTree;
		_viewModel = new TreeNodeVM(name, isCheckable, isPinnable);
		Initialize(ViewModel);
	}

	public TreeNode(ITree containingTree, TreeNodeVM vm)
	{
		ContainingTree = containingTree;
		_viewModel = vm;
		Initialize(ViewModel);
	}

	public bool IsFullyEnabled()
	{
		if (ViewModel.IsCheckable && !ViewModel.IsChecked)
		{
			return false;
		}
		if (Parent == null)
		{
			return true;
		}
		return Parent.IsFullyEnabled();
	}

	private void Initialize(TreeNodeVM vm)
	{
		Link = vm.Linkage.CreateLinkFocus();
	}

	public void Deserialize(XElement element)
	{
		throw new NotImplementedException();
	}

	public void Serialize(XElement serializeTo)
	{
		throw new NotImplementedException();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnIsExpandedChanged(bool oldValue, bool newValue)
	{
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnParentChanged(TreeNode oldValue, TreeNode newValue)
	{
		ParentChanged?.Invoke(this, oldValue, newValue);
	}
}
