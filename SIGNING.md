# Release signing

## Current baseline

Every CI-built release archive receives:

- a SHA-256 checksum; and
- a GitHub artifact provenance attestation generated with `actions/attest`.

For a public repository, contributors can verify provenance with:

```powershell
gh attestation verify .\LegionGo2ControllerBridge-win-x64.zip -R OWNER/REPOSITORY
```

This establishes which repository, workflow, commit, and build environment
produced the archive. It does not create a Windows Authenticode signature or
remove SmartScreen warnings by itself.

## Planned Authenticode signing

Microsoft Azure Artifact Signing Public Trust is the preferred design because
it keeps certificate key material in a managed service and supports GitHub
Actions authentication through OIDC. The release workflow should eventually:

1. Publish the self-contained executable into a staging directory.
2. Authenticate to Azure using a federated GitHub identity.
3. Sign `LegionGo2ControllerBridge.exe` with
   `azure/artifact-signing-action@v1` and an RFC 3161 timestamp.
4. Verify the Authenticode signature and certificate chain.
5. Package the signed executable, generate its checksum, and attest the final
   ZIP—not the unsigned intermediate.

Required external setup:

- an Azure Artifact Signing account;
- completed individual or organization identity validation;
- a Public Trust certificate profile;
- the `Artifact Signing Certificate Profile Signer` role for the federated
  identity; and
- GitHub environment variables for the signing endpoint, account, profile,
  Azure tenant, and client IDs.

Do not store a PFX or long-lived Azure client secret in the repository. Until
Artifact Signing is configured, releases must be labeled unsigned prereleases
and accompanied by checksums and provenance attestations.
