using System;

namespace HeatherAmiDigital.FlowInterrogator.Core.Models;

/// <summary>
/// Lightweight summary of a Power Automate cloud flow retrieved from the Dataverse
/// <c>workflow</c> entity. Contains the metadata required to render a flow in a list
/// or grid; the serialised flow definition is held separately in <c>FlowDefinition</c>.
/// </summary>
public sealed class FlowSummary
{
    /// <summary>
    /// Gets or sets the Dataverse <c>workflowid</c> of the flow.
    /// This is the canonical identifier used in both Dataverse queries and Power Automate deep links.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the user-visible name of the flow.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the flow description, if any has been provided by the author.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the activation state of the flow.
    /// </summary>
    public FlowState State { get; set; }

    /// <summary>
    /// Gets or sets the timestamp at which the flow record was created in Dataverse.
    /// </summary>
    public DateTime? CreatedOn { get; set; }

    /// <summary>
    /// Gets or sets the timestamp at which the flow record was last modified in Dataverse.
    /// </summary>
    public DateTime? ModifiedOn { get; set; }

    /// <summary>
    /// Gets or sets the Dataverse identifier of the user or team that owns the flow.
    /// </summary>
    public Guid? OwnerId { get; set; }

    /// <summary>
    /// Gets or sets the display name of the flow owner, resolved at query time for grid display.
    /// </summary>
    public string OwnerName { get; set; }

    /// <summary>
    /// Gets or sets the Power Platform environment identifier in the
    /// <c>Default-{tenantId}</c> or GUID form expected by Power Automate URLs.
    /// Populated by the query layer so callers do not need to know the source environment.
    /// </summary>
    public string EnvironmentId { get; set; }

    /// <summary>
    /// Returns the canonical Power Automate portal URL for this flow, or <see langword="null"/>
    /// if <see cref="EnvironmentId"/> has not been set.
    /// </summary>
    /// <returns>
    /// A deep link of the form
    /// <c>https://make.powerautomate.com/environments/{environmentId}/flows/{flowId}/details</c>,
    /// or <see langword="null"/> when the environment is unknown.
    /// </returns>
    public string GetPortalUrl()
    {
        if (string.IsNullOrWhiteSpace(EnvironmentId))
        {
            return null;
        }

        return $"https://make.powerautomate.com/environments/{EnvironmentId}/flows/{Id}/details";
    }
}
