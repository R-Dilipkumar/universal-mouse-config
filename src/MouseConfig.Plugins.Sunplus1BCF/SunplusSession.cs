namespace MouseConfig.Plugins.Sunplus1BCF;

/// <summary>Sends and receives 8-byte HID feature reports (report ID 4). The real one talks to COL04; tests use a fake.</summary>
public interface IFeatureChannel
{
    void SetFeature(byte[] report);
    byte[] GetFeature();
}

public sealed class SunplusProtocolException(string message) : Exception(message);

/// <summary>
/// One configuration session with the mouse, following the reference tool's tested order:
/// 0x01 handshake, 0x03 identity, 0x04 + 0x05×43 read, then always 0x08 and 0x02 once the handshake was answered.
/// Only the GET is retried; a command is never sent twice.
/// </summary>
public sealed class SunplusSession
{
    public const byte ReportId = 0x04;
    public const int FrameBytes = 8;
    private const byte StatusDone = 0xCC;
    private const byte StatusNotReady = 0x00;
    private const int MaxGetTries = 8;
    /// <summary>The identity the reference tool was tested with: reading and (later) writing allowed.</summary>
    public static readonly byte[] TestedIdentity = [0xAA, 0x0C, 0xFD];

    /// <summary>
    /// Identities seen on real mice that answer the same handshake but weren't tested by the reference tool.
    /// Reading approved by the user on 2026-11-09 for their Redgear (AA0EFD); writing only when the caller says the user OKed it.
    /// </summary>
    public static readonly byte[][] UntestedIdentities = [[0xAA, 0x0E, 0xFD]];

    /// <summary>True when the identity is the tested one, so writes may be offered.</summary>
    public bool IsTestedIdentity => Identity is not null && Identity.AsSpan().SequenceEqual(TestedIdentity);

    private readonly IFeatureChannel _channel;
    private readonly Action<int> _sleepMs;

    public SunplusSession(IFeatureChannel channel, Action<int>? sleepMs = null)
    {
        _channel = channel;
        _sleepMs = sleepMs ?? Thread.Sleep;
    }

    /// <summary>The identity bytes the mouse answered with (bytes 1–3 of the 0x03 reply), once known.</summary>
    public byte[]? Identity { get; private set; }

    public SunplusConfig ReadConfig() => InSession(allowUntestedIdentity: true, () => ReadBlock());

    /// <summary>
    /// Writes the writable areas that differ between the mouse's current block and <paramref name="wanted"/>.
    /// The block is read first and must still equal <paramref name="expectedCurrent"/>, so nothing changed behind our back.
    /// Order as the reference tool: 01, 03, 04, 05×43, then 06 + 07s per changed area, then 08, 02.
    /// </summary>
    public void WriteConfig(SunplusConfig expectedCurrent, SunplusConfig wanted, bool allowUntestedIdentity)
    {
        InSession(allowUntestedIdentity, () =>
        {
            SunplusConfig current = ReadBlock();
            if (current.DifferencesFrom(expectedCurrent).Count > 0)
            {
                throw new SunplusProtocolException("The mouse's settings changed since they were last read. Read them again first.");
            }
            foreach ((int address, int length) in SunplusConfig.WritableRegions)
            {
                byte[] bytes = wanted.Data[address..(address + length)];
                if (!bytes.AsSpan().SequenceEqual(current.Data.AsSpan(address, length)))
                {
                    Write(address, bytes);
                }
            }
            return current;
        });
    }

    private T InSession<T>(bool allowUntestedIdentity, Func<T> work)
    {
        byte[] handshake = Exchange(0x01); // if this throws, the mouse never entered config mode: nothing to close
        bool finished = false;
        try
        {
            if (handshake[1] != 0xA6)
            {
                throw new SunplusProtocolException($"Handshake answer was 0x{handshake[1]:X2}, expected 0xA6. This mouse uses a different protocol.");
            }
            byte[] identity = Exchange(0x03);
            Identity = identity[1..4];
            bool known = IsTestedIdentity || UntestedIdentities.Any(id => Identity.AsSpan().SequenceEqual(id));
            if (!known)
            {
                throw new SunplusProtocolException(
                    $"Identity is {Convert.ToHexString(Identity)}, expected {Convert.ToHexString(TestedIdentity)}. Unsupported model: hardware settings stay off.");
            }
            if (!IsTestedIdentity && !allowUntestedIdentity)
            {
                throw new SunplusProtocolException(
                    $"Identity {Convert.ToHexString(Identity)} isn't the tested model. Changing it needs your OK first.");
            }
            T result = work();
            finished = true;
            return result;
        }
        finally
        {
            Close(throwOnError: finished);
        }
    }

