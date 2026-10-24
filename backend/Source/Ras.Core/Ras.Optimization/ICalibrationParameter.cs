using System.Collections.Generic;

namespace Ras.Optimization;

public interface ICalibrationParameter
{
	string Name { get; }

	double Min { get; }

	double Max { get; }

	double Initial { get; }

	ParameterScale Scale { get; }

	bool AffectsManning { get; }

	IReadOnlyList<string> ProducesKeys { get; }

	IReadOnlyList<string> RequiresKeys { get; }

	void Apply(double doubleValue, CalibrationContext ctx);
}
