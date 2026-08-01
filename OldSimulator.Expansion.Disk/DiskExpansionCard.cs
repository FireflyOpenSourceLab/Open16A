namespace OldSimulator.Expansion.Disk;

public static class DiskCardProtocol
{
    public const byte   ProtocolVersion = 1;
    public const int    SectorSize      = 512;
    public const ushort HeaderBytes     = 0x010;
    public const ushort DataOffset      = HeaderBytes;

    public const ushort CommandIdentify    = 0x0000;
    public const ushort CommandRead        = 0x0001;
    public const ushort CommandWrite       = 0x0002;

    public const ushort StatusOk             = 0x0000;
    public const ushort StatusUnknownCommand = 0x0001;
    public const ushort StatusLbaOutOfRange  = 0x0002;
    public const ushort StatusWriteProtected = 0x0003;
    public const ushort StatusHostIoError    = 0x0004;

    public const ushort FlagReadOnly = 1 << 0;

    public const ushort StatusOffset   = 0x000;
    public const ushort LbaOffset      = 0x002;
    public const ushort MagicOffset    = 0x002;
    public const ushort VersionOffset  = 0x006;
    public const ushort SectorSizeOffset = 0x008;
    public const ushort SectorCountOffset = 0x00A;
    public const ushort FlagsOffset    = 0x00E;

    public static readonly byte[] MagicBytes = [(byte)'O', (byte)'D', (byte)'S', (byte)'K'];
}

internal sealed class DiskExpansionCard : IExpansionCard
{
    private readonly bool readOnly;
    private readonly ulong latencyCycles;
    private readonly uint sectorCount;
    private readonly FileStream stream;

    private IExpansionCardCommand? _pendingCommand;
    private ulong _remainingCycles;
    private bool _disposed;

    public DiskExpansionCard(DiskCardSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        FileStream stream;
        try
        {
            stream = new FileStream(
                settings.ImagePath,
                FileMode.Open,
                settings.ReadOnly ? FileAccess.Read : FileAccess.ReadWrite,
                settings.ReadOnly ? FileShare.Read : FileShare.None);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new ArgumentException(
                $"Disk card image '{settings.ImagePath}' could not be opened: {error.Message}",
                nameof(settings));
        }

        long bytes;
        try
        {
            bytes = stream.Length;
        }
        catch
        {
            stream.Dispose();
            throw;
        }

        if (bytes <= 0 || bytes % DiskCardProtocol.SectorSize != 0)
        {
            stream.Dispose();
            throw new ArgumentException(
                $"Disk card image '{settings.ImagePath}' must have a non-zero length that is a " +
                $"multiple of {DiskCardProtocol.SectorSize} bytes; got {bytes}.",
                nameof(settings));
        }

        uint sectors;
        try
        {
            sectors = checked((uint)(bytes / DiskCardProtocol.SectorSize));
        }
        catch (OverflowException)
        {
            stream.Dispose();
            throw new ArgumentException(
                $"Disk card image '{settings.ImagePath}' is too large for a 32-bit LBA.",
                nameof(settings));
        }

        this.readOnly = settings.ReadOnly;
        this.latencyCycles = settings.LatencyCycles;
        this.sectorCount = sectors;
        this.stream = stream;
    }

    public void BeginCommand(ushort command, Memory<byte> mailbox, IExpansionCardCommand completion)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(completion);

        if (mailbox.Length != ExpansionCardApi.MailboxSize)
        {
            throw new ArgumentException(
                $"Mailbox must be exactly {ExpansionCardApi.MailboxSize} bytes.",
                nameof(mailbox));
        }

        if (_pendingCommand is not null)
        {
            throw new InvalidOperationException("The disk card already has a command in progress.");
        }

        execute(command, mailbox.Span);

        _pendingCommand = completion;
        _remainingCycles = latencyCycles;

