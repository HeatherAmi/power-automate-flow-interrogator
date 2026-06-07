using System.Configuration;
using XrmToolBox.Extensibility;

namespace HeatherAmiDigital.FlowInterrogator.XrmToolBox;

/// <summary>
/// Persists user preferences and plugin state across XrmToolBox sessions.
/// </summary>
public sealed class FlowInterrogatorSettings : SettingsBase
{
    /// <summary>
    /// Gets or sets the default number of days to look back when querying flow run history.
    /// </summary>
    public int DefaultRunHistoryDays { get; set; } = 7;

    /// <summary>
    /// Gets or sets a value indicating whether to automatically expand failed actions in the run detail view.
    /// </summary>
    public bool AutoExpandFailedActions { get; set; } = true;
}
