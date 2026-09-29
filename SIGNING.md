# Release signing

## Current baseline

Every CI-built release archive receives a SHA-256 checksum. When the repository
is public, CI also generates a GitHub artifact provenance attestation with
`actions/attest`.

GitHub Free supports attestations for public repositories, but not user-owned
private repositories. The workflow skips attestation while the repository is
private and enables it automatically after the repository becomes public.

Consumers can verify an attested public build with:

```powershell
gh attestation verify .\LegionGo2ControllerBridge-win-x64.zip -R f0rkz/legion-go2-controller-init
```

Provenance establishes which repository, workflow, commit, and build
environment produced an archive. It does not create a Windows Authenticode
signature or remove SmartScreen warnings by itself.

## Preferred Authenticode path: SignPath Foundation

The project intends to apply to SignPath Foundation for its free open-source
code-signing program. SignPath provides qualifying projects with Authenticode
signatures under a certificate issued to SignPath Foundation, keeps private key
material in an HSM, and verifies that submitted binaries originated from the
declared source repository and trusted build system.

Before applying, this project must:

- remain entirely open source under the MIT license;
- make the repository and build history public;
- publish an initial unsigned release in the form intended for signing;
- require MFA for maintainers and repository access;
- document functionality, privacy, signing policy, and project roles;
- keep release builds reproducible through GitHub Actions; and
- accept manual approval and origin verification for release signing.

SignPath Foundation, rather than an individual maintainer, appears as the
certificate publisher. Approval is discretionary, and new projects may need to
establish maintenance history and reputation before acceptance.

See [CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md) and
[PRIVACY.md](PRIVACY.md).

## Other options

- **Microsoft Store/MSIX:** Microsoft signs Store-distributed MSIX packages at
  no additional certificate cost. This application would need packaging and
  lifecycle changes, and Store policy review, before that route is viable.
- **Unsigned ZIP plus attestations:** Free and transparent, but users may still
  see SmartScreen warnings. This is acceptable for alpha testing.
- **Self-signed Authenticode:** Useful only for local development or managed
  fleets where the root certificate can be distributed out of band. Requiring
  community users to trust a custom root is not acceptable release UX.
- **Commercial certificate or Azure Artifact Signing:** Technically sound but
  rejected for now because maintainers should not pay recurring signing fees to
  distribute a workaround for Lenovo software defects.
