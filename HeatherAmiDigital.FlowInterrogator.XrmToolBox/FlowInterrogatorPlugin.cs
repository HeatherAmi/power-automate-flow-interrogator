using System;
using System.ComponentModel.Composition;
using System.Net.Http;
using System.Windows.Forms;
using HeatherAmiDigital.FlowInterrogator.Core.Services;
using McTools.Xrm.Connection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace HeatherAmiDigital.FlowInterrogator.XrmToolBox;

/// <summary>
/// MEF entry point discovered by XrmToolBox. Acts as the plugin factory:
/// XrmToolBox imports <see cref="IXrmToolBoxPlugin"/> exports and calls
/// <see cref="GetControl"/> to instantiate the tool's UI.
/// </summary>
[Export(typeof(IXrmToolBoxPlugin)),
 ExportMetadata("Name", "Power Automate Flow Interrogator"),
 ExportMetadata("Description", "Search and investigate Power Automate cloud flows and run history."),
 ExportMetadata("BackgroundColor", "White"),
 ExportMetadata("PrimaryFontColor", "#333333"),
 ExportMetadata("SecondaryFontColor", "#666666"),
 ExportMetadata("SmallImageBase64", PluginIcon.SmallBase64),
 ExportMetadata("BigImageBase64", PluginIcon.SmallBase64)]
public sealed class FlowInterrogatorPluginFactory : PluginBase
{
    /// <summary>
    /// Creates the tool's main control. Invoked by XrmToolBox each time the tool is opened.
    /// </summary>
    public override IXrmToolBoxPluginControl GetControl() => new FlowInterrogatorPlugin();
}

/// <summary>
/// Hosts the plugin UI, owns the Dataverse connection lifecycle, and builds the
/// Dependency Injection container for the Core services.
/// </summary>
public sealed class FlowInterrogatorPlugin : PluginControlBase
{
    private readonly MainControl _mainControl;
    private ServiceProvider _serviceProvider;

    /// <summary>
    /// Gets the persisted user settings for this tool, loaded once at construction and
    /// saved when the tool closes.
    /// </summary>
    public FlowInterrogatorSettings Settings { get; }

    /// <summary>
    /// Initializes the plugin UI, loads settings, and registers lifecycle handlers.
    /// </summary>
    public FlowInterrogatorPlugin()
    {
        Settings = LoadSettings();

        _mainControl = new MainControl
        {
            Dock = DockStyle.Fill,
            Host = this,
            Settings = Settings
        };
        Controls.Add(_mainControl);

        OnCloseTool += (sender, e) =>
        {
            SettingsManager.Instance.Save(typeof(FlowInterrogatorSettings), Settings);
            _serviceProvider?.Dispose();
        };
    }

    /// <summary>
    /// Invoked by XrmToolBox when a new Dataverse connection is established or changed.
    /// Rebuilds the DI container with the new <see cref="IOrganizationService"/>.
    /// </summary>
    public override void UpdateConnection(IOrganizationService newService, ConnectionDetail detail, string actionName, object parameter)
    {
        base.UpdateConnection(newService, detail, actionName, parameter);
        InitializeServices(detail);
    }

    /// <summary>
    /// Loads the persisted settings, falling back to defaults when none exist or loading fails.
    /// </summary>
    private static FlowInterrogatorSettings LoadSettings()
    {
        try
        {
            if (SettingsManager.Instance.TryLoad(typeof(FlowInterrogatorSettings), out FlowInterrogatorSettings loaded) && loaded != null)
            {
                return loaded;
            }
        }
        catch
        {
            // A corrupt or missing settings file must never block the tool from opening.
        }

        return new FlowInterrogatorSettings();
    }

    /// <summary>
    /// Configures and builds the Dependency Injection container for the Core services.
    /// </summary>
    private void InitializeServices(ConnectionDetail detail)
    {
        var environmentId = ExtractEnvironmentId(detail);

        var services = new ServiceCollection();

        services.AddSingleton<IFlowLogger>(new XrmToolBoxFlowLogger(this));
        services.AddSingleton<FlowParser>();

        services.AddSingleton(sp => new FlowQueryService(
            Service,
            sp.GetRequiredService<FlowParser>(),
            sp.GetRequiredService<IFlowLogger>())
        {
            EnvironmentId = environmentId
        });

        services.AddSingleton(sp => new PowerAutomateAuthService(
            result => _mainControl.ShowDeviceCodeAsync(result)));
        services.AddSingleton<ITokenProvider>(sp => sp.GetRequiredService<PowerAutomateAuthService>());

        services.AddSingleton<HttpClient>();
        services.AddSingleton(sp => new FlowRunService(
            sp.GetRequiredService<HttpClient>(),
            sp.GetRequiredService<ITokenProvider>(),
            sp.GetRequiredService<IFlowLogger>()));

        _serviceProvider?.Dispose();
        _serviceProvider = services.BuildServiceProvider();

        _mainControl.ServiceProvider = _serviceProvider;
        _mainControl.OnConnectionUpdated();
    }

    /// <summary>
    /// Extracts the Power Platform Environment ID from the XrmToolBox connection details.
    /// </summary>
    private static string ExtractEnvironmentId(ConnectionDetail detail)
    {
        if (detail == null) return null;

        // Modern Dataverse connections expose the Environment ID directly.
        if (!string.IsNullOrWhiteSpace(detail.EnvironmentId))
        {
            return detail.EnvironmentId;
        }

        // Fallback for older connection types (yields the org URL, not a Default-{guid} id;
        // deep links may be inaccurate — documented as out of scope for v0.1).
        return detail.Organization?.ToString();
    }
}
