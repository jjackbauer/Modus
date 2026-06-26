using Wip.ShellHost.Hosting;

var options = WipShellHostOptions.FromArgs(args, Directory.GetCurrentDirectory());
var bootstrap = DotNetWorkspaceBootstrap.Validate(options);
foreach (var diagnostic in bootstrap.Diagnostics)
{
	Console.Out.WriteLine($"startup-bootstrap: {diagnostic}");
}

if (!bootstrap.Succeeded)
{
	return bootstrap.ExitCode;
}

foreach (var diagnostic in options.GetStartupDiagnostics())
{
	Console.Out.WriteLine($"startup-config: {diagnostic}");
}

await using var host = WipShellHostFactory.CreateDefault(options, Console.In, Console.Out);
var exitCode = await host.RunAsync(CancellationToken.None);
return exitCode;
