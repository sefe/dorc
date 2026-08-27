---
name: SPEC-S-008 - Windows worker installer wiring
description: Install, configure, and start the Windows worker with the same MSI that configures the primary API.
type: spec
status: READY FOR REVIEW
---

# SPEC-S-008 - Windows worker installer wiring

## Scope

S-008 makes the worker topology deployable as one release train. The existing DOrc
MSI installs the worker payload, registers the Windows service, provisions the shared
loopback secret and service-account settings, enables the primary worker client, and
writes an authentication scheme accepted by the S-007 startup guard.

## Installer contract

- `InstallerPayload.proj` publishes `Dorc.Api.WindowsWorker`.
- `ApiWindowsWorker.wxs` installs and controls the worker service.
- The worker binds to loopback and receives the same shared secret written into the
  primary API's `WindowsWorker` configuration.
- Install and upgrade paths set `WindowsWorker:Enabled=true`.
- `RequestApi.wxs` always writes a non-Negotiate authentication scheme supported by
  the primary API.
- Uninstall removes the service registration and payload without deleting external
  customer data.

## Release boundary

S-004 through S-008 are independently reviewable code changes but form one release
train: no release may include a primary API that routes an operation to the worker
without also including this installer step.

## Verification

The PR build publishes both API payloads and builds the WiX project. Installer review
must confirm new install, upgrade, repair, and uninstall behavior for both production
and non-production conditions.
