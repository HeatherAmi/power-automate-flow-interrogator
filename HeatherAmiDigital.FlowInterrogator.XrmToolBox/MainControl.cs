using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using HeatherAmiDigital.FlowInterrogator.Core.Models;
using HeatherAmiDigital.FlowInterrogator.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace HeatherAmiDigital.FlowInterrogator.XrmToolBox;

/// <summary>
/// Primary user control hosting the search interface, results grid, and detail panes.
/// </summary>
public sealed class MainControl : UserControl
{
    private FlowQueryService _queryService;
    private FlowParser _parser;

    private List<FlowSummary> _flows = new();
    private List<FlowMatch> _matches = new();
    private readonly Dictionary<Guid, FlowDefinition> _definitionCache = new();

    // UI Controls
    private ToolStripTextBox _txtSearch;
    private ToolStripLabel _lblStatus;
    private DataGridView _dgvResults;
    private PropertyGrid _pgSummary;
    private TextBox _txtDefinition;
    private DataGridView _dgvActions;

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
    /// Resolves services and triggers the initial data load.
    /// </summary>
    public void OnConnectionUpdated()
    {
        if (ServiceProvider == null) return;

        _queryService = ServiceProvider.GetRequiredService<FlowQueryService>();
        _parser = ServiceProvider.GetRequiredService<FlowParser>();

        _ = LoadFlowsAsync();
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        // --- Top Toolbar ---
        var toolStrip = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
        _txtSearch = new ToolStripTextBox { Width = 300, ToolTipText = "Search GUID, email, or URL across all definitions" };
        var btnSearch = new ToolStripButton("Search Definitions") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        var btnRefresh = new ToolStripButton("Refresh Flows") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        _lblStatus = new ToolStripLabel("Ready") { Alignment = ToolStripItemAlignment.Right };

        toolStrip.Items.AddRange(new ToolStripItem[]
        {
            new ToolStripLabel("Search:"), _txtSearch, btnSearch,
            new ToolStripSeparator(), btnRefresh,
            new ToolStripSeparator(), _lblStatus
        });

        // --- Main Split Container ---
        var splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 500
        };

