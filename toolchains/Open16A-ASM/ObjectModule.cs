namespace Open16A.Asm;

public enum RelocationKind
{
    Absolute16,
    Absolute20,
    Relative16
}

public sealed record ObjectSymbol(string Name, int Offset, bool Exported);
public sealed record ObjectRelocation(int Offset, RelocationKind Kind, string Symbol, int Addend, bool Local);
public sealed record ObjectModule(byte[] Bytes, IReadOnlyList<ObjectSymbol> Symbols, IReadOnlyList<ObjectRelocation> Relocations);