    private SunplusConfig ReadBlock() => new(Read(0, SunplusConfig.Size));

    /// <summary>0x06 then one 0x07 per 6 bytes (last one padded with 0xFF). Each reply must echo the 6 bytes sent.</summary>
    private void Write(int address, byte[] bytes)
    {
        if (address < 0 || address + bytes.Length > SunplusConfig.Size)
        {
            throw new ArgumentOutOfRangeException(nameof(address), "Writes must stay inside bytes 0–254.");
        }
        Exchange(0x06, (byte)(address & 0xFF), (byte)(address >> 8), (byte)(bytes.Length >> 8), (byte)(bytes.Length & 0xFF));
        for (int start = 0; start < bytes.Length; start += 6)
        {
            byte[] chunk = Enumerable.Range(start, 6).Select(i => i < bytes.Length ? bytes[i] : (byte)0xFF).ToArray();
            byte[] reply = Exchange(0x07, chunk);
            if (!reply.AsSpan(1, 6).SequenceEqual(chunk))
            {
                throw new SunplusProtocolException(
                    $"The mouse didn't echo the data at byte {address + start}: sent {Convert.ToHexString(chunk)}, got {Convert.ToHexString(reply[1..7])}.");
            }
        }
    }

    /// <summary>0x08 then 0x02 (leave config mode). 0x02 is sent even if 0x08 fails. A close error never hides an earlier error.</summary>
    private void Close(bool throwOnError)
    {
        Exception? error = null;
        try
        {
            Exchange(0x08);
        }
        catch (Exception e)
        {
            error = e;
        }
        try
        {
            Exchange(0x02);
        }
        catch (Exception e)
        {
            error ??= e;
        }
        if (error is not null && throwOnError)
        {
            throw new SunplusProtocolException($"Closing the settings session failed: {error.Message}");
        }
    }

    private byte[] Read(int address, int size)
    {
        Exchange(0x04, (byte)(address & 0xFF), (byte)(address >> 8), (byte)(size >> 8), (byte)(size & 0xFF));
        var data = new List<byte>(size + 6);
        int chunks = (size + 5) / 6;
        for (int i = 0; i < chunks; i++)
        {
            byte[] reply = Exchange(0x05);
            data.AddRange(reply[1..7]);
        }
        return data.Take(size).ToArray();
    }

    /// <summary>SET once, wait 15 ms, then GET up to 8 times 15 ms apart until the mouse says done (0xCC).</summary>
    private byte[] Exchange(byte command, params byte[] args)
    {
        var frame = new byte[FrameBytes];
        frame[0] = ReportId;
        frame[1] = command;
        args.CopyTo(frame, 2);
        _channel.SetFeature(frame);
        for (int attempt = 0; attempt < MaxGetTries; attempt++)
        {
            _sleepMs(15);
            byte[] reply = _channel.GetFeature();
            if (reply.Length != FrameBytes || reply[0] != ReportId)
            {
                throw new SunplusProtocolException($"Command 0x{command:X2}: bad answer {Convert.ToHexString(reply)}.");
            }
            if (reply[7] == StatusDone)
            {
                return reply;
            }
            if (reply[7] != StatusNotReady)
            {
                throw new SunplusProtocolException($"Command 0x{command:X2}: unexpected status 0x{reply[7]:X2} ({Convert.ToHexString(reply)}).");
            }
        }
        throw new SunplusProtocolException($"Command 0x{command:X2}: the mouse didn't answer after {MaxGetTries} tries.");
    }
}
