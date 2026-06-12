using System;
using System.Collections.Generic;
using System.Linq;
using HeatherAmiDigital.FlowInterrogator.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HeatherAmiDigital.FlowInterrogator.Core.Services;

/// <summary>
/// Parses raw Power Automate JSON definitions into strongly-typed models
/// and executes deep-text searches across the definition tree.
/// </summary>
public sealed class FlowParser
{
    private const int SnippetContextLength = 40;

    /// <summary>
    /// Parses a raw JSON flow definition into a <see cref="FlowDefinition"/> model.
    /// Recursively extracts nested actions (e.g., inside Scopes or Conditions) into a flat dictionary.
    /// </summary>
    /// <param name="flowId">The Dataverse identifier of the flow.</param>
    /// <param name="rawJson">The raw JSON string from the Dataverse <c>clientdata</c> or <c>definition</c> column.</param>
    /// <returns>A parsed <see cref="FlowDefinition"/>, or a degraded definition containing only the raw JSON if parsing fails.</returns>
    public FlowDefinition ParseDefinition(Guid flowId, string rawJson)
    {
        var definition = new FlowDefinition
        {
            FlowId = flowId,
            Actions = new Dictionary<string, FlowAction>(),
            Triggers = new Dictionary<string, FlowTrigger>()
        };

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return definition;
        }

        JToken rootToken;
        try
        {
            rootToken = JToken.Parse(rawJson);
        }
        catch (JsonException)
        {
            // Return a degraded definition so the UI can still display the raw text
            definition.RawJson = JValue.CreateString(rawJson);
            return definition;
        }

        definition.RawJson = rootToken;
        definition.Schema = rootToken["$schema"]?.ToString();
        definition.ContentVersion = rootToken["contentVersion"]?.ToString();
        definition.Parameters = rootToken["parameters"];

        if (rootToken is JObject rootObj)
        {
            ParseTriggers(rootObj, definition);
            ExtractActionsRecursive(rootObj, (Dictionary<string, FlowAction>)definition.Actions);
        }

