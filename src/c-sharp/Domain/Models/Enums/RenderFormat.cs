namespace Archlens.Domain.Models.Enums;

public enum RenderFormat
{
    Json,
    PlantUML
}

public static class RenderFormatExtensions
{
    public static string ToFileExtension(this RenderFormat format)
    {
        return format switch
        {
            RenderFormat.Json => "json",
            RenderFormat.PlantUML => "puml",
            _ => format.ToString(),
        };
    }
}