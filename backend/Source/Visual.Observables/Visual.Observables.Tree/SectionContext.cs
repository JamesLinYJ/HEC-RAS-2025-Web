using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using Utility.Interfaces;

namespace Visual.Observables.Tree;

public class SectionContext : ObservableValidator, IReadOnlyDisplayName
{
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SectionName
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(field, value))
			{
				OnPropertyChanging("SectionName");
				field = value;
				OnPropertyChanged("SectionName");
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
				OnPropertyChanging("IsExpanded");
				field = value;
				OnPropertyChanged("IsExpanded");
			}
		}
	} = true;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public int TotalChildCount
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<int>.Default.Equals(field, value))
			{
				OnPropertyChanging("TotalChildCount");
				field = value;
				OnPropertyChanged("TotalChildCount");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public int CheckedChildCount
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<int>.Default.Equals(field, value))
			{
				OnPropertyChanging("CheckedChildCount");
				field = value;
				OnPropertyChanged("CheckedChildCount");
			}
		}
	}

	public object Tree { get; }

	public string DisplayName => SectionName;

	public SectionContext(string sectionName, object tree)
	{
		SectionName = sectionName;
		Tree = tree;
	}

	public void UpdateChildStatistics(int totalCount, int checkedCount)
	{
		TotalChildCount = totalCount;
		CheckedChildCount = checkedCount;
	}

	public string GetStatusDescription()
	{
		if (TotalChildCount == 0)
		{
			return "No items";
		}
		if (CheckedChildCount != 0)
		{
			return $"{CheckedChildCount} of {TotalChildCount} checked";
		}
		return $"{TotalChildCount} items";
	}
}
