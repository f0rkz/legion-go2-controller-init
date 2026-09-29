# Code signing policy

Free code signing is intended to be provided by SignPath.io, with a certificate
provided by SignPath Foundation.

## Source and build integrity

- Repository: https://github.com/f0rkz/legion-go2-controller-init
- Release binaries are built from this repository by GitHub Actions.
- Release signing must use origin verification against this repository.
- Only tagged release builds may request production signing.
- Pull-request contributions require review before merge.
- Every production signing request requires explicit maintainer approval.
- The project never signs Lenovo binaries or any artifact not built from this
  repository's source.

## Roles

- Committer and reviewer: [f0rkz](https://github.com/f0rkz)
- Release approver: [f0rkz](https://github.com/f0rkz)

These roles will be expanded when additional maintainers join the project.

## Privacy

See [PRIVACY.md](PRIVACY.md). The application does not transmit telemetry or
other information to networked systems.
