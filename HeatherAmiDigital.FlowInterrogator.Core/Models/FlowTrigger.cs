using Newtonsoft.Json.Linq;

namespace HeatherAmiDigital.FlowInterrogator.Core.Models;

/// <summary>
/// Represents a trigger within a Power Automate cloud flow definition.
/// </summary>
public sealed class FlowTrigger
{
    /// <summary>
    /// Gets or sets the internal name of the trigger as defined in the flow JSON.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the connector type of the trigger (e.g., <c>ApiConnection</c>, <c>Recurrence</c>).
    /// </summary>
    public string Type { get; set; }

    /// <summary>
    /// Gets or sets the kind of trigger (e.g., <c>Shared</c>, <c>VirtualNetwork</c>).
    /// </summary>
    public string Kind { get; set; }

    /// <summary>
    /// Gets or sets the raw JSON representation of the trigger.
    /// Retained to support deep text search and to preserve properties not explicitly mapped.
    /// </summary>
    public JToken RawJson { get; set; }
}
