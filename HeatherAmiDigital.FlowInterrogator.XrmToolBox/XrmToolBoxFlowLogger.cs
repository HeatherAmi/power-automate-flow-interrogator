using System;
using HeatherAmiDigital.FlowInterrogator.Core.Services;
using XrmToolBox.Extensibility;

namespace HeatherAmiDigital.FlowInterrogator.XrmToolBox;

/// <summary>
/// Adapts the Core <see cref="IFlowLogger"/> abstraction onto the XrmToolBox plugin log,
/// surfacing Core service diagnostics in the tool's log window.
/// </summary>
public sealed class XrmToolBoxFlowLogger : IFlowLogger
{
    private readonly PluginControlBase _plugin;

    /// <summary>
    /// Initializes a new instance of the <see cref="XrmToolBoxFlowLogger"/> class.
    /// </summary>
    /// <param name="plugin">The plugin whose log surface receives the messages.</param>
    public XrmToolBoxFlowLogger(PluginControlBase plugin)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
    }

    /// <inheritdoc />
    public void Info(string message) => _plugin.LogInfo("{0}", message);

    /// <inheritdoc />
    public void Warning(string message, Exception exception = null)
    {
        _plugin.LogWarning("{0}", message);
        if (exception != null)
        {
            _plugin.LogWarning("{0}", exception);
        }
    }

    /// <inheritdoc />
    public void Error(string message, Exception exception = null)
    {
        _plugin.LogError("{0}", message);
        if (exception != null)
        {
            _plugin.LogError("{0}", exception);
        }
    }
}
