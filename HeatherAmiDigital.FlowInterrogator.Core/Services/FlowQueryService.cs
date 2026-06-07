using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly IOrganizationService _service;
    private readonly FlowParser _parser;

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
    public FlowQueryService(IOrganizationService service, FlowParser parser)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    /// <summary>
    /// Retrieves a lightweight list of all cloud flows in the environment.
    /// Does not fetch the heavy <c>clientdata</c> JSON definition to ensure fast grid loading.
    /// </summary>
    /// <returns>A read-only list of flow summaries.</returns>
    public IReadOnlyList<FlowSummary> GetFlowSummaries()
    {
        var query = new QueryExpression("workflow")
        {
            NoLock = true, // Avoid blocking other transactions in the production environment
            ColumnSet = new ColumnSet(
                "workflowid", "name", "description", "statecode",
                "createdon", "modifiedon", "ownerid")
        };

        // Category 5 = Modern Flow (Power Automate cloud flow)
        query.Criteria.AddCondition("category", ConditionOperator.Equal, 5);

        var results = _service.RetrieveMultiple(query);

        return results.Entities
            .Select(MapToSummary)
            .OrderByDescending(f => f.ModifiedOn)
            .ToList();
    }

    /// <summary>
    /// Searches the definitions of all cloud flows for a specific term.
    /// Fetches the <c>clientdata</c> column, parses the JSON, and returns matching nodes.
    /// </summary>
    /// <param name="searchTerm">The term to search for (e.g., a GUID, email address, or URL).</param>
    /// <returns>A read-only list of matches found across all flow definitions.</returns>
    public IReadOnlyList<FlowMatch> SearchFlowDefinitions(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return Array.Empty<FlowMatch>();
        }

        var query = new QueryExpression("workflow")
        {
            NoLock = true,
            ColumnSet = new ColumnSet(
                "workflowid", "name", "description", "statecode",
                "createdon", "modifiedon", "ownerid", "clientdata")
        };

        query.Criteria.AddCondition("category", ConditionOperator.Equal, 5);

        var results = _service.RetrieveMultiple(query);
        var matches = new List<FlowMatch>();

        foreach (var entity in results.Entities)
        {
            var summary = MapToSummary(entity);
            var rawJson = entity.GetAttributeValue<string>("clientdata");

            if (string.IsNullOrWhiteSpace(rawJson))
            {
                continue;
            }

            var definition = _parser.ParseDefinition(summary.Id, rawJson);
            var flowMatches = _parser.SearchDefinition(summary, definition, searchTerm);

            matches.AddRange(flowMatches);
        }

        return matches;
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
