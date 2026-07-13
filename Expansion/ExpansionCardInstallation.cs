namespace OldSimulator.Expansion;

public sealed record ExpansionCardInstallation(
    int Slot,
    ExpansionCardDescriptor Descriptor,
    IExpansionCard Card);
