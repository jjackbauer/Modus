using System.Text.Json.Nodes;
using Wip.Factory.Host.Factory;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// D-09 composed (v0.3 §4.3): a requirement may name an approved template; intake instantiates it, and approval can be
/// granted by policy with full provenance instead of a human digest.
/// </summary>
public sealed class TemplateIntakeTests
{
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

    private const string Requirement = """
        ---
        title: Ping HTTP API
        supportedClass: aspnetcore-http-net10
        template: ping-service@1
        parameters: {"path":"/ping"}
        ---

        Create an HTTP JSON API with a health endpoint.

        ## Acceptance expectations
        - GET /ping returns 200 with JSON {"status":"ok"}.
        """;

    private static FactoryControlRoot Root(out string basePath)
    {
        basePath = Path.Combine(Path.GetTempPath(), "factory-tplintake-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(basePath, "target");
        Directory.CreateDirectory(repository);

        var (root, error) = FactoryControlRoot.Resolve(repository, Path.Combine(basePath, "control"));
        Assert.NotNull(root);
        Assert.Null(error);
        return root!;
    }

    [Fact]
    public async Task Contract_GivenTemplateRequirement_IntakeInstantiatesAndPolicyApproves()
    {
        var root = Root(out _);

        // Operator registers and approves the template.
        var registry = new FactoryTemplateRegistry(root.Root);
        var manifest = registry.Register(JsonNode.Parse(TemplateJson)!);
        Assert.True(registry.Approve(manifest.TemplateId, manifest.TemplateVersion, manifest.ManifestDigest));

        var runtime = new FactoryRuntime(root);
        var submit = await runtime.SubmitAsync(Requirement);
        Assert.True(submit.Accepted, string.Join(";", submit.Errors));

        await runtime.RunAsync(16);

        var (proposal, showError) = await runtime.ContractShowAsync(submit.WorkItemId!);
        Assert.Null(showError);
        Assert.True(proposal!.Executable);

        var (ok, error, decision) = await runtime.ContractPolicyApproveAsync(submit.WorkItemId!);
        Assert.True(ok, error);
        Assert.Equal("template-policy", decision!.ApprovalKind);
        Assert.Equal("ping-service", decision.TemplateId);

        // Exactly one pass: this asserts the transition to RequirementsAligned and stops there.
        await runtime.RunAsync(1);

        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("RequirementsAligned", status.State);
    }

    [Fact]
    public async Task Contract_GivenTemplateOutOfBounds_FallsBackWithoutApproving()
    {
        var root = Root(out _);
        var registry = new FactoryTemplateRegistry(root.Root);
        var manifest = registry.Register(JsonNode.Parse(TemplateJson)!);
        Assert.True(registry.Approve(manifest.TemplateId, manifest.TemplateVersion, manifest.ManifestDigest));

        var runtime = new FactoryRuntime(root);
        var submit = await runtime.SubmitAsync(Requirement.Replace("\"/ping\"", "\"/everything\"", StringComparison.Ordinal));
        Assert.True(submit.Accepted, string.Join(";", submit.Errors));

        await runtime.RunAsync(16);

        var (ok, error, _) = await runtime.ContractPolicyApproveAsync(submit.WorkItemId!);
        Assert.False(ok);
        Assert.Contains("parameter-not-permitted", error!, StringComparison.Ordinal);

        var status = Assert.Single(await runtime.StatusAsync());
        Assert.Equal("Proposed", status.State);
    }

    [Fact]
    public async Task Contract_GivenPolicyContractApproval_CannotAuthorizeMerge()
    {
        var root = Root(out _);
        var registry = new FactoryTemplateRegistry(root.Root);
        var manifest = registry.Register(JsonNode.Parse(TemplateJson)!);
        Assert.True(registry.Approve(manifest.TemplateId, manifest.TemplateVersion, manifest.ManifestDigest));

        var runtime = new FactoryRuntime(root);
        var submit = await runtime.SubmitAsync(Requirement);
        Assert.True(submit.Accepted, string.Join(";", submit.Errors));
        await runtime.RunAsync(16);

        var (ok, error, _) = await runtime.ContractPolicyApproveAsync(submit.WorkItemId!);
        Assert.True(ok, error);

        // D-09 authorises the contract only. Merge authorisation is separate and cannot be inferred from it.
        var (mergeOk, mergeError) = await runtime.ApproveMergeAsync(submit.WorkItemId!, "sha256:anything");
        Assert.False(mergeOk);
        Assert.Equal("no-review-to-approve", mergeError);
    }
}