using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using HeatherAmiDigital.FlowInterrogator.Core.Models;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace HeatherAmiDigital.FlowInterrogator.Core.Services;

/// <summary>
/// Queries Microsoft Dataverse for Power Automate cloud flow records and 
/// orchestrates deep-text searches across their definitions.
/// </summary>
public sealed class FlowQueryService
{
    /// <summary>The Dataverse page size used when paging large flow result sets.</summary>
    private const int PageSize = 5000;

    /// <summary>
    /// Dataverse fault error code (0x80040217) returned when a requested record does not exist.
    /// </summary>
    private const int ObjectDoesNotExistErrorCode = -2147220969;

    private readonly IOrganizationService _service;
    private readonly FlowParser _parser;
    private readonly IFlowLogger _logger;

    /// <summary>
    /// Gets or sets the Power Platform environment identifier (e.g., <c>Default-{tenantId}</c>).
    /// Used to generate deep links to the Power Automate portal. 
    /// Must be set by the calling UI layer after the connection is established.
    /// </summary>
    public string EnvironmentId { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FlowQueryService"/> class.
    /// </summary>
    /// <param name="service">The Dataverse organization service used to execute queries.</param>
    /// <param name="parser">The parser used to deserialize and search flow definitions.</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger when null.</param>
    public FlowQueryService(IOrganizationService service, FlowParser parser, IFlowLogger logger = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _logger = logger ?? NullFlowLogger.Instance;
    }

    /// <summary>
    /// Retrieves a lightweight list of all cloud flows in the environment.
    /// Does not fetch the heavy <c>clientdata</c> JSON definition to ensure fast grid loading.
    /// </summary>
    /// <returns>A read-only list of flow summaries.</returns>
    public IReadOnlyList<FlowSummary> GetFlowSummaries()
    {
        _logger.Info("Querying Dataverse for cloud flow summaries.");

        var query = BuildFlowQuery(includeClientData: false);
        var summaries = new List<FlowSummary>();

        foreach (var entity in RetrieveAll(query, isCancellationRequested: null, onPageRetrieved: null))
        {
            summaries.Add(MapToSummary(entity));
        }

        _logger.Info($"Retrieved {summaries.Count} cloud flow summaries.");

        return summaries
            .OrderByDescending(f => f.ModifiedOn)
            .ToList();
    }

    /// <summary>
    /// Retrieves and parses the definition of a single cloud flow by its Dataverse identifier.
    /// </summary>
    /// <param name="flowId">The Dataverse <c>workflowid</c> of the flow.</param>
    /// <returns>
    /// The parsed <see cref="FlowDefinition"/>, or <c>null</c> if the record no longer exists.
    /// A record with empty <c>clientdata</c> yields a definition with no triggers or actions.
    /// </returns>
    public FlowDefinition GetFlowDefinition(Guid flowId)
    {
        _logger.Info($"Retrieving definition for flow {flowId}.");

        Entity entity;
        try
        {
            entity = _service.Retrieve(
                "workflow",
                flowId,
                new ColumnSet("clientdata", "name", "description", "statecode", "modifiedon"));
        }
        catch (FaultException<OrganizationServiceFault> ex) when (ex.Detail?.ErrorCode == ObjectDoesNotExistErrorCode)
        {
            _logger.Warning($"Flow {flowId} no longer exists in Dataverse.");
            return null;
        }

        var rawJson = entity.GetAttributeValue<string>("clientdata");
        return _parser.ParseDefinition(flowId, rawJson);
    }

    /// <summary>
    /// Searches the definitions of all cloud flows for a specific term.
    /// Fetches the <c>clientdata</c> column, parses the JSON, and returns matching nodes.
    /// Results are paged to stay within the Dataverse response size cap on large environments.
    /// </summary>
    /// <param name="searchTerm">The term to search for (e.g., a GUID, email address, or URL).</param>
    /// <param name="onPageRetrieved">
    /// Optional callback invoked after each page is retrieved, receiving the 1-based page number.
    /// </param>
    /// <param name="isCancellationRequested">
    /// Optional predicate polled between pages; when it returns <c>true</c> the search stops and
    /// returns the matches gathered so far.
    /// </param>
    /// <returns>A read-only list of matches found across all flow definitions.</returns>
    public IReadOnlyList<FlowMatch> SearchFlowDefinitions(
        string searchTerm,
        Action<int> onPageRetrieved = null,
        Func<bool> isCancellationRequested = null)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return Array.Empty<FlowMatch>();
        }

