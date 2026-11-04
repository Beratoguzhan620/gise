using System.Buffers.Text;
using System.Security.Cryptography;
using Gise.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Gise.Infrastructure.Tickets;

/// <summary>What a valid code says: which ticket, for which event.</summary>
public sealed record TicketCode(Guid TicketId, Guid EventId);

/// <summary>
/// The text inside a ticket's QR code: <c>GS1.&lt;payload&gt;.&lt;signature&gt;</c>, both base64url. The payload is
/// 37 bytes — a version byte, the first four bytes of the key's id, the ticket id and the event id — and the
/// signature is ECDSA P-256 over SHA-256 of those bytes, as 64 raw bytes. Gate devices hold only the public key, so
/// they can tell a real ticket from a forged one without a connection; the server checks the same way.
/// </summary>
public sealed class TicketCodes
{
    public const string Prefix = "GS1.";
    private const byte Version = 1;
    private const int PayloadLength = 1 + 4 + 16 + 16;
    private const int SignatureLength = 64;

    private ECDsa? _key;
    private byte[] _keyId = [];

    /// <summary>The public key in SubjectPublicKeyInfo form, for gate devices.</summary>
    public byte[] PublicKey { get; private set; } = [];

    /// <summary>Hex of the first four bytes of the SHA-256 of <see cref="PublicKey"/>.</summary>
    public string KeyId => Convert.ToHexStringLower(_keyId);

    public bool IsLoaded => _key is not null;

    public string Sign(Guid ticketId, Guid eventId)
    {
        var payload = new byte[PayloadLength];
        payload[0] = Version;
        _keyId.CopyTo(payload, 1);
        ticketId.TryWriteBytes(payload.AsSpan(5), bigEndian: true, out _);
        eventId.TryWriteBytes(payload.AsSpan(21), bigEndian: true, out _);
        var signature = Key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{Prefix}{Base64Url.EncodeToString(payload)}.{Base64Url.EncodeToString(signature)}";
    }

    /// <summary>The ticket a code stands for, or <c>null</c> when it is not a code we signed with the current key.</summary>
    public TicketCode? Read(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 300 || !code.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var parts = code[Prefix.Length..].Trim().Split('.');
        if (parts.Length != 2)
        {
            return null;
        }

        byte[] payload, signature;
        try
        {
            payload = Base64Url.DecodeFromChars(parts[0]);
            signature = Base64Url.DecodeFromChars(parts[1]);
        }
        catch (FormatException)
        {
            return null;
        }

        if (payload.Length != PayloadLength || signature.Length != SignatureLength || payload[0] != Version
            || !payload.AsSpan(1, 4).SequenceEqual(_keyId)
            || !Key.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        {
            return null;
        }

        return new TicketCode(new Guid(payload.AsSpan(5, 16), bigEndian: true), new Guid(payload.AsSpan(21, 16), bigEndian: true));
    }

    public void Load(ECDsa key)
    {
        if (key.KeySize != 256)
        {
            throw new InvalidOperationException("Tickets:SigningKey must be an ECDSA P-256 key.");
        }

        _key = key;
        PublicKey = key.ExportSubjectPublicKeyInfo();
        _keyId = SHA256.HashData(PublicKey)[..4];
    }

    /// <summary>
    /// Loads the configured key; otherwise, when allowed, the one kept in the database, creating it on first start.
    /// Runs once at start-up, after migrations.
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var codes = services.GetRequiredService<TicketCodes>();
        var options = services.GetRequiredService<IOptions<TicketOptions>>().Value;
        var key = ECDsa.Create();
        if (!string.IsNullOrWhiteSpace(options.SigningKey))
        {
            key.ImportFromPem(options.SigningKey);
            codes.Load(key);
            return;
        }

        if (!options.GenerateKeyIfMissing)
        {
            throw new InvalidOperationException(
                "Tickets:SigningKey is not configured. Set an ECDSA P-256 private key in PEM, or Tickets:GenerateKeyIfMissing=true for a local run.");
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GiseDbContext>();
        var stored = await db.TicketKeys.AsNoTracking().OrderBy(k => k.CreatedAt).FirstOrDefaultAsync(ct);
        if (stored is null)
        {
            key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var id = Convert.ToHexStringLower(SHA256.HashData(key.ExportSubjectPublicKeyInfo())[..4]);
            db.TicketKeys.Add(TicketKey.Create(id, key.ExportPkcs8PrivateKey(), services.GetRequiredService<TimeProvider>().GetUtcNow()));
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Another instance created one at the same moment; use the first.
                db.ChangeTracker.Clear();
            }

            stored = await db.TicketKeys.AsNoTracking().OrderBy(k => k.CreatedAt).FirstAsync(ct);
        }

        key.ImportPkcs8PrivateKey(stored.PrivateKey, out _);
        codes.Load(key);
    }

    private ECDsa Key => _key ?? throw new InvalidOperationException("The ticket signing key has not been loaded.");
}
