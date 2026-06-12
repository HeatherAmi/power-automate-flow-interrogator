using System;
using System.Linq;
using HeatherAmiDigital.FlowInterrogator.Core.Models;
using HeatherAmiDigital.FlowInterrogator.Core.Services;
using Xunit;

namespace HeatherAmiDigital.FlowInterrogator.Core.Tests;

/// <summary>
/// Tests for <see cref="FlowParser"/> covering recursive action flattening
/// (Scope &gt; Condition &gt; Switch), <c>runAfter</c> preservation, and deep search.
/// </summary>
public sealed class FlowParserTests
{
    // Scope_Top
    //   ├─ Condition_1 (If)
    //   │    ├─ then: Set_variable
    //   │    └─ else: Terminate
    //   └─ Switch_1  (runAfter Condition_1)
    //        ├─ Case_A: Send_email (contains the GUID we search for)
    //        └─ default: Compose_default
    private const string NestedFlowJson = @"
    {
      ""$schema"": ""https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#"",
      ""contentVersion"": ""1.0.0.0"",
      ""triggers"": {
        ""manual"": { ""type"": ""Request"", ""kind"": ""Http"" }
      },
      ""actions"": {
        ""Scope_Top"": {
          ""type"": ""Scope"",
          ""runAfter"": {},
          ""actions"": {
            ""Condition_1"": {
              ""type"": ""If"",
              ""runAfter"": {},
              ""actions"": {
                ""Set_variable"": { ""type"": ""SetVariable"", ""runAfter"": {} }
              },
              ""else"": {
                ""actions"": {
                  ""Terminate"": { ""type"": ""Terminate"", ""runAfter"": {} }
                }
              }
            },
            ""Switch_1"": {
              ""type"": ""Switch"",
              ""runAfter"": { ""Condition_1"": [ ""Succeeded"" ] },
              ""cases"": {
                ""Case_A"": {
                  ""actions"": {
                    ""Send_email"": {
                      ""type"": ""ApiConnection"",
                      ""runAfter"": {},
                      ""inputs"": { ""to"": ""user-3F2504E0-4F89-41D3-9A0C-0305E82C3301@example.com"" }
                    }
                  }
                }
              },
              ""default"": {
                ""actions"": {
                  ""Compose_default"": { ""type"": ""Compose"", ""runAfter"": {} }
                }
              }
            }
          }
        }
      }
    }";

    [Fact]
    public void ParseDefinition_flattens_all_nested_actions()
    {
        var parser = new FlowParser();

        var definition = parser.ParseDefinition(Guid.NewGuid(), NestedFlowJson);

        Assert.Equal(
            new[] { "Compose_default", "Condition_1", "Scope_Top", "Send_email", "Set_variable", "Switch_1", "Terminate" },
            definition.Actions.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void ParseDefinition_parses_triggers()
    {
        var parser = new FlowParser();

        var definition = parser.ParseDefinition(Guid.NewGuid(), NestedFlowJson);

        Assert.True(definition.Triggers.ContainsKey("manual"));
        Assert.Equal("Request", definition.Triggers["manual"].Type);
    }

    [Fact]
    public void ParseDefinition_preserves_runAfter_dependencies()
    {
        var parser = new FlowParser();

        var definition = parser.ParseDefinition(Guid.NewGuid(), NestedFlowJson);

        var switchAction = definition.Actions["Switch_1"];
        Assert.True(switchAction.RunAfter.ContainsKey("Condition_1"));
        Assert.Equal(new[] { "Succeeded" }, switchAction.RunAfter["Condition_1"].ToArray());

        // A leaf with no dependencies has an empty runAfter map.
        Assert.Empty(definition.Actions["Set_variable"].RunAfter);
    }

    [Fact]
    public void ParseDefinition_invalid_json_returns_degraded_definition_with_raw_text()
    {
        var parser = new FlowParser();

        var definition = parser.ParseDefinition(Guid.NewGuid(), "{ not valid json ");

        Assert.NotNull(definition.RawJson);
        Assert.Empty(definition.Actions);
        Assert.Empty(definition.Triggers);
    }

    [Theory]
    [InlineData("3f2504e0-4f89-41d3-9a0c-0305e82c3301")] // lower-case
    [InlineData("3F2504E0-4F89-41D3-9A0C-0305E82C3301")] // upper-case
    public void SearchDefinition_is_case_insensitive(string term)
    {
        var parser = new FlowParser();
        var summary = new FlowSummary { Id = Guid.NewGuid(), Name = "Demo Flow" };
        var definition = parser.ParseDefinition(summary.Id, NestedFlowJson);

        var matches = parser.SearchDefinition(summary, definition, term);

        // The term lives in the Send_email leaf; a deep search also reports each ancestor
        // action whose JSON subtree contains it (Scope_Top, Switch_1).
        Assert.All(matches, m => Assert.Equal(FlowMatchLocation.Action, m.Location));
        Assert.Contains(matches, m => m.NodeName == "Send_email");
    }

    [Fact]
    public void SearchDefinition_returns_snippet_around_match()
    {
        var parser = new FlowParser();
        var summary = new FlowSummary { Id = Guid.NewGuid(), Name = "Demo Flow" };
        var definition = parser.ParseDefinition(summary.Id, NestedFlowJson);

        var match = parser.SearchDefinition(summary, definition, "0305E82C3301")
            .First(m => m.NodeName == "Send_email");

        Assert.Contains("0305e82c3301", match.MatchSnippet, StringComparison.OrdinalIgnoreCase);
        // The snippet is a window, not the entire document.
        Assert.True(match.MatchSnippet.Length < definition.RawJson.ToString().Length);
    }

    [Fact]
    public void SearchDefinition_no_match_returns_empty()
    {
        var parser = new FlowParser();
        var summary = new FlowSummary { Id = Guid.NewGuid(), Name = "Demo Flow" };
        var definition = parser.ParseDefinition(summary.Id, NestedFlowJson);

        Assert.Empty(parser.SearchDefinition(summary, definition, "value-that-does-not-exist"));
    }
}
