using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Rendering;
using Utility;

namespace Visual.Mapping.Observables.Symbology;

public class ObservableVectorSymbology : ObservableObject
{
	private readonly VectorSymbology _symbology;

	private readonly ISynchronizationContext _syncContext;

	private bool _initializing;

	public VectorSymbology Symbology => _symbology;
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public Color PenColor
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<Color>.Default.Equals(field, value))
			{
				Color oldValue = field;
				OnPropertyChanging("PenColor");
				field = value;
				OnPenColorChanged(oldValue, value);
				OnPropertyChanged("PenColor");
			}
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public double PenWidth
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<double>.Default.Equals(field, value))
			{
				double oldValue = field;
				OnPropertyChanging("PenWidth");
				field = value;
				OnPenWidthChanged(oldValue, value);
				OnPropertyChanged("PenWidth");
			}
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public Color FillColor
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<Color>.Default.Equals(field, value))
			{
				Color oldValue = field;
				OnPropertyChanging("FillColor");
				field = value;
				OnFillColorChanged(oldValue, value);
				OnPropertyChanged("FillColor");
			}
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public int PointSize
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<int>.Default.Equals(field, value))
			{
				int oldValue = field;
				OnPropertyChanging("PointSize");
				field = value;
				OnPointSizeChanged(oldValue, value);
				OnPropertyChanged("PointSize");
			}
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HasShadow
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				bool oldValue = field;
				OnPropertyChanging("HasShadow");
				field = value;
				OnHasShadowChanged(oldValue, value);
				OnPropertyChanged("HasShadow");
			}
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsDotted
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				bool oldValue = field;
				OnPropertyChanging("IsDotted");
				field = value;
				OnIsDottedChanged(oldValue, value);
				OnPropertyChanged("IsDotted");
			}
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public float[] DottedStrokeStyle
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<float[]>.Default.Equals(field, value))
			{
				float[] oldValue = field;
				OnPropertyChanging("DottedStrokeStyle");
				field = value;
				OnDottedStrokeStyleChanged(oldValue, value);
				OnPropertyChanged("DottedStrokeStyle");
			}
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsTranslated
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				bool oldValue = field;
				OnPropertyChanging("IsTranslated");
				field = value;
				OnIsTranslatedChanged(oldValue, value);
				OnPropertyChanged("IsTranslated");
			}
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public float[] Translation
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<float[]>.Default.Equals(field, value))
			{
				float[] oldValue = field;
				OnPropertyChanging("Translation");
				field = value;
				OnTranslationChanged(oldValue, value);
				OnPropertyChanged("Translation");
			}
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public Color HaloColor
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<Color>.Default.Equals(field, value))
			{
				Color oldValue = field;
				OnPropertyChanging("HaloColor");
				field = value;
				OnHaloColorChanged(oldValue, value);
				OnPropertyChanged("HaloColor");
			}
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public float HaloThickness
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<float>.Default.Equals(field, value))
			{
				float oldValue = field;
				OnPropertyChanging("HaloThickness");
				field = value;
				OnHaloThicknessChanged(oldValue, value);
				OnPropertyChanged("HaloThickness");
			}
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Font
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(field, value))
			{
				string oldValue = field;
				OnPropertyChanging("Font");
				field = value;
				OnFontChanged(oldValue, value);
				OnPropertyChanged("Font");
			}
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public int TextEmphasis
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<int>.Default.Equals(field, value))
			{
				int oldValue = field;
				OnPropertyChanging("TextEmphasis");
				field = value;
				OnTextEmphasisChanged(oldValue, value);
				OnPropertyChanged("TextEmphasis");
			}
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public int TextMaxWidth
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<int>.Default.Equals(field, value))
			{
				int oldValue = field;
				OnPropertyChanging("TextMaxWidth");
				field = value;
				OnTextMaxWidthChanged(oldValue, value);
				OnPropertyChanged("TextMaxWidth");
			}
		}
	}

	public ObservableVectorSymbology(VectorSymbology symb, ISynchronizationContext syncContext)
	{
		_symbology = symb;
		_syncContext = syncContext;
		_initializing = true;
		PenColor = symb.PenColor;
		PenWidth = symb.PenWidth;
		FillColor = symb.FillColor;
		PointSize = symb.PointSize;
		HasShadow = symb.HasShadow;
		IsDotted = symb.IsDotted;
		DottedStrokeStyle = symb.DottedStrokeStyle;
		IsTranslated = symb.IsTranslated;
		Translation = symb.Translation;
		HaloColor = symb.HaloColor;
		HaloThickness = symb.HaloThickness;
		Font = symb.Font;
		TextEmphasis = symb.TextEmphasis;
		TextMaxWidth = symb.TextMaxWidth;
		_initializing = false;
	}

	private void SyncToModel(Action action)
	{
		if (!_initializing)
		{
			_syncContext.Sync(action);
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnPenColorChanged(Color oldValue, Color newValue)
	{
		SyncToModel(() =>
		{
			Symbology.PenColor = newValue;
		});
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnPenWidthChanged(double oldValue, double newValue)
	{
		SyncToModel(() =>
		{
			Symbology.PenWidth = newValue;
		});
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnFillColorChanged(Color oldValue, Color newValue)
	{
		SyncToModel(() =>
		{
			Symbology.FillColor = newValue;
		});
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnPointSizeChanged(int oldValue, int newValue)
	{
		SyncToModel(() =>
		{
			Symbology.PointSize = newValue;
		});
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnHasShadowChanged(bool oldValue, bool newValue)
	{
		SyncToModel(() =>
		{
			Symbology.HasShadow = newValue;
		});
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnIsDottedChanged(bool oldValue, bool newValue)
	{
		SyncToModel(() =>
		{
			Symbology.IsDotted = newValue;
		});
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnDottedStrokeStyleChanged(float[] oldValue, float[] newValue)
	{
		SyncToModel(() =>
		{
			Symbology.DottedStrokeStyle = newValue;
		});
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnIsTranslatedChanged(bool oldValue, bool newValue)
	{
		SyncToModel(() =>
		{
			Symbology.IsTranslated = newValue;
		});
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnTranslationChanged(float[] oldValue, float[] newValue)
	{
		SyncToModel(() =>
		{
			Symbology.Translation = newValue;
		});
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnHaloColorChanged(Color oldValue, Color newValue)
	{
		SyncToModel(() =>
		{
			Symbology.HaloColor = newValue;
		});
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnHaloThicknessChanged(float oldValue, float newValue)
	{
		SyncToModel(() =>
		{
			Symbology.HaloThickness = newValue;
		});
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnFontChanged(string oldValue, string newValue)
	{
		SyncToModel(() =>
		{
			Symbology.Font = newValue;
		});
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnTextEmphasisChanged(int oldValue, int newValue)
	{
		SyncToModel(() =>
		{
			Symbology.TextEmphasis = newValue;
		});
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnTextMaxWidthChanged(int oldValue, int newValue)
	{
		SyncToModel(() =>
		{
			Symbology.TextMaxWidth = newValue;
		});
	}
}
