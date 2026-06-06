namespace Rpack.Open;

internal sealed class OpenOptions
{
    private static readonly HashSet<string> OptionsWithValues = new(StringComparer.Ordinal)
    {
        "--repo",
        "--path-prefix"
    };

    private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Positionals { get; private set; } = [];

    public static OpenOptions Parse(string[] args)
    {
        var options = new OpenOptions();
        var positionals = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("-", StringComparison.Ordinal))
            {
                positionals.Add(arg);
                continue;
            }

            if (OptionsWithValues.Contains(arg))
            {
                if (i + 1 >= args.Length)
                {
                    throw new InvalidOperationException($"Missing value for {arg}.");
                }

                options._values[arg] = args[i + 1];
                i++;
            }
            else
            {
                options._values[arg] = null;
            }
        }

        options.Positionals = positionals;
        return options;
    }

    public bool Has(string name) => _values.ContainsKey(name);

    public string? Value(string name) => _values.TryGetValue(name, out var value) ? value : null;
}
