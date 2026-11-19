namespace Wip.Factory.Host.Tests;

/// <summary>
/// A minimal candidate workspace that satisfies the deterministic architectural conformance policy: ASP.NET Core
/// on .NET 10, the web SDK, a Program.cs entry point, and one project inside the worktree. Tests that exercise the
/// review gate use this so a missing check, not a malformed fixture, is what makes them fail.
/// </summary>
internal static class FactoryTestWorkspace
{
    public const string ProjectFile = """
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <OutputType>Exe</OutputType>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
          </PropertyGroup>
        </Project>
        """;

    public const string ProgramFile = """
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.Run();
        """;

    public static void Write(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "App.csproj"), ProjectFile);
        File.WriteAllText(Path.Combine(directory, "Program.cs"), ProgramFile);
    }
}
