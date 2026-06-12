using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using HeatherAmiDigital.FlowInterrogator.Core.Models;
using HeatherAmiDigital.FlowInterrogator.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Client;
using Newtonsoft.Json;
using XrmToolBox.Extensibility;

namespace HeatherAmiDigital.FlowInterrogator.XrmToolBox;

/// <summary>
/// Primary user control hosting the search interface, results grid, flow/run detail panes,
/// and the cross-flow run history view.
/// </summary>
public sealed class MainControl : UserControl, IDeviceCodePrompt
{
    /// <summary>Status background/foreground colours, pinned so the UI is consistent across views.</summary>
    private static readonly IReadOnlyDictionary<FlowRunStatus, (Color Back, Color Fore)> StatusColours =
        new Dictionary<FlowRunStatus, (Color, Color)>
        {
            [FlowRunStatus.Succeeded] = (Color.PaleGreen, Color.DarkGreen),
            [FlowRunStatus.Failed] = (Color.LightCoral, Color.DarkRed),
            [FlowRunStatus.Cancelled] = (Color.LightGray, Color.Black),
            [FlowRunStatus.Skipped] = (Color.LightGray, Color.Black),
            [FlowRunStatus.Aborted] = (Color.LightGray, Color.Black),
            [FlowRunStatus.Waiting] = (Color.LightYellow, Color.DarkGoldenrod),
            [FlowRunStatus.Unknown] = (Color.White, Color.Black),
        };

    // --- Injected collaborators ---

    /// <summary>Gets or sets the hosting plugin, used for WorkAsync, status, logging, and cancellation.</summary>
    public PluginControlBase Host { get; set; }

    /// <summary>Gets or sets the persisted user settings.</summary>
    public FlowInterrogatorSettings Settings { get; set; }

    /// <summary>Gets or sets the Dependency Injection service provider configured by the parent plugin.</summary>
    public ServiceProvider ServiceProvider { get; set; }

    private FlowQueryService _queryService;
    private FlowRunService _runService;
    private IFlowLogger _logger;

    // --- State ---

    private List<FlowSummary> _flows = new();
    private List<FlowMatch> _matches = new();
    private readonly Dictionary<Guid, FlowDefinition> _definitionCache = new();
    private List<FlowRun> _flowRuns = new();
    private List<FlowRun> _historyRuns = new();
    private List<FlowRunAction> _runActions = new();
    private readonly Dictionary<string, string> _errorMessageCache = new();
    private Guid _selectedFlowId;
    private string _selectedFlowName;

    // --- UI controls ---

    private ToolStripTextBox _txtSearch;
    private ToolStripButton _btnCancel;
    private ToolStripLabel _lblStatus;
    private System.Windows.Forms.Timer _statusTimer;

    private TabControl _topTabs;
    private DataGridView _dgvResults;
    private PropertyGrid _pgSummary;
    private TextBox _txtDefinition;
    private DataGridView _dgvFlowActions;
    private DataGridView _dgvRunActions;
    private TextBox _txtInputs;
    private TextBox _txtOutputs;

    private DateTimePicker _dtRunStart;
    private ComboBox _cboRunStatus;
    private DataGridView _dgvRuns;

    private DateTimePicker _dtHistStart;
    private ComboBox _cboHistStatus;
    private ToolStripTextBox _txtHistSearch;
    private ToolStripButton _chkErrorText;
    private DataGridView _dgvRunHistory;

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
        _runService = ServiceProvider.GetRequiredService<FlowRunService>();
        _logger = ServiceProvider.GetService<IFlowLogger>();

        var defaultStart = DateTime.Now.AddDays(-(Settings?.DefaultRunHistoryDays ?? 7));
        _dtRunStart.Value = defaultStart;
        _dtHistStart.Value = defaultStart;

        _definitionCache.Clear();
        _errorMessageCache.Clear();

