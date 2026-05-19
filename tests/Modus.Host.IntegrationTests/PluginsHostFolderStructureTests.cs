using Xunit;
using Modus.Host.Plugins.Host;
using Modus.Host.Plugins.Scanning;
using Modus.Host.Plugins.Validation;
using System.Reflection;

namespace Modus.Host.IntegrationTests;

public sealed class PluginsHostFolderStructureTests
{
    [Fact]
    [Trait("ChecklistItem", "Structure.CreatePluginsHostSubfolder")]
    public void CreateHostSubfolder_GivenPluginsFolderExists_ExpectedHostFolderCreated()
    {
        var root = FindRepositoryRoot();
        var hostFolder = Path.Combine(root, "src", "Modus.Host", "Domain", "Plugins", "Host");

        Assert.True(Directory.Exists(hostFolder));
    }

    [Theory]
    [Trait("ChecklistItem", "Structure.CreatePluginsHostSubfolder")]
    [InlineData("HostRunner.cs")]
    [InlineData("HostStartResult.cs")]
    [InlineData("AssemblyLifecycleHost.cs")]
    public void CreateHostSubfolder_GivenHostRunnerAndDependents_ExpectedFilesMovedWithoutError(string fileName)
    {
        var root = FindRepositoryRoot();
        var newPath = Path.Combine(root, "src", "Modus.Host", "Domain", "Plugins", "Host", fileName);
        var oldPath = Path.Combine(root, "src", "Modus.Host", "Domain", "Plugins", fileName);

        Assert.True(File.Exists(newPath));
        Assert.False(File.Exists(oldPath));
    }

