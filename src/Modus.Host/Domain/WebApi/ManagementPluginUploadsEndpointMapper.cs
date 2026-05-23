using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Modus.Host.Plugins.Authorization;
using Modus.Host.Plugins.Uploads;

namespace Modus.Host.Domain.WebApi;

internal sealed class ManagementPluginUploadsEndpointMapper
{
    private readonly PluginUploadAuthorizationPipeline _authorizationPipeline;
    private readonly PluginUploadPipeline _uploadPipeline;
    private readonly PluginUploadOperationStore _operationStore;

    public ManagementPluginUploadsEndpointMapper(
        PluginUploadAuthorizationPipeline authorizationPipeline,
        PluginUploadPipeline uploadPipeline,
        PluginUploadOperationStore operationStore)
    {
        _authorizationPipeline = authorizationPipeline ?? throw new ArgumentNullException(nameof(authorizationPipeline));
        _uploadPipeline = uploadPipeline ?? throw new ArgumentNullException(nameof(uploadPipeline));
        _operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
    }

    public WebApplication Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost(
            "/management/plugins/uploads",
            (HttpContext httpContext, CancellationToken cancellationToken) => HandleUploadAsync(httpContext, cancellationToken))
            .WithName("ManagementPluginsUploads_Post")
            .WithTags("Management")
            .WithSummary("Submit a plugin package upload for asynchronous processing")
            .WithDescription("Accepts a signed plugin package upload, then runs extract, validate, load, and run stages asynchronously.");

        app.MapGet(
            "/management/plugins/uploads/{operationId:guid}",
            (Guid operationId) => HandleStatus(operationId))
            .WithName("ManagementPluginsUploads_GetByOperationId")
            .WithTags("Management")
            .WithSummary("Get plugin upload operation status")
            .WithDescription("Returns current progress for an asynchronous plugin upload operation, including terminal success or failure details.");

        return app;
    }

    private IResult HandleStatus(Guid operationId)
    {
        if (!_operationStore.TryGet(operationId, out var status) || status is null)
        {
            return Results.NotFound(new
            {
                error = $"Upload operation '{operationId}' was not found.",
            });
        }

        return Results.Ok(ManagementPluginUploadOperationStatusResponse.FromStatus(status));
    }

    private async Task<IResult> HandleUploadAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var request = httpContext.Request;
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new
            {
                error = "Expected multipart/form-data body with package and signature files.",
            });
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var packageFile = form.Files.GetFile("package");
        var signatureFile = form.Files.GetFile("signature");

        if (packageFile is null || signatureFile is null)
        {
            return Results.BadRequest(new
            {
                error = "Form fields 'package' and 'signature' are required.",
            });
        }

        var packageBytes = await ReadBytesAsync(packageFile, cancellationToken);
        var signatureBytes = await ReadBytesAsync(signatureFile, cancellationToken);
        if (packageBytes.Length == 0 || signatureBytes.Length == 0)
        {
            return Results.BadRequest(new
            {
                error = "Package and signature files must be non-empty.",
            });
        }

        var authorization = _authorizationPipeline.VerifyPluginUploadSignature(
            new PluginUploadSignatureVerificationRequest(packageFile.FileName, packageBytes, signatureBytes));
        if (!authorization.IsAuthorized)
        {
            return Results.Json(
                new
                {
                    status = "Rejected",
                    error = authorization.FailureReason ?? "Plugin upload authorization failed.",
                },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var operation = _operationStore.CreateQueued(packageFile.FileName);
        _uploadPipeline.StartUpload(operation.OperationId, packageFile.FileName, packageBytes, signatureBytes);

        var body = new
        {
            operationId = operation.OperationId,
            status = operation.Stage.ToString(),
        };

        return Results.Accepted($"/management/plugins/uploads/{operation.OperationId}", body);
    }

    private static async Task<byte[]> ReadBytesAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var source = file.OpenReadStream();
        using var output = new MemoryStream();
        await source.CopyToAsync(output, cancellationToken);
        return output.ToArray();
    }
}
