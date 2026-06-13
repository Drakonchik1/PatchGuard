namespace PatchGuard.Models;

public enum SensorKind
{
    Temperature,
    Load,
    Clock,
    Fan,
    Power,
    Data,
    Voltage,
    Other
}

public sealed record SensorReading
{
    public required string Hardware { get; init; }
    public required string Name { get; init; }
    public required SensorKind Kind { get; init; }
    public double Value { get; init; }
    public string Unit { get; init; } = string.Empty;

    public string DisplayValue => Kind switch
    {
        SensorKind.Temperature => $"{Value:F0} °C",
        SensorKind.Load => $"{Value:F0} %",
        SensorKind.Clock => $"{Value:F0} MHz",
        SensorKind.Fan => $"{Value:F0} RPM",
        SensorKind.Power => $"{Value:F1} W",
        SensorKind.Voltage => $"{Value:F2} V",
        SensorKind.Data => $"{Value:F1} {Unit}",
        _ => $"{Value:F1} {Unit}".Trim()
    };
}
