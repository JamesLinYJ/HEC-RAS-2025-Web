using System.Collections.Generic;

namespace Utility.Resources;

public struct ResourceQuery
{
	public string Resource { get; }

	public ResourceState State { get; }

	public List<ResourceModifier> Modifiers { get; }

	public ResourceQuery(string resource, ResourceState state = ResourceState.Default, List<ResourceModifier> modifiers = null)
	{
		Resource = resource;
		Modifiers = modifiers;
		State = state;
	}
}
