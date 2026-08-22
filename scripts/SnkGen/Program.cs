// Generates a valid .snk (strong-name key) file from scratch.
//
// Layout: PUBLICKEYBLOB + PRIVATEKEYBLOB concatenated, no header.
// This is the layout that the .NET compiler's Csc task expects
// when reading a .snk via <AssemblyOriginatorKeyFile>. The
// PUBLICKEYBLOB at the start has its own BLOBHEADER which the
// compiler reads to extract the public key.
//
// Uses the legacy RSACryptoServiceProvider which natively produces
// the CSP PRIVATEKEYBLOB (ExportCspBlob(true)).
//
// Usage: snkgen <output.snk> [keySizeInBits]
//   keySizeInBits: 1024, 2048, or 4096. Default 2048.

using System.Security.Cryptography;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: snkgen <output.snk> [keySizeInBits]");
    return 1;
}

string outPath = args[0];
int requestedKeySize = args.Length >= 2 && int.TryParse(args[1], out var k) ? k : 2048;

#pragma warning disable SYSLIB0001 // RSACryptoServiceProvider is the only API that natively produces the .snk CSP format.
using var csp = new RSACryptoServiceProvider(requestedKeySize);
#pragma warning restore SYSLIB0001

int keySize = csp.KeySize;
Console.WriteLine($"Generated {keySize}-bit RSA key via RSACryptoServiceProvider.");

// Export the private CSP blob (full PRIVATEKEYBLOB: BLOBHEADER +
// RSAPUBKEY + modulus + private parameters).
byte[] privateCspBlob = csp.ExportCspBlob(includePrivateParameters: true);

// Build the public blob by copying the header (BLOBHEADER + RSAPUBKEY)
// + modulus from the private blob, then patching bType, aiKeyAlg, and
// the magic. The .NET strong-name code path requires aiKeyAlg =
// 0x00002400 (CALG_RSA_SIGN) in the PUBLICKEYBLOB, while the
// private blob uses 0x0000A400 (CALG_RSA_KEYX).
int modSize = keySize / 8;
int pubHeaderSize = 8 + 4 + 4 + 4; // BLOBHEADER + magic + bitlen + pubexp
byte[] publicCspBlob = new byte[pubHeaderSize + modSize];
Array.Copy(privateCspBlob, 0, publicCspBlob, 0, pubHeaderSize + modSize);
publicCspBlob[0] = 0x06;  // PUBLICKEYBLOB
publicCspBlob[4] = 0x00;
publicCspBlob[5] = 0x24;  // aiKeyAlg = 0x00002400 (CALG_RSA_SIGN)
publicCspBlob[6] = 0x00;
publicCspBlob[7] = 0x00;
publicCspBlob[8] = 0x52;  // 'R' ("RSA1")
publicCspBlob[9] = 0x53;  // 'S'
publicCspBlob[10] = 0x41; // 'A'
publicCspBlob[11] = 0x31; // '1'

// .snk file: PUBLICKEYBLOB + PRIVATEKEYBLOB concatenated.
using var fs = File.Create(outPath);
fs.Write(publicCspBlob);
fs.Write(privateCspBlob);

long totalLen = publicCspBlob.Length + privateCspBlob.Length;
Console.WriteLine($"Wrote {totalLen}-byte .snk to {outPath}");
return 0;
