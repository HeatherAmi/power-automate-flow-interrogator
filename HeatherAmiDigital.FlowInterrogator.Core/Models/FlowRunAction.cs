using System;
using Newtonsoft.Json.Linq;

namespace HeatherAmiDigital.FlowInterrogator.Core.Models;

/// <summary>
/// Represents the execution details of a single action within a specific flow run.
/// Contains the runtime inputs, outputs, and error details required for deep debugging.
/// </summary>
public sealed class FlowRunAction
{
    /// <summary>
    /// Gets or sets the internal name of the action.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the execution status of the action.
    /// </summary>
    public FlowRunStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the action started executing.
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the action finished executing.
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// Gets or sets the error code if the action failed.
    /// </summary>
    public string ErrorCode { get; set; }

    /// <summary>
    /// Gets or sets the detailed error message if the action failed.
    /// </summary>
    public string ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the raw JSON inputs passed to the action at runtime.
    /// Retained as a <see cref="JToken"/> to allow the UI to format or query the payload dynamically.
    /// </summary>
    public JToken Inputs { get; set; }

    /// <summary>
    /// Gets or sets the raw JSON outputs produced by the action at runtime.
    /// Retained as a <see cref="JToken"/> to allow the UI to format or query the payload dynamically.
    /// </summary>
    public JToken Outputs { get; set; }
}
