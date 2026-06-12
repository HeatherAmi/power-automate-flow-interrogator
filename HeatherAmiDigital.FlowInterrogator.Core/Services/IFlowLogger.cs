using System;

namespace HeatherAmiDigital.FlowInterrogator.Core.Services;

/// <summary>
/// Abstraction for surfacing diagnostic messages from the Core services to a host
/// (e.g., the XrmToolBox log window). Core ships a no-op default
/// (<see cref="NullFlowLogger"/>); the hosting layer supplies an adapter that
/// delegates to its own logging surface.
/// </summary>
public interface IFlowLogger
{
    /// <summary>Logs an informational message.</summary>
    /// <param name="message">The message to log.</param>
    void Info(string message);

    /// <summary>Logs a warning, optionally with an associated exception.</summary>
    /// <param name="message">The message to log.</param>
    /// <param name="exception">Optional exception providing additional context.</param>
    void Warning(string message, Exception exception = null);

    /// <summary>Logs an error, optionally with an associated exception.</summary>
    /// <param name="message">The message to log.</param>
    /// <param name="exception">Optional exception providing additional context.</param>
    void Error(string message, Exception exception = null);
}

/// <summary>
/// A logger that discards all messages. Used as the default when the hosting
/// layer does not supply a concrete logger, so Core services never need to
/// null-check their logger dependency.
/// </summary>
public sealed class NullFlowLogger : IFlowLogger
{
    /// <summary>Gets the shared singleton instance.</summary>
    public static readonly NullFlowLogger Instance = new NullFlowLogger();

    private NullFlowLogger()
    {
    }

    /// <inheritdoc />
    public void Info(string message)
    {
    }

    /// <inheritdoc />
    public void Warning(string message, Exception exception = null)
    {
    }

    /// <inheritdoc />
    public void Error(string message, Exception exception = null)
    {
    }
}
