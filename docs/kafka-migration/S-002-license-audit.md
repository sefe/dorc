# S-002 — NuGet Transitive-Closure License Audit

| Field | Value |
|---|---|
| **Status** | COMPLETE |
| **Author** | Claude (Opus 4.6) |
| **Date** | 2026-04-14 |
| **Governing Spec** | `SPEC-S-002-Kafka-Client-Layer.md` §4 AT-6, R-7 |
| **Governing HLPS** | `HLPS-Kafka-Migration.md` C-1 (OSI deps only), R-7 (license audit) |

## Verdict

**All packages resolve to OSI-approved licenses.** No non-OSI finding; S-002's R-7 acceptance gate is satisfied.

## Scope

Per AT-6, the audit covers:

- `Dorc.Kafka.Client` (the S-002 client-layer assembly).
- `Dorc.Kafka.Client.Tests` (unit tests introduced in S-002).
- `Dorc.Kafka.Client.IntegrationTests` (integration tests introduced in S-002).
- `Dorc.Kafka.SmokeTests` (re-included from S-001 per AT-6; S-002 may add packages to it in future).

Method: `dotnet list package --include-transitive` on each csproj, cross-referenced to SPDX license identifiers read from each package's `.nuspec` under `%USERPROFILE%\.nuget\packages\`.

