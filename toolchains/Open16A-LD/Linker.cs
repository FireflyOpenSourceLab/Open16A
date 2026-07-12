using System.Globalization;
using Open16A.Asm;

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

    public LinkResult LinkObjects(IEnumerable<ObjectModule> inputs, uint baseAddress)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var modules = inputs.ToList();
        if (modules.Count == 0)
            throw new LinkException("At least one object module is required.");
        if (baseAddress >= PhysicalAddressSpace)
            throw new LinkException("Link base must be within the 1 MiB physical address space.");

        var bases = new uint[modules.Count];
        uint next = baseAddress;
        for (var index = 0; index < modules.Count; index++)
        {
            ValidateObjectModule(modules[index]);
            if ((next & 1) != 0) next++;
            if ((ulong)next + (uint)modules[index].Bytes.Length > PhysicalAddressSpace)
                throw new LinkException("Linked objects do not fit within the 1 MiB physical address space.");
            bases[index] = next;
            next += (uint)modules[index].Bytes.Length;
        }

        var exports = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < modules.Count; index++)
        {
            foreach (ObjectSymbol symbol in modules[index].Symbols.Where(symbol => symbol.Exported))
            {
                uint address = checked(bases[index] + (uint)symbol.Offset);
                if (!exports.TryAdd(symbol.Name, address))
                    throw new LinkException($"Duplicate global symbol '{symbol.Name}'.");
            }
        }

        var bytes = new byte[checked((int)(next - baseAddress))];
        var entries = new List<LinkMapEntry>(modules.Count);
        for (var index = 0; index < modules.Count; index++)
        {
            ObjectModule module = modules[index];
            int start = checked((int)(bases[index] - baseAddress));
            module.Bytes.CopyTo(bytes, start);
            var locals = module.Symbols.ToDictionary(symbol => symbol.Name, symbol => checked(bases[index] + (uint)symbol.Offset), StringComparer.OrdinalIgnoreCase);
            foreach (ObjectRelocation relocation in module.Relocations)
            {
                uint target;
                if (relocation.Local)
                {
                    if (!locals.TryGetValue(relocation.Symbol, out target))
                        throw new LinkException($"Module-local symbol '{relocation.Symbol}' is missing.");
                }
                else if (!exports.TryGetValue(relocation.Symbol, out target))
                    throw new LinkException($"Unresolved external symbol '{relocation.Symbol}'.");

                long value = checked((long)target + relocation.Addend);
                int offset = start + relocation.Offset;
                Patch(bytes, offset, bases[index] + (uint)relocation.Offset, relocation.Kind, value);
            }
            entries.Add(new LinkMapEntry($"module-{index}", bases[index], module.Bytes.Length));
        }

        return new LinkResult(baseAddress, bytes, entries);
    }

    private static void ValidateObjectModule(ObjectModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (module.Bytes is null || module.Symbols is null || module.Relocations is null)
            throw new LinkException("Object module is missing required data.");

        foreach (ObjectSymbol symbol in module.Symbols)
        {
            if (string.IsNullOrWhiteSpace(symbol.Name) || symbol.Offset < 0 || symbol.Offset > module.Bytes.Length)
                throw new LinkException("Object module contains an invalid symbol.");
        }
        if (module.Symbols.Select(symbol => symbol.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != module.Symbols.Count)
            throw new LinkException("Object module contains duplicate symbols.");

        foreach (ObjectRelocation relocation in module.Relocations)
        {
            int width = relocation.Kind switch
            {
                RelocationKind.Absolute16 or RelocationKind.Relative16 => 2,
                RelocationKind.Absolute20 => 4,
                _ => throw new LinkException("Object module contains an unknown relocation kind.")
            };
            if (string.IsNullOrWhiteSpace(relocation.Symbol) || relocation.Offset < 0 || relocation.Offset > module.Bytes.Length - width)
                throw new LinkException("Object module contains an invalid relocation.");
        }
    }

    private static void Patch(byte[] bytes, int offset, uint place, RelocationKind kind, long value)
    {
        switch (kind)
        {
            case RelocationKind.Absolute16:
                if (value is < short.MinValue or > ushort.MaxValue) throw new LinkException("ABS16 relocation does not fit a 16-bit word.");
                WriteWord(bytes, offset, unchecked((ushort)value));
                return;
            case RelocationKind.Absolute20:
                if (value is < 0 or >= PhysicalAddressSpace) throw new LinkException("ABS20 relocation does not fit a physical address.");
                WriteWord(bytes, offset, (ushort)value);
                WriteWord(bytes, offset + 2, (ushort)(value >> 16));
                return;
            case RelocationKind.Relative16:
                long delta = value - ((long)place + 2);
                if ((delta & 1) != 0 || delta is < -65536 or > 65534) throw new LinkException("REL16 relocation target is out of range.");
                WriteWord(bytes, offset, unchecked((ushort)(short)(delta / 2)));
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static void WriteWord(byte[] bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)(value >> 8);
        bytes[offset + 1] = (byte)value;
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
