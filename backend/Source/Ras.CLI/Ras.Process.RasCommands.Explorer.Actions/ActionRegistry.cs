using System;
using System.Collections.Generic;
using System.Linq;
using Ras.Layers;
using Ras.Process.RasCommands.Explorer.Actions.FlowArea;
using Ras.Process.RasCommands.Explorer.Actions.GeometryActions;
using Ras.Process.RasCommands.Explorer.Actions.ProjectActions;
using Ras.Process.RasCommands.Explorer.Actions.Result;
using Ras.Process.RasCommands.Explorer.Actions.Terrain;

namespace Ras.Process.RasCommands.Explorer.Actions;

public class ActionRegistry
{
	private readonly Dictionary<Type, List<ILayerAction>> _actions = new Dictionary<Type, List<ILayerAction>>();

	public void Register<T>(ILayerAction action)
	{
		Type typeFromHandle = typeof(T);
		if (!_actions.TryGetValue(typeFromHandle, out var actions))
		{
			actions = new List<ILayerAction>();
			_actions[typeFromHandle] = actions;
		}
		actions.Add(action);
	}

	public IEnumerable<ILayerAction> GetActions(object dataSource)
	{
		if (dataSource == null)
		{
			return Enumerable.Empty<ILayerAction>();
		}
		List<ILayerAction> iLayerActions = new List<ILayerAction>();
		Type type = dataSource.GetType();
		if (_actions.TryGetValue(type, out var localValue))
		{
			iLayerActions.AddRange(localValue);
		}
		Type baseType = type.BaseType;
		while (baseType != null && baseType != typeof(object))
		{
			if (_actions.TryGetValue(baseType, out var localValue2))
			{
				iLayerActions.AddRange(localValue2);
			}
			baseType = baseType.BaseType;
		}
		Type[] interfaces = type.GetInterfaces();
		foreach (Type key in interfaces)
		{
			if (_actions.TryGetValue(key, out var localValue3))
			{
				iLayerActions.AddRange(localValue3);
			}
		}
		return iLayerActions.Distinct();
	}

	public static ActionRegistry CreateDefault()
	{
		ActionRegistry actionRegistry = new ActionRegistry();
		actionRegistry.Register<Ras.Layers.Terrain>(new ExportFullCopyAction());
		actionRegistry.Register<Ras.Layers.Terrain>(new ExportClippedAction());
		actionRegistry.Register<Ras.Layers.Result>(new ArchiveResultAction());
		actionRegistry.Register<Ras.Layers.Result>(new SaveAsReadyToRunAction());
		actionRegistry.Register<FlowAreaLayer>(new Ras.Process.RasCommands.Explorer.Actions.FlowArea.ComputePropertyTablesAction());
		actionRegistry.Register<Project>(new ExportProjectionAction());
		actionRegistry.Register<Geometry>(new Ras.Process.RasCommands.Explorer.Actions.GeometryActions.ComputePropertyTablesAction());
		return actionRegistry;
	}
}
