using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Visual.Observables.Linking;

public class Link : LinkFocus
{
	public delegate void LinkStatusChangedEvent(Link sender, bool isLinked);

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsLinked
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("IsLinked");
				field = value;
				OnIsLinkedChanged(value);
				OnPropertyChanged("IsLinked");
			}
		}
	}

	public ILinked LinkedObject { get; }

	public event LinkStatusChangedEvent LinkStatusChanged;

	internal Link(Linkage parent, ILinked linkedObject, LinkFocusPriority priority = LinkFocusPriority.High)
		: base(parent, priority)
	{
		LinkedObject = linkedObject;
	}

	public override bool Dispose()
	{
		return Parent.DisposeLink(this);
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnIsLinkedChanged(bool condition)
	{
		LinkStatusChanged?.Invoke(this, condition);
		LinkedObject.SetLinkContext(Parent.CurrentContext);
	}
}
