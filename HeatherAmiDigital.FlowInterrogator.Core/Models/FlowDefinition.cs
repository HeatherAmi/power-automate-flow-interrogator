using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace HeatherAmiDigital.FlowInterrogator.Core.Models;

/// <summary>
/// Represents the parsed execution definition of a Power Automate cloud flow.
/// Extracted from the <c>clientdata</c> or <c>definition</c> column of the Dataverse workflow entity.
/// </summary>
public sealed class FlowDefinition
{
    /// <summary>
    /// Gets or sets the Dataverse <c>workflowid</c> of the flow this definition belongs to.
    /// </summary>
    public Guid FlowId { get; set; }

    /// <summary>
    /// Gets or sets the schema version of the flow definition (e.g., <c>2016-04-01</c>).
    /// </summary>
    public string Schema { get; set; }

    /// <summary>
    /// Gets or sets the content version of the flow definition.
    /// </summary>
    public string ContentVersion { get; set; }

    /// <summary>
    /// Gets or sets the top-level parameters defined for the flow.
    /// </summary>
    public JToken Parameters { get; set; }

    /// <summary>
    /// Gets or sets the triggers configured for the flow.
    /// The key is the internal name of the trigger.
    /// </summary>
    public IReadOnlyDictionary<string, FlowTrigger> Triggers { get; set; }

    /// <summary>
    /// Gets or sets the actions configured for the flow.
    /// The key is the internal name of the action.
    /// </summary>
    public IReadOnlyDictionary<string, FlowAction> Actions { get; set; }

    /// <summary>
    /// Gets or sets the raw JSON representation of the entire definition.
    /// Used as a fallback for deep searching across the entire document.
    /// </summary>
    public JToken RawJson { get; set; }
}
