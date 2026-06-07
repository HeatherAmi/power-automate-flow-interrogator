using System;

namespace HeatherAmiDigital.FlowInterrogator.Core.Models;

/// <summary>
/// Defines the specific area within a flow definition where a search match was found.
/// </summary>
public enum FlowMatchLocation
{
    /// <summary>Match found in the flow's global parameters or metadata.</summary>
    Parameter,

    /// <summary>Match found within a trigger definition.</summary>
    Trigger,

    /// <summary>Match found within an action definition.</summary>
    Action,

    /// <summary>Match found in the raw definition JSON but not mapped to a specific node.</summary>
    RawDefinition
}

/// <summary>
/// Represents a single search hit within a flow definition.
/// Used to populate the search results grid when a user queries for a specific GUID, email, or string.
/// </summary>
public sealed class FlowMatch
{
    /// <summary>
    /// Gets or sets the Dataverse identifier of the flow containing the match.
    /// </summary>
    public Guid FlowId { get; set; }

    /// <summary>
    /// Gets or sets the display name of the flow containing the match.
    /// </summary>
    public string FlowName { get; set; }

    /// <summary>
    /// Gets or sets the specific area of the flow where the match occurred.
    /// </summary>
    public FlowMatchLocation Location { get; set; }

    /// <summary>
    /// Gets or sets the internal name of the node (trigger or action) where the match was found.
    /// Null if the match is at the flow or parameter level.
    /// </summary>
    public string NodeName { get; set; }

    /// <summary>
    /// Gets or sets a short text snippet showing the matched value in context.
    /// </summary>
    public string MatchSnippet { get; set; }
}
