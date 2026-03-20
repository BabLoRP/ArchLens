using System;

namespace Archlens.Infra;

public class Logger : ILogger
{
    public void LogInformation(string input)
    {
        Console.WriteLine($"INFO: {input}");
    }

    public void LogWarning(string input)
    {
        Console.WriteLine($"WARN: {input}");
    }

    public void LogError(string input)
    {
        Console.WriteLine($"ERROR: {input}");
    }
}
