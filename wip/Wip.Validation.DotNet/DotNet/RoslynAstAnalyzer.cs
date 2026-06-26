using System.Globalization;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Wip.Validation.DotNet.DotNet;

public interface IRoslynAstAnalyzer
{
    ValueTask<AstValidationResult> AnalyzeAsync(AstValidationRequest request, CancellationToken cancellationToken);
}

public sealed class RoslynAstAnalyzer : IRoslynAstAnalyzer
{
    public async ValueTask<AstValidationResult> AnalyzeAsync(AstValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidateRequired(request.RepositoryPath, nameof(request.RepositoryPath));
        ValidateRequired(request.ProjectPath, nameof(request.ProjectPath));

        if (request.Timeout is { } timeout && timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(request.Timeout), "Timeout must be greater than zero.");

        var startedAtUtc = DateTimeOffset.UtcNow;
        var diagnostics = new List<AstDiagnostic>();
        var documentCount = 0;
        var syntaxTreeCount = 0;

        var requestedTimeout = request.Timeout;
        using var timeoutCts = requestedTimeout.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;

        if (timeoutCts is not null)
            timeoutCts.CancelAfter(requestedTimeout.GetValueOrDefault());

        var effectiveCancellationToken = timeoutCts?.Token ?? cancellationToken;

        try
        {
            var projectPath = ResolveProjectPath(request.RepositoryPath, request.ProjectPath);
            if (!File.Exists(projectPath))
            {
                return AstValidationResult.Failure(
                    failureReason: $"Project file not found: {NormalizePath(projectPath, request.RepositoryPath)}",
                    diagnostics: [],
                    documentCount: 0,
                    syntaxTreeCount: 0,
                    startedAtUtc: startedAtUtc,
                    completedAtUtc: DateTimeOffset.UtcNow);
            }

            var projectDirectory = Path.GetDirectoryName(projectPath)
                ?? throw new InvalidOperationException($"Unable to determine project directory for '{projectPath}'.");

            var documentPaths = await GetDocumentPathsAsync(projectPath, projectDirectory, effectiveCancellationToken);
            documentCount = documentPaths.Count;

            foreach (var documentPath in documentPaths)
            {
                effectiveCancellationToken.ThrowIfCancellationRequested();

                var sourceText = SourceText.From(
                    await File.ReadAllTextAsync(documentPath, effectiveCancellationToken));

                var syntaxTree = CSharpSyntaxTree.ParseText(
                    sourceText,
                    path: documentPath,
                    cancellationToken: effectiveCancellationToken);

                syntaxTreeCount++;
                diagnostics.AddRange(ProjectDiagnostics(
                    syntaxTree.GetDiagnostics(effectiveCancellationToken),
                    request.RepositoryPath));
            }

            var orderedDiagnostics = OrderDiagnostics(diagnostics);
            var completedAtUtc = DateTimeOffset.UtcNow;

            return orderedDiagnostics.Any(static diagnostic => string.Equals(diagnostic.Severity, "Error", StringComparison.Ordinal))
                ? AstValidationResult.Failure(
                    failureReason: "AST analysis found one or more error diagnostics.",
                    diagnostics: orderedDiagnostics,
                    documentCount: documentCount,
                    syntaxTreeCount: syntaxTreeCount,
                    startedAtUtc: startedAtUtc,
                    completedAtUtc: completedAtUtc)
                : AstValidationResult.Success(
                    documentCount: documentCount,
                    syntaxTreeCount: syntaxTreeCount,
                    diagnostics: orderedDiagnostics,
                    startedAtUtc: startedAtUtc,
                    completedAtUtc: completedAtUtc);
        }
        catch (OperationCanceledException) when (timeoutCts is not null && !cancellationToken.IsCancellationRequested)
        {
            return AstValidationResult.Failure(
                failureReason: "AST analysis timed out.",
                diagnostics: OrderDiagnostics(diagnostics),
                documentCount: documentCount,
                syntaxTreeCount: syntaxTreeCount,
                startedAtUtc: startedAtUtc,
                completedAtUtc: DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
        {
            return AstValidationResult.Failure(
                failureReason: "AST analysis canceled.",
                diagnostics: OrderDiagnostics(diagnostics),
                documentCount: documentCount,
                syntaxTreeCount: syntaxTreeCount,
                startedAtUtc: startedAtUtc,
                completedAtUtc: DateTimeOffset.UtcNow);
        }
        catch (Exception exception)
        {
            return AstValidationResult.Failure(
                failureReason: $"AST analysis failed: {exception.Message}",
                diagnostics: OrderDiagnostics(diagnostics),
                documentCount: documentCount,
                syntaxTreeCount: syntaxTreeCount,
                startedAtUtc: startedAtUtc,
                completedAtUtc: DateTimeOffset.UtcNow);
        }
    }

    private static async Task<IReadOnlyList<string>> GetDocumentPathsAsync(
        string projectPath,
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(projectPath);
        var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);

        var compileIncludes = document
            .Descendants()
            .Where(static element => string.Equals(element.Name.LocalName, "Compile", StringComparison.Ordinal))
            .Select(static element => (Include: (string?)element.Attribute("Include"), Remove: (string?)element.Attribute("Remove")))
            .Where(static element => !string.IsNullOrWhiteSpace(element.Include) && string.IsNullOrWhiteSpace(element.Remove))
            .Select(element => element.Include!)
            .ToArray();

        if (compileIncludes.Length > 0 && compileIncludes.All(static include => !HasWildcard(include)))
        {
            return compileIncludes
                .Select(include => ResolveDocumentPath(projectDirectory, include))
                .Where(static path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return Directory
            .EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !IsIgnoredPath(path))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<AstDiagnostic> ProjectDiagnostics(
        IEnumerable<Diagnostic> diagnostics,
        string repositoryPath)
    {
        foreach (var diagnostic in diagnostics)
        {
            var span = diagnostic.Location.GetLineSpan();
            yield return new AstDiagnostic(
                Id: diagnostic.Id,
                Severity: diagnostic.Severity.ToString(),
                Message: diagnostic.GetMessage(CultureInfo.InvariantCulture),
                FilePath: span.Path.Length == 0 ? null : NormalizePath(span.Path, repositoryPath),
                StartLine: span.StartLinePosition.Line + 1,
                StartColumn: span.StartLinePosition.Character + 1,
                EndLine: span.EndLinePosition.Line + 1,
                EndColumn: span.EndLinePosition.Character + 1);
        }
    }

    private static IReadOnlyList<AstDiagnostic> OrderDiagnostics(IEnumerable<AstDiagnostic> diagnostics)
        => diagnostics
            .OrderBy(static diagnostic => diagnostic.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static diagnostic => diagnostic.StartLine)
            .ThenBy(static diagnostic => diagnostic.StartColumn)
            .ThenBy(static diagnostic => diagnostic.EndLine)
            .ThenBy(static diagnostic => diagnostic.EndColumn)
            .ThenBy(static diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToArray();

    private static string ResolveProjectPath(string repositoryPath, string projectPath)
        => Path.GetFullPath(Path.IsPathRooted(projectPath)
            ? projectPath
            : Path.Combine(repositoryPath, projectPath));

    private static string ResolveDocumentPath(string projectDirectory, string includePath)
        => Path.GetFullPath(Path.IsPathRooted(includePath)
            ? includePath
            : Path.Combine(projectDirectory, includePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string NormalizePath(string path, string repositoryPath)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRepositoryPath = Path.GetFullPath(repositoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (fullPath.StartsWith(fullRepositoryPath, StringComparison.OrdinalIgnoreCase))
        {
            var relativePath = Path.GetRelativePath(fullRepositoryPath, fullPath);
            return relativePath.Replace(Path.DirectorySeparatorChar, '/');
        }

        return fullPath.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static bool IsIgnoredPath(string path)
        => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(static segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));

    private static bool HasWildcard(string includePath)
        => includePath.Contains('*', StringComparison.Ordinal)
            || includePath.Contains('?', StringComparison.Ordinal);

    private static void ValidateRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", parameterName);
    }
}