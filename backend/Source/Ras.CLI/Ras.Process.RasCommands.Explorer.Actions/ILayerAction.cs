using System.Collections.Generic;
using Ras.Layers;
using Utility.Progress;

namespace Ras.Process.RasCommands.Explorer.Actions;

public interface ILayerAction
{
	string Id { get; }

	string Name { get; }

	string Description { get; }

	IReadOnlyList<IActionParameter> Parameters { get; }

	bool CanExecute(object dataSource, Project project);

	string GetDisabledReason(object dataSource, Project project);

	object GetParameterDefault(string parameterName, object dataSource, Project project);

	ActionResult Execute(object dataSource, Project project, IDictionary<string, object> parameters, ProgressReporter reporter);
}