        _logger.Info($"Searching all flow definitions for '{searchTerm}'.");

        var query = BuildFlowQuery(includeClientData: true);
        var matches = new List<FlowMatch>();

        foreach (var entity in RetrieveAll(query, isCancellationRequested, onPageRetrieved))
        {
            var summary = MapToSummary(entity);
            var rawJson = entity.GetAttributeValue<string>("clientdata");

            if (string.IsNullOrWhiteSpace(rawJson))
            {
                continue;
            }

            var definition = _parser.ParseDefinition(summary.Id, rawJson);
            matches.AddRange(_parser.SearchDefinition(summary, definition, searchTerm));
        }

        _logger.Info($"Search for '{searchTerm}' found {matches.Count} matches.");
        return matches;
    }

    /// <summary>
    /// Builds the base <c>workflow</c> query filtered to modern cloud flows (category 5),
    /// with <see cref="QueryExpression.NoLock"/> set for production safety.
    /// </summary>
    private static QueryExpression BuildFlowQuery(bool includeClientData)
    {
        var columns = new List<string>
        {
            "workflowid", "name", "description", "statecode",
            "createdon", "modifiedon", "ownerid"
        };

        if (includeClientData)
        {
            columns.Add("clientdata");
        }

        var query = new QueryExpression("workflow")
        {
            NoLock = true, // Avoid blocking other transactions in the production environment
            ColumnSet = new ColumnSet(columns.ToArray()),
            PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 }
        };

        // Category 5 = Modern Flow (Power Automate cloud flow)
        query.Criteria.AddCondition("category", ConditionOperator.Equal, 5);

        return query;
    }

    /// <summary>
    /// Executes a paged retrieval, yielding every matching entity across all pages.
    /// Stops early if <paramref name="isCancellationRequested"/> returns <c>true</c>.
    /// </summary>
    private IEnumerable<Entity> RetrieveAll(
        QueryExpression query,
        Func<bool> isCancellationRequested,
        Action<int> onPageRetrieved)
    {
        while (true)
        {
            if (isCancellationRequested?.Invoke() == true)
            {
                _logger.Info($"Retrieval cancelled before page {query.PageInfo.PageNumber}.");
                yield break;
            }

            var results = _service.RetrieveMultiple(query);
            _logger.Info($"Retrieved page {query.PageInfo.PageNumber} ({results.Entities.Count} records).");

            foreach (var entity in results.Entities)
            {
                yield return entity;
            }

            onPageRetrieved?.Invoke(query.PageInfo.PageNumber);

            if (!results.MoreRecords)
            {
                yield break;
            }

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = results.PagingCookie;
        }
    }

    /// <summary>
    /// Maps a Dataverse <c>workflow</c> entity to a <see cref="FlowSummary"/> model.
    /// </summary>
    private FlowSummary MapToSummary(Entity entity)
    {
        return new FlowSummary
        {
            Id = entity.Id,
            Name = entity.GetAttributeValue<string>("name"),
            Description = entity.GetAttributeValue<string>("description"),
            State = (FlowState)(entity.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 0),
            CreatedOn = entity.GetAttributeValue<DateTime?>("createdon"),
            ModifiedOn = entity.GetAttributeValue<DateTime?>("modifiedon"),
            OwnerId = entity.GetAttributeValue<EntityReference>("ownerid")?.Id,
            OwnerName = entity.GetAttributeValue<EntityReference>("ownerid")?.Name,
            EnvironmentId = EnvironmentId
        };
    }
}
