using System;
using Ras.Layers;

namespace Ras.Process.PlanCommands;

internal sealed class PlanFieldDescriptor
{
	public string Name { get; }

	public Type Type { get; }

	public Func<Plan, object> Getter { get; }

	public Action<Plan, object> Setter { get; }

	public bool Settable => Setter != null;

	public PlanFieldDescriptor(string name, Type type, Func<Plan, object> getter, Action<Plan, object> setter)
	{
		Name = name;
		Type = type;
		Getter = getter;
		Setter = setter;
	}
}