        LoadFlows();
    }

    #region Layout

    private void InitializeComponent()
    {
        SuspendLayout();

        _statusTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _statusTimer.Tick += (s, e) => SetStatus("Ready");

        Controls.Add(BuildTopTabs());
        Controls.Add(BuildToolStrip());

        ResumeLayout(false);
    }

    private ToolStrip BuildToolStrip()
    {
        var toolStrip = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };

        _txtSearch = new ToolStripTextBox { Width = 300, ToolTipText = "Search GUID, email, or URL across all definitions" };
        var btnSearch = new ToolStripButton("Search Definitions") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        var btnRefresh = new ToolStripButton("Refresh Flows") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        _btnCancel = new ToolStripButton("Cancel") { DisplayStyle = ToolStripItemDisplayStyle.Text, Enabled = false };
        _lblStatus = new ToolStripLabel("Ready") { Alignment = ToolStripItemAlignment.Right };

        toolStrip.Items.AddRange(new ToolStripItem[]
        {
            new ToolStripLabel("Search:"), _txtSearch, btnSearch,
            new ToolStripSeparator(), btnRefresh,
            new ToolStripSeparator(), _btnCancel,
            new ToolStripSeparator(), _lblStatus
        });

        btnRefresh.Click += (s, e) => LoadFlows();
        btnSearch.Click += (s, e) => SearchDefinitions(_txtSearch.Text);
        _btnCancel.Click += (s, e) => { Host?.CancelWorker(); SetStatus("Cancelling…"); };
        _txtSearch.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                SearchDefinitions(_txtSearch.Text);
            }
        };

        return toolStrip;
    }

    private TabControl BuildTopTabs()
    {
        _topTabs = new TabControl { Dock = DockStyle.Fill };

        var tabFlows = new TabPage("Flows");
        tabFlows.Controls.Add(BuildFlowsTab());

        var tabHistory = new TabPage("Run History");
        tabHistory.Controls.Add(BuildRunHistoryTab());

        _topTabs.TabPages.AddRange(new[] { tabFlows, tabHistory });
        return _topTabs;
    }

    private Control BuildFlowsTab()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 480 };

        _dgvResults = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.None,
            RowHeadersVisible = false
        };
        _dgvResults.SelectionChanged += (s, e) => OnFlowSelectionChanged();
        split.Panel1.Controls.Add(_dgvResults);

        // Right side: vertical split — details (top) over runs (bottom).
        var rightSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        rightSplit.Panel1.Controls.Add(BuildDetailsTabs());
        rightSplit.Panel2.Controls.Add(BuildRunsPanel());
        split.Panel2.Controls.Add(rightSplit);
        rightSplit.SplitterDistance = 320;

        return split;
    }

    private Control BuildDetailsTabs()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };

        var tabSummary = new TabPage("Summary");
        _pgSummary = new PropertyGrid { Dock = DockStyle.Fill, PropertySort = PropertySort.Categorized, ToolbarVisible = false };
        tabSummary.Controls.Add(_pgSummary);

        var tabDefinition = new TabPage("Definition JSON");
        _txtDefinition = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 9.5f),
            WordWrap = false,
            BackColor = SystemColors.Window
        };
        tabDefinition.Controls.Add(_txtDefinition);

        var tabActions = new TabPage("Actions");
        tabActions.Controls.Add(BuildActionsSubTabs());

        tabs.TabPages.AddRange(new[] { tabSummary, tabDefinition, tabActions });
        return tabs;
    }

    private Control BuildActionsSubTabs()
    {
        var subTabs = new TabControl { Dock = DockStyle.Fill };

        var tabFlowActions = new TabPage("Flow Actions");
        _dgvFlowActions = CreateReadOnlyGrid();
        tabFlowActions.Controls.Add(_dgvFlowActions);

        var tabRunActions = new TabPage("Run Actions");
        tabRunActions.Controls.Add(BuildRunActionsPanel());

        subTabs.TabPages.AddRange(new[] { tabFlowActions, tabRunActions });
        return subTabs;
    }

    private Control BuildRunActionsPanel()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };

        _dgvRunActions = CreateReadOnlyGrid();
        _dgvRunActions.AutoGenerateColumns = false;
        _dgvRunActions.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", DataPropertyName = "Status" },
            new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Action", DataPropertyName = "Name" },
            new DataGridViewTextBoxColumn { Name = "Duration", HeaderText = "Duration" },
            new DataGridViewTextBoxColumn { Name = "ErrorCode", HeaderText = "Error code", DataPropertyName = "ErrorCode" },
            new DataGridViewTextBoxColumn { Name = "ErrorMessage", HeaderText = "Error message", DataPropertyName = "ErrorMessage" }
        });
        _dgvRunActions.CellFormatting += OnRunActionsCellFormatting;
        _dgvRunActions.SelectionChanged += (s, e) => ShowSelectedRunActionDetail();
        split.Panel1.Controls.Add(_dgvRunActions);

        var ioSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        ioSplit.Panel1.Controls.Add(CreateJsonViewer("Inputs", out _txtInputs));
        ioSplit.Panel2.Controls.Add(CreateJsonViewer("Outputs", out _txtOutputs));
        split.Panel2.Controls.Add(ioSplit);

        split.SplitterDistance = 150;
        return split;
    }

    private Control BuildRunsPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill };

        var filter = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
        _dtRunStart = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 110 };
        _cboRunStatus = CreateStatusCombo();
        var btnRefreshRuns = new ToolStripButton("Refresh runs") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        btnRefreshRuns.Click += (s, e) => LoadRuns();
        filter.Items.AddRange(new ToolStripItem[]
        {
            new ToolStripLabel("Runs since:"), new ToolStripControlHost(_dtRunStart),
            new ToolStripLabel("Status:"), new ToolStripControlHost(_cboRunStatus),
            new ToolStripSeparator(), btnRefreshRuns
        });

        _dgvRuns = CreateRunsGrid(includeError: false);
        _dgvRuns.SelectionChanged += (s, e) => OnRunSelectionChanged();

        panel.Controls.Add(_dgvRuns);
        panel.Controls.Add(filter);
        return panel;
    }

    private Control BuildRunHistoryTab()
    {
        var panel = new Panel { Dock = DockStyle.Fill };

        var filter = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
        _dtHistStart = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 110 };
        _cboHistStatus = CreateStatusCombo();
        _txtHistSearch = new ToolStripTextBox { Width = 220, ToolTipText = "Filter by flow name, trigger, correlation id, or run id" };
        var btnSearchRuns = new ToolStripButton("Search runs") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        _chkErrorText = new ToolStripButton("Search error text") { CheckOnClick = true, DisplayStyle = ToolStripItemDisplayStyle.Text };
        btnSearchRuns.Click += (s, e) => SearchRunHistory();
        _txtHistSearch.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SearchRunHistory(); }
        };

        filter.Items.AddRange(new ToolStripItem[]
        {
            new ToolStripLabel("Runs since:"), new ToolStripControlHost(_dtHistStart),
            new ToolStripLabel("Status:"), new ToolStripControlHost(_cboHistStatus),
            new ToolStripLabel("Term:"), _txtHistSearch, btnSearchRuns,
            new ToolStripSeparator(), _chkErrorText
        });

        _dgvRunHistory = CreateRunsGrid(includeError: true);

        panel.Controls.Add(_dgvRunHistory);
        panel.Controls.Add(filter);
        return panel;
    }

    private static ComboBox CreateStatusCombo()
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        combo.Items.AddRange(new object[] { "Any", "Succeeded", "Failed", "Cancelled", "Waiting" });
        combo.SelectedIndex = 0;
        return combo;
    }

    private static DataGridView CreateReadOnlyGrid() => new DataGridView
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = SystemColors.Window,
        BorderStyle = BorderStyle.None,
        RowHeadersVisible = false
    };

    private DataGridView CreateRunsGrid(bool includeError)
    {
        var dgv = CreateReadOnlyGrid();
        dgv.AutoGenerateColumns = false;
        dgv.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", DataPropertyName = "Status" },
            new DataGridViewTextBoxColumn { Name = "StartUtc", HeaderText = "Start (UTC)" },
            new DataGridViewTextBoxColumn { Name = "StartLocal", HeaderText = "Start (local)" },
            new DataGridViewTextBoxColumn { Name = "End", HeaderText = "End (UTC)" },
            new DataGridViewTextBoxColumn { Name = "Duration", HeaderText = "Duration" },
            new DataGridViewTextBoxColumn { Name = "Trigger", HeaderText = "Trigger", DataPropertyName = "TriggerName" },
            new DataGridViewTextBoxColumn { Name = "CorrelationId", HeaderText = "Correlation id", DataPropertyName = "CorrelationId" }
        });

        if (includeError)
        {
            dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "Error", HeaderText = "Error" });
        }

        dgv.Columns.Add(new DataGridViewLinkColumn
        {
            Name = "Portal",
            HeaderText = string.Empty,
            Text = "Open in portal",
            UseColumnTextForLinkValue = true,
            LinkBehavior = LinkBehavior.HoverUnderline
        });

        dgv.CellFormatting += OnRunsCellFormatting;
        dgv.CellContentClick += OnRunsCellContentClick;
        return dgv;
    }

    private static Control CreateJsonViewer(string title, out TextBox box)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        box = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9f),
            BackColor = SystemColors.Window
        };
        panel.Controls.Add(box);
        panel.Controls.Add(new Label { Dock = DockStyle.Top, Text = title, Font = new Font(Control.DefaultFont, FontStyle.Bold), Height = 18 });
        return panel;
    }

    #endregion

    #region Flow list + definition

    private void LoadFlows()
    {
        if (_queryService == null) return;

        RunWork(
            "Loading flows…",
            cancelable: false,
            work: _ => _queryService.GetFlowSummaries(),
            onSuccess: result =>
            {
                _flows = result.ToList();
                _matches.Clear();
                BindSummaries();
                SetStatus($"Loaded {_flows.Count} flows.");
            },
            failMessage: "Failed to load flows.");
    }

    private void SearchDefinitions(string term)
    {
        if (_queryService == null || string.IsNullOrWhiteSpace(term)) return;

        SetStatus($"Searching for '{term}'…");
        RunWork(
            $"Searching for '{term}'…",
            cancelable: true,
            work: worker => _queryService.SearchFlowDefinitions(
                term,
                onPageRetrieved: page => worker.ReportProgress(0, $"Searching… page {page}"),
                isCancellationRequested: () => worker.CancellationPending),
            onSuccess: result =>
            {
                _matches = result.ToList();
                BindMatches();
                SetStatus($"Found {_matches.Count} matches.");
            },
            failMessage: "Failed to search flows.");
    }

    private void OnFlowSelectionChanged()
    {
        if (_dgvResults.SelectedRows.Count == 0)
        {
            _pgSummary.SelectedObject = null;
            _txtDefinition.Clear();
            _dgvFlowActions.DataSource = null;
            return;
        }

        var item = _dgvResults.SelectedRows[0].DataBoundItem;
        if (item is FlowSummary summary)
        {
            _selectedFlowId = summary.Id;
            _selectedFlowName = summary.Name;
            _pgSummary.SelectedObject = summary;
        }
        else if (item is FlowMatch match)
        {
            _selectedFlowId = match.FlowId;
            _selectedFlowName = match.FlowName;
            _pgSummary.SelectedObject = match;
        }
        else
        {
            return;
        }

        ShowDefinition(_selectedFlowId);
        LoadRuns();
    }

    private void ShowDefinition(Guid flowId)
    {
        if (_definitionCache.TryGetValue(flowId, out var cached))
        {
            BindDefinition(cached);
            return;
        }

        if (_queryService == null) return;

        RunWork(
            "Loading flow definition…",
            cancelable: false,
            work: _ => _queryService.GetFlowDefinition(flowId),
            onSuccess: definition =>
            {
                if (definition == null)
                {
                    _txtDefinition.Text = "This flow no longer exists in Dataverse.";
                    _dgvFlowActions.DataSource = null;
                    return;
                }

                _definitionCache[flowId] = definition;
                BindDefinition(definition);
            },
            failMessage: "Failed to load flow definition.");
    }

    private void BindDefinition(FlowDefinition definition)
    {
        _txtDefinition.Text = definition.RawJson?.ToString() ?? string.Empty;
        _dgvFlowActions.DataSource = definition.Actions?.Values.Select(a => new
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

        HideColumn(_dgvResults, "Id");
        HideColumn(_dgvResults, "EnvironmentId");
        HideColumn(_dgvResults, "OwnerId");
    }

    private void BindMatches()
    {
        _dgvResults.DataSource = null;
        _dgvResults.Columns.Clear();
        _dgvResults.DataSource = new BindingSource(_matches, null);
        HideColumn(_dgvResults, "FlowId");
    }

    private static void HideColumn(DataGridView grid, string name)
    {
        if (grid.Columns.Contains(name))
        {
            grid.Columns[name].Visible = false;
        }
    }

    #endregion

    #region Runs (per-flow)

    private void LoadRuns()
    {
        if (_runService == null || _selectedFlowId == Guid.Empty) return;

        var environmentId = _queryService?.EnvironmentId;
        var flowId = _selectedFlowId;
        var flowName = _selectedFlowName;
        var start = _dtRunStart.Value.ToUniversalTime();
        var status = ParseStatusFilter(_cboRunStatus);

        RunWork(
            "Loading runs…",
            cancelable: false,
            work: _ => _runService.GetRunsAsync(flowId, environmentId, flowName, start, status).GetAwaiter().GetResult(),
            onSuccess: runs =>
            {
                _flowRuns = runs.ToList();
                BindRuns(_dgvRuns, _flowRuns);
                SetStatus($"Loaded {_flowRuns.Count} runs for '{flowName}'.");
            },
            failMessage: "Failed to load runs.");
    }

    private void OnRunSelectionChanged()
    {
        if (_dgvRuns.SelectedRows.Count == 0 || _runService == null) return;

        var index = _dgvRuns.SelectedRows[0].Index;
        if (index < 0 || index >= _flowRuns.Count) return;

        var run = _flowRuns[index];
        var environmentId = _queryService?.EnvironmentId;

        RunWork(
            "Loading run actions…",
            cancelable: false,
            work: _ => _runService.GetRunActionsAsync(run.FlowId, environmentId, run.RunId).GetAwaiter().GetResult(),
            onSuccess: actions =>
            {
                _runActions = actions.ToList();
                _dgvRunActions.DataSource = null;
                _dgvRunActions.DataSource = _runActions;
                SelectInitialRunAction();
            },
            failMessage: "Failed to load run actions.");
    }

    private void SelectInitialRunAction()
    {
        if (_runActions.Count == 0)
        {
            ShowRunActionDetail(null);
            return;
        }

        var index = 0;
        if (Settings?.AutoExpandFailedActions == true)
        {
            var failedIndex = _runActions.FindIndex(a => a.Status == FlowRunStatus.Failed);
            if (failedIndex >= 0) index = failedIndex;
        }

        _dgvRunActions.ClearSelection();
        _dgvRunActions.Rows[index].Selected = true;
    }

    private void ShowSelectedRunActionDetail()
    {
        if (_dgvRunActions.SelectedRows.Count == 0) return;
        var index = _dgvRunActions.SelectedRows[0].Index;
        if (index < 0 || index >= _runActions.Count) return;
        ShowRunActionDetail(_runActions[index]);
    }

    private void ShowRunActionDetail(FlowRunAction action)
    {
        _txtInputs.Text = action?.Inputs != null ? action.Inputs.ToString(Formatting.Indented) : "No data";
        _txtOutputs.Text = action?.Outputs != null ? action.Outputs.ToString(Formatting.Indented) : "No data";
    }

    #endregion

    #region Run history (cross-flow)

    private void SearchRunHistory()
    {
        if (_runService == null) return;
        if (_flows.Count == 0)
        {
            SetStatus("Load flows first (Refresh Flows).", isError: true);
            return;
        }

        var environmentId = _queryService?.EnvironmentId;
        var flows = _flows.ToList();
        var start = _dtHistStart.Value.ToUniversalTime();
        var status = ParseStatusFilter(_cboHistStatus);
        var term = _txtHistSearch.Text?.Trim();
        var searchErrorText = _chkErrorText.Checked;

        RunWork(
            "Searching run history…",
            cancelable: true,
            work: worker =>
            {
                var all = _runService.SearchRunsAsync(
                    flows,
                    environmentId,
                    term: null,
                    startDate: start,
                    statusFilter: status,
                    isCancellationRequested: () => worker.CancellationPending,
                    onFlowProcessed: (p, t) => worker.ReportProgress(0, $"Querying flow {p} of {t}…")).GetAwaiter().GetResult();

                if (string.IsNullOrWhiteSpace(term))
                {
                    return all.ToList();
                }

                var result = all.Where(r => MatchesStandard(r, term)).ToList();

                if (searchErrorText)
                {
                    foreach (var run in all.Where(r => r.Status == FlowRunStatus.Failed && !result.Contains(r)))
                    {
                        if (worker.CancellationPending) break;

                        if (!_errorMessageCache.TryGetValue(run.RunId, out var message))
                        {
                            message = _runService.GetFirstErrorMessageAsync(run).GetAwaiter().GetResult();
                            _errorMessageCache[run.RunId] = message;
                        }

                        if (message != null && message.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            result.Add(run);
                        }
                    }
                }

                return result.OrderByDescending(r => r.StartTime).ToList();
            },
            onSuccess: runs =>
            {
                _historyRuns = runs;
                BindRuns(_dgvRunHistory, _historyRuns);
                SetStatus($"Found {_historyRuns.Count} runs.");
            },
            failMessage: "Failed to search run history.");
    }

    private static bool MatchesStandard(FlowRun run, string term)
    {
        return Contains(run.FlowName, term)
            || Contains(run.TriggerName, term)
            || Contains(run.CorrelationId, term)
            || Contains(run.RunId, term);
    }

    private static bool Contains(string value, string term)
        => value != null && value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;

    #endregion

    #region Run grid formatting

    private void BindRuns(DataGridView grid, List<FlowRun> runs)
    {
        grid.DataSource = null;
        grid.DataSource = runs;
    }

    private void OnRunsCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
    {
        var grid = (DataGridView)sender;
        if (grid.DataSource is not List<FlowRun> runs || e.RowIndex < 0 || e.RowIndex >= runs.Count) return;

        var run = runs[e.RowIndex];
        var columnName = grid.Columns[e.ColumnIndex].Name;

        switch (columnName)
        {
            case "Status":
                if (StatusColours.TryGetValue(run.Status, out var colours))
                {
                    e.CellStyle.BackColor = colours.Back;
                    e.CellStyle.ForeColor = colours.Fore;
                }
                break;
            case "StartUtc":
                e.Value = run.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
                e.FormattingApplied = true;
                break;
            case "StartLocal":
                e.Value = run.StartTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                e.FormattingApplied = true;
                break;
            case "End":
                e.Value = run.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—";
                e.FormattingApplied = true;
                break;
            case "Duration":
                e.Value = run.EndTime.HasValue ? (run.EndTime.Value - run.StartTime).ToString(@"hh\:mm\:ss") : "—";
                e.FormattingApplied = true;
                break;
            case "Error":
                e.Value = _errorMessageCache.TryGetValue(run.RunId, out var msg) ? msg : string.Empty;
                e.FormattingApplied = true;
                break;
        }
    }

    private void OnRunsCellContentClick(object sender, DataGridViewCellEventArgs e)
    {
        var grid = (DataGridView)sender;
        if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "Portal") return;
        if (grid.DataSource is not List<FlowRun> runs || e.RowIndex >= runs.Count) return;

        var url = runs[e.RowIndex].GetPortalUrl();
        if (string.IsNullOrWhiteSpace(url))
        {
            SetStatus("No portal URL: environment id is unknown.", isError: true);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger?.Error("Failed to open portal URL.", ex);
            SetStatus("Could not open browser.", isError: true);
        }
    }

    private void OnRunActionsCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _runActions.Count) return;
        var grid = (DataGridView)sender;
        if (grid.Columns[e.ColumnIndex].Name != "Status") return;

        if (StatusColours.TryGetValue(_runActions[e.RowIndex].Status, out var colours))
        {
            e.CellStyle.BackColor = colours.Back;
            e.CellStyle.ForeColor = colours.Fore;
        }
    }

    private static FlowRunStatus? ParseStatusFilter(ComboBox combo)
    {
        var text = combo.SelectedItem as string;
        if (string.IsNullOrEmpty(text) || text == "Any") return null;
        return Enum.TryParse<FlowRunStatus>(text, out var status) ? status : (FlowRunStatus?)null;
    }

    #endregion

    #region Device code prompt

    /// <inheritdoc />
    public Task ShowDeviceCodeAsync(DeviceCodeResult info)
    {
        // Marshal to the UI thread and show the prompt modeless so the rest of the tool stays
        // interactive while MSAL polls the sign-in endpoint on the calling background thread.
        void Show()
        {
            var form = new DeviceCodeForm(info);
            form.Show(this);
        }

        if (InvokeRequired)
        {
            BeginInvoke((Action)Show);
        }
        else
        {
            Show();
        }

        return Task.CompletedTask;
    }

    #endregion

    #region Async + status plumbing

    /// <summary>
    /// Runs work on the XrmToolBox single-flight background worker, marshalling completion,
    /// progress, and failures back to the UI thread.
    /// </summary>
    private void RunWork<T>(string message, bool cancelable, Func<BackgroundWorker, T> work, Action<T> onSuccess, string failMessage)
    {
        if (Host == null) return;

        _btnCancel.Enabled = cancelable;

        Host.WorkAsync(new WorkAsyncInfo
        {
            Message = message,
            IsCancelable = cancelable,
            Work = (worker, args) => args.Result = work(worker),
            ProgressChanged = args => Host.SetWorkingMessage(args.UserState as string ?? message),
            PostWorkCallBack = args =>
            {
                _btnCancel.Enabled = false;

                if (args.Cancelled)
                {
                    SetStatus("Cancelled.");
                    return;
                }

                if (args.Error != null)
                {
                    _logger?.Error(failMessage, args.Error);
                    SetStatus($"{failMessage} {args.Error.Message}", isError: true);
                    return;
                }

                onSuccess((T)args.Result);
            }
        });
    }

    private void SetStatus(string message, bool isError = false)
    {
        void Apply()
        {
            _lblStatus.ForeColor = isError ? Color.Firebrick : SystemColors.ControlText;
            _lblStatus.Text = message;
            _statusTimer.Stop();
            if (isError)
            {
                _statusTimer.Start();
            }
        }

        if (InvokeRequired)
        {
            Invoke((Action)Apply);
        }
        else
        {
            Apply();
        }
    }

    #endregion
}
