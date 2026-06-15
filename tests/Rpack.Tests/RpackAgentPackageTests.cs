using System.IO.Compression;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rpack.AgentPackages;
using Rpack.Core;
using Rpack.Core.Issues;

namespace Rpack.Tests;

public class RpackAgentPackageTests
{
    [Fact]
    public void PackRoot_CreatesValidRpack()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("package-root");
        Directory.CreateDirectory(Path.Combine(root, "payload", "text"));

        var payloadPath = Path.Combine(root, "payload", "text", "abc.txt");
        File.WriteAllText(payloadPath, "hello", new UTF8Encoding(false));

        var payloadHash = Sha256.ForBytes(File.ReadAllBytes(payloadPath));
        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "agent-1",
              "Title": "Agent package",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations", "payload-store", "partial-apply", "journal"]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [
                {
                  "Id": "op-1",
                  "Kind": "AddTextFile",
                  "Path": "src/File.txt",
                  "TargetSha256": "__HASH__",
                  "PayloadPath": "payload/text/abc.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                }
              ],
              "Groups": []
            }
            """.Replace("__HASH__", payloadHash), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "README.md"), "readme", new UTF8Encoding(false));

        var packer = new AgentPackageRootPacker();
        var output = Path.Combine(workspace.Path, "agent.rpack");
        var result = packer.Pack(root, output);

        Assert.True(result.Success, result.Message);
        using var archive = ZipFile.OpenRead(output);
        Assert.Contains(archive.Entries, entry => entry.FullName == "manifest.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == "operations.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == "payload/text/abc.txt");
        Assert.Contains(archive.Entries, entry => entry.FullName == "checksums.json");
    }

    [Fact]
    public void PackRootDetailed_ReturnsArtifact()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("package-root");
        Directory.CreateDirectory(Path.Combine(root, "payload", "text"));

        var payloadPath = Path.Combine(root, "payload", "text", "abc.txt");
        File.WriteAllText(payloadPath, "hello", new UTF8Encoding(false));

        var payloadHash = Sha256.ForBytes(File.ReadAllBytes(payloadPath));
        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "agent-1",
              "Title": "Agent package",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations", "payload-store", "partial-apply", "journal"]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [
                {
                  "Id": "op-1",
                  "Kind": "AddTextFile",
                  "Path": "src/File.txt",
                  "TargetSha256": "__HASH__",
                  "PayloadPath": "payload/text/abc.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                }
              ],
              "Groups": []
            }
            """.Replace("__HASH__", payloadHash), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "README.md"), "readme", new UTF8Encoding(false));

        var packer = new AgentPackageRootPacker();
        var output = Path.Combine(workspace.Path, "agent.rpack");
        var result = packer.PackDetailed(root, output);

        Assert.True(result.Success, result.Summary);
        Assert.Single(result.Artifacts);
        Assert.Equal("package", result.Artifacts[0].Kind);
        Assert.Equal(Path.GetFullPath(output), result.Artifacts[0].Path);
    }

    [Fact]
    public void AgentPackageRootValidator_RejectsMissingPayload()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("package-root");
        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "agent-1",
              "Title": "Agent package",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations"]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [
                {
                  "Id": "op-1",
                  "Kind": "AddTextFile",
                  "Path": "src/File.txt",
                  "TargetSha256": "abc",
                  "PayloadPath": "payload/text/missing.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                }
              ],
              "Groups": []
            }
            """, new UTF8Encoding(false));

        var result = new AgentPackageRootValidator().Validate(root);

        Assert.False(result.Success);
        Assert.Contains("Missing payload file", result.Message);
    }

    [Fact]
    public void AgentPackageRootValidator_DetailedValidation_ReturnsStructuredIssue()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("package-root");
        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "agent-1",
              "Title": "Agent package",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations"]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [
                {
                  "Id": "op-1",
                  "Kind": "AddTextFile",
                  "Path": "src/File.txt",
                  "TargetSha256": "abc",
                  "PayloadPath": "payload/text/missing.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                }
              ],
              "Groups": []
            }
            """, new UTF8Encoding(false));

        var result = new AgentPackageRootValidator().ValidateDetailed(root);

        Assert.False(result.Success);
        Assert.Single(result.Issues);
        Assert.Equal("payload.missing", result.Issues[0].Code);
        Assert.Equal(RpackStage.PayloadValidation, result.Issues[0].Stage);
        Assert.Contains("Missing payload file", result.Summary);
    }

    [Fact]
    public void AgentPackageRootValidator_RejectsPayloadHashMismatch_PlannedCode()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("package-root");
        Directory.CreateDirectory(Path.Combine(root, "payload", "text"));

        File.WriteAllText(Path.Combine(root, "payload", "text", "abc.txt"), "hello", new UTF8Encoding(false));
        var payloadHash = Sha256.ForBytes(File.ReadAllBytes(Path.Combine(root, "payload", "text", "abc.txt")));
        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "agent-1",
              "Title": "Agent package",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations"]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [
                {
                  "Id": "op-1",
                  "Kind": "AddTextFile",
                  "Path": "src/File.txt",
                  "TargetSha256": "abc",
                  "PayloadPath": "payload/text/abc.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                }
              ],
              "Groups": []
            }
            """, new UTF8Encoding(false));

        var result = new AgentPackageRootPacker().Pack(root, Path.Combine(workspace.Path, "out.rpack"));

        Assert.False(result.Success);
        Assert.Contains("Payload hash mismatch", result.Message);
    }

    [Fact]
    public void AgentPackageRootValidator_AcceptsRepairMetadata()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("package-root");
        Directory.CreateDirectory(Path.Combine(root, "payload", "text"));

        File.WriteAllText(Path.Combine(root, "payload", "text", "abc.txt"), "hello", new UTF8Encoding(false));
        var payloadHash = Sha256.ForBytes(File.ReadAllBytes(Path.Combine(root, "payload", "text", "abc.txt")));
        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "repair-1",
              "Title": "Repair package",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations", "payload-store"],
              "RepairsPackageId": "agent-1",
              "RepairsOperations": ["op-1"]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [
                {
                  "Id": "op-1",
                  "Kind": "AddTextFile",
                  "Path": "src/File.txt",
                  "TargetSha256": "__HASH__",
                  "PayloadPath": "payload/text/abc.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                }
              ],
              "Groups": []
            }
            """.Replace("__HASH__", payloadHash), new UTF8Encoding(false));

        var result = new AgentPackageRootValidator().Validate(root);

        Assert.True(result.Success, result.Message);
    }

    [Fact]
    public void AgentPackageRootValidator_AcceptsDeclaredFilesAndValidationCommands()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("package-root");
        Directory.CreateDirectory(Path.Combine(root, "payload", "text"));

        File.WriteAllText(Path.Combine(root, "payload", "text", "abc.txt"), "hello", new UTF8Encoding(false));
        var payloadHash = Sha256.ForBytes(File.ReadAllBytes(Path.Combine(root, "payload", "text", "abc.txt")));
        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "agent-validate-1",
              "Title": "Agent package",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations"],
              "DeclaredFiles": ["src/File.txt"],
              "Validation": [
                { "Name": "Echo", "Command": "echo validation ok", "Optional": false }
              ]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [
                {
                  "Id": "op-1",
                  "Kind": "AddTextFile",
                  "Path": "src/File.txt",
                  "TargetSha256": "__HASH__",
                  "PayloadPath": "payload/text/abc.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                }
              ],
              "Groups": []
            }
            """.Replace("__HASH__", payloadHash), new UTF8Encoding(false));

        var result = new AgentPackageRootValidator().ValidateDetailed(root);

        Assert.True(result.Success, result.Summary);
    }

    [Fact]
    public void AgentPackageRootValidator_RejectsMissingMetadata()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("package-root");
        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "",
              "Title": "",
              "CreatedAtUtc": "",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations"]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [],
              "Groups": []
            }
            """, new UTF8Encoding(false));

        var result = new AgentPackageRootValidator().ValidateDetailed(root);

        Assert.False(result.Success);
        Assert.Single(result.Issues);
        Assert.Equal("agent.metadata-missing", result.Issues[0].Code);
    }

    [Fact]
    public void AgentPackageRootValidator_RejectsDuplicateOperationPath()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("package-root");
        Directory.CreateDirectory(Path.Combine(root, "payload", "text"));

        File.WriteAllText(Path.Combine(root, "payload", "text", "abc.txt"), "hello", new UTF8Encoding(false));
        var payloadHash = Sha256.ForBytes(File.ReadAllBytes(Path.Combine(root, "payload", "text", "abc.txt")));
        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "agent-validate-dup",
              "Title": "Agent package",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations"]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [
                {
                  "Id": "op-1",
                  "Kind": "AddTextFile",
                  "Path": "src/File.txt",
                  "TargetSha256": "__HASH__",
                  "PayloadPath": "payload/text/abc.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                },
                {
                  "Id": "op-2",
                  "Kind": "AddTextFile",
                  "Path": "src/File.txt",
                  "TargetSha256": "__HASH__",
                  "PayloadPath": "payload/text/abc.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                }
              ],
              "Groups": []
            }
            """.Replace("__HASH__", payloadHash), new UTF8Encoding(false));

        var result = new AgentPackageRootValidator().ValidateDetailed(root);

        Assert.False(result.Success);
        Assert.Single(result.Issues);
        Assert.Equal("agent.operation-duplicate-path", result.Issues[0].Code);
    }

    [Fact]
    public void AgentPackageRootValidator_RejectsPayloadHashMismatch()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("package-root");
        Directory.CreateDirectory(Path.Combine(root, "payload", "text"));

        File.WriteAllText(Path.Combine(root, "payload", "text", "abc.txt"), "hello", new UTF8Encoding(false));
        var payloadHash = Sha256.ForBytes(File.ReadAllBytes(Path.Combine(root, "payload", "text", "abc.txt")));
        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "agent-validate-mismatch",
              "Title": "Agent package",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations"]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [
                {
                  "Id": "op-1",
                  "Kind": "AddTextFile",
                  "Path": "src/File.txt",
                  "TargetSha256": "deadbeef",
                  "PayloadPath": "payload/text/abc.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                }
              ],
              "Groups": []
            }
            """, new UTF8Encoding(false));

        var result = new AgentPackageRootValidator().ValidateDetailed(root);

        Assert.False(result.Success);
        Assert.Single(result.Issues);
        Assert.Equal("agent.payload-hash-mismatch", result.Issues[0].Code);
    }

    [Fact]
    public void AgentPackageRootValidator_RejectsDeclaredFilesMismatch()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("package-root");
        Directory.CreateDirectory(Path.Combine(root, "payload", "text"));

        File.WriteAllText(Path.Combine(root, "payload", "text", "abc.txt"), "hello", new UTF8Encoding(false));
        var payloadHash = Sha256.ForBytes(File.ReadAllBytes(Path.Combine(root, "payload", "text", "abc.txt")));
        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "agent-validate-2",
              "Title": "Agent package",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations"],
              "DeclaredFiles": ["src/File.txt", "src/Extra.txt"]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [
                {
                  "Id": "op-1",
                  "Kind": "AddTextFile",
                  "Path": "src/File.txt",
                  "TargetSha256": "__HASH__",
                  "PayloadPath": "payload/text/abc.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                }
              ],
              "Groups": []
            }
            """.Replace("__HASH__", payloadHash), new UTF8Encoding(false));

        var result = new AgentPackageRootValidator().ValidateDetailed(root);

        Assert.False(result.Success);
        Assert.Single(result.Issues);
        Assert.Equal("agent.declared-files-mismatch", result.Issues[0].Code);
    }

    [Fact]
    public void AgentPackageRootValidator_RejectsBinaryContent()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("package-root");
        Directory.CreateDirectory(Path.Combine(root, "payload", "data"));

        var binaryPath = Path.Combine(root, "payload", "data", "blob.bin");
        File.WriteAllBytes(binaryPath, [0x00, 0x01, 0x02, 0x03, 0x04, 0x05]);
        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "agent-validate-binary",
              "Title": "Agent package",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations"]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [],
              "Groups": []
            }
            """, new UTF8Encoding(false));

        var result = new AgentPackageRootValidator().ValidateDetailed(root);

        Assert.False(result.Success);
        Assert.Single(result.Issues);
        Assert.Equal("agent.risk-binary-change", result.Issues[0].Code);
    }

    [Fact]
    public void AgentPackageRootValidator_RejectsValidationCommandFailure()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("package-root");
        Directory.CreateDirectory(Path.Combine(root, "payload", "text"));

        File.WriteAllText(Path.Combine(root, "payload", "text", "abc.txt"), "hello", new UTF8Encoding(false));
        var payloadHash = Sha256.ForBytes(File.ReadAllBytes(Path.Combine(root, "payload", "text", "abc.txt")));
        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "agent-validate-3",
              "Title": "Agent package",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations"],
              "DeclaredFiles": ["src/File.txt"],
              "Validation": [
                { "Name": "Fail", "Command": "exit 1", "Optional": false }
              ]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [
                {
                  "Id": "op-1",
                  "Kind": "AddTextFile",
                  "Path": "src/File.txt",
                  "TargetSha256": "__HASH__",
                  "PayloadPath": "payload/text/abc.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                }
              ],
              "Groups": []
            }
            """.Replace("__HASH__", payloadHash), new UTF8Encoding(false));

        var result = new AgentPackageRootValidator().ValidateDetailed(root);

        Assert.False(result.Success);
        Assert.Single(result.Issues);
        Assert.Equal("agent.validation-command-failed", result.Issues[0].Code);
    }

    [Fact]
    public void AgentApplyPlanBuilder_MarksOperationsReady()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("package-root");
        var target = workspace.CreateDirectory("target");
        Directory.CreateDirectory(Path.Combine(root, "payload", "text"));

        File.WriteAllText(Path.Combine(root, "payload", "text", "abc.txt"), "hello", new UTF8Encoding(false));
        var payloadHash = Sha256.ForBytes(File.ReadAllBytes(Path.Combine(root, "payload", "text", "abc.txt")));
        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "agent-1",
              "Title": "Agent package",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations"]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [
                {
                  "Id": "op-1",
                  "Kind": "AddTextFile",
                  "Path": "src/File.txt",
                  "TargetSha256": "__HASH__",
                  "PayloadPath": "payload/text/abc.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                }
              ],
              "Groups": []
            }
            """.Replace("__HASH__", payloadHash), new UTF8Encoding(false));

        var planBuilder = new AgentApplyPlanBuilder();
        var plan = planBuilder.BuildPlan(root, target);
        var rendered = planBuilder.Render(plan);

        Assert.Contains("Package: Agent package (agent-1)", rendered);
        Assert.Contains("op-1  readywithpayload  AddTextFile  src/File.txt", rendered);
    }

    [Fact]
    public void AgentApplyPlanBuilder_DetectsModifyBaseMismatch()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("package-root");
        var target = workspace.CreateDirectory("target");
        Directory.CreateDirectory(Path.Combine(root, "payload", "text"));

        File.WriteAllText(Path.Combine(root, "payload", "text", "abc.txt"), "new", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(target, "src.txt"), "edited", new UTF8Encoding(false));
        var baseHash = Sha256.ForBytes(Encoding.UTF8.GetBytes("old"));
        var targetHash = Sha256.ForBytes(Encoding.UTF8.GetBytes("new"));
        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "agent-1",
              "Title": "Agent package",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations"]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [
                {
                  "Id": "op-1",
                  "Kind": "ModifyTextFile",
                  "Path": "src.txt",
                  "BaseSha256": "__BASE__",
                  "TargetSha256": "__TARGET__",
                  "PayloadPath": "payload/text/abc.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                }
              ],
              "Groups": []
            }
            """.Replace("__BASE__", baseHash).Replace("__TARGET__", targetHash), new UTF8Encoding(false));

        var planBuilder = new AgentApplyPlanBuilder();
        var plan = planBuilder.BuildPlan(root, target);
        var rendered = planBuilder.Render(plan);

        Assert.Contains("conflictbasehashmismatch", rendered);
    }

    [Fact]
    public void AgentPackageApplier_AppliesPayloadsAndWritesJournal()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("package-root");
        var target = workspace.CreateDirectory("target");
        Directory.CreateDirectory(Path.Combine(root, "payload", "text"));
        Directory.CreateDirectory(Path.Combine(target, "src"));
        InitializeRepository(target);

        File.WriteAllText(Path.Combine(target, "src", "File.txt"), "old", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "payload", "text", "modify.txt"), "new", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "payload", "text", "add.txt"), "added", new UTF8Encoding(false));

        var baseHash = Sha256.ForBytes(Encoding.UTF8.GetBytes("old"));
        var newHash = Sha256.ForBytes(Encoding.UTF8.GetBytes("new"));
        var addedHash = Sha256.ForBytes(Encoding.UTF8.GetBytes("added"));

        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "agent-apply-1",
              "Title": "Agent package",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations", "payload-store", "journal"]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [
                {
                  "Id": "op-1",
                  "Kind": "ModifyTextFile",
                  "Path": "src/File.txt",
                  "BaseSha256": "__BASE__",
                  "TargetSha256": "__TARGET__",
                  "PayloadPath": "payload/text/modify.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                },
                {
                  "Id": "op-2",
                  "Kind": "AddTextFile",
                  "Path": "src/New.txt",
                  "TargetSha256": "__ADDED__",
                  "PayloadPath": "payload/text/add.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                }
              ],
              "Groups": []
            }
            """.Replace("__BASE__", baseHash).Replace("__TARGET__", newHash).Replace("__ADDED__", addedHash), new UTF8Encoding(false));

        var result = new AgentPackageApplier().Apply(root, target);

        Assert.True(result.Success, result.Message);
        Assert.Equal("new", File.ReadAllText(Path.Combine(target, "src", "File.txt")));
        Assert.Equal("added", File.ReadAllText(Path.Combine(target, "src", "New.txt")));

        var journalRoot = ResolveGitPath(target, "rpack");
        var journalFiles = Directory.GetFiles(journalRoot, "journal.json", SearchOption.AllDirectories);
        Assert.Single(journalFiles);
        var journalText = File.ReadAllText(journalFiles[0]);
        Assert.Contains("agent-apply-1", journalText);
        Assert.Contains("op-1", journalText);
        Assert.Contains("op-2", journalText);
    }

    [Fact]
    public void AgentPackageApplier_RejectsBaseMismatchAndRollsBack()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("package-root");
        var target = workspace.CreateDirectory("target");
        Directory.CreateDirectory(Path.Combine(root, "payload", "text"));
        Directory.CreateDirectory(Path.Combine(target, "src"));
        InitializeRepository(target);

        File.WriteAllText(Path.Combine(target, "src", "File.txt"), "edited", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "payload", "text", "modify.txt"), "new", new UTF8Encoding(false));

        var baseHash = Sha256.ForBytes(Encoding.UTF8.GetBytes("old"));
        var targetHash = Sha256.ForBytes(Encoding.UTF8.GetBytes("new"));

        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "agent-apply-2",
              "Title": "Agent package",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations", "payload-store", "journal"]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [
                {
                  "Id": "op-1",
                  "Kind": "ModifyTextFile",
                  "Path": "src/File.txt",
                  "BaseSha256": "__BASE__",
                  "TargetSha256": "__TARGET__",
                  "PayloadPath": "payload/text/modify.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                }
              ],
              "Groups": []
            }
            """.Replace("__BASE__", baseHash).Replace("__TARGET__", targetHash), new UTF8Encoding(false));

        var result = new AgentPackageApplier().Apply(root, target);

        Assert.False(result.Success);
        Assert.Equal("edited", File.ReadAllText(Path.Combine(target, "src", "File.txt")));
        Assert.False(File.Exists(Path.Combine(target, "src", "New.txt")));
    }

    [Fact]
    public void AgentConflictReportBuilder_RendersCompactConflictReport()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("package-root");
        var target = workspace.CreateDirectory("target");
        Directory.CreateDirectory(Path.Combine(root, "payload", "text"));

        File.WriteAllText(Path.Combine(root, "payload", "text", "modify.txt"), "new", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(target, "src.txt"), "edited", new UTF8Encoding(false));
        var baseHash = Sha256.ForBytes(Encoding.UTF8.GetBytes("old"));
        var targetHash = Sha256.ForBytes(Encoding.UTF8.GetBytes("new"));

        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "agent-report-1",
              "Title": "Agent package",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations", "payload-store"]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [
                {
                  "Id": "op-1",
                  "Kind": "ModifyTextFile",
                  "Path": "src.txt",
                  "BaseSha256": "__BASE__",
                  "TargetSha256": "__TARGET__",
                  "PayloadPath": "payload/text/modify.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                }
              ],
              "Groups": []
            }
            """.Replace("__BASE__", baseHash).Replace("__TARGET__", targetHash), new UTF8Encoding(false));

        var plan = new AgentApplyPlanBuilder().BuildPlan(root, target);
        var report = new AgentConflictReportBuilder().Render(plan, root, target);

        Assert.Contains("RPACK Agent Package conflict report", report);
        Assert.Contains("OperationId", report);
        Assert.Contains("op-1", report);
        Assert.Contains("Problem: ConflictBaseHashMismatch", report);
        Assert.Contains("PromptForRegeneration", report);
    }

    [Fact]
    public void AgentApplyPlanBuilder_RenderJson_IsValidJson()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("package-root");
        var target = workspace.CreateDirectory("target");
        Directory.CreateDirectory(Path.Combine(root, "payload", "text"));

        File.WriteAllText(Path.Combine(root, "payload", "text", "abc.txt"), "hello", new UTF8Encoding(false));
        var payloadHash = Sha256.ForBytes(File.ReadAllBytes(Path.Combine(root, "payload", "text", "abc.txt")));
        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "agent-json-1",
              "Title": "Agent package",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations"]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [
                {
                  "Id": "op-1",
                  "Kind": "AddTextFile",
                  "Path": "src/File.txt",
                  "TargetSha256": "__HASH__",
                  "PayloadPath": "payload/text/abc.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                }
              ],
              "Groups": []
            }
            """.Replace("__HASH__", payloadHash), new UTF8Encoding(false));

        var json = new AgentApplyPlanBuilder().RenderJson(new AgentApplyPlanBuilder().BuildPlan(root, target));
        var rootNode = JsonNode.Parse(json);

        Assert.NotNull(rootNode);
        Assert.True(rootNode!["success"]?.GetValue<bool>());
        Assert.Equal("agent-json-1", rootNode["package"]?["id"]?.GetValue<string>());
        Assert.Equal("op-1", rootNode["operations"]?[0]?["id"]?.GetValue<string>());
    }

    [Fact]
    public void AgentApplyPlanBuilder_RenderJson_EmitsIssueCodes()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("package-root");
        var target = workspace.CreateDirectory("target");
        Directory.CreateDirectory(Path.Combine(root, "payload", "text"));

        File.WriteAllText(Path.Combine(root, "payload", "text", "abc.txt"), "hello", new UTF8Encoding(false));
        var payloadHash = Sha256.ForBytes(File.ReadAllBytes(Path.Combine(root, "payload", "text", "abc.txt")));
        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "agent-json-issue-code",
              "Title": "Agent package",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations"]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [
                {
                  "Id": "op-1",
                  "Kind": "ModifyTextFile",
                  "Path": "src/File.txt",
                  "BaseSha256": "__BASE__",
                  "TargetSha256": "__TARGET__",
                  "PayloadPath": "payload/text/abc.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                }
              ],
              "Groups": []
            }
            """.Replace("__BASE__", "deadbeef").Replace("__TARGET__", payloadHash), new UTF8Encoding(false));

        Directory.CreateDirectory(Path.Combine(target, "src"));
        File.WriteAllText(Path.Combine(target, "src", "File.txt"), "edited", new UTF8Encoding(false));

        var plan = new AgentApplyPlanBuilder().BuildPlan(root, target);
        var json = new AgentApplyPlanBuilder().RenderJson(plan);
        var rootNode = JsonNode.Parse(json);

        Assert.NotNull(rootNode);
        Assert.Equal("agent.base-hash-mismatch", plan.Operations![0].IssueCode);
        Assert.Equal("agent.base-hash-mismatch", rootNode!["operations"]?[0]?["issueCode"]?.GetValue<string>());
    }

    [Fact]
    public void AgentConflictReportBuilder_RenderJson_IsValidJson()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("package-root");
        var target = workspace.CreateDirectory("target");
        Directory.CreateDirectory(Path.Combine(root, "payload", "text"));

        File.WriteAllText(Path.Combine(root, "payload", "text", "modify.txt"), "new", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(target, "src.txt"), "edited", new UTF8Encoding(false));
        var baseHash = Sha256.ForBytes(Encoding.UTF8.GetBytes("old"));
        var targetHash = Sha256.ForBytes(Encoding.UTF8.GetBytes("new"));

        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "agent-json-2",
              "Title": "Agent package",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations", "payload-store"]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [
                {
                  "Id": "op-1",
                  "Kind": "ModifyTextFile",
                  "Path": "src.txt",
                  "BaseSha256": "__BASE__",
                  "TargetSha256": "__TARGET__",
                  "PayloadPath": "payload/text/modify.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                }
              ],
              "Groups": []
            }
            """.Replace("__BASE__", baseHash).Replace("__TARGET__", targetHash), new UTF8Encoding(false));

        var plan = new AgentApplyPlanBuilder().BuildPlan(root, target);
        var json = new AgentConflictReportBuilder().RenderJson(plan, root, target);
        var rootNode = JsonNode.Parse(json);

        Assert.NotNull(rootNode);
        Assert.True(rootNode!["success"]?.GetValue<bool>());
        Assert.Equal("agent-json-2", rootNode["package"]?.GetValue<string>());
        Assert.Equal("op-1", rootNode["conflicts"]?[0]?["operationId"]?.GetValue<string>());
        Assert.Equal("agent.base-hash-mismatch", rootNode["conflicts"]?[0]?["issueCode"]?.GetValue<string>());
    }

    [Fact]
    public void AgentPackageUndoer_RestoresAppliedFiles()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("package-root");
        var target = workspace.CreateDirectory("target");
        Directory.CreateDirectory(Path.Combine(root, "payload", "text"));
        Directory.CreateDirectory(Path.Combine(target, "src"));
        InitializeRepository(target);

        File.WriteAllText(Path.Combine(target, "src", "File.txt"), "old", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "payload", "text", "modify.txt"), "new", new UTF8Encoding(false));

        var baseHash = Sha256.ForBytes(Encoding.UTF8.GetBytes("old"));
        var newHash = Sha256.ForBytes(Encoding.UTF8.GetBytes("new"));

        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "agent-undo-1",
              "Title": "Agent package",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations", "payload-store", "journal"]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [
                {
                  "Id": "op-1",
                  "Kind": "ModifyTextFile",
                  "Path": "src/File.txt",
                  "BaseSha256": "__BASE__",
                  "TargetSha256": "__TARGET__",
                  "PayloadPath": "payload/text/modify.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                }
              ],
              "Groups": []
            }
            """.Replace("__BASE__", baseHash).Replace("__TARGET__", newHash), new UTF8Encoding(false));

        var apply = new AgentPackageApplier().Apply(root, target);
        Assert.True(apply.Success, apply.Message);

        var undo = new AgentPackageUndoer().Undo(target);

        Assert.True(undo.Success, undo.Message);
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "src", "File.txt")));
    }

    [Fact]
    public void AgentPackageUndoer_DetectsTargetChangesAndCanForceUndo()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("package-root");
        var target = workspace.CreateDirectory("target");
        Directory.CreateDirectory(Path.Combine(root, "payload", "text"));
        Directory.CreateDirectory(Path.Combine(target, "src"));
        InitializeRepository(target);

        File.WriteAllText(Path.Combine(target, "src", "File.txt"), "old", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "payload", "text", "modify.txt"), "new", new UTF8Encoding(false));

        var baseHash = Sha256.ForBytes(Encoding.UTF8.GetBytes("old"));
        var newHash = Sha256.ForBytes(Encoding.UTF8.GetBytes("new"));

        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "agent-undo-3",
              "Title": "Agent package",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations", "payload-store", "journal"]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [
                {
                  "Id": "op-1",
                  "Kind": "ModifyTextFile",
                  "Path": "src/File.txt",
                  "BaseSha256": "__BASE__",
                  "TargetSha256": "__TARGET__",
                  "PayloadPath": "payload/text/modify.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                }
              ],
              "Groups": []
            }
            """.Replace("__BASE__", baseHash).Replace("__TARGET__", newHash), new UTF8Encoding(false));

        var apply = new AgentPackageApplier().Apply(root, target);
        Assert.True(apply.Success, apply.Message);

        File.WriteAllText(Path.Combine(target, "src", "File.txt"), "edited-after-apply", new UTF8Encoding(false));

        var undoer = new AgentPackageUndoer();
        var strictUndo = undoer.UndoDetailed(target);
        Assert.False(strictUndo.Success);
        Assert.Contains(strictUndo.Issues, issue => issue.Code == "state.target-changed-after-apply");

        var forcedUndo = undoer.UndoDetailed(target, force: true);
        Assert.True(forcedUndo.Success, forcedUndo.Summary);
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "src", "File.txt")));
    }

    [Fact]
    public void AgentRepairPackageGenerator_CreatesRepairRoot()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("package-root");
        var target = workspace.CreateDirectory("target");
        var repairRoot = workspace.CreateDirectory("repair-root");
        Directory.CreateDirectory(Path.Combine(root, "payload", "text"));
        Directory.CreateDirectory(Path.Combine(target, "src"));

        File.WriteAllText(Path.Combine(target, "src", "File.txt"), "edited", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "payload", "text", "modify.txt"), "new", new UTF8Encoding(false));

        var baseHash = Sha256.ForBytes(Encoding.UTF8.GetBytes("old"));
        var targetHash = Sha256.ForBytes(Encoding.UTF8.GetBytes("new"));

        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "agent-repair-src",
              "Title": "Agent package",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations", "payload-store"]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [
                {
                  "Id": "op-1",
                  "Kind": "ModifyTextFile",
                  "Path": "src/File.txt",
                  "BaseSha256": "__BASE__",
                  "TargetSha256": "__TARGET__",
                  "PayloadPath": "payload/text/modify.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                }
              ],
              "Groups": []
            }
            """.Replace("__BASE__", baseHash).Replace("__TARGET__", targetHash), new UTF8Encoding(false));

        var generator = new AgentRepairPackageGenerator();
        var result = generator.Generate(root, target, repairRoot);

        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(Path.Combine(repairRoot, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(repairRoot, "operations.json")));
        Assert.True(File.Exists(Path.Combine(repairRoot, "README.md")));
        Assert.Equal("repair-agent-repair-src", JsonNode.Parse(File.ReadAllText(Path.Combine(repairRoot, "manifest.json")))!["Id"]?.GetValue<string>());
        Assert.Equal("agent-repair-src", JsonNode.Parse(File.ReadAllText(Path.Combine(repairRoot, "manifest.json")))!["RepairsPackageId"]?.GetValue<string>());
        Assert.True(new AgentPackageRootValidator().Validate(repairRoot).Success);
    }

    [Fact]
    public void AgentPackageApplier_ApplyRemainingSkipsJournaledPaths()
    {
        using var workspace = new TempWorkspace();
        var original = workspace.CreateDirectory("original");
        var repair = workspace.CreateDirectory("repair");
        var target = workspace.CreateDirectory("target");
        Directory.CreateDirectory(Path.Combine(original, "payload", "text"));
        Directory.CreateDirectory(Path.Combine(repair, "payload", "text"));
        Directory.CreateDirectory(Path.Combine(target, "src"));
        InitializeRepository(target);

        File.WriteAllText(Path.Combine(target, "src", "File.txt"), "old", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(original, "payload", "text", "original.txt"), "original-new", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(repair, "payload", "text", "repair.txt"), "repair-new", new UTF8Encoding(false));

        var baseHash = Sha256.ForBytes(Encoding.UTF8.GetBytes("old"));
        var originalHash = Sha256.ForBytes(Encoding.UTF8.GetBytes("original-new"));
        var repairHash = Sha256.ForBytes(Encoding.UTF8.GetBytes("repair-new"));

        WriteAgentPackage(original, "agent-original", "original", baseHash, originalHash, "payload/text/original.txt");
        WriteAgentPackage(repair, "agent-repair", "repair", baseHash, repairHash, "payload/text/repair.txt");

        var applyRepair = new AgentPackageApplier().Apply(repair, target);
        Assert.True(applyRepair.Success, applyRepair.Message);
        Assert.Equal("repair-new", File.ReadAllText(Path.Combine(target, "src", "File.txt")).TrimEnd('\r', '\n'));

        var applyRemaining = new AgentPackageApplier().ApplyRemaining(original, target);
        Assert.True(applyRemaining.Success, applyRemaining.Message);
        Assert.Equal("repair-new", File.ReadAllText(Path.Combine(target, "src", "File.txt")).TrimEnd('\r', '\n'));
    }

    [Fact]
    public void AgentPackageUndoer_DetailedReportsMissingJournalAndBackup()
    {
        using var workspace = new TempWorkspace();
        var target = workspace.CreateDirectory("target");
        InitializeRepository(target);

        var undoer = new AgentPackageUndoer();
        var missingJournal = undoer.UndoDetailed(target);
        Assert.False(missingJournal.Success);
        Assert.Contains(missingJournal.Issues, issue => issue.Code == "state.journal-missing");

        var root = workspace.CreateDirectory("package-root");
        Directory.CreateDirectory(Path.Combine(root, "payload", "text"));
        Directory.CreateDirectory(Path.Combine(target, "src"));
        File.WriteAllText(Path.Combine(target, "src", "File.txt"), "old", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "payload", "text", "modify.txt"), "new", new UTF8Encoding(false));

        var baseHash = Sha256.ForBytes(Encoding.UTF8.GetBytes("old"));
        var newHash = Sha256.ForBytes(Encoding.UTF8.GetBytes("new"));

        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "agent-undo-2",
              "Title": "Agent package",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations", "payload-store", "journal"]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [
                {
                  "Id": "op-1",
                  "Kind": "ModifyTextFile",
                  "Path": "src/File.txt",
                  "BaseSha256": "__BASE__",
                  "TargetSha256": "__TARGET__",
                  "PayloadPath": "payload/text/modify.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                }
              ],
              "Groups": []
            }
            """.Replace("__BASE__", baseHash).Replace("__TARGET__", newHash), new UTF8Encoding(false));

        var apply = new AgentPackageApplier().Apply(root, target);
        Assert.True(apply.Success, apply.Message);

        var journalPath = Directory.GetFiles(Path.Combine(target, ".git", "rpack", "operation-journal"), "journal.json", SearchOption.AllDirectories).Single();
        using (var stream = File.OpenRead(journalPath))
        {
            var doc = JsonDocument.Parse(stream);
            var backupPath = doc.RootElement.GetProperty("Operations")[0].GetProperty("BackupPath").GetString();
            Assert.False(string.IsNullOrWhiteSpace(backupPath));
            File.Delete(Path.Combine(target, ".git", "rpack", backupPath!.Replace('/', Path.DirectorySeparatorChar)));
        }

        var missingBackup = undoer.UndoDetailed(target);
        Assert.False(missingBackup.Success);
        Assert.Contains(missingBackup.Issues, issue => issue.Code == "state.backup-missing");
    }

    private static void WriteAgentPackage(string root, string id, string title, string baseHash, string targetHash, string payloadPath)
    {
        File.WriteAllText(Path.Combine(root, "manifest.json"), $$"""
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "{{id}}",
              "Title": "{{title}}",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations", "payload-store", "journal"]
            }
            """, new UTF8Encoding(false));

        File.WriteAllText(Path.Combine(root, "operations.json"), $$"""
            {
              "Operations": [
                {
                  "Id": "op-1",
                  "Kind": "ModifyTextFile",
                  "Path": "src/File.txt",
                  "BaseSha256": "{{baseHash}}",
                  "TargetSha256": "{{targetHash}}",
                  "PayloadPath": "{{payloadPath}}",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                }
              ],
              "Groups": []
            }
            """, new UTF8Encoding(false));

        Directory.CreateDirectory(Path.Combine(root, "payload", "text"));
        var payloadFile = Path.Combine(root, payloadPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(payloadFile)!);
        var payloadText = payloadPath.Contains("repair", StringComparison.OrdinalIgnoreCase) ? "repair-new" : "original-new";
        File.WriteAllText(payloadFile, payloadText, new UTF8Encoding(false));
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rpack-agent-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateDirectory(string name)
        {
            var path = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(Path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(entry, FileAttributes.Normal);
                }

                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private static void InitializeRepository(string path)
    {
        RunGit(path, "init");
    }

    private static string ResolveGitPath(string repositoryPath, string relativePath)
    {
        var output = RunGit(repositoryPath, "rev-parse", "--git-path", relativePath).Trim();
        return Path.IsPathRooted(output)
            ? output
            : Path.GetFullPath(Path.Combine(repositoryPath, output));
    }

    private static string RunGit(string workingDirectory, params string[] args)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output : error);
        }

        return output;
    }
}
