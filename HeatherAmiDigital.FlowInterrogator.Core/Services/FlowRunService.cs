using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using HeatherAmiDigital.FlowInterrogator.Core.Models;
using Newtonsoft.Json.Linq;

namespace HeatherAmiDigital.FlowInterrogator.Core.Services;

/// <summary>
/// Interacts with the Power Automate Management API to retrieve flow run history 
/// and detailed action execution data (inputs/outputs).
/// </summary>
public sealed class FlowRunService
{
    private const string ApiVersion = "2016-11-01";
    private const string BaseUrl = "https://api.flow.microsoft.com";

    private readonly HttpClient _httpClient;
    private readonly ITokenProvider _tokenProvider;
    private readonly IFlowLogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlowRunService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client used for API requests.</param>
    /// <param name="tokenProvider">The token provider used to acquire Bearer tokens.</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger when null.</param>
    public FlowRunService(HttpClient httpClient, ITokenProvider tokenProvider, IFlowLogger logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _logger = logger ?? NullFlowLogger.Instance;
    }

    /// <summary>
    /// Retrieves the execution history for a specific flow.
    /// </summary>
    /// <param name="flowId">The Dataverse identifier of the flow.</param>
    /// <param name="environmentId">The Power Platform environment identifier.</param>
    /// <param name="flowName">The display name of the flow, used to populate the result models.</param>
    /// <param name="startDate">Optional. Filters runs to those started on or after this UTC date.</param>
    /// <param name="statusFilter">Optional. Filters runs to a specific execution status.</param>
    /// <returns>A read-only list of flow runs, ordered by start time descending.</returns>
    public async Task<IReadOnlyList<FlowRun>> GetRunsAsync(
        Guid flowId,
        string environmentId,
        string flowName,
        DateTime? startDate = null,
        FlowRunStatus? statusFilter = null)
    {
        var url = $"{BaseUrl}/providers/Microsoft.ProcessSimple/environments/{environmentId}/flows/{flowId}/runs?api-version={ApiVersion}";

        var filters = new List<string>();
        if (startDate.HasValue)
        {
            var isoDate = startDate.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            filters.Add($"startTime ge {isoDate}");
        }
        if (statusFilter.HasValue && statusFilter.Value != FlowRunStatus.Unknown)
        {
            filters.Add($"status eq '{statusFilter.Value}'");
        }

        if (filters.Any())
        {
            url += "&$filter=" + string.Join(" and ", filters);
        }

        _logger.Info($"Fetching runs for flow '{flowName}' ({flowId}).");

        var json = await SendAuthenticatedRequestAsync(url);
        var runs = new List<FlowRun>();

        if (json["value"] is JArray runsArray)
        {
            foreach (var runToken in runsArray)
            {
                runs.Add(MapToFlowRun(runToken, flowId, flowName, environmentId));
            }
        }

        return runs.OrderByDescending(r => r.StartTime).ToList();
    }

    /// <summary>
    /// Retrieves the detailed execution data for all actions within a specific flow run.
    /// Includes runtime inputs, outputs, and error messages.
    /// </summary>
    /// <param name="flowId">The Dataverse identifier of the flow.</param>
    /// <param name="environmentId">The Power Platform environment identifier.</param>
    /// <param name="runId">The unique identifier of the run.</param>
    /// <returns>A read-only list of run actions, ordered by start time.</returns>
    public async Task<IReadOnlyList<FlowRunAction>> GetRunActionsAsync(Guid flowId, string environmentId, string runId)
    {
        var url = $"{BaseUrl}/providers/Microsoft.ProcessSimple/environments/{environmentId}/flows/{flowId}/runs/{runId}/actions?api-version={ApiVersion}";

        _logger.Info($"Fetching actions for run {runId} (flow {flowId}).");

        var json = await SendAuthenticatedRequestAsync(url);
        var actions = new List<FlowRunAction>();

        if (json["value"] is JArray actionsArray)
        {
            foreach (var actionToken in actionsArray)
            {
                actions.Add(MapToFlowRunAction(actionToken));
            }
        }

        return actions.OrderBy(a => a.StartTime).ToList();
    }

