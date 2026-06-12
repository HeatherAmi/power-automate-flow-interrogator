using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using HeatherAmiDigital.FlowInterrogator.Core.Models;
using HeatherAmiDigital.FlowInterrogator.Core.Services;
using HeatherAmiDigital.FlowInterrogator.Core.Tests.Fakes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace HeatherAmiDigital.FlowInterrogator.Core.Tests;

/// <summary>
/// Tests for <see cref="FlowQueryService"/> covering single-record fetch (R11.1.1),
/// paged search (R11.1.2), and graceful mapping of sparse entities (R15.3).
/// </summary>
public sealed class FlowQueryServiceTests
{
    private const int ObjectDoesNotExistErrorCode = -2147220969;

    private static Entity Workflow(Guid id, string name, string clientData = null)
    {
        var entity = new Entity("workflow", id) { ["name"] = name };
        if (clientData != null)
        {
            entity["clientdata"] = clientData;
        }

        return entity;
    }

    private static string ActionJson(string needle)
        => $"{{ \"actions\": {{ \"Compose_1\": {{ \"type\": \"Compose\", \"inputs\": \"{needle}\" }} }} }}";

    [Fact]
    public void SearchFlowDefinitions_empty_term_returns_empty_without_querying()
    {
        var service = new FakeOrganizationService(); // no responses seeded
        var sut = new FlowQueryService(service, new FlowParser());

        Assert.Empty(sut.SearchFlowDefinitions(null));
        Assert.Empty(sut.SearchFlowDefinitions("   "));
        Assert.Equal(0, service.RetrieveMultipleCallCount);
    }

    [Fact]
    public void SearchFlowDefinitions_collects_matches_across_pages()
    {
        var flowA = Guid.NewGuid();
        var flowB = Guid.NewGuid();

        var page1 = new EntityCollection(new List<Entity> { Workflow(flowA, "Flow A", ActionJson("needle")) })
        {
            MoreRecords = true,
            PagingCookie = "cookie-1"
        };
        var page2 = new EntityCollection(new List<Entity> { Workflow(flowB, "Flow B", ActionJson("needle")) })
        {
            MoreRecords = false
        };

        var service = new FakeOrganizationService(page1, page2);
        var sut = new FlowQueryService(service, new FlowParser());

        var matches = sut.SearchFlowDefinitions("needle");

        Assert.Equal(2, service.RetrieveMultipleCallCount);
        Assert.Contains(matches, m => m.FlowId == flowA);
        Assert.Contains(matches, m => m.FlowId == flowB);

        // The paging loop advanced the page number and carried the cookie forward.
        var lastQuery = Assert.IsType<QueryExpression>(service.RetrieveMultipleQueries.Last());
        Assert.Equal(2, lastQuery.PageInfo.PageNumber);
        Assert.Equal("cookie-1", lastQuery.PageInfo.PagingCookie);
    }

    [Fact]
    public void SearchFlowDefinitions_invokes_page_callback_per_page()
    {
        var page1 = new EntityCollection(new List<Entity>()) { MoreRecords = true, PagingCookie = "c" };
        var page2 = new EntityCollection(new List<Entity>()) { MoreRecords = false };
        var service = new FakeOrganizationService(page1, page2);
        var sut = new FlowQueryService(service, new FlowParser());

        var pages = new List<int>();
        sut.SearchFlowDefinitions("needle", onPageRetrieved: pages.Add);

        Assert.Equal(new[] { 1, 2 }, pages.ToArray());
    }

    [Fact]
    public void SearchFlowDefinitions_stops_when_cancellation_requested()
    {
        var page1 = new EntityCollection(new List<Entity>()) { MoreRecords = true, PagingCookie = "c" };
        var service = new FakeOrganizationService(page1); // only one response: a 2nd call would throw
        var sut = new FlowQueryService(service, new FlowParser());

        var calls = 0;
        var matches = sut.SearchFlowDefinitions(
            "needle",
            isCancellationRequested: () => calls++ > 0); // allow first page, cancel before second

        Assert.Empty(matches);
        Assert.Equal(1, service.RetrieveMultipleCallCount);
    }

    [Fact]
    public void GetFlowDefinition_valid_record_with_clientdata_parses_actions()
    {
        var flowId = Guid.NewGuid();
        var service = new FakeOrganizationService
        {
            RetrieveHandler = (_, _, _) => Workflow(flowId, "Flow", ActionJson("hello"))
        };
        var sut = new FlowQueryService(service, new FlowParser());

        var definition = sut.GetFlowDefinition(flowId);

        Assert.NotNull(definition);
        Assert.True(definition.Actions.ContainsKey("Compose_1"));
    }

    [Fact]
    public void GetFlowDefinition_valid_record_with_empty_clientdata_returns_empty_definition()
    {
        var flowId = Guid.NewGuid();
        var service = new FakeOrganizationService
        {
            RetrieveHandler = (_, _, _) => Workflow(flowId, "Flow", clientData: string.Empty)
        };
        var sut = new FlowQueryService(service, new FlowParser());

        var definition = sut.GetFlowDefinition(flowId);

        Assert.NotNull(definition);
        Assert.Empty(definition.Actions);
        Assert.Empty(definition.Triggers);
    }

    [Fact]
    public void GetFlowDefinition_missing_record_returns_null()
    {
        var fault = new OrganizationServiceFault { ErrorCode = ObjectDoesNotExistErrorCode };
        var service = new FakeOrganizationService
        {
            RetrieveHandler = (_, _, _) => throw new FaultException<OrganizationServiceFault>(fault, new FaultReason("does not exist"))
        };
        var sut = new FlowQueryService(service, new FlowParser());

        Assert.Null(sut.GetFlowDefinition(Guid.NewGuid()));
    }

    [Fact]
    public void GetFlowDefinition_unrelated_fault_is_rethrown()
    {
        var fault = new OrganizationServiceFault { ErrorCode = -1 };
        var service = new FakeOrganizationService
        {
            RetrieveHandler = (_, _, _) => throw new FaultException<OrganizationServiceFault>(fault, new FaultReason("boom"))
        };
        var sut = new FlowQueryService(service, new FlowParser());

        Assert.Throws<FaultException<OrganizationServiceFault>>(() => sut.GetFlowDefinition(Guid.NewGuid()));
    }

    [Fact]
    public void GetFlowSummaries_maps_sparse_entity_without_throwing()
    {
        var id = Guid.NewGuid();
        // Entity with only an id: every other attribute is missing.
        var page = new EntityCollection(new List<Entity> { new Entity("workflow", id) }) { MoreRecords = false };
        var service = new FakeOrganizationService(page);
        var sut = new FlowQueryService(service, new FlowParser()) { EnvironmentId = "env-1" };

        var summaries = sut.GetFlowSummaries();

        var summary = Assert.Single(summaries);
        Assert.Equal(id, summary.Id);
        Assert.Null(summary.Name);
        Assert.Null(summary.OwnerId);
        Assert.Equal((FlowState)0, summary.State);
        Assert.Equal("env-1", summary.EnvironmentId);
    }
}
