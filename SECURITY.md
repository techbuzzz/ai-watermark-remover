# Security Policy

## Responsible disclosure

If you discover a security vulnerability in **WatermarkRemover**, please report it
privately so we can investigate and ship a fix before public disclosure.

📧 **Email:** [security@techbuzzz.dev](mailto:security@techbuzzz.dev)
(please replace with the maintainer's actual security contact if this address is
not monitored)

🔐 **Optional:** for sensitive reports, request our PGP key in your first email
and we will reply with it.

**Please do not** file a public GitHub issue, open a discussion, or post about
the vulnerability on social media until we have confirmed a fix or 90 days have
elapsed (whichever is sooner).

### What to include

A good report helps us triage fast:

- A clear description of the vulnerability and its impact.
- A reproducer (command, payload, HTTP request, or minimal test case).
- The affected version (commit SHA or release tag).
- Your environment (OS, .NET runtime version) — only if relevant.
- Whether you intend to publish the issue or request a CVE.

We will acknowledge receipt within **3 business days** and aim to ship a fix or
mitigation within **30 days** for high-severity issues, **90 days** for everything
else. We will coordinate disclosure timing with you.

## Supported versions

Only the latest release receives security fixes. Older releases may receive
fixes at our discretion; please upgrade before reporting an issue.

| Release line | Supported          |
|--------------|--------------------|
| `latest`     | ✅ Active          |
| older        | ❌ Best effort     |

## Hall of fame

We thank the following researchers for responsible disclosures (none so far —
be the first!):

<!-- _No entries yet. Researchers who report a confirmed vulnerability with
     permission to acknowledge will be listed here._ -->

---

## Responsible use

> ⚠️ **Read this before using WatermarkRemover.**

WatermarkRemover is a legitimate tool with many lawful, beneficial uses:
cleaning content **you authored**, normalising corpora for ML training, security
research, forensic analysis of suspicious documents, and so on.

The same capabilities can also be abused. The maintainers do not condone using
WatermarkRemover to:

- ❌ Strip watermarks from third-party content in order to **evade attribution**
  or to misrepresent the provenance of generated media.
- ❌ Bypass content-moderation or copyright-protection controls in violation of
  the relevant platform's terms of service or applicable law.
- ❌ Generate, transform, or distribute content that is illegal in your
  jurisdiction (CSAM, targeted harassment, fraud, malware, etc.).
- ❌ Process documents you do not have the right to modify.

If you are unsure whether your use case is appropriate, consult counsel, or
contact us at [security@techbuzzz.dev](mailto:security@techbuzzz.dev) for
guidance.

### What the tool will and will not do

- ✅ Strips **invisible Unicode steganography** (zero-width chars, bidi controls,
  variation selectors) from text.
- ✅ Strips **byte-level metadata** (EXIF, XMP, IPTC, C2PA, ICC) from
  supported file formats, and removes PDF/DOCX/HTML document properties.
- ✅ Removes **visual watermarks** (logos, text overlays) from images via LaMa
  inpainting. Auto-detection is heuristic.
- ✅ Detects and reports (without removing) AI-provenance markers in text and
  markdown for forensic purposes.
- ❌ **Does not** crack, reverse, or extract the private key of any vendor
  watermark. Vendor detectors are heuristic, not oracle attacks.
- ❌ **Does not** bypass DRM, encryption, or access-control systems. If a file
  is encrypted, it stays encrypted.
- ❌ **Does not** exfiltrate or transmit your files anywhere — everything runs
  locally on your machine (or in your own container).

### The HTTP server

The built-in `serve` command binds to `0.0.0.0` by default so it can accept
connections from outside `localhost`. **Before exposing the port**:

- Always pass `--api-key <random>` in production. Without an API key, every
  endpoint is open.
- Terminate the service with TLS at a reverse proxy (Caddy, nginx, Traefik, …).
  The server speaks plain HTTP only.
- Restrict the source IP range with a firewall or `--host 127.0.0.1` if the
  service only needs to be reachable from the same host.
- The default rate-limit is 100 req/min/IP; tune via reverse-proxy or by
  fronting the API with a managed gateway (Kong, Cloudflare, etc.) for higher
  traffic.

### Docker images

The published `watermarkremover` images run as a **dedicated non-root user**
(`wr`, uid:gid `10001`) and ship with a read-only root filesystem friendly
default. The bundled `HEALTHCHECK` is intentionally benign. If you find a way
to escalate privileges or escape the container, please report it privately.

## Threat model

We consider the following in scope for security fixes:

| In scope                                                                 | Out of scope                                              |
|--------------------------------------------------------------------------|-----------------------------------------------------------|
| Vulnerabilities in the on-disk binary or hosted HTTP API                 | Vulnerabilities in third-party NuGet dependencies (file upstream) |
| Container-escape / privilege-escalation in the published Docker image    | Attacks that require the operator to disable auth, run as root, or expose the port to the public internet |
| Sensitive data leakage in logs / error messages                          | Social engineering or phishing of maintainers              |
| Path traversal in the `clean-file` / `clean-image` commands              | Denial-of-service by uploading huge files (rate limit is a hint) |
| ZIP-bomb / decompression attacks via uploaded files                      |                                                           |

## Security automation

- **Dependabot** keeps NuGet and GitHub Actions dependencies current
  (see [`.github/dependabot.yml`](./.github/dependabot.yml)).
- **Build-and-test** workflow runs on every push / PR to `main` across Linux
  + Windows runners.
- **TreatWarningsAsErrors** is enabled in all `src/` projects, so any code
  smell that affects the security analyzer (CA rules) is caught at build time.

Thanks for keeping WatermarkRemover and its users safe. 🙏
