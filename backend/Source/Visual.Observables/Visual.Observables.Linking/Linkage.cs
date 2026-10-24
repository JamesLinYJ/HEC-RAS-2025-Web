using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Utility.DataStructures;

namespace Visual.Observables.Linking;

public class Linkage : ObservableObject
{
	public delegate void ParentContextChangedEvent(Linkage sender, object parentContext);

	public delegate void AnyLinkFocusChangedEvent(Linkage sender, bool isAnyLinkFocused);

	public delegate void HighestFocusPriorityChangedEvent(Linkage sender, LinkFocusPriority highestPriority);

	private ParentContextChangedEvent _parentContextChanged;

	private AnyLinkFocusChangedEvent _anyLinkFocusChanged;

	private HighestFocusPriorityChangedEvent _highestFocusPriorityChanged;

	private bool _isAnyLinkFocused;

	private LinkFocusPriority _highestFocusedPriority;

	private AdvancedCollection<LinkFocus> _focusedLinks;

	private AdvancedCollection<Link> _links;

	private AdvancedCollection<LinkFocus> _linkFocuses;

	public static bool LinkSystemEnabled { get; set; }

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public Color LinkColor
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<Color>.Default.Equals(field, value))
			{
				OnPropertyChanging("LinkColor");
				field = value;
				OnPropertyChanged("LinkColor");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public object ParentContext
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<object>.Default.Equals(field, value))
			{
				OnPropertyChanging("ParentContext");
				field = value;
				OnParentContextChanged(value);
				OnPropertyChanged("ParentContext");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public object CurrentContext
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<object>.Default.Equals(field, value))
			{
				OnPropertyChanging("CurrentContext");
				field = value;
				OnCurrentContextChanged(value);
				OnPropertyChanged("CurrentContext");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsChildrenUpdatingEnabled
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("IsChildrenUpdatingEnabled");
				field = value;
				OnPropertyChanged("IsChildrenUpdatingEnabled");
			}
		}
	}

	public bool IsAnyLinkFocused
	{
		get
		{
			return _isAnyLinkFocused;
		}
		private set
		{
			if (value != IsAnyLinkFocused)
			{
				_isAnyLinkFocused = value;
				OnPropertyChanged("IsAnyLinkFocused");
				_anyLinkFocusChanged?.Invoke(this, value);
			}
		}
	}

	public LinkFocusPriority HighestFocusedPriority
	{
		get
		{
			return _highestFocusedPriority;
		}
		private set
		{
			if (value != HighestFocusedPriority)
			{
				_highestFocusedPriority = value;
				OnPropertyChanged("HighestFocusedPriority");
				_highestFocusPriorityChanged?.Invoke(this, value);
			}
		}
	}

	public IReadOnlyAdvancedCollection<LinkFocus> FocusedLinks => _focusedLinks;

	public IReadOnlyAdvancedCollection<Link> Links => _links;

	public IReadOnlyAdvancedCollection<LinkFocus> LinkFocuses => _linkFocuses;

	public event ParentContextChangedEvent ParentContextChanged
	{
		add
		{
			if (LinkSystemEnabled)
			{
				_parentContextChanged = (ParentContextChangedEvent)Delegate.Combine(_parentContextChanged, value);
			}
		}
		remove
		{
			_parentContextChanged = (ParentContextChangedEvent)Delegate.Remove(_parentContextChanged, value);
		}
	}

	public event AnyLinkFocusChangedEvent AnyLinkFocusChanged
	{
		add
		{
			if (LinkSystemEnabled)
			{
				_anyLinkFocusChanged = (AnyLinkFocusChangedEvent)Delegate.Combine(_anyLinkFocusChanged, value);
			}
		}
		remove
		{
			_anyLinkFocusChanged = (AnyLinkFocusChangedEvent)Delegate.Remove(_anyLinkFocusChanged, value);
		}
	}

	public event HighestFocusPriorityChangedEvent HighestFocusPriorityChanged
	{
		add
		{
			if (LinkSystemEnabled)
			{
				_highestFocusPriorityChanged = (HighestFocusPriorityChangedEvent)Delegate.Combine(_highestFocusPriorityChanged, value);
			}
		}
		remove
		{
			_highestFocusPriorityChanged = (HighestFocusPriorityChangedEvent)Delegate.Remove(_highestFocusPriorityChanged, value);
		}
	}

	public Linkage()
	{
		_links = new AdvancedCollection<Link>();
		_linkFocuses = new AdvancedCollection<LinkFocus>();
		_focusedLinks = new AdvancedCollection<LinkFocus>();
	}

	public Link CreateLink(ILinked linkedObject, bool initiallyLinked = true)
	{
		Link link = new Link(this, linkedObject)
		{
			IsLinked = initiallyLinked
		};
		link.LinkedObject.SetLinkContext(CurrentContext);
		linkedObject.Link = link;
		if (LinkSystemEnabled)
		{
			link.LinkFocusChanged += LinkFocusChanged;
			_links.Add(link);
			_linkFocuses.Add(link);
		}
		return link;
	}

	public bool DisposeLink(Link link)
	{
		if (!LinkSystemEnabled)
		{
			if (link?.LinkedObject != null)
			{
				link.LinkedObject.Link = null;
			}
			return true;
		}
		if (!_links.Contains(link))
		{
			return false;
		}
		link.LinkFocusChanged -= LinkFocusChanged;
		link.LinkedObject.Link = null;
		_links.Remove(link);
		_linkFocuses.Remove(link);
		return true;
	}

	public bool DisposeLinkFocus(LinkFocus link)
	{
		if (!LinkSystemEnabled)
		{
			return true;
		}
		if (!Links.Contains(link))
		{
			return false;
		}
		link.LinkFocusChanged -= LinkFocusChanged;
		_linkFocuses.Remove(link);
		return true;
	}

	public LinkFocus CreateLinkFocus()
	{
		LinkFocus linkFocus = new LinkFocus(this);
		if (LinkSystemEnabled)
		{
			linkFocus.LinkFocusChanged += LinkFocusChanged;
			_linkFocuses.Add(linkFocus);
		}
		return linkFocus;
	}

	private void LinkFocusChanged(LinkFocus sender, bool isFocused)
	{
		if (LinkSystemEnabled)
		{
			if (isFocused)
			{
				_focusedLinks.Add(sender);
			}
			else
			{
				_focusedLinks.Remove(sender);
			}
			IsAnyLinkFocused = FocusedLinks.Count != 0;
			HighestFocusedPriority = (IsAnyLinkFocused ? FocusedLinks.Max((LinkFocus linkFocus) => linkFocus.Priority) : LinkFocusPriority.Nothing);
		}
	}

	private void SyncLinksWithCurrentContext()
	{
		if (!LinkSystemEnabled)
		{
			return;
		}
		foreach (Link link in Links)
		{
			if (link.IsLinked)
			{
				link.LinkedObject.SetLinkContext(CurrentContext);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnParentContextChanged(object localValue)
	{
		_parentContextChanged?.Invoke(this, localValue);
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnCurrentContextChanged(object localValue)
	{
		SyncLinksWithCurrentContext();
	}
}
