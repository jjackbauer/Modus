using System.Text.Json.Nodes;
using Wip.Factory.Host.Factory;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// D-09 (v0.3 §4.3): automated contract approval only for an exact, operator-approved template with permitted
/// parameters. Anything else falls back to human approval.
/// </summary>
public sealed class TemplatePolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factory-tpl-" + Guid.NewGuid().ToString("N"));

    private const string TemplateJson = """
        {
          "templateId": "ping-service",
          "templateVersion": 1,
          "schemaVersion": 1,
          "supportedClass": "aspnetcore-http-net10",
          "parameters": { "path": { "type": "string", "allowed": ["/ping", "/health"] } },
          "scenarios": [
            { "id": "AC-1", "method": "GET", "path": "${path}",
              "expect": { "status": 200, "jsonSubset": { "status": "ok" } } }
          ]
        }
        """;

    private static JsonNode Proposal(string templateId = "ping-service", int version = 1, string path = "/ping", JsonNode? scenarios = null)
    {
        var defaultScenarios = new JsonArray
        {
            new JsonObject
            {
                ["id"] = "AC-1",
                ["method"] = "GET",
                ["path"] = path,
                ["expect"] = new JsonObject
                {
                    ["status"] = 200,
                    ["jsonSubset"] = new JsonObject { ["status"] = "ok" }
                }
            }
        };

        return new JsonObject
        {
            ["templateId"] = templateId,
            ["templateVersion"] = version,
            ["parameters"] = new JsonObject { ["path"] = path },
            ["scenarios"] = scenarios ?? defaultScenarios
        };
    }
    private FactoryTemplateRegistry Approved(out TemplateManifest manifest)
    {
        var registry = new FactoryTemplateRegistry(_root);
        manifest = registry.Register(JsonNode.Parse(TemplateJson)!);
        Assert.True(registry.Approve(manifest.TemplateId, manifest.TemplateVersion, manifest.ManifestDigest));
        return registry;
    }

    [Fact]
    public void Contract_GivenApprovedTemplateAndValidParameters_AutoApprovesWithProvenance()
    {
        var registry = Approved(out var manifest);

        var decision = FactoryTemplatePolicy.Evaluate(registry, Proposal());

        Assert.True(decision.Approved, decision.Reason);
        Assert.Equal("template-policy", decision.ApprovalKind);
        Assert.Equal(manifest.TemplateId, decision.TemplateId);
        Assert.Equal(manifest.ManifestDigest, decision.ManifestDigest);
        Assert.NotNull(decision.ParametersDigest);
        Assert.NotNull(decision.ContractDigest);
    }

    [Fact]
    public void Contract_GivenUnknownTemplate_FallsBackToHumanApproval()
    {
        var registry = Approved(out _);

        var decision = FactoryTemplatePolicy.Evaluate(registry, Proposal(templateId: "not-registered"));

        Assert.False(decision.Approved);
        Assert.StartsWith(FactoryTemplateRegistry.UnknownTemplate, decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Contract_GivenUnapprovedVersion_FallsBackToHumanApproval()
    {
        var registry = new FactoryTemplateRegistry(_root);
        var manifest = registry.Register(JsonNode.Parse(TemplateJson)!);
        // Registered but never approved.
        Assert.False(string.IsNullOrEmpty(manifest.ManifestDigest));

        var decision = FactoryTemplatePolicy.Evaluate(registry, Proposal());

        Assert.False(decision.Approved);
        Assert.StartsWith(FactoryTemplateRegistry.Revoked, decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Contract_GivenRevokedTemplate_FallsBackToHumanApproval()
    {
        var registry = Approved(out var manifest);
        registry.Revoke(manifest.TemplateId, manifest.TemplateVersion);

        var decision = FactoryTemplatePolicy.Evaluate(registry, Proposal());

        Assert.False(decision.Approved);
        Assert.StartsWith(FactoryTemplateRegistry.Revoked, decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Contract_GivenTamperedManifestBody_RefusesPolicyApproval()
    {
        var registry = Approved(out var manifest);
        // Rewrite the stored body after approval; the digest no longer matches.
        var stored = Path.Combine(_root, "templates", manifest.TemplateId, manifest.TemplateVersion + ".json");
        File.WriteAllText(stored, TemplateJson.Replace("\"status\": \"ok\"", "\"status\": \"fine\"", StringComparison.Ordinal));

        var decision = FactoryTemplatePolicy.Evaluate(registry, Proposal());

        Assert.False(decision.Approved);
        Assert.StartsWith(FactoryTemplateRegistry.DigestMismatch, decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Contract_GivenTemplateAssertionEdited_RefusesPolicyApproval()
    {
        var registry = Approved(out _);
        // Same template, but the proposed scenario weakens the assertion.
        var altered = new JsonArray
        {
            new JsonObject
            {
                ["id"] = "AC-1",
                ["method"] = "GET",
                ["path"] = "/ping",
                ["expect"] = new JsonObject { ["status"] = 200 }
            }
        };

        var decision = FactoryTemplatePolicy.Evaluate(registry, Proposal(scenarios: altered));

        Assert.False(decision.Approved);
        Assert.Equal(FactoryTemplatePolicy.ScenarioMismatch, decision.Reason);
    }

    [Fact]
    public void Contract_GivenTemplateParamOutOfBounds_RefusesPolicyApproval()
    {
        var registry = Approved(out _);

        var decision = FactoryTemplatePolicy.Evaluate(registry, Proposal(path: "/everything"));

        Assert.False(decision.Approved);
        Assert.Contains("out-of-range", decision.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void Contract_GivenUnexpectedParameter_RefusesPolicyApproval()
    {
        var registry = Approved(out _);
        var proposal = Proposal();
        ((JsonObject)proposal["parameters"]!)["extra"] = "surprise";

        var decision = FactoryTemplatePolicy.Evaluate(registry, proposal);

        Assert.False(decision.Approved);
        Assert.Contains("unexpected", decision.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void Contract_GivenNonTemplateProposal_DoesNotAutoApprove()
    {
        var registry = Approved(out _);
        var plain = JsonNode.Parse("""{ "schemaVersion": 1, "scenarios": [] }""")!;

        var decision = FactoryTemplatePolicy.Evaluate(registry, plain);

        Assert.False(decision.Approved);
        Assert.Equal(FactoryTemplatePolicy.NotATemplateProposal, decision.Reason);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}