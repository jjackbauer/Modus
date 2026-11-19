using System.Text.Json.Nodes;
using Wip.Factory.Host.Factory;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// FAC-P0-03 / K-05 / K-06: (templateId, version) is immutable, approval is bound to the stored body digest, and
/// revocation survives restart. A changed body can never inherit an old approval.
/// </summary>
public sealed class TemplateImmutabilityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factory-tpl-immutable-" + Guid.NewGuid().ToString("N"));

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

    private static JsonNode Proposal(string path = "/ping")
        => new JsonObject
        {
            ["templateId"] = "ping-service",
            ["templateVersion"] = 1,
            ["parameters"] = new JsonObject { ["path"] = path },
            ["scenarios"] = new JsonArray
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
            }
        };

    private FactoryTemplateRegistry Approved()
    {
        var registry = new FactoryTemplateRegistry(_root);
        var manifest = registry.Register(JsonNode.Parse(TemplateJson)!);
        Assert.True(registry.Approve(manifest.TemplateId, manifest.TemplateVersion, manifest.ManifestDigest));
        return registry;
    }

    [Fact]
    public void Template_GivenApprovedVersionReRegisteredWithDifferentBody_RefusesOverwrite()
    {
        var registry = Approved();
        var original = registry.Resolve("ping-service", 1).Manifest!;

        var changed = JsonNode.Parse(TemplateJson.Replace("\"status\": \"ok\"", "\"status\": \"fine\"", StringComparison.Ordinal))!;
        var conflict = Assert.Throws<TemplateVersionConflictException>(() => registry.Register(changed));

        Assert.Equal("template-version-conflict", conflict.Message.Split(':')[0]);
        Assert.Equal(original.ManifestDigest, registry.Resolve("ping-service", 1).Manifest!.ManifestDigest);

        var stored = Path.Combine(_root, "templates", "ping-service", "1.json");
        Assert.DoesNotContain("fine", File.ReadAllText(stored), StringComparison.Ordinal);

        // The original approval is still exactly bound to the original body.
        var decision = FactoryTemplatePolicy.Evaluate(registry, Proposal());
        Assert.True(decision.Approved, decision.Reason);
        Assert.Equal(original.ManifestDigest, decision.ManifestDigest);
    }

    [Fact]
    public void Template_GivenSameBodyRegisteredTwice_IsIdempotent()
    {
        var registry = Approved();
        var first = registry.Resolve("ping-service", 1).Manifest!;

        var again = registry.Register(JsonNode.Parse(TemplateJson)!);

        Assert.Equal(first.ManifestDigest, again.ManifestDigest);
        Assert.True(FactoryTemplatePolicy.Evaluate(registry, Proposal()).Approved);
    }

    [Fact]
    public void Template_GivenApprovalMarkerDigestMismatchesBodyAfterRestart_RefusesPolicy()
    {
        var registry = Approved();
        var marker = Path.Combine(_root, "templates", "ping-service", "1.approved");
        var forged = new JsonObject
        {
            ["templateId"] = "ping-service",
            ["templateVersion"] = 1,
            ["manifestDigest"] = "sha256:forged-marker",
            ["approver"] = "someone",
            ["approvedAtUtc"] = DateTimeOffset.UtcNow.ToString("O")
        };
        File.WriteAllText(marker, forged.ToJsonString());

        var restarted = new FactoryTemplateRegistry(_root);
        var decision = FactoryTemplatePolicy.Evaluate(restarted, Proposal());

        Assert.False(decision.Approved);
        Assert.Contains(FactoryTemplateRegistry.DigestMismatch, decision.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void Template_GivenManifestTamperedOnDiskAfterApproval_RefusesPolicy()
    {
        var registry = Approved();
        var stored = Path.Combine(_root, "templates", "ping-service", "1.json");
        File.WriteAllText(stored, TemplateJson.Replace("\"status\": \"ok\"", "\"status\": \"fine\"", StringComparison.Ordinal));

        var decision = FactoryTemplatePolicy.Evaluate(registry, Proposal());

        Assert.False(decision.Approved);
        Assert.StartsWith(FactoryTemplateRegistry.DigestMismatch, decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Template_GivenRevokedVersionAndRestart_RemainsRevoked()
    {
        var registry = Approved();
        registry.Revoke("ping-service", 1);

        var restarted = new FactoryTemplateRegistry(_root);
        var (manifest, error) = restarted.Resolve("ping-service", 1);

        Assert.Null(manifest);
        Assert.StartsWith(FactoryTemplateRegistry.Revoked, error, StringComparison.Ordinal);
        Assert.False(FactoryTemplatePolicy.Evaluate(restarted, Proposal()).Approved);
    }

    [Fact]
    public void Template_GivenRevokedVersion_CannotBeReApproved()
    {
        var registry = Approved();
        var digest = registry.Resolve("ping-service", 1).Manifest!.ManifestDigest;
        registry.Revoke("ping-service", 1);

        Assert.False(registry.Approve("ping-service", 1, digest));
        Assert.StartsWith(FactoryTemplateRegistry.Revoked, new FactoryTemplateRegistry(_root).Resolve("ping-service", 1).Error!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("nested/id")]
    [InlineData("C:\\absolute")]
    [InlineData("")]
    public void Template_GivenTraversalId_RefusesOutOfRootWrite(string templateId)
    {
        var registry = new FactoryTemplateRegistry(_root);
        var body = JsonNode.Parse(TemplateJson)!;
        body["templateId"] = templateId;

        Assert.Throws<InvalidOperationException>(() => registry.Register(body));

        var outside = Path.Combine(Path.GetDirectoryName(_root)!, "escape");
        Assert.False(Directory.Exists(outside));
        Assert.False(File.Exists(Path.Combine(outside, "1.json")));
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