        if (_remainingCycles == 0)
        {
            CompletePendingCommand();
        }
    }

    public void AdvanceCycles(ulong cycles)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_pendingCommand is null)
        {
            return;
        }

        if (cycles < _remainingCycles)
        {
            _remainingCycles -= cycles;
            return;
        }

        CompletePendingCommand();
    }

    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancelPendingCommand();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CancelPendingCommand();
        stream.Dispose();
        _disposed = true;
    }

    private void execute(ushort command, Span<byte> mailbox)
    {
        switch (command)
        {
            case DiskCardProtocol.CommandIdentify:
                writeIdentify(mailbox);
                break;
            case DiskCardProtocol.CommandRead:
                executeRead(mailbox);
                break;
            case DiskCardProtocol.CommandWrite:
                executeWrite(mailbox);
                break;
            default:
                writeError(mailbox, DiskCardProtocol.StatusUnknownCommand);
                break;
        }
    }

    private void executeRead(Span<byte> mailbox)
    {
        uint lba = readLba(mailbox);
        Span<byte> data = mailbox.Slice(DiskCardProtocol.DataOffset, DiskCardProtocol.SectorSize);

        if (lba >= sectorCount)
        {
            writeError(mailbox, DiskCardProtocol.StatusLbaOutOfRange);
            return;
        }

        try
        {
            stream.Seek((long)lba * DiskCardProtocol.SectorSize, SeekOrigin.Begin);
            int transferred = 0;
            while (transferred < DiskCardProtocol.SectorSize)
            {
                int read = stream.Read(data[transferred..]);
                if (read == 0)
                {
                    throw new EndOfStreamException("Unexpected end of disk image.");
                }
                transferred += read;
            }

            writeStatus(mailbox, DiskCardProtocol.StatusOk);
        }
        catch (Exception error) when (error is IOException or EndOfStreamException or NotSupportedException)
        {
            writeError(mailbox, DiskCardProtocol.StatusHostIoError);
        }
    }

    private void executeWrite(Span<byte> mailbox)
    {
        if (readOnly)
        {
            writeError(mailbox, DiskCardProtocol.StatusWriteProtected);
            return;
        }

        uint lba = readLba(mailbox);
        Span<byte> data = mailbox.Slice(DiskCardProtocol.DataOffset, DiskCardProtocol.SectorSize);

        if (lba >= sectorCount)
        {
            writeError(mailbox, DiskCardProtocol.StatusLbaOutOfRange);
            return;
        }

        try
        {
            stream.Seek((long)lba * DiskCardProtocol.SectorSize, SeekOrigin.Begin);
            stream.Write(data);
            stream.Flush();
            writeStatus(mailbox, DiskCardProtocol.StatusOk);
        }
        catch (Exception error) when (error is IOException or NotSupportedException)
        {
            writeError(mailbox, DiskCardProtocol.StatusHostIoError);
        }
    }

    private void writeIdentify(Span<byte> mailbox)
    {
        Span<byte> body = mailbox;
        DiskCardProtocol.MagicBytes.CopyTo(body.Slice(DiskCardProtocol.MagicOffset));
        writeUInt16(body.Slice(DiskCardProtocol.VersionOffset), DiskCardProtocol.ProtocolVersion);
        writeUInt16(body.Slice(DiskCardProtocol.SectorSizeOffset), (ushort)DiskCardProtocol.SectorSize);
        writeUInt32(body.Slice(DiskCardProtocol.SectorCountOffset), sectorCount);
        writeUInt16(body.Slice(DiskCardProtocol.FlagsOffset), readOnly ? DiskCardProtocol.FlagReadOnly : (ushort)0);
        writeStatus(mailbox, DiskCardProtocol.StatusOk);
    }

    private static uint readLba(Span<byte> mailbox) =>
        readUInt32(mailbox.Slice(DiskCardProtocol.LbaOffset, 4));

    private static void writeStatus(Span<byte> mailbox, ushort status) =>
        writeUInt16(mailbox.Slice(DiskCardProtocol.StatusOffset, 2), status);

    private static void writeError(Span<byte> mailbox, ushort status)
    {
        writeStatus(mailbox, status);
        mailbox.Slice(DiskCardProtocol.DataOffset, DiskCardProtocol.SectorSize).Clear();
    }

    private static ushort readUInt16(ReadOnlySpan<byte> span) =>
        (ushort)((span[0] << 8) | span[1]);

    private static uint readUInt32(ReadOnlySpan<byte> span) =>
        ((uint)span[0] << 24) | ((uint)span[1] << 16) | ((uint)span[2] << 8) | span[3];

    private static void writeUInt16(Span<byte> span, ushort value)
    {
        span[0] = (byte)(value >> 8);
        span[1] = (byte)value;
    }

    private static void writeUInt32(Span<byte> span, uint value)
    {
        span[0] = (byte)(value >> 24);
        span[1] = (byte)(value >> 16);
        span[2] = (byte)(value >> 8);
        span[3] = (byte)value;
    }

    private void CompletePendingCommand()
    {
        IExpansionCardCommand completion = _pendingCommand!;
        CancelPendingCommand();
        completion.Complete();
    }

    private void CancelPendingCommand()
    {
        _pendingCommand = null;
        _remainingCycles = 0;
    }
}
