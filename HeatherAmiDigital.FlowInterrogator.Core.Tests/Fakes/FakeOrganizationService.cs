using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace HeatherAmiDigital.FlowInterrogator.Core.Tests.Fakes;

/// <summary>
/// A minimal <see cref="IOrganizationService"/> test double. Returns a pre-seeded queue of
/// <see cref="EntityCollection"/> responses for successive <see cref="RetrieveMultiple"/> calls
/// (to exercise paging), and delegates <see cref="Retrieve"/> to a configurable handler.
/// All other members throw, so a test fails loudly if it exercises an unexpected path.
/// </summary>
public sealed class FakeOrganizationService : IOrganizationService
{
    private readonly Queue<EntityCollection> _retrieveMultipleResponses;

    /// <summary>Gets the queries passed to <see cref="RetrieveMultiple"/>, in order.</summary>
    public List<QueryBase> RetrieveMultipleQueries { get; } = new();

    /// <summary>Gets the number of times <see cref="RetrieveMultiple"/> was invoked.</summary>
    public int RetrieveMultipleCallCount { get; private set; }

    /// <summary>Gets or sets the handler used to satisfy <see cref="Retrieve"/> calls.</summary>
    public Func<string, Guid, ColumnSet, Entity> RetrieveHandler { get; set; }

    /// <summary>
    /// Initializes a new instance seeded with the responses returned, in order, by successive
    /// <see cref="RetrieveMultiple"/> calls.
    /// </summary>
    public FakeOrganizationService(params EntityCollection[] retrieveMultipleResponses)
    {
        _retrieveMultipleResponses = new Queue<EntityCollection>(retrieveMultipleResponses ?? Array.Empty<EntityCollection>());
    }

    /// <inheritdoc />
    public EntityCollection RetrieveMultiple(QueryBase query)
    {
        RetrieveMultipleCallCount++;
        RetrieveMultipleQueries.Add(query);
        return _retrieveMultipleResponses.Dequeue();
    }

    /// <inheritdoc />
    public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
    {
        if (RetrieveHandler == null)
        {
            throw new InvalidOperationException("RetrieveHandler was not configured for this test.");
        }

        return RetrieveHandler(entityName, id, columnSet);
    }

    /// <inheritdoc />
    public Guid Create(Entity entity) => throw new NotSupportedException();

    /// <inheritdoc />
    public void Update(Entity entity) => throw new NotSupportedException();

    /// <inheritdoc />
    public void Delete(string entityName, Guid id) => throw new NotSupportedException();

    /// <inheritdoc />
    public OrganizationResponse Execute(OrganizationRequest request) => throw new NotSupportedException();

    /// <inheritdoc />
    public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        => throw new NotSupportedException();

    /// <inheritdoc />
    public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        => throw new NotSupportedException();
}
