using System;
using System.Collections.Generic;

namespace Visual.Mapping.Observables;

public interface IVectorVisualContext
{
	List<VectorSymbologyOption> SymbologyOptions { get; set; }

	static virtual VectorSymbologyDefinition[] DefaultSymbologyDefinitions => Array.Empty<VectorSymbologyDefinition>();
}
