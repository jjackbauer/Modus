using System.Xml.Linq;
using Modus.Core.Plugins;

namespace Modus.Host.Plugins;

public sealed class PluginProjectDescriptorFactory
{
    public PluginDescriptor Create(string csprojPath)
    {
        if (string.IsNullOrWhiteSpace(csprojPath))
        {
            throw new ArgumentException("A project file path is required.", nameof(csprojPath));
        }

        if (!string.Equals(Path.GetExtension(csprojPath), ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Project file must have .csproj extension.");
        }

        if (!File.Exists(csprojPath))
        {
            throw new FileNotFoundException("Project file not found.", csprojPath);
        }

        XDocument document;
        try
        {
            document = XDocument.Load(csprojPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Malformed project metadata.", ex);
        }

        var pluginId = NormalizePluginId(Path.GetFileNameWithoutExtension(csprojPath));
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            throw new InvalidOperationException("Unable to derive plugin identity from project filename.");
        }

        var assemblyName = NormalizePluginId(ReadProperty(document, "AssemblyName") ?? pluginId);
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            throw new InvalidOperationException("Unable to derive assembly identity from project metadata.");
        }

        var version = ParseVersionOrDefault(ReadProperty(document, "ModusVersion"), new Version(1, 0, 0));
        var isContractCompliant = ParseBoolOrDefault(ReadProperty(document, "ModusContractCompliant"), true, "ModusContractCompliant");
        var isValidAssembly = ParseBoolOrDefault(ReadProperty(document, "ModusIsValidAssembly"), true, "ModusIsValidAssembly");
        var usesOnlyStandardLibrary = ParseBoolOrDefault(ReadProperty(document, "ModusUsesOnlyStandardLibrary"), true, "ModusUsesOnlyStandardLibrary");
        var failOnActivation = ParseBoolOrDefault(ReadProperty(document, "ModusFailOnActivation"), false, "ModusFailOnActivation");
        var capabilities = ParseCapabilityList(ReadProperty(document, "ModusCapabilities"));
        var dependsOn = ParseCapabilityList(ReadProperty(document, "ModusDependsOn"));
        var declaredOperations = ParseOperationList(ReadProperty(document, "ModusOperations"));
        var failingOperations = ParseOperationList(ReadProperty(document, "ModusFailingOperations"));

        if (capabilities.Count == 0)
        {
            capabilities = [new CapabilityName($"Cap.{pluginId}")];
        }

        return new PluginDescriptor(
            PluginId: new PluginId(pluginId),
            AssemblyName: assemblyName,
            Version: version,
            Capabilities: capabilities,
            DependsOn: dependsOn,
            IsContractCompliant: isContractCompliant,
            IsValidAssembly: isValidAssembly,
            UsesOnlyStandardLibrary: usesOnlyStandardLibrary,
                FailOnActivation: failOnActivation,
                DeclaredOperations: declaredOperations,
                FailingOperations: failingOperations);
    }

    private static string? ReadProperty(XDocument document, string propertyName)
    {
        return document
            .Descendants()
            .FirstOrDefault(x => string.Equals(x.Name.LocalName, propertyName, StringComparison.OrdinalIgnoreCase))
            ?.Value
            .Trim();
    }

    private static Version ParseVersionOrDefault(string? rawValue, Version defaultVersion)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultVersion;
        }

        if (!Version.TryParse(rawValue, out var version))
        {
            throw new InvalidOperationException("Invalid ModusVersion metadata.");
        }

        return version;
    }

    private static bool ParseBoolOrDefault(string? rawValue, bool defaultValue, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        if (!bool.TryParse(rawValue, out var value))
        {
            throw new InvalidOperationException($"Invalid {propertyName} metadata.");
        }

        return value;
    }

    private static List<CapabilityName> ParseCapabilityList(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return [];
        }

        return rawValue
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .Select(x => new CapabilityName(x))
            .ToList();
    }

    private static List<OperationName> ParseOperationList(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return [];
        }

        return rawValue
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .Select(x => new OperationName(x))
            .ToList();
    }

    private static string NormalizePluginId(string projectName)
    {
        var normalizedChars = projectName
            .Select(ch => char.IsLetterOrDigit(ch) || ch == '.' ? ch : '.')
            .ToArray();

        var collapsed = string.Join(
            ".",
            new string(normalizedChars)
                .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return collapsed;
    }
}