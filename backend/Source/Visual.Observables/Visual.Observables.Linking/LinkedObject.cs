using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Visual.Observables.Linking;

public class LinkedObject<T> : ObservableObject, ILinked<T>, ILinked where T : class
{
	public delegate void ContextChangedEvent(LinkedObject<T> sender);

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public T LinkContext
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<T>.Default.Equals(field, value))
			{
				T oldValue = field;
				OnPropertyChanging("LinkContext");
				field = value;
				OnLinkContextChanged(oldValue, value);
				OnPropertyChanged("LinkContext");
			}
		}
	}

	public Link Link { get; set; }

	public bool IsLinkable => true;

	public event ContextChangedEvent ContextChanged;

	public static LinkedObject<T> CreateFrom(Linkage linkage)
	{
		LinkedObject<T> linkedObject = new LinkedObject<T>();
		linkage.CreateLink(linkedObject);
		return linkedObject;
	}

	private LinkedObject()
	{
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnLinkContextChanged(T oldValue, T newValue)
	{
		ContextChanged?.Invoke(this);
	}
}
