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
/// Main entry point for the XrmToolBox plugin. 
/// Handles MEF export, connection lifecycle, and Dependency Injection configuration.
/// </summary>
[Export(typeof(IXrmToolBoxPluginInterface)),
 ExportMetadata("Name", "Power Automate Flow Interrogator"),
 ExportMetadata("Description", "Search and investigate Power Automate cloud flows and run history."),
 ExportMetadata("IconBase64", ""),
 ExportMetadata("BackgroundColor", "White"),
 ExportMetadata("PrimaryFontColor", "#333333"),
 ExportMetadata("SecondaryFontColor", "#666666")]
public sealed class FlowInterrogatorPlugin : PluginControlBase
{
    private MainControl _mainControl;
    private ServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes the plugin UI and registers event handlers.
    /// </summary>
    public FlowInterrogatorPlugin()
    {
        _mainControl = new MainControl();
        _mainControl.Dock = DockStyle.Fill;
        Controls.Add(_mainControl);

        // Subscribe to the close event to clean up DI resources when the tool is closed
        this.OnCloseTool += (sender, e) => _serviceProvider?.Dispose();
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
    /// Configures and builds the Dependency Injection container for the Core services.
    /// </summary>
    private void InitializeServices(ConnectionDetail detail)
    {
        var services = new ServiceCollection();

        services.AddSingleton<FlowParser>();

        services.AddSingleton<FlowQueryService>(sp =>
        {
            var parser = sp.GetRequiredService<FlowParser>();
            return new FlowQueryService(Service, parser)
            {
                EnvironmentId = ExtractEnvironmentId(detail)
            };
        });

        services.AddSingleton<PowerAutomateAuthService>(sp =>
        {
            return new PowerAutomateAuthService(result =>
            {
                // Fallback UI for device code prompt; the MainControl will override this with a better dialog later
                MessageBox.Show(
                    $"Please visit {result.VerificationUrl} and enter code:\n\n{result.UserCode}",
                    "Power Automate Authentication",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return System.Threading.Tasks.Task.CompletedTask;
            });
        });

        services.AddSingleton<HttpClient>();
        services.AddSingleton<FlowRunService>();

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

        // Modern Dataverse connections expose the Environment ID directly
        if (!string.IsNullOrWhiteSpace(detail.EnvironmentId))
        {
            return detail.EnvironmentId;
        }

        // Fallback for older connection types
        return detail.Organization.ToString();
    }
}

 
