using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Visual.Observables.Tree;

public class RootTreeNode : TreeNode
{
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsVisible
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("IsVisible");
				field = value;
				OnPropertyChanged("IsVisible");
			}
		}
	} = true;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsVisibleWhenEmpty
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("IsVisibleWhenEmpty");
				field = value;
				OnPropertyChanged("IsVisibleWhenEmpty");
			}
		}
	} = true;

	public override bool IsRootNode => true;

	public RootTreeNode(ITree containingTree, SectionContext context)
		: base(containingTree, new TreeNodeVM(context.SectionName, isCheckable: false)
		{
			Context = context
		})
	{
		CanBeMoved = false;
		IsLeaf = false;
		IsExpanded = true;
		IsVisible = true;
		IsVisibleWhenEmpty = true;
	}
}
