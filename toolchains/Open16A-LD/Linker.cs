using System.Globalization;

namespace Open16A.Ld;

public sealed record LinkInput(string Path, uint Address);
public sealed record LinkMapEntry(string Path, uint Address, int Length);
public sealed record LinkResult(uint Origin, byte[] Bytes, IReadOnlyList<LinkMapEntry> Entries);

public sealed class LinkException(string message) : Exception(message);

public sealed class Linker
{
    public const uint PhysicalAddressSpace = 0x1_00000;

    public LinkResult Link(IEnumerable<LinkInput> inputs, uint? baseAddress = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var modules = inputs.Select(input => new
        {
            Input = input,
            Bytes = File.ReadAllBytes(input.Path)
        }).OrderBy(module => module.Input.Address).ToList();

        if (modules.Count == 0)
            throw new LinkException("At least one input module is required.");
        if (modules.Any(module => module.Bytes.Length == 0))
            throw new LinkException("Input modules must not be empty.");

        foreach (var module in modules)
        {
            if (module.Input.Address >= PhysicalAddressSpace || (ulong)module.Input.Address + (uint)module.Bytes.Length > PhysicalAddressSpace)
                throw new LinkException($"Module '{module.Input.Path}' does not fit within the 1 MiB physical address space.");
        }

        for (var index = 1; index < modules.Count; index++)
        {
            ulong previousEnd = (ulong)modules[index - 1].Input.Address + (uint)modules[index - 1].Bytes.Length;
            if (previousEnd > modules[index].Input.Address)
                throw new LinkException($"Modules '{modules[index - 1].Input.Path}' and '{modules[index].Input.Path}' overlap.");
        }

        uint origin = baseAddress ?? modules[0].Input.Address;
        if (origin > modules[0].Input.Address)
            throw new LinkException("Link base must not be above the first module address.");

        uint end = checked((uint)((ulong)modules[^1].Input.Address + (uint)modules[^1].Bytes.Length));
        var output = new byte[checked((int)(end - origin))];
        var entries = new List<LinkMapEntry>(modules.Count);
        foreach (var module in modules)
        {
            module.Bytes.CopyTo(output, checked((int)(module.Input.Address - origin)));
            entries.Add(new LinkMapEntry(module.Input.Path, module.Input.Address, module.Bytes.Length));
        }

        return new LinkResult(origin, output, entries);
    }

    public static LinkInput ParseInput(string value)
    {
        int separator = value.LastIndexOf('@');
        if (separator <= 0 || separator == value.Length - 1)
            throw new LinkException($"Input '{value}' must use <file>@<physical-address>.");
        return new LinkInput(value[..separator], ParseAddress(value[(separator + 1)..]));
    }

    public static uint ParseAddress(string value)
    {
        value = value.Trim();
        NumberStyles style = NumberStyles.Integer;
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..];
            style = NumberStyles.AllowHexSpecifier;
        }
        else if (value.EndsWith("h", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^1];
            style = NumberStyles.AllowHexSpecifier;
        }

        if (!uint.TryParse(value, style, CultureInfo.InvariantCulture, out uint address) || address >= PhysicalAddressSpace)
            throw new LinkException($"'{value}' is not a physical address in 00000h-FFFFFh.");
        return address;
    }
}
