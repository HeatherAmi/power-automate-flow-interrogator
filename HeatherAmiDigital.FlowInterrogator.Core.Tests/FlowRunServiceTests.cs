using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using HeatherAmiDigital.FlowInterrogator.Core.Models;
using HeatherAmiDigital.FlowInterrogator.Core.Services;
using HeatherAmiDigital.FlowInterrogator.Core.Tests.Fakes;
using Xunit;

namespace HeatherAmiDigital.FlowInterrogator.Core.Tests;

/// <summary>
/// Tests for <see cref="FlowRunService"/> covering OData filter construction and error
/// surfacing (R15.4), plus cross-flow run search aggregation (R12.1).
/// </summary>
public sealed class FlowRunServiceTests
{
    private const string Environment = "Default-1111";

    private static string RunsJson(string runId, string correlationId, string status = "Succeeded")
        => $@"{{ ""value"": [ {{
                ""name"": ""{runId}"",
                ""properties"": {{
                    ""status"": ""{status}"",
                    ""startTime"": ""2024-01-02T03:04:05Z"",
                    ""trigger"": {{ ""name"": ""manual"" }},
                    ""correlation"": {{ ""clientTrackingId"": ""{correlationId}"" }}
                }}
            }} ] }}";

    private static FlowRunService CreateService(out FakeHttpMessageHandler handler, params HttpResponseMessage[] responses)
    {
        handler = new FakeHttpMessageHandler(responses);
        var httpClient = new HttpClient(handler);
        return new FlowRunService(httpClient, new FakeTokenProvider());
    }

    [Fact]
    public async Task GetRunsAsync_builds_filter_for_date_and_status()
    {
        var service = CreateService(out var handler, FakeHttpMessageHandler.Json(RunsJson("R1", "C1")));

        await service.GetRunsAsync(
            Guid.NewGuid(),
            Environment,
            "Flow",
            startDate: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            statusFilter: FlowRunStatus.Failed);

        var requested = Uri.UnescapeDataString(handler.RequestUris.Single());
        Assert.Contains("$filter=startTime ge 2024-01-01T00:00:00Z and status eq 'Failed'", requested);
        Assert.Contains("api-version=2016-11-01", requested);
    }

    [Fact]
    public async Task GetRunsAsync_omits_filter_when_no_date_or_status()
    {
        var service = CreateService(out var handler, FakeHttpMessageHandler.Json(RunsJson("R1", "C1")));

        await service.GetRunsAsync(Guid.NewGuid(), Environment, "Flow");

        Assert.DoesNotContain("$filter", handler.RequestUris.Single());
    }

    [Fact]
    public async Task GetRunsAsync_status_Unknown_is_ignored_in_filter()
    {
        var service = CreateService(out var handler, FakeHttpMessageHandler.Json(RunsJson("R1", "C1")));

        await service.GetRunsAsync(Guid.NewGuid(), Environment, "Flow", statusFilter: FlowRunStatus.Unknown);

        Assert.DoesNotContain("$filter", handler.RequestUris.Single());
    }

    [Fact]
    public async Task GetRunsAsync_non_success_throws_with_body_in_message()
    {
        var service = CreateService(out _, FakeHttpMessageHandler.Json("the error body", HttpStatusCode.BadRequest));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => service.GetRunsAsync(Guid.NewGuid(), Environment, "Flow"));

        Assert.Contains("400", ex.Message);
        Assert.Contains("the error body", ex.Message);
    }

    [Fact]
    public async Task SearchRunsAsync_matches_correlation_id_across_all_flows()
    {
        var service = CreateService(
            out _,
            FakeHttpMessageHandler.Json(RunsJson("R-A", "CORR-123")),
            FakeHttpMessageHandler.Json(RunsJson("R-B", "CORR-123")));

        var flows = new List<FlowSummary>
        {
            new FlowSummary { Id = Guid.NewGuid(), Name = "Alpha Flow" },
            new FlowSummary { Id = Guid.NewGuid(), Name = "Beta Flow" }
        };

        var results = await service.SearchRunsAsync(flows, Environment, "CORR-123");

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.RunId == "R-A");
        Assert.Contains(results, r => r.RunId == "R-B");
    }

    [Fact]
    public async Task SearchRunsAsync_matches_flow_name_substring_for_single_flow()
    {
        var service = CreateService(
            out _,
            FakeHttpMessageHandler.Json(RunsJson("R-A", "CORR-1")),
            FakeHttpMessageHandler.Json(RunsJson("R-B", "CORR-2")));

        var flows = new List<FlowSummary>
        {
            new FlowSummary { Id = Guid.NewGuid(), Name = "Alpha Flow" },
            new FlowSummary { Id = Guid.NewGuid(), Name = "Beta Flow" }
        };

        var results = await service.SearchRunsAsync(flows, Environment, "Alpha");

        var match = Assert.Single(results);
        Assert.Equal("R-A", match.RunId);
        Assert.Equal("Alpha Flow", match.FlowName);
    }

    [Fact]
    public async Task SearchRunsAsync_reports_progress_per_flow()
    {
        var service = CreateService(
            out _,
            FakeHttpMessageHandler.Json(RunsJson("R-A", "C1")),
            FakeHttpMessageHandler.Json(RunsJson("R-B", "C2")));

        var flows = new List<FlowSummary>
        {
            new FlowSummary { Id = Guid.NewGuid(), Name = "Alpha" },
            new FlowSummary { Id = Guid.NewGuid(), Name = "Beta" }
        };

        var progress = new List<(int processed, int total)>();
        await service.SearchRunsAsync(flows, Environment, term: null, onFlowProcessed: (p, t) => progress.Add((p, t)));

        Assert.Equal(new[] { (1, 2), (2, 2) }, progress.ToArray());
    }
}
