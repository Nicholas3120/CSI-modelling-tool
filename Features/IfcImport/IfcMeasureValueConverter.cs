using System.Globalization;
using System.Reflection;

namespace CSIModellingTools.Features.IfcImport;

internal static class IfcMeasureValueConverter
{
    public static double ToDouble(object? value)
    {
        if (value == null)
            return double.NaN;

        if (value is IConvertible convertible)
            return Convert.ToDouble(convertible, CultureInfo.InvariantCulture);

        PropertyInfo? valueProperty = value.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
        if (valueProperty != null)
            return ToDouble(valueProperty.GetValue(value));

        string text = value.ToString() ?? "";
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            return parsed;

        throw new InvalidCastException($"Unable to convert IFC measure value of type '{value.GetType().FullName}' to double.");
    }
}
