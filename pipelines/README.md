# Azure DevOps pipelines

CI runs on GitHub-hosted runners. What is left in this folder is what has not
been moved yet, not what is preferred.

## Retired

| Was | Replaced by |
| --- | --- |
| `dorc-build.yml` | `.github/workflows/release.yml` |
| `dorc-kafka.yml` | `.github/workflows/kafka-integration.yml` |
| `ApplyVersionToAssemblies.ps1` | the `Version AssemblyInfo files` step in `release.yml`, which carries the same regex inline |

Both ran on the `TRADING-DOTNET-03` self-hosted pool. `dorc-kafka.yml` also
only triggered on `feat/kafka-migration`, and its Kafka + Karapace stack now
comes up under Podman on `ubuntu-latest` instead of needing Podman on the
agent.

**Deleting the YAML does not delete the pipeline.** The Azure DevOps pipeline
definitions still point at these paths and will fail on their next trigger
until someone disables or deletes them in Azure DevOps.

## Still here

`dorc-cmdlet.yml` publishes the `DOrc.Cmdlet` PowerShell module, and it has no
GitHub equivalent — `release.yml` copies the module's files into the build
artifact but never runs `Publish-Module`. It cannot simply be moved either: it
publishes to an on-premises ProGet feed at `https://proget:8143` and signs the
module, and neither is reachable from a GitHub-hosted runner. Retiring it needs
a decision about where the module should be published to, so it stays until
that decision is made.

`NuGet.Config` is not an Azure DevOps file. `release.yml` passes it to
`dotnet restore`, so it stays regardless of what happens to the pipelines.
