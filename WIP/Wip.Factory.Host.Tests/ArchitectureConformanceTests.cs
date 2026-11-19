using Wip.Factory.Host.Factory;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// Adversarial proof for the deterministic architectural conformance check. Every classification below is derived
/// from structural MSBuild facts (SDK identity, OutputType, test-provider references, framework references and
/// entry-point evidence), never from a project filename or free text.
/// </summary>
public sealed class ArchitectureConformanceTests
{
    [Fact]
    public void Architecture_GivenSecondWebApplication_Refuses()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("Second/Web.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        workspace.WriteFile("Second/Program.cs", """
            var builder = WebApplication.CreateBuilder(args);
            builder.Build().Run();
            """);

        var result = ArchitectureInspection.Inspect(workspace.Path);

        Assert.False(result.Passed);
        Assert.Contains(result.Checks, static check => check.Name == "aspnetcore-web-sdk" && !check.Passed);
    }

    [Fact]
    public void Architecture_GivenExecutableDisguisedByTestsFilename_Refuses()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("tools/Backdoor.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType>
              </PropertyGroup>
            </Project>
            """);

        var result = ArchitectureInspection.Inspect(workspace.Path);

        Assert.False(result.Passed);
        Assert.Contains(result.Checks, static check => check.Name == "no-unsupported-application-project" && !check.Passed);
    }

    [Fact]
    public void Architecture_GivenExecutableTestDeclaration_Refuses()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteDirectory("tests");
        workspace.WriteFile("tests/Disguised.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType>
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="xunit" Version="2.9.3" />
              </ItemGroup>
            </Project>
            """);
        workspace.WriteFile("tests/Program.cs", """
            System.Console.WriteLine("this is an application, not a test");
            """);

        var result = ArchitectureInspection.Inspect(workspace.Path);

        Assert.False(result.Passed);
        var layout = Assert.Single(result.Checks, static check => check.Name == "project-layout-supported");
        Assert.False(layout.Passed);
        Assert.Contains("disguised-executable", layout.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Architecture_GivenLegitimateTestProject_Allows()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("tests/UnitTests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
                <PackageReference Include="xunit" Version="2.9.3" />
              </ItemGroup>
            </Project>
            """);

        var result = ArchitectureInspection.Inspect(workspace.Path);

        Assert.True(result.Passed, Detail(result));
    }

    [Fact]
    public void Architecture_GivenExecutableTestProviderWithoutEntryPoint_Allows()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("tests/XunitV3Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType>
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="xunit.v3" Version="2.0.3" />
              </ItemGroup>
            </Project>
            """);

        var result = ArchitectureInspection.Inspect(workspace.Path);

        Assert.True(result.Passed, Detail(result));
    }

    [Fact]
    public void Architecture_GivenAspNetCoreLibrary_Allows()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("library/Shared.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Library</OutputType>
              </PropertyGroup>
              <ItemGroup>
                <FrameworkReference Include="Microsoft.AspNetCore.App" />
              </ItemGroup>
            </Project>
            """);

        var result = ArchitectureInspection.Inspect(workspace.Path);

        Assert.True(result.Passed, Detail(result));
    }

    [Fact]
    public void Architecture_GivenTestProviderProjectUsingWebSdk_Refuses()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("tests/WebTests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
              </ItemGroup>
            </Project>
            """);

        var result = ArchitectureInspection.Inspect(workspace.Path);

        Assert.False(result.Passed);
        Assert.Contains(result.Checks, static check => check.Name == "project-layout-supported" && !check.Passed);
    }

    [Fact]
    public void Architecture_GivenTestsFilenameOnPlainLibrary_Allows()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("shared/Math.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var result = ArchitectureInspection.Inspect(workspace.Path);

        Assert.True(result.Passed, Detail(result));
    }

    [Fact]
    public void Architecture_GivenUnknownSdk_Refuses()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("custom/Custom.csproj", """
            <Project Sdk="Contoso.Custom.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var result = ArchitectureInspection.Inspect(workspace.Path);

        Assert.False(result.Passed);
        Assert.Contains(result.Checks, static check => check.Name == "project-layout-supported" && !check.Passed);
    }

    [Fact]
    public void Architecture_GivenNonSdkProject_Refuses()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("legacy/Legacy.csproj", """
            <Project ToolsVersion="15.0">
              <PropertyGroup>
                <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
              </PropertyGroup>
            </Project>
            """);

        var result = ArchitectureInspection.Inspect(workspace.Path);

        Assert.False(result.Passed);
        Assert.Contains(result.Checks, static check => check.Name == "project-layout-supported" && !check.Passed);
    }

    [Fact]
    public void Architecture_GivenMalformedProject_Refuses()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("broken/Broken.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>");

        var result = ArchitectureInspection.Inspect(workspace.Path);

        Assert.False(result.Passed);
        Assert.Contains(result.Checks, static check => check.Name == "project-layout-supported" && !check.Passed);
    }

    [Fact]
    public void Architecture_GivenConditionalOutputType_Refuses()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("conditional/Conditional.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup Condition="'$(Configuration)' == 'Release'">
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Library</OutputType>
              </PropertyGroup>
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var result = ArchitectureInspection.Inspect(workspace.Path);

        Assert.False(result.Passed);
        Assert.Contains(result.Checks, static check => check.Name == "project-layout-supported" && !check.Passed);
    }

    [Fact]
    public void Architecture_GivenMultipleProgramFiles_Refuses()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("second-entry/Program.cs", "System.Console.WriteLine(\"unexpected\");");

        var result = ArchitectureInspection.Inspect(workspace.Path);

        Assert.False(result.Passed);
        Assert.Contains(result.Checks, static check => check.Name == "entry-point" && !check.Passed);
    }

    [Fact]
    public void Architecture_GivenWorkerExecutable_Refuses()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("worker/Worker.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Worker">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var result = ArchitectureInspection.Inspect(workspace.Path);

        Assert.False(result.Passed);
        Assert.Contains(result.Checks, static check => check.Name == "no-unsupported-application-project" && !check.Passed);
    }

    [Fact]
    public void Architecture_GivenMissingTargetFramework_Refuses()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("noframework/NoFramework.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
              </PropertyGroup>
            </Project>
            """);

        var result = ArchitectureInspection.Inspect(workspace.Path);

        Assert.False(result.Passed);
        Assert.Contains(result.Checks, static check => check.Name == "framework-net10" && !check.Passed);
    }

    [Fact]
    public void Architecture_GivenUnsupportedTargetFramework_Refuses()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("legacyframework/LegacyFramework.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var result = ArchitectureInspection.Inspect(workspace.Path);

        Assert.False(result.Passed);
        Assert.Contains(result.Checks, static check => check.Name == "framework-net10" && !check.Passed);
    }

    [Fact]
    public void Architecture_GivenConditionalTestPackage_Refuses()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("tests/ConditionalTests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup Condition="'$(Configuration)' == 'Debug'">
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
              </ItemGroup>
            </Project>
            """);

        var result = ArchitectureInspection.Inspect(workspace.Path);

        Assert.False(result.Passed);
        Assert.Contains(result.Checks, static check => check.Name == "project-layout-supported" && !check.Passed);
    }

    [Fact]
    public void Architecture_GivenCentralDirectoryBuildPropsTargetFramework_Allows()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        workspace.WriteFile("Directory.Build.props", """
            <Project>
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var result = ArchitectureInspection.Inspect(workspace.Path);

        Assert.True(result.Passed, Detail(result));
    }

    [Fact]
    public void Architecture_GivenDirectoryBuildPropsOutputTypeOverride_Refuses()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("Directory.Build.props", """
            <Project>
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType>
              </PropertyGroup>
            </Project>
            """);

        var result = ArchitectureInspection.Inspect(workspace.Path);

        Assert.False(result.Passed);
        Assert.Contains(result.Checks, static check => check.Name == "project-layout-supported" && !check.Passed);
    }

    [Fact]
    public void Architecture_GivenExplicitMsbuildImport_Refuses()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <Import Project="common.props" />
            </Project>
            """);

        var result = ArchitectureInspection.Inspect(workspace.Path);

        Assert.False(result.Passed);
        Assert.Contains(result.Checks, static check => check.Name == "project-layout-supported" && !check.Passed);
    }

    [Fact]
    public void Architecture_GivenWebSdkWithoutEntryPoint_Refuses()
    {
        using var workspace = new TempWorkspace();
        File.Delete(Path.Combine(workspace.Path, "Program.cs"));

        var result = ArchitectureInspection.Inspect(workspace.Path);

        Assert.False(result.Passed);
        Assert.Contains(result.Checks, static check => check.Name == "entry-point" && !check.Passed);
    }

    [Fact]
    public void Architecture_GivenWebApplicationLibraryAndTest_Allows()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("lib/Domain.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        workspace.WriteFile("tests/App.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
                <PackageReference Include="xunit" Version="2.9.3" />
                <ProjectReference Include="../lib/Domain.csproj" />
              </ItemGroup>
            </Project>
            """);

        var result = ArchitectureInspection.Inspect(workspace.Path);

        Assert.True(result.Passed, Detail(result));
    }

    private static string Detail(ArchitectureInspectionResult result)
        => string.Join(";", result.Checks.Select(static check => check.Name + "=" + check.Passed + ":" + check.Detail));

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "factory-arch-conformance-" + Guid.NewGuid().ToString("N"));
            FactoryTestWorkspace.Write(Path);
        }

        public string Path { get; }

        public void WriteDirectory(string relativePath)
            => Directory.CreateDirectory(System.IO.Path.Combine(Path, relativePath));

        public void WriteFile(string relativePath, string content)
        {
            var target = System.IO.Path.Combine(Path, relativePath);
            var directory = System.IO.Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(target, content);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
