using System;
using System.Collections.Generic;
using Utility.Helpers;

namespace Geospatial.Rendering;

public interface IProfileState
{
	int CurrentProfileIndex { get; set; }

	int ProfileCount { get; }

	IReadOnlyList<string> ProfileNames { get; }

	long Version { get; }

	bool IsRegularProfile { get; }

	bool IsNull { get; }

	event ProfileState.ProfileChangedEvent ProfileChanged;

	event ProfileState.ProfileStructureChangedEvent ProfileStructureChanged;

	static WeakHandler SubscribeProfileChanged<TModel>(IProfileState publisher, TModel model, Action<TModel, int> action) where TModel : class
	{
		return WeakHandler.TrySubscribe(publisher, "ProfileChanged", model, action);
	}

	WeakHandler SubscribeProfileChanged<TModel>(TModel model, Action<TModel, int> action) where TModel : class
	{
		return SubscribeProfileChanged(this, model, action);
	}

	static WeakHandler SubscribeProfileStructureChanged<TModel>(IProfileState publisher, TModel model, Action<TModel> action) where TModel : class
	{
		return WeakHandler.TrySubscribe(publisher, "ProfileStructureChanged", model, action);
	}

	WeakHandler SubscribeProfileStructureChanged<TModel>(TModel model, Action<TModel> action) where TModel : class
	{
		return SubscribeProfileStructureChanged(this, model, action);
	}
}
