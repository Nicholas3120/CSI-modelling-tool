using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace CSIModellingTools.Controls;

public partial class SliderNumberInput : UserControl, INotifyPropertyChanged
{
    private bool _updatingText;
    private string _valueText = "0";

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(SliderNumberInput), new PropertyMetadata(""));

    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(nameof(Unit), typeof(string), typeof(SliderNumberInput), new PropertyMetadata(""));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(double),
            typeof(SliderNumberInput),
            new FrameworkPropertyMetadata(
                0.0,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnValueChanged,
                CoerceValue));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(
            nameof(Minimum),
            typeof(double),
            typeof(SliderNumberInput),
            new PropertyMetadata(0.0, OnLimitChanged));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(
            nameof(Maximum),
            typeof(double),
            typeof(SliderNumberInput),
            new PropertyMetadata(100.0, OnLimitChanged));

    public static readonly DependencyProperty SmallChangeProperty =
        DependencyProperty.Register(nameof(SmallChange), typeof(double), typeof(SliderNumberInput), new PropertyMetadata(1.0));

    public static readonly DependencyProperty LargeChangeProperty =
        DependencyProperty.Register(nameof(LargeChange), typeof(double), typeof(SliderNumberInput), new PropertyMetadata(10.0));

    public static readonly DependencyProperty DecimalPlacesProperty =
        DependencyProperty.Register(
            nameof(DecimalPlaces),
            typeof(int),
            typeof(SliderNumberInput),
            new PropertyMetadata(3, OnFormatChanged));

    public static readonly DependencyProperty IsIntegerProperty =
        DependencyProperty.Register(
            nameof(IsInteger),
            typeof(bool),
            typeof(SliderNumberInput),
            new PropertyMetadata(false, OnFormatChanged));

    public SliderNumberInput()
    {
        InitializeComponent();
        UpdateValueText();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double SmallChange
    {
        get => (double)GetValue(SmallChangeProperty);
        set => SetValue(SmallChangeProperty, value);
    }

    public double LargeChange
    {
        get => (double)GetValue(LargeChangeProperty);
        set => SetValue(LargeChangeProperty, value);
    }

    public int DecimalPlaces
    {
        get => (int)GetValue(DecimalPlacesProperty);
        set => SetValue(DecimalPlacesProperty, value);
    }

    public bool IsInteger
    {
        get => (bool)GetValue(IsIntegerProperty);
        set => SetValue(IsIntegerProperty, value);
    }

    public string ValueText
    {
        get => _valueText;
        set
        {
            if (_valueText == value)
                return;

            _valueText = value ?? "";
            OnPropertyChanged(nameof(ValueText));

            if (_updatingText)
                return;

            if (double.TryParse(_valueText, NumberStyles.Float, CultureInfo.CurrentCulture, out double parsed) ||
                double.TryParse(_valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                Value = parsed;
            }
        }
    }

    private static object CoerceValue(DependencyObject dependencyObject, object baseValue)
    {
        var input = (SliderNumberInput)dependencyObject;
        double value = baseValue is double number && double.IsFinite(number) ? number : 0.0;
        double minimum = Math.Min(input.Minimum, input.Maximum);
        double maximum = Math.Max(input.Minimum, input.Maximum);
        value = Math.Clamp(value, minimum, maximum);

        if (input.IsInteger)
            value = Math.Round(value, MidpointRounding.AwayFromZero);
        else if (input.SmallChange > 0 && double.IsFinite(input.SmallChange))
            value = Math.Round(value / input.SmallChange, MidpointRounding.AwayFromZero) * input.SmallChange;

        return value;
    }

    private static void OnValueChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        ((SliderNumberInput)dependencyObject).UpdateValueText();
    }

    private static void OnLimitChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        dependencyObject.CoerceValue(ValueProperty);
    }

    private static void OnFormatChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var input = (SliderNumberInput)dependencyObject;
        dependencyObject.CoerceValue(ValueProperty);
        input.UpdateValueText();
    }

    private void UpdateValueText()
    {
        _updatingText = true;
        try
        {
            ValueText = FormatValue(Value);
        }
        finally
        {
            _updatingText = false;
        }
    }

    private string FormatValue(double value)
    {
        if (IsInteger)
            return Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.CurrentCulture);

        int places = Math.Clamp(DecimalPlaces, 0, 6);
        string format = places == 0 ? "0" : "0." + new string('#', places);
        return value.ToString(format, CultureInfo.CurrentCulture);
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
