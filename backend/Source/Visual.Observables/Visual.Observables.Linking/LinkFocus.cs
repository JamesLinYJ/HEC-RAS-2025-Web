using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Visual.Observables.Linking;

public class LinkFocus : ObservableObject
{
	public delegate void LinkFocusChangedEvent(LinkFocus sender, bool isFocused);

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsInFocus
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("IsInFocus");
				field = value;
				OnIsInFocusChanged(value);
				OnPropertyChanged("IsInFocus");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public LinkFocusPriority Priority
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<LinkFocusPriority>.Default.Equals(field, value))
			{
				OnPropertyChanging("Priority");
				field = value;
				OnPropertyChanged("Priority");
			}
		}
	}

	public Linkage Parent { get; }

	public event LinkFocusChangedEvent LinkFocusChanged;

	public LinkFocus(Linkage parent, LinkFocusPriority priority = LinkFocusPriority.Default)
	{
		if (parent == null)
		{
			throw new ArgumentException("Link must be part of a linkage (The parent can nto be null).");
		}
		Parent = parent;
		Priority = priority;
	}

	public virtual bool Dispose()
	{
		return Parent.DisposeLinkFocus(this);
	}

	public void BeginFocus()
	{
		if (!IsInFocus)
		{
			IsInFocus = true;
		}
	}

	public void EndFocus()
	{
		if (IsInFocus)
		{
			IsInFocus = false;
		}
	}

	public void CancelFocus()
	{
		if (IsInFocus)
		{
			IsInFocus = false;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnIsInFocusChanged(bool condition)
	{
		LinkFocusChanged?.Invoke(this, condition);
	}
}