    [Fact]
    [Trait("ChecklistItem", "Namespaces.UpdateNamespacesInHostFolder")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-host-namespaces-2026-05-18")]
    public void UpdateNamespacesInHostFolder_GivenHostRunnerMoved_ExpectedNamespaceUpdatedToHostSubfolder()
    {
        Assert.Equal("Modus.Host.Plugins.Host", typeof(HostRunner).Namespace);
    }

    [Fact]
    [Trait("ChecklistItem", "Namespaces.UpdateNamespacesInHostFolder")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-host-namespaces-2026-05-18")]
    public void UpdateNamespacesInHostFolder_GivenAllHostClassesUpdated_ExpectedNoReferenceErrorsInAssembly()
    {
        var assembly = typeof(HostRunner).Assembly;

        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Host.HostRunner"));
        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Host.HostStartResult"));
        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Host.AssemblyLifecycleHost"));

        Assert.Null(assembly.GetType("Modus.Host.Plugins.HostRunner"));
        Assert.Null(assembly.GetType("Modus.Host.Plugins.HostStartResult"));
        Assert.Null(assembly.GetType("Modus.Host.Plugins.AssemblyLifecycleHost"));
    }

    [Fact]
    [Trait("ChecklistItem", "Structure.CreatePluginsScanningSubfolder")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-scanning-structure-2026-05-18")]
    public void CreateScanningSubfolder_GivenPluginsFolderExists_ExpectedScanningFolderCreated()
    {
        var root = FindRepositoryRoot();
        var scanningFolder = Path.Combine(root, "src", "Modus.Host", "Domain", "Plugins", "Scanning");

        Assert.True(Directory.Exists(scanningFolder));
    }

    [Theory]
    [Trait("ChecklistItem", "Structure.CreatePluginsScanningSubfolder")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-scanning-structure-2026-05-18")]
    [InlineData("PluginDiscoveryService.cs")]
    [InlineData("InMemoryPluginDiscoveryService.cs")]
    [InlineData("PluginFolderWatcher.cs")]
    [InlineData("PluginLoader.cs")]
    [InlineData("InMemoryPluginLoader.cs")]
    public void CreateScanningSubfolder_GivenDiscoveryAndLoaderClasses_ExpectedFilesMovedWithoutError(string fileName)
    {
        var root = FindRepositoryRoot();
        var newPath = Path.Combine(root, "src", "Modus.Host", "Domain", "Plugins", "Scanning", fileName);
        var oldPath = Path.Combine(root, "src", "Modus.Host", "Domain", "Plugins", fileName);

        Assert.True(File.Exists(newPath));
        Assert.False(File.Exists(oldPath));
    }

    [Fact]
    [Trait("ChecklistItem", "Namespaces.UpdateNamespacesInScanningFolder")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-scanning-namespaces-2026-05-18")]
    public void UpdateNamespacesInScanningFolder_GivenPluginDiscoveryServiceMoved_ExpectedNamespaceUpdatedToScanningSubfolder()
    {
        Assert.Equal("Modus.Host.Plugins.Scanning", typeof(PluginDiscoveryService).Namespace);
    }

    [Fact]
    [Trait("ChecklistItem", "Namespaces.UpdateNamespacesInScanningFolder")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-scanning-namespaces-2026-05-18")]
    public void UpdateNamespacesInScanningFolder_GivenAllScanningClassesUpdated_ExpectedNoReferenceErrorsInAssembly()
    {
        var assembly = typeof(PluginDiscoveryService).Assembly;

        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Scanning.PluginDiscoveryService"));
        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Scanning.InMemoryPluginDiscoveryService"));
        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Scanning.PluginFolderWatcher"));
        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Scanning.PluginLoader"));
        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Scanning.InMemoryPluginLoader"));

        Assert.Null(assembly.GetType("Modus.Host.Plugins.PluginDiscoveryService"));
        Assert.Null(assembly.GetType("Modus.Host.Plugins.InMemoryPluginDiscoveryService"));
        Assert.Null(assembly.GetType("Modus.Host.Plugins.PluginFolderWatcher"));
        Assert.Null(assembly.GetType("Modus.Host.Plugins.PluginLoader"));
        Assert.Null(assembly.GetType("Modus.Host.Plugins.InMemoryPluginLoader"));
    }

    [Fact]
    [Trait("ChecklistItem", "Structure.CreatePluginsDescriptorsSubfolder")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-descriptors-structure-2026-05-18")]
    public void CreateDescriptorsSubfolder_GivenPluginsFolderExists_ExpectedDescriptorsFolderCreated()
    {
        var root = FindRepositoryRoot();
        var descriptorsFolder = Path.Combine(root, "src", "Modus.Host", "Domain", "Plugins", "Descriptors");

        Assert.True(Directory.Exists(descriptorsFolder));
    }

    [Theory]
    [Trait("ChecklistItem", "Structure.CreatePluginsDescriptorsSubfolder")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-descriptors-structure-2026-05-18")]
    [InlineData("PluginDescriptor.cs")]
    [InlineData("PluginSpec.cs")]
    [InlineData("PluginOnboardingResult.cs")]
    [InlineData("PluginWatcherStartResult.cs")]
    public void CreateDescriptorsSubfolder_GivenDescriptorAndSpecClasses_ExpectedFilesMovedWithoutError(string fileName)
    {
        var root = FindRepositoryRoot();
        var newPath = Path.Combine(root, "src", "Modus.Host", "Domain", "Plugins", "Descriptors", fileName);
        var oldPath = Path.Combine(root, "src", "Modus.Host", "Domain", "Plugins", fileName);

        Assert.True(File.Exists(newPath));
        Assert.False(File.Exists(oldPath));
    }

    [Fact]
    [Trait("ChecklistItem", "Namespaces.UpdateNamespacesInDescriptorsFolder")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-descriptors-namespaces-2026-05-18")]
    public void UpdateNamespacesInDescriptorsFolder_GivenPluginDescriptorMoved_ExpectedNamespaceUpdatedToDescriptorsSubfolder()
    {
        var assembly = typeof(HostRunner).Assembly;
        var descriptorType = assembly.GetType("Modus.Host.Plugins.Descriptors.PluginDescriptor");

        Assert.NotNull(descriptorType);
        Assert.Equal("Modus.Host.Plugins.Descriptors", descriptorType!.Namespace);
        Assert.Null(assembly.GetType("Modus.Host.Plugins.PluginDescriptor"));
    }

    [Fact]
    [Trait("ChecklistItem", "Namespaces.UpdateNamespacesInDescriptorsFolder")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-descriptors-namespaces-2026-05-18")]
    public void UpdateNamespacesInDescriptorsFolder_GivenAllDescriptorClassesUpdated_ExpectedNoReferenceErrorsInAssembly()
    {
        var assembly = typeof(HostRunner).Assembly;

        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Descriptors.PluginDescriptor"));
        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Descriptors.PluginSpec"));
        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Descriptors.PluginOnboardingResult"));
        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Descriptors.PluginWatcherStartResult"));

        Assert.Null(assembly.GetType("Modus.Host.Plugins.PluginDescriptor"));
        Assert.Null(assembly.GetType("Modus.Host.Plugins.PluginSpec"));
        Assert.Null(assembly.GetType("Modus.Host.Plugins.PluginOnboardingResult"));
        Assert.Null(assembly.GetType("Modus.Host.Plugins.PluginWatcherStartResult"));
    }

    [Fact]
    [Trait("ChecklistItem", "Structure.CreatePluginsValidationSubfolder")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-validation-structure-2026-05-18")]
    public void CreateValidationSubfolder_GivenPluginsFolderExists_ExpectedValidationFolderCreated()
    {
        var root = FindRepositoryRoot();
        var validationFolder = Path.Combine(root, "src", "Modus.Host", "Domain", "Plugins", "Validation");

        Assert.True(Directory.Exists(validationFolder));
    }

    [Theory]
    [Trait("ChecklistItem", "Structure.CreatePluginsValidationSubfolder")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-validation-structure-2026-05-18")]
    [InlineData("PluginValidationService.cs")]
    [InlineData("PluginIsolationBoundary.cs")]
    [InlineData("PluginQuarantineStore.cs")]
    public void CreateValidationSubfolder_GivenValidationAndIsolationClasses_ExpectedFilesMovedWithoutError(string fileName)
    {
        var root = FindRepositoryRoot();
        var newPath = Path.Combine(root, "src", "Modus.Host", "Domain", "Plugins", "Validation", fileName);
        var oldPath = Path.Combine(root, "src", "Modus.Host", "Domain", "Plugins", fileName);

        Assert.True(File.Exists(newPath));
        Assert.False(File.Exists(oldPath));
    }

    [Fact]
    [Trait("ChecklistItem", "Namespaces.UpdateNamespacesInValidationFolder")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-validation-namespaces-2026-05-18")]
    public void UpdateNamespacesInValidationFolder_GivenPluginValidationServiceMoved_ExpectedNamespaceUpdatedToValidationSubfolder()
    {
        Assert.Equal("Modus.Host.Plugins.Validation", typeof(PluginValidationService).Namespace);
    }

    [Fact]
    [Trait("ChecklistItem", "Namespaces.UpdateNamespacesInValidationFolder")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-validation-namespaces-2026-05-18")]
    public void UpdateNamespacesInValidationFolder_GivenAllValidationClassesUpdated_ExpectedNoReferenceErrorsInAssembly()
    {
        var assembly = typeof(HostRunner).Assembly;

        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Validation.PluginValidationService"));
        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Validation.PluginIsolationBoundary"));
        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Validation.PluginQuarantineStore"));

        Assert.Null(assembly.GetType("Modus.Host.Plugins.PluginValidationService"));
        Assert.Null(assembly.GetType("Modus.Host.Plugins.PluginIsolationBoundary"));
        Assert.Null(assembly.GetType("Modus.Host.Plugins.PluginQuarantineStore"));
    }

    [Fact]
    [Trait("ChecklistItem", "Structure.CreatePluginsLifecycleSubfolder")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-lifecycle-structure-2026-05-18")]
    public void CreateLifecycleSubfolder_GivenPluginsFolderExists_ExpectedLifecycleFolderCreated()
    {
        var root = FindRepositoryRoot();
        var lifecycleFolder = Path.Combine(root, "src", "Modus.Host", "Domain", "Plugins", "Lifecycle");

        Assert.True(Directory.Exists(lifecycleFolder));
    }

    [Theory]
    [Trait("ChecklistItem", "Structure.CreatePluginsLifecycleSubfolder")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-lifecycle-structure-2026-05-18")]
    [InlineData("PluginLifecycleOrchestrator.cs")]
    [InlineData("PluginUnloadCoordinator.cs")]
    [InlineData("PluginRollbackCoordinator.cs")]
    [InlineData("InMemoryLifecycleEngine.cs")]
    [InlineData("InMemoryHostRuntime.cs")]
    [InlineData("PluginRetryPolicy.cs")]
    [InlineData("RegistrationTransactionLog.cs")]
    public void CreateLifecycleSubfolder_GivenOrchestrationAndRuntimeClasses_ExpectedFilesMovedWithoutError(string fileName)
    {
        var root = FindRepositoryRoot();
        var newPath = Path.Combine(root, "src", "Modus.Host", "Domain", "Plugins", "Lifecycle", fileName);
        var oldPath = Path.Combine(root, "src", "Modus.Host", "Domain", "Plugins", fileName);

        Assert.True(File.Exists(newPath));
        Assert.False(File.Exists(oldPath));
    }

    [Fact]
    [Trait("ChecklistItem", "Namespaces.UpdateNamespacesInLifecycleFolder")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-lifecycle-namespaces-2026-05-18")]
    public void UpdateNamespacesInLifecycleFolder_GivenPluginLifecycleOrchestratorMoved_ExpectedNamespaceUpdatedToLifecycleSubfolder()
    {
        var assembly = typeof(HostRunner).Assembly;
        var orchestratorType = assembly.GetType("Modus.Host.Plugins.Lifecycle.PluginLifecycleOrchestrator");

        Assert.NotNull(orchestratorType);
        Assert.Equal("Modus.Host.Plugins.Lifecycle", orchestratorType!.Namespace);
        Assert.Null(assembly.GetType("Modus.Host.Plugins.PluginLifecycleOrchestrator"));
    }

    [Fact]
    [Trait("ChecklistItem", "Namespaces.UpdateNamespacesInLifecycleFolder")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-lifecycle-namespaces-2026-05-18")]
    public void UpdateNamespacesInLifecycleFolder_GivenAllLifecycleClassesUpdated_ExpectedNoReferenceErrorsInAssembly()
    {
        var assembly = typeof(HostRunner).Assembly;

        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Lifecycle.PluginLifecycleOrchestrator"));
        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Lifecycle.PluginUnloadCoordinator"));
        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Lifecycle.PluginRollbackCoordinator"));
        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Lifecycle.InMemoryLifecycleEngine"));
        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Lifecycle.InMemoryHostRuntime"));
        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Lifecycle.PluginRetryPolicy"));
        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Lifecycle.RegistrationTransactionLog"));

        Assert.Null(assembly.GetType("Modus.Host.Plugins.PluginLifecycleOrchestrator"));
        Assert.Null(assembly.GetType("Modus.Host.Plugins.PluginUnloadCoordinator"));
        Assert.Null(assembly.GetType("Modus.Host.Plugins.PluginRollbackCoordinator"));
        Assert.Null(assembly.GetType("Modus.Host.Plugins.InMemoryLifecycleEngine"));
        Assert.Null(assembly.GetType("Modus.Host.Plugins.InMemoryHostRuntime"));
        Assert.Null(assembly.GetType("Modus.Host.Plugins.PluginRetryPolicy"));
        Assert.Null(assembly.GetType("Modus.Host.Plugins.RegistrationTransactionLog"));
    }

    [Fact]
    [Trait("ChecklistItem", "Verification.VerifyInMemoryHostRuntimeComposition")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-inmemoryhostruntime-verification-2026-05-18")]
    public void VerifyInMemoryHostRuntimeComposition_GivenLifecycleFolderStructureCreated_ExpectedRuntimeComposesCorrectly()
    {
        var runtime = new InMemoryHostRuntime();
        var runtimeType = typeof(InMemoryHostRuntime);

        var discoveryField = runtimeType.GetField("_discovery", BindingFlags.Instance | BindingFlags.NonPublic);
        var validationField = runtimeType.GetField("_validation", BindingFlags.Instance | BindingFlags.NonPublic);
        var isolationField = runtimeType.GetField("_isolationBoundary", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(discoveryField);
        Assert.NotNull(validationField);
        Assert.NotNull(isolationField);

        var discoveryInstance = discoveryField!.GetValue(runtime);
        var validationInstance = validationField!.GetValue(runtime);
        var isolationInstance = isolationField!.GetValue(runtime);

        Assert.NotNull(discoveryInstance);
        Assert.NotNull(validationInstance);
        Assert.NotNull(isolationInstance);

        Assert.Equal(typeof(PluginDiscoveryService), discoveryInstance!.GetType());
        Assert.Equal(typeof(PluginValidationService), validationInstance!.GetType());
        Assert.Equal("Modus.Host.Plugins.Validation", isolationInstance!.GetType().Namespace);
        Assert.Equal("Modus.Host.Plugins.Lifecycle", runtimeType.Namespace);
    }

    [Fact]
    [Trait("ChecklistItem", "Verification.VerifyInMemoryHostRuntimeComposition")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-inmemoryhostruntime-verification-2026-05-18")]
    public void VerifyInMemoryHostRuntimeComposition_GivenAllDependenciesInDifferentFolders_ExpectedServiceLocatorResolvesAllDependencies()
    {
        var root = FindRepositoryRoot();
        var runtimePath = Path.Combine(root, "src", "Modus.Host", "Domain", "Plugins", "Lifecycle", "InMemoryHostRuntime.cs");
        var source = File.ReadAllText(runtimePath);
        var assembly = typeof(InMemoryHostRuntime).Assembly;

        Assert.Contains("using Modus.Host.Plugins.Scanning;", source, StringComparison.Ordinal);
        Assert.Contains("using Modus.Host.Plugins.Validation;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("using Modus.Host.Plugins;", source, StringComparison.Ordinal);

        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Scanning.PluginDiscoveryService"));
        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Validation.PluginValidationService"));
        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Validation.PluginIsolationBoundary"));

        Assert.Null(assembly.GetType("Modus.Host.Plugins.PluginDiscoveryService"));
        Assert.Null(assembly.GetType("Modus.Host.Plugins.PluginValidationService"));
        Assert.Null(assembly.GetType("Modus.Host.Plugins.PluginIsolationBoundary"));
    }

    [Fact]
    [Trait("ChecklistItem", "Structure.CreatePluginsResultsSubfolder")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-results-structure-2026-05-18")]
    public void CreateResultsSubfolder_GivenPluginsFolderExists_ExpectedResultsFolderCreated()
    {
        var root = FindRepositoryRoot();
        var resultsFolder = Path.Combine(root, "src", "Modus.Host", "Domain", "Plugins", "Results");

        Assert.True(Directory.Exists(resultsFolder));
    }

    [Theory]
    [Trait("ChecklistItem", "Structure.CreatePluginsResultsSubfolder")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-results-structure-2026-05-18")]
    [InlineData("EventDispatchResult.cs")]
    [InlineData("LifecycleResult.cs")]
    public void CreateResultsSubfolder_GivenResultAndEventClasses_ExpectedFilesMovedWithoutError(string fileName)
    {
        var root = FindRepositoryRoot();
        var newPath = Path.Combine(root, "src", "Modus.Host", "Domain", "Plugins", "Results", fileName);
        var oldPath = Path.Combine(root, "src", "Modus.Host", "Domain", "Plugins", fileName);

        Assert.True(File.Exists(newPath));
        Assert.False(File.Exists(oldPath));
    }

    [Fact]
    [Trait("ChecklistItem", "Namespaces.UpdateNamespacesInResultsFolder")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-results-namespaces-2026-05-18")]
    public void UpdateNamespacesInResultsFolder_GivenEventDispatchResultMoved_ExpectedNamespaceUpdatedToResultsSubfolder()
    {
        var assembly = typeof(HostRunner).Assembly;
        var eventDispatchResultType = assembly.GetType("Modus.Host.Plugins.Results.EventDispatchResult");

        Assert.NotNull(eventDispatchResultType);
        Assert.Equal("Modus.Host.Plugins.Results", eventDispatchResultType!.Namespace);
        Assert.Null(assembly.GetType("Modus.Host.Plugins.EventDispatchResult"));
    }

    [Fact]
    [Trait("ChecklistItem", "Namespaces.UpdateNamespacesInResultsFolder")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-results-namespaces-2026-05-18")]
    public void UpdateNamespacesInResultsFolder_GivenAllResultClassesUpdated_ExpectedNoReferenceErrorsInAssembly()
    {
        var assembly = typeof(HostRunner).Assembly;

        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Results.EventDispatchResult"));
        Assert.NotNull(assembly.GetType("Modus.Host.Plugins.Results.LifecycleResult"));

        Assert.Null(assembly.GetType("Modus.Host.Plugins.EventDispatchResult"));
        Assert.Null(assembly.GetType("Modus.Host.Plugins.LifecycleResult"));
    }

    [Fact]
    [Trait("ChecklistItem", "Imports.UpdateImportsInParentFolder")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-parent-imports-2026-05-18")]
    public void UpdateImportsInParentFolder_GivenHostRunnerImportedDirectly_ExpectedImportUpdatedToSubfolderNamespace()
    {
        var root = FindRepositoryRoot();
        var hostRunnerPath = Path.Combine(root, "src", "Modus.Host", "Domain", "Plugins", "Host", "HostRunner.cs");
        var source = File.ReadAllText(hostRunnerPath);

        Assert.Contains("using Modus.Host.Plugins.Scanning;", source, StringComparison.Ordinal);
        Assert.Contains("using Modus.Host.Plugins.Descriptors;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("using Modus.Host.Plugins;", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "Imports.UpdateImportsInParentFolder")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-parent-imports-2026-05-18")]
    public void UpdateImportsInParentFolder_GivenMultipleFoldersCreated_ExpectedAllImportsResolved()
    {
        var root = FindRepositoryRoot();
        var pluginsFolder = Path.Combine(root, "src", "Modus.Host", "Domain", "Plugins");
        var concernFolders = new[] { "Host", "Scanning", "Descriptors", "Validation", "Lifecycle", "Results" };

        var sourceFiles = concernFolders
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(pluginsFolder, folder), "*.cs", SearchOption.AllDirectories))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var offenders = sourceFiles
            .Where(file => File
                .ReadLines(file)
                .Any(line => string.Equals(line.Trim(), "using Modus.Host.Plugins;", StringComparison.Ordinal)))
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Found legacy parent namespace imports in subfolder files:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    [Trait("ChecklistItem", "Imports.UpdateImportsInProjectFile")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-project-imports-2026-05-18")]
    public void UpdateImportsInProjectFile_GivenSubfolderStructureCreated_ExpectedProjectFileStillCompiles()
    {
        var root = FindRepositoryRoot();
        var csprojPath = Path.Combine(root, "src", "Modus.Host", "Modus.Host.csproj");
        var source = File.ReadAllText(csprojPath);

        Assert.DoesNotContain("<Compile Include=\"Domain\\Plugins\\*.cs\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Compile Remove=\"Domain\\Plugins\\", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Compile Include=\"Domain/Plugins/*.cs\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Compile Remove=\"Domain/Plugins/", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "Imports.UpdateImportsInProjectFile")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-project-imports-2026-05-18")]
    public void UpdateImportsInProjectFile_GivenFolderScopedUsingsApplied_ExpectedAllNamespacesResolved()
    {
        var root = FindRepositoryRoot();
        var csprojPath = Path.Combine(root, "src", "Modus.Host", "Modus.Host.csproj");
        var source = File.ReadAllText(csprojPath);

        Assert.DoesNotContain("<Using Include=\"Modus.Host.Plugins\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Using Remove=\"Modus.Host.Plugins\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("old path", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("ChecklistItem", "Imports.UpdateImportsInIntegrationTests")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-integrationtests-imports-2026-05-18")]
    public void UpdateImportsInIntegrationTests_GivenHostRunnerUsedInTests_ExpectedTestImportsUpdatedToSubfolderNamespace()
    {
        var root = FindRepositoryRoot();
        var globalUsingsPath = Path.Combine(root, "tests", "Modus.Host.IntegrationTests", "GlobalUsings.cs");
        var source = File.ReadAllText(globalUsingsPath);

        Assert.Contains("global using Modus.Host.Plugins.Host;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("global using Modus.Host.Plugins;", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "Imports.UpdateImportsInIntegrationTests")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-integrationtests-imports-2026-05-18")]
    public void UpdateImportsInIntegrationTests_GivenMultipleFoldersReferenced_ExpectedAllTestImportsResolved()
    {
        var root = FindRepositoryRoot();
        var testsFolder = Path.Combine(root, "tests", "Modus.Host.IntegrationTests");
        var allowedLegacyRootImports = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "tests/Modus.Host.IntegrationTests/ConcretePluginArtifactsTests.cs",
            "tests/Modus.Host.IntegrationTests/DescriptorConstructionTests.cs",
            "tests/Modus.Host.IntegrationTests/StandardPluginAuthoringWorkflowTests.cs",
        };

        var offenders = Directory
            .EnumerateFiles(testsFolder, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(file => File
                .ReadLines(file)
                .Any(line => string.Equals(line.Trim(), "using Modus.Host.Plugins;", StringComparison.Ordinal)))
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(path => !allowedLegacyRootImports.Contains(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Found stale integration-test imports for moved plugin namespaces:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var solutionPath = Path.Combine(directory.FullName, "Modus.slnx");
            if (File.Exists(solutionPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing Modus.slnx.");
    }
}