OSI-approved licenses encountered in this closure: **MIT**, **Apache-2.0**, **BSD-2-Clause** (all on the OSI-approved list at <https://opensource.org/licenses>).

## Findings

### Direct + transitive — `Dorc.Kafka.Client`

| Package | Version | License | Scope |
|---|---|---|---|
| Chr.Avro | 10.11.1 | MIT | direct |
| Chr.Avro.Confluent | 10.11.1 | MIT | direct |
| Confluent.Kafka | 2.11.1 | Apache-2.0 | direct |
| Confluent.SchemaRegistry | 2.11.1 | Apache-2.0 | direct |
| Confluent.SchemaRegistry.Serdes.Avro | 2.11.1 | Apache-2.0 | direct |
| Microsoft.Extensions.Configuration.Abstractions | 8.0.0 | MIT | direct |
| Microsoft.Extensions.Configuration.Binder | 8.0.2 | MIT | direct |
| Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.2 | MIT | direct |
| Microsoft.Extensions.Logging.Abstractions | 8.0.2 | MIT | direct |
| Microsoft.Extensions.Options | 8.0.2 | MIT | direct |
| Microsoft.Extensions.Options.ConfigurationExtensions | 8.0.0 | MIT | direct |
| Apache.Avro | 1.12.0 | Apache-2.0 | transitive |
| Chr.Avro.Binary | 10.11.1 | MIT | transitive |
| Chr.Avro.Json | 10.11.1 | MIT | transitive |
| librdkafka.redist | 2.11.1 | BSD-2-Clause (librdkafka core) + bundled permissive third-party (zlib, zstd, OpenSSL — see `LICENSES.txt` in the package); all OSI-approved | transitive |
| Microsoft.CSharp | 4.7.0 | MIT | transitive |
| Microsoft.Extensions.Caching.Abstractions | 8.0.0 | MIT | transitive |
| Microsoft.Extensions.Caching.Memory | 8.0.1 | MIT | transitive |
| Microsoft.Extensions.Primitives | 8.0.0 | MIT | transitive |
| Microsoft.NETCore.Platforms | 1.1.0 | MIT | transitive |
| Microsoft.NETCore.Targets | 1.1.0 | MIT | transitive |
| Newtonsoft.Json | 13.0.1 | MIT | transitive |
| System.CodeDom | 8.0.0 | MIT | transitive |
| System.Collections.Immutable | 5.0.0 | MIT | transitive |
| System.ComponentModel.Annotations | 5.0.0 | MIT | transitive |
| System.Globalization | 4.3.0 | MIT | transitive |
| System.IO | 4.3.0 | MIT | transitive |
| System.Reflection | 4.3.0 | MIT | transitive |
| System.Reflection.Primitives | 4.3.0 | MIT | transitive |
| System.Resources.ResourceManager | 4.3.0 | MIT | transitive |
| System.Runtime | 4.3.0 | MIT | transitive |
| System.Runtime.Extensions | 4.3.0 | MIT | transitive |
| System.Runtime.Numerics | 4.3.0 | MIT | transitive |
| System.Text.Encoding | 4.3.0 | MIT | transitive |
| System.Threading.Tasks | 4.3.0 | MIT | transitive |

### Test-only additions — `Dorc.Kafka.Client.Tests`, `Dorc.Kafka.Client.IntegrationTests`, `Dorc.Kafka.SmokeTests`

Packages below extend the closure only through the test projects; the production `Dorc.Kafka.Client` assembly does not depend on them.

| Package | Version (where pinned) | License |
|---|---|---|
| Microsoft.NET.Test.Sdk | 18.0.0 | MIT |
| MSTest.TestAdapter | 4.0.1 | MIT |
| MSTest.TestFramework | 4.0.1 | MIT |
| MSTest.Analyzers | 4.0.1 | MIT |
| coverlet.collector | 6.0.4 | MIT |
| Microsoft.Extensions.Configuration | 8.0.0 | MIT |
| Microsoft.Extensions.Configuration.Json | 8.0.1 | MIT |
| Microsoft.Extensions.Configuration.FileExtensions | 8.0.1 | MIT |
| Microsoft.Extensions.DependencyInjection | 8.0.1 | MIT |
| Microsoft.Extensions.Logging | 8.0.1 | MIT |
| Microsoft.Extensions.FileProviders.Abstractions | 8.0.0 | MIT |
| Microsoft.Extensions.FileProviders.Physical | 8.0.0 | MIT |
| Microsoft.Extensions.FileSystemGlobbing | 8.0.0 | MIT |
| Microsoft.Testing.Platform | 2.0.1 | MIT |
| Microsoft.Testing.Platform.MSBuild | 2.0.1 | MIT |
| Microsoft.Testing.Extensions.Telemetry | 2.0.1 | MIT |
| Microsoft.Testing.Extensions.TrxReport.Abstractions | 2.0.1 | MIT |
| Microsoft.Testing.Extensions.VSTestBridge | 2.0.1 | MIT |
| Microsoft.TestPlatform.AdapterUtilities | 18.0.0 | MIT |
| Microsoft.TestPlatform.ObjectModel | 18.0.0 | MIT |
| Microsoft.TestPlatform.TestHost | 18.0.0 | MIT |
| Microsoft.CodeCoverage | 18.0.0 | MIT |
| Microsoft.ApplicationInsights | 2.23.0 | MIT |
| Newtonsoft.Json | 13.0.3 | MIT |
| System.Reflection.Metadata | 8.0.0 | MIT |
| System.Diagnostics.DiagnosticSource | 5.0.0 | MIT |
| System.Collections.Immutable | 8.0.0 | MIT |

## Notes

- **librdkafka.redist (BSD-2-Clause, bundled)** — the package ships the native `librdkafka.dll` (BSD-2-Clause) plus statically-linked dependencies (zlib, zstd, OpenSSL, cyrus-sasl-plain on some platforms). All bundled dependencies are under OSI-approved permissive licenses per the upstream `LICENSES.txt`. This is the only native-binary transitive dependency in scope.
- **Confluent.* packages** are under Apache-2.0, which is OSI-approved.
- **Chr.Avro family** is MIT; matches HLPS C-10's "Chr.Avro (MIT)" note.
- No GPL / LGPL / AGPL / proprietary-EULA packages observed.
- Version pinning: `Chr.Avro.Confluent` is held at **10.11.1** (not the latest 10.13.x) to stay compatible with `Confluent.Kafka 2.11.1` per HLPS C-10's version pin. See commit history for the compatibility rationale.

## Re-run Procedure

To regenerate this audit after a package-set change in S-002's scope:

1. `dotnet list <csproj> package --include-transitive` for each in-scope csproj.
2. For each package, read `%USERPROFILE%\.nuget\packages\<name>\<version>\<name>.nuspec` and extract the `<license>` or `<licenseUrl>` element.
3. Cross-reference to the OSI-approved list at <https://opensource.org/licenses>.
4. Record findings here; flag any non-OSI entry as **FAIL** and escalate per HLPS R-7.
