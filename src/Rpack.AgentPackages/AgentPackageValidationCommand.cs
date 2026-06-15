namespace Rpack.AgentPackages;

public sealed class AgentPackageValidationCommand
{
    public string Name { get; init; } = "";
    public string Command { get; init; } = "";
    public bool Optional { get; init; }
}
