using System;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace HeatherAmiDigital.FlowInterrogator.XrmToolBox;

/// <summary>
/// Primary user control hosting the search interface, results grid, and detail panes.
/// </summary>
public sealed class MainControl : UserControl
{
    /// <summary>
    /// Gets or sets the Dependency Injection service provider configured by the parent plugin.
    /// </summary>
    public ServiceProvider ServiceProvider { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainControl"/> class.
    /// </summary>
    public MainControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Invoked by the parent plugin when the Dataverse connection is updated.
    /// </summary>
    public void OnConnectionUpdated()
    {
        // TODO: Trigger initial data load (e.g., fetch flow summaries)
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        // Temporary label to verify the UI loads in XrmToolBox
        var lblStatus = new Label
        {
            Text = "Power Automate Flow Interrogator loaded successfully.",
            Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
            Font = new System.Drawing.Font("Segoe UI", 12F)
        };

        Controls.Add(lblStatus);
        ResumeLayout(false);
    }
}
