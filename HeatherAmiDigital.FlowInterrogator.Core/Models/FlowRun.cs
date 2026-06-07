using System;

namespace HeatherAmiDigital.FlowInterrogator.Core.Models;

/// <summary>
/// Represents the execution status of a flow run or action, mirroring the standard
/// Logic Apps / Power Automate run status values.
/// </summary>
public enum FlowRunStatus
{
    /// <summary>The execution completed successfully.</summary>
    Succeeded,

    /// <summary>The execution failed due to an error.</summary>
    Failed,

    /// <summary>The execution was cancelled by a user or system process.</summary>
    Cancelled,

    /// <summary>The execution was skipped due to branch conditions.</summary>
    Skipped,

    /// <summary>The execution was aborted.</summary>
    Aborted,

    /// <summary>The execution is waiting for an external event or approval.</summary>
    Waiting,

    /// <summary>An unrecognized status was returned by the API.</summary>
    Unknown
}

/// <summary>
/// Represents a single execution (run) of a Power Automate cloud flow,
/// retrieved from the Power Automate Management API.
/// </summary>
public sealed class FlowRun
{
    /// <summary>
    /// Gets or sets the unique identifier of the run.
    /// This is a string (e.g., <c>08585...</c>) rather than a GUID, as dictated by the Logic Apps runtime.
    /// </summary>
    public string RunId { get; set; }

    /// <summary>
    /// Gets or sets the Dataverse identifier of the flow that was executed.
    /// </summary>
    public Guid FlowId { get; set; }

    /// <summary>
    /// Gets or sets the display name of the flow.
    /// </summary>
    public string FlowName { get; set; }

    /// <summary>
    /// Gets or sets the final status of the run.
    /// </summary>
    public FlowRunStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the run started.
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the run ended. Null if the run is still in progress.
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// Gets or sets the internal name of the trigger that initiated the run.
    /// </summary>
    public string TriggerName { get; set; }

    /// <summary>
    /// Gets or sets the client tracking ID (correlation ID) for the run.
    /// Crucial for cross-referencing with downstream systems or Dataverse plugin trace logs.
    /// </summary>
    public string CorrelationId { get; set; }

    /// <summary>
    /// Gets or sets the Power Platform environment identifier.
    /// </summary>
    public string EnvironmentId { get; set; }

    /// <summary>
    /// Returns the canonical Power Automate portal URL for this specific run.
    /// </summary>
    public string GetPortalUrl()
    {
        if (string.IsNullOrWhiteSpace(EnvironmentId))
        {
            return null;
        }

        return $"https://make.powerautomate.com/environments/{EnvironmentId}/flows/{FlowId}/runs/{RunId}";
    }
}