        return definition;
    }

    /// <summary>
    /// Searches a parsed flow definition for a specific term (e.g., GUID, email, URL).
    /// </summary>
    /// <param name="summary">The flow summary for context (name, ID).</param>
    /// <param name="definition">The parsed flow definition to search.</param>
    /// <param name="searchTerm">The term to search for (case-insensitive).</param>
    /// <returns>A list of matches indicating where the term was found.</returns>
    public IReadOnlyList<FlowMatch> SearchDefinition(FlowSummary summary, FlowDefinition definition, string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm) || definition?.RawJson == null)
        {
            return Array.Empty<FlowMatch>();
        }

        var matches = new List<FlowMatch>();
        var lowerSearchTerm = searchTerm.ToLowerInvariant();

        // Search parameters
        if (definition.Parameters != null && ContainsTerm(definition.Parameters.ToString(), lowerSearchTerm))
        {
            matches.Add(CreateMatch(summary, FlowMatchLocation.Parameter, "Parameters", definition.Parameters.ToString(), lowerSearchTerm));
        }

        // Search triggers
        foreach (var trigger in definition.Triggers.Values)
        {
            var triggerJson = trigger.RawJson?.ToString() ?? string.Empty;
            if (ContainsTerm(triggerJson, lowerSearchTerm))
            {
                matches.Add(CreateMatch(summary, FlowMatchLocation.Trigger, trigger.Name, triggerJson, lowerSearchTerm));
            }
        }

        // Search actions
        foreach (var action in definition.Actions.Values)
        {
            var actionJson = action.RawJson?.ToString() ?? string.Empty;
            if (ContainsTerm(actionJson, lowerSearchTerm))
            {
                matches.Add(CreateMatch(summary, FlowMatchLocation.Action, action.Name, actionJson, lowerSearchTerm));
            }
        }

        // Fallback: if no specific nodes matched but the raw JSON did, report a raw definition match
        if (matches.Count == 0 && ContainsTerm(definition.RawJson.ToString(), lowerSearchTerm))
        {
            matches.Add(CreateMatch(summary, FlowMatchLocation.RawDefinition, null, definition.RawJson.ToString(), lowerSearchTerm));
        }

        return matches;
    }

    private void ParseTriggers(JObject rootObj, FlowDefinition definition)
    {
        if (rootObj["triggers"] is not JObject triggersObj) return;

        var triggers = new Dictionary<string, FlowTrigger>();
        foreach (var prop in triggersObj.Properties())
        {
            if (prop.Value is not JObject triggerJson) continue;

            triggers[prop.Name] = new FlowTrigger
            {
                Name = prop.Name,
                Type = triggerJson["type"]?.ToString(),
                Kind = triggerJson["kind"]?.ToString(),
                RawJson = triggerJson
            };
        }

        definition.Triggers = triggers;
    }

    /// <summary>
    /// Recursively traverses the JSON tree to extract all actions, flattening nested structures
    /// like Scopes, Conditions (If/Else), and Switches into a single dictionary.
    /// Logic App action names are guaranteed to be globally unique within a flow.
    /// </summary>
    private void ExtractActionsRecursive(JToken currentToken, Dictionary<string, FlowAction> collectedActions)
    {
        if (currentToken is not JObject currentObj) return;

        // Extract standard actions block
        if (currentObj.TryGetValue("actions", out var actionsValue) && actionsValue is JObject actionsObj)
        {
            foreach (var actionProp in actionsObj.Properties())
            {
                if (actionProp.Value is not JObject actionJson) continue;

                collectedActions[actionProp.Name] = ParseAction(actionProp.Name, actionJson);

                // Recurse into the action itself to find nested scopes/conditions
                ExtractActionsRecursive(actionJson, collectedActions);
            }
        }

        // Handle Switch cases
        if (currentObj.TryGetValue("cases", out var casesValue) && casesValue is JObject casesObj)
        {
            foreach (var caseProp in casesObj.Properties())
            {
                ExtractActionsRecursive(caseProp.Value, collectedActions);
            }
        }

        // Handle If/Else branches
        if (currentObj.TryGetValue("else", out var elseValue))
        {
            ExtractActionsRecursive(elseValue, collectedActions);
        }

        // Handle the default branch of a Switch
        if (currentObj.TryGetValue("default", out var defaultValue))
        {
            ExtractActionsRecursive(defaultValue, collectedActions);
        }
    }

    private FlowAction ParseAction(string name, JObject actionJson)
    {
        var runAfter = new Dictionary<string, IReadOnlyList<string>>();
        if (actionJson["runAfter"] is JObject runAfterObj)
        {
            foreach (var prop in runAfterObj.Properties())
            {
                runAfter[prop.Name] = prop.Value.ToObject<List<string>>() ?? new List<string>();
            }
        }

        return new FlowAction
        {
            Name = name,
            Type = actionJson["type"]?.ToString(),
            Kind = actionJson["kind"]?.ToString(),
            RunAfter = runAfter,
            RawJson = actionJson
        };
    }

    private bool ContainsTerm(string text, string lowerSearchTerm)
    {
        return text != null && text.IndexOf(lowerSearchTerm, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private FlowMatch CreateMatch(FlowSummary summary, FlowMatchLocation location, string nodeName, string jsonContext, string lowerSearchTerm)
    {
        return new FlowMatch
        {
            FlowId = summary.Id,
            FlowName = summary.Name,
            Location = location,
            NodeName = nodeName,
            MatchSnippet = ExtractSnippet(jsonContext, lowerSearchTerm)
        };
    }

    /// <summary>
    /// Extracts a short text snippet centered around the first occurrence of the search term.
    /// </summary>
    private string ExtractSnippet(string text, string lowerSearchTerm)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var index = text.IndexOf(lowerSearchTerm, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return string.Empty;

        var start = Math.Max(0, index - SnippetContextLength);
        var length = Math.Min(text.Length - start, lowerSearchTerm.Length + (SnippetContextLength * 2));

        var snippet = text.Substring(start, length).Replace("\r", " ").Replace("\n", " ");

        if (start > 0) snippet = "..." + snippet;
        if (start + length < text.Length) snippet += "...";

        return snippet;
    }
}
