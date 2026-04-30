using System;

namespace Shade.Models;

public enum LogStream { Stdout, Stderr, System }

public sealed record LogLine(DateTime Timestamp, LogStream Stream, string Text)
{
    public Guid Id { get; } = Guid.NewGuid();

    public static LogLine System(string text) => new(DateTime.UtcNow, LogStream.System, text);
    public static LogLine Stdout(string text) => new(DateTime.UtcNow, LogStream.Stdout, text);
    public static LogLine Stderr(string text) => new(DateTime.UtcNow, LogStream.Stderr, text);
}
