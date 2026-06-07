namespace HeatherAmiDigital.FlowInterrogator.Core.Models;

/// <summary>
/// Represents the activation state of a cloud flow, mirroring the <c>statecode</c>
/// option set on the Dataverse <c>workflow</c> entity.
/// </summary>
public enum FlowState
{
    /// <summary>
    /// The flow exists but has never been activated, or has been reverted to draft.
    /// Corresponds to <c>statecode = 0</c>.
    /// </summary>
    Draft = 0,

    /// <summary>
    /// The flow is turned on and will respond to its trigger.
    /// Corresponds to <c>statecode = 1</c>.
    /// </summary>
    Activated = 1,

    /// <summary>
    /// The flow has been suspended (turned off) by a user or by the platform after repeated failures.
    /// Corresponds to <c>statecode = 2</c>.
    /// </summary>
    Suspended = 2
}