        // --- Left Panel: Results Grid ---
        _dgvResults = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.None
        };

        // --- Right Panel: Details Tabs ---
        var tabDetails = new TabControl { Dock = DockStyle.Fill };

        // Tab 1: Summary
        var tabSummary = new TabPage("Summary");
        _pgSummary = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            PropertySort = PropertySort.Categorized,
            ToolbarVisible = false
        };
        tabSummary.Controls.Add(_pgSummary);

        // Tab 2: Definition JSON
        var tabDefinition = new TabPage("Definition JSON");
        _txtDefinition = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 9.5f),
            WordWrap = false,
            BackColor = SystemColors.Window // <--- FIXED
        };

        tabDefinition.Controls.Add(_txtDefinition);

        // Tab 3: Actions
        var tabActions = new TabPage("Actions");
        _dgvActions = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.None
        };
        tabActions.Controls.Add(_dgvActions);

        tabDetails.TabPages.AddRange(new[] { tabSummary, tabDefinition, tabActions });

        splitContainer.Panel1.Controls.Add(_dgvResults);
        splitContainer.Panel2.Controls.Add(tabDetails);

        Controls.Add(splitContainer);
        Controls.Add(toolStrip);

        // --- Event Wiring ---
        btnRefresh.Click += async (s, e) => await LoadFlowsAsync();
        btnSearch.Click += async (s, e) => await SearchFlowsAsync(_txtSearch.Text);
        _txtSearch.KeyDown += async (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await SearchFlowsAsync(_txtSearch.Text);
            }
        };

        _dgvResults.SelectionChanged += (s, e) => ShowFlowDetails();

        ResumeLayout(false);
    }

    /// <summary>
    /// Fetches lightweight flow summaries from Dataverse and binds them to the grid.
    /// </summary>
    private async Task LoadFlowsAsync()
    {
        if (_queryService == null) return;

        SetStatus("Loading flows...");
        try
        {
            _flows = (await Task.Run(() => _queryService.GetFlowSummaries())).ToList();
            _matches.Clear();
            BindSummaries();
            SetStatus($"Loaded {_flows.Count} flows.");
        }
        catch (Exception ex)
        {
            SetStatus("Error loading flows.");
            MessageBox.Show($"Failed to load flows: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Executes a deep search across all flow definitions and binds the matches to the grid.
    /// </summary>
    private async Task SearchFlowsAsync(string searchTerm)
    {
        if (_queryService == null || string.IsNullOrWhiteSpace(searchTerm)) return;

        SetStatus($"Searching for '{searchTerm}'...");
        try
        {
            _matches = (await Task.Run(() => _queryService.SearchFlowDefinitions(searchTerm))).ToList();
            BindMatches();
            SetStatus($"Found {_matches.Count} matches.");
        }
        catch (Exception ex)
        {
            SetStatus("Error searching flows.");
            MessageBox.Show($"Failed to search flows: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Updates the right-hand detail pane based on the currently selected row in the results grid.
    /// </summary>
    private void ShowFlowDetails()
    {
        if (_dgvResults.SelectedRows.Count == 0)
        {
            _pgSummary.SelectedObject = null;
            _txtDefinition.Clear();
            _dgvActions.DataSource = null;
            return;
        }

        var row = _dgvResults.SelectedRows[0];
        Guid flowId;

        // Determine if we are viewing a Summary or a Match
        if (row.DataBoundItem is FlowSummary summary)
        {
            flowId = summary.Id;
            _pgSummary.SelectedObject = summary;
        }
        else if (row.DataBoundItem is FlowMatch match)
        {
            flowId = match.FlowId;
            // For matches, show the match details in the property grid instead of the full summary
            _pgSummary.SelectedObject = match;
        }
        else
        {
            return;
        }

        // Fetch and cache the definition if we haven't already
        if (!_definitionCache.TryGetValue(flowId, out var definition))
        {
            // In a real scenario, we'd fetch the specific record here. 
            // For now, we rely on the search having populated the cache, or we just show empty.
            // To keep this step simple, we'll parse it on demand if it's missing (requires a quick Dataverse fetch).
            // TODO: Implement single-record fetch in FlowQueryService for R11.
            _txtDefinition.Text = "Select a flow from the Refresh list to view its definition, or perform a search.";
            _dgvActions.DataSource = null;
            return;
        }

        _txtDefinition.Text = definition.RawJson?.ToString() ?? string.Empty;
        _dgvActions.DataSource = definition.Actions?.Values.Select(a => new
        {
            a.Name,
            a.Type,
            a.Kind,
            Dependencies = string.Join(", ", a.RunAfter?.Keys ?? Array.Empty<string>())
        }).ToList();
    }

    private void BindSummaries()
    {
        _dgvResults.DataSource = null;
        _dgvResults.Columns.Clear();
        _dgvResults.DataSource = new BindingSource(_flows, null);

        // Hide internal IDs, show user-friendly columns
        if (_dgvResults.Columns.Contains("Id")) _dgvResults.Columns["Id"].Visible = false;
        if (_dgvResults.Columns.Contains("EnvironmentId")) _dgvResults.Columns["EnvironmentId"].Visible = false;
        if (_dgvResults.Columns.Contains("OwnerId")) _dgvResults.Columns["OwnerId"].Visible = false;
    }

    private void BindMatches()
    {
        _dgvResults.DataSource = null;
        _dgvResults.Columns.Clear();
        _dgvResults.DataSource = new BindingSource(_matches, null);

        if (_dgvResults.Columns.Contains("FlowId")) _dgvResults.Columns["FlowId"].Visible = false;
    }

    private void SetStatus(string message)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => _lblStatus.Text = message));
        }
        else
        {
            _lblStatus.Text = message;
        }
    }
}
