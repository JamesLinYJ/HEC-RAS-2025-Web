using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Ras.Observables;

public class ObservablePayloadWithContextWrapper : ObservableObject
{
	private ObservableBoundaryConditionLine _boundaryConditionLine;
	private ObservablePayload _payload;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ObservableBoundaryConditionLine BoundaryConditionLine
	{
		get
		{
			return _boundaryConditionLine;
		}
		[MemberNotNull("_boundaryConditionLine")]
		set
		{
			if (!EqualityComparer<ObservableBoundaryConditionLine>.Default.Equals(_boundaryConditionLine, value))
			{
				OnPropertyChanging("BoundaryConditionLine");
				_boundaryConditionLine = value;
				OnPropertyChanged("BoundaryConditionLine");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ObservablePayload Payload
	{
		get
		{
			return _payload;
		}
		[MemberNotNull("_payload")]
		set
		{
			if (!EqualityComparer<ObservablePayload>.Default.Equals(_payload, value))
			{
				OnPropertyChanging("Payload");
				_payload = value;
				OnPropertyChanged("Payload");
			}
		}
	}

	public ObservablePayloadWithContextWrapper(ObservablePayload payload, ObservableBoundaryConditionLine bc)
	{
		Payload = payload;
		BoundaryConditionLine = bc;
	}
}