    /// <summary>
    /// Searches run history across multiple flows, aggregating and filtering client-side.
    /// The Flow Management API is per-flow and not batchable, so runs are fetched sequentially.
    /// Matching is a case-insensitive substring test against the flow name, trigger name,
    /// correlation id, and run id. Error-message search is handled separately and on-demand
    /// via <see cref="GetFirstErrorMessageAsync"/>.
    /// </summary>
    /// <param name="flows">The flows whose run history should be searched.</param>
    /// <param name="environmentId">The Power Platform environment identifier.</param>
    /// <param name="term">The search term; when null or empty, all runs are returned.</param>
    /// <param name="startDate">Optional. Filters runs to those started on or after this UTC date.</param>
    /// <param name="statusFilter">Optional. Filters runs to a specific execution status.</param>
    /// <param name="isCancellationRequested">Optional predicate polled between flows; stops early when true.</param>
    /// <param name="onFlowProcessed">Optional progress callback receiving (processed, total) flow counts.</param>
    /// <returns>A read-only list of matching runs across all flows, ordered by start time descending.</returns>
    public async Task<IReadOnlyList<FlowRun>> SearchRunsAsync(
        IReadOnlyCollection<FlowSummary> flows,
        string environmentId,
        string term,
        DateTime? startDate = null,
        FlowRunStatus? statusFilter = null,
        Func<bool> isCancellationRequested = null,
        Action<int, int> onFlowProcessed = null)
    {
        if (flows == null) throw new ArgumentNullException(nameof(flows));

        _logger.Info($"Searching runs across {flows.Count} flows for '{term}'.");

        var aggregated = new List<FlowRun>();
        var total = flows.Count;
        var processed = 0;

        foreach (var flow in flows)
        {
            if (isCancellationRequested?.Invoke() == true)
            {
                _logger.Info("Cross-flow run search cancelled.");
                break;
            }

            var runs = await GetRunsAsync(flow.Id, environmentId, flow.Name, startDate, statusFilter);
            aggregated.AddRange(runs.Where(run => MatchesTerm(run, term)));

            onFlowProcessed?.Invoke(++processed, total);
        }

        return aggregated.OrderByDescending(r => r.StartTime).ToList();
    }

    /// <summary>
    /// Fetches the error message of the first failed action in a run.
    /// Used by the opt-in error-text search to avoid eagerly pulling action data for every run.
    /// </summary>
    /// <param name="run">The run to inspect.</param>
    /// <returns>The first failed action's error message, or <c>null</c> if none.</returns>
    public async Task<string> GetFirstErrorMessageAsync(FlowRun run)
    {
        if (run == null) throw new ArgumentNullException(nameof(run));

        var actions = await GetRunActionsAsync(run.FlowId, run.EnvironmentId, run.RunId);
        return actions
            .FirstOrDefault(a => a.Status == FlowRunStatus.Failed && !string.IsNullOrWhiteSpace(a.ErrorMessage))
            ?.ErrorMessage;
    }

    private static bool MatchesTerm(FlowRun run, string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return true;
        }

        return Contains(run.FlowName, term)
            || Contains(run.TriggerName, term)
            || Contains(run.CorrelationId, term)
            || Contains(run.RunId, term);
    }

    private static bool Contains(string value, string term)
    {
        return value != null && value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Sends an authenticated GET request to the Flow Management API.
    /// </summary>
    private async Task<JObject> SendAuthenticatedRequestAsync(string url)
    {
        var token = await _tokenProvider.GetAccessTokenAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.Error($"Flow API request failed with status {(int)response.StatusCode}.");
            throw new HttpRequestException($"Flow API request failed with status {(int)response.StatusCode}: {errorContent}");
        }

        var content = await response.Content.ReadAsStringAsync();
        return JObject.Parse(content);
    }

    private FlowRun MapToFlowRun(JToken runToken, Guid flowId, string flowName, string environmentId)
    {
        var props = runToken["properties"];
        var correlation = props?["correlation"];

        return new FlowRun
        {
            RunId = runToken["name"]?.ToString(),
            FlowId = flowId,
            FlowName = flowName,
            EnvironmentId = environmentId,
            Status = ParseStatus(props?["status"]?.ToString()),
            StartTime = props?["startTime"]?.ToObject<DateTime>() ?? DateTime.MinValue,
            EndTime = props?["endTime"]?.ToObject<DateTime?>(),
            TriggerName = props?["trigger"]?["name"]?.ToString(),
            CorrelationId = correlation?["clientTrackingId"]?.ToString()
        };
    }

    private FlowRunAction MapToFlowRunAction(JToken actionToken)
    {
        var props = actionToken["properties"];
        var error = props?["error"];

        return new FlowRunAction
        {
            Name = actionToken["name"]?.ToString(),
            Status = ParseStatus(props?["status"]?.ToString()),
            StartTime = props?["startTime"]?.ToObject<DateTime>() ?? DateTime.MinValue,
            EndTime = props?["endTime"]?.ToObject<DateTime?>(),
            ErrorCode = error?["code"]?.ToString(),
            ErrorMessage = error?["message"]?.ToString(),
            Inputs = props?["inputs"],
            Outputs = props?["outputs"]
        };
    }

    private FlowRunStatus ParseStatus(string statusString)
    {
        if (string.IsNullOrWhiteSpace(statusString))
        {
            return FlowRunStatus.Unknown;
        }

        return Enum.TryParse<FlowRunStatus>(statusString, true, out var status)
            ? status
            : FlowRunStatus.Unknown;
    }
}
