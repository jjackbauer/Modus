using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class PluginUploadPackagingScriptTests
{
    [Fact]
    [Trait("ChecklistItem", "Add helper script to package plugins for signed upload payload creation [mandatory - operator gate script]")]
    public async Task PackPluginForUploadScript_GivenPluginArtifacts_ProducesSignedUploadPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-pack-script-success-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var pluginAssemblyPath = Path.Combine(root, "Plugin.Sample.dll");
            var pluginAssemblyBytes = "sample-plugin-binary"u8.ToArray();
            await File.WriteAllBytesAsync(pluginAssemblyPath, pluginAssemblyBytes);

            using var key = RSA.Create(2048);
            var privateKeyPath = Path.Combine(root, "plugin-author.private.json");
            await File.WriteAllTextAsync(privateKeyPath, SerializeRsaPrivateKeyAsJson(key.ExportParameters(true)));

            var outputDirectory = Path.Combine(root, "out");
            Directory.CreateDirectory(outputDirectory);
            var packagePath = Path.Combine(outputDirectory, "plugin.bundle.zip");
            var signaturePath = Path.Combine(outputDirectory, "plugin.bundle.sig");

            var scriptPath = Path.Combine(FindRepositoryRoot(), "scripts", "pack-plugin-upload.ps1");

            var execution = await RunPowerShellScriptAsync(
                scriptPath,
                [
                    "-PluginAssemblyPath", pluginAssemblyPath,
                    "-PrivateKeyPath", privateKeyPath,
                    "-OutputDirectory", outputDirectory,
                    "-PackageName", "plugin.bundle.zip",
                    "-SignatureName", "plugin.bundle.sig"
                ],
                root);

            Assert.True(
                execution.ExitCode == 0,
                $"Expected script to succeed but got exit code {execution.ExitCode}. StdOut: {execution.StdOut} StdErr: {execution.StdErr}");
            Assert.True(File.Exists(packagePath), $"Expected upload package at '{packagePath}'. StdErr: {execution.StdErr}");
            Assert.True(File.Exists(signaturePath), $"Expected signature file at '{signaturePath}'. StdErr: {execution.StdErr}");

            await using (var zipStream = File.OpenRead(packagePath))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
            {
                var entry = archive.GetEntry("plugins/Plugin.Sample.dll");
                Assert.NotNull(entry);
                await using var entryStream = entry!.Open();
                using var memory = new MemoryStream();
                await entryStream.CopyToAsync(memory);
                Assert.Equal(pluginAssemblyBytes, memory.ToArray());
            }

            var packageBytes = await File.ReadAllBytesAsync(packagePath);
            var signatureBytes = await File.ReadAllBytesAsync(signaturePath);
            var verified = key.VerifyData(packageBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            Assert.True(verified, "Expected signature to verify against package bytes using the generated public key.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "Add helper script to package plugins for signed upload payload creation [mandatory - operator gate script]")]
    public async Task PackPluginForUploadScript_GivenMissingArtifacts_ExitsNonZeroWithActionableError()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-pack-script-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            using var key = RSA.Create(2048);
            var privateKeyPath = Path.Combine(root, "plugin-author.private.json");
            await File.WriteAllTextAsync(privateKeyPath, SerializeRsaPrivateKeyAsJson(key.ExportParameters(true)));

            var outputDirectory = Path.Combine(root, "out");
            Directory.CreateDirectory(outputDirectory);
            var missingAssemblyPath = Path.Combine(root, "Plugin.Missing.dll");

            var scriptPath = Path.Combine(FindRepositoryRoot(), "scripts", "pack-plugin-upload.ps1");

            var execution = await RunPowerShellScriptAsync(
                scriptPath,
                [
                    "-PluginAssemblyPath", missingAssemblyPath,
                    "-PrivateKeyPath", privateKeyPath,
                    "-OutputDirectory", outputDirectory
                ],
                root);

            Assert.NotEqual(0, execution.ExitCode);
            Assert.Contains("Plugin assembly was not found", execution.StdErr + execution.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunPowerShellScriptAsync(
        string scriptPath,
        IReadOnlyList<string> scriptArguments,
        string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "powershell" : "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        foreach (var argument in scriptArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var stdOut = await process.StandardOutput.ReadToEndAsync();
        var stdErr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, stdOut, stdErr);
    }

    private static string FindRepositoryRoot()
    {
        var current = Directory.GetCurrentDirectory();

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current, "Modus.slnx")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private static string SerializeRsaPrivateKeyAsJson(RSAParameters parameters)
    {
        return "{" +
               $"\"D\":\"{Convert.ToBase64String(parameters.D!)}\"," +
               $"\"DP\":\"{Convert.ToBase64String(parameters.DP!)}\"," +
               $"\"DQ\":\"{Convert.ToBase64String(parameters.DQ!)}\"," +
               $"\"Exponent\":\"{Convert.ToBase64String(parameters.Exponent!)}\"," +
               $"\"InverseQ\":\"{Convert.ToBase64String(parameters.InverseQ!)}\"," +
               $"\"Modulus\":\"{Convert.ToBase64String(parameters.Modulus!)}\"," +
               $"\"P\":\"{Convert.ToBase64String(parameters.P!)}\"," +
               $"\"Q\":\"{Convert.ToBase64String(parameters.Q!)}\"" +
               "}";
    }
}