using HarnessSync;

var argsList = args.ToList();
var isCheck = argsList.Remove("--check");
var isGenerate = argsList.Remove("generate") || !isCheck;

if (!isCheck && !isGenerate)
{
    Console.Error.WriteLine("Usage: HarnessSync [generate] | [--check]");
    return 1;
}

var repositoryRoot = RepositoryLocator.FindRepositoryRoot();
var engine = new SyncEngine(repositoryRoot);
var mode = isCheck ? SyncMode.Check : SyncMode.Generate;
var issues = engine.Run(mode);
var pointerIssues = engine.ValidatePointerIntegrity();

foreach (var issue in issues)
{
    Console.Error.WriteLine($"[{issue.Code}] {issue.Message}");
}

foreach (var pointer in pointerIssues)
{
    Console.Error.WriteLine($"[pointer-integrity] Broken reference: {pointer}");
}

var hasFailures = issues.Count > 0 || pointerIssues.Count > 0;
if (isCheck && hasFailures)
{
    return 1;
}

if (isGenerate)
{
    Console.WriteLine($"HarnessSync generate complete for {repositoryRoot}");
}

return 0;