using System.ComponentModel.DataAnnotations;

namespace MedicalManager.Data;

[AttributeUsage(AttributeTargets.Property)]
public sealed class VitalRangeAttribute : ValidationAttribute
{
    public VitalRangeAttribute(double minimum, double maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
    }

    public double Minimum { get; }

    public double Maximum { get; }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null)
            return ValidationResult.Success;

        if (validationContext.ObjectInstance is IAllowOutOfRange { AllowOutOfRange: true })
            return ValidationResult.Success;

        double number;
        try
        {
            number = Convert.ToDouble(value);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return new ValidationResult("Enter a number.");
        }

        if (number >= Minimum && number <= Maximum)
            return ValidationResult.Success;

        return new ValidationResult(
            $"{validationContext.DisplayName} must be from {Minimum:0.##} to {Maximum:0.##}. Use Overwrite to save a value outside that range.");
    }
}
