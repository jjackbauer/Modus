using Wip.ShellHost.Hosting;

var options = WipShellHostOptions.FromArgs(args, Directory.GetCurrentDirectory());
await using var host = WipShellHostFactory.CreateDefault(options, Console.In, Console.Out);
var exitCode = await host.RunAsync(CancellationToken.None);
return exitCode;
