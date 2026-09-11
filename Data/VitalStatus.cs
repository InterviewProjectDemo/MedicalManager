namespace MedicalManager.Data;

public readonly record struct StatusTag(string Label, string CssClass);

public static class VitalStatus
{
    public static StatusTag ForBloodPressure(int systolic, int diastolic)
    {
        if (systolic >= 180 || diastolic >= 120)
            return new("Crisis", "status-danger");
        if (systolic >= 140 || diastolic >= 90)
            return new("High", "status-danger");
        if (systolic >= 130 || diastolic >= 80)
            return new("Elevated", "status-warn");
        if (systolic >= 120)
            return new("Watch", "status-warn");
        return new("Normal", "status-ok");
    }

    public static StatusTag ForSugar(decimal value, SugarKind kind)
    {
        var afterMeal = kind is SugarKind.AfterMeal;
        if (value < 70)
            return new("Low", "status-danger");
        if (afterMeal)
        {
            if (value >= 200) return new("High", "status-danger");
            if (value >= 140) return new("Elevated", "status-warn");
            return new("Normal", "status-ok");
        }

        if (value >= 126) return new("High", "status-danger");
        if (value >= 100) return new("Elevated", "status-warn");
        return new("Normal", "status-ok");
    }

    public static bool IsBloodPressureOutOfRange(int systolic, int diastolic) =>
        ForBloodPressure(systolic, diastolic).CssClass == "status-danger";

    public static bool IsSugarOutOfRange(decimal value, SugarKind kind) =>
        ForSugar(value, kind).CssClass == "status-danger";
}
