using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace HeatherAmiDigital.FlowInterrogator.Core.Models;

/// <summary>
/// Represents a single action within a Power Automate cloud flow definition.
/// </summary>
public sealed class FlowAction
{
    /// <summary>
    /// Gets or sets the internal name of the action as defined in the flow JSON.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the connector type of the action (e.g., <c>ApiConnection</c>, <c>Compose</c>).
    /// </summary>
    public string Type { get; set; }

    /// <summary>
    /// Gets or sets the kind of action (e.g., <c>Shared</c>, <c>Http</c>).
    /// </summary>
    public string Kind { get; set; }

    /// <summary>
    /// Gets or sets the execution dependencies for this action.
    /// The key is the name of the preceding action, and the value is a list of
    /// terminal statuses (e.g., <c>Succeeded</c>, <c>Failed</c>) that will cause this action to run.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> RunAfter { get; set; }

    /// <summary>
    /// Gets or sets the raw JSON representation of the action.
    /// Retained to support deep text search and to preserve properties not explicitly mapped.
    /// </summary>
    public JToken RawJson { get; set; }
}
