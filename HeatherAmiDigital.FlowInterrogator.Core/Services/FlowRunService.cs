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
    private readonly PowerAutomateAuthService _authService;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlowRunService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client used for API requests.</param>
    /// <param name="authService">The authentication service used to acquire Bearer tokens.</param>
    public FlowRunService(HttpClient httpClient, PowerAutomateAuthService authService)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
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
    /// Sends an authenticated GET request to the Flow Management API.
    /// </summary>
    private async Task<JObject> SendAuthenticatedRequestAsync(string url)
    {
        var token = await _authService.GetAccessTokenAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
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
