# Build time — what was measured

Status: RECORD (measurement, not a plan)

G-3 of the HLPS asks whether decomposing `Setup.Dorc.msi` made the build
faster. Everything here is a measurement, with the run it came from, so the
numbers can be re-checked rather than believed.

Caveat that applies throughout: these are **single samples** on GitHub-hosted
`windows-latest` runners. Runner-to-runner variance on shared hardware is real
and is not quantified below. Differences under ~15 seconds should not be read
as signal.

## Job wall-clock

| Run | Commit | Cache | Build solution | Run tests | Job |
| --- | --- | --- | --- | --- | --- |
| 30847083209 (pre-split) | — | warm | 2m 31s | 35s | — |
| 30889337127 (post-split) | `4462786` | warm | 2m 20s | 1m 58s | **14m 27s** |
| 30905477688 | `f7fb35b` | **cold** | 2m 22s | 1m 49s | 10m 51s |
| 30909238019 | `21def91` | warm | 2m 14s | **44s** | 10m 13s |

Run 30889337127 was reported as ~9m 10s at the time. That was wrong: 9m 10s is
the elapsed time to the last visible build step, and the job then spent a
further 5m 11s in the post-job NuGet cache save. The job took 14m 27s.

Run 30905477688 is not comparable on total time. Both cache steps missed:

```
Cache not found for input keys: Windows-nuget-e9d19eca…, Windows-nuget-
Cache not found for input keys: Windows-wix-6.0.1
```

The NuGet miss was caused by the change that was meant to shrink that cache.
`actions/cache` derives a *cache version* from the declared paths, so adding
`!~/.nuget/packages/**/*.nupkg` made every existing entry unreachable —
including via `restore-keys`, which cannot cross a version boundary. Restore
went from 16s to 2m 02s as a result. That is a one-off cost, already paid: the
run saved a new entry under the new version, so the next run is warm again.
Whether the trim actually helps is still unmeasured and needs a warm run to
answer.

## Does the split let the packages build concurrently? (U-17)

Yes. From the binlog of run 30905477688 — all five installers start within two
seconds of each other, and `Build solution` ends when the slowest one does.

| Project | Wall-clock | of which `WixBuild` |
| --- | --- | --- |
| Setup.Dorc.Monitors | 139.6s | **116.7s** |
| Setup.Dorc.Api | 97.2s | 69.1s |
| Setup.Dorc.Cli | 87.4s | — |
| Setup.Acceptance | 80.8s | — |
| Setup.Dorc.Web | 7.5s | 6.7s |

`Build solution` took 141.5s in total, and `Setup.Dorc.Monitors` finished 2ms
before the solution did. So:

- The four packages **do** build in parallel — the split did not multiply the
  MSI authoring cost, which is why `Build solution` barely moved (2m 31s → 2m
  20s → 2m 22s).
- The step is now floored by a single package. Nothing else in the solution
  matters to its duration until `Setup.Dorc.Monitors` gets faster than 140s.
- The remaining cost is inside `WixBuild` — cabbing the payload — not in
  harvesting or in the project references. `Setup.Dorc.Monitors` spends 116.7s
  of its 139.6s there; `ResolveProjectReferences` accounts for 19.8s.

**G-3 verdict: the split neither helped nor hurt build time.** Independent
deployability (U-2) was the objective and is unaffected by this; the honest
answer on speed is that it is a wash.

## Test time

`Run tests` grew from 35s to ~1m 49s, and that growth is entirely the installer
comparison this work introduced. Per-assembly, from run 30905477688:

| Assembly | Tests | Duration |
| --- | --- | --- |
| Dorc.Installer.Tests | 3 | **1m 05s** |
| Dorc.Kafka.Lock.Tests | 66 | 5s |
| Dorc.Kafka.Client.Tests | 56 | 5s |
| Dorc.Api.Tests | 240 | 2s |
| everything else | 312 | <2s each |

Memoising the package reads was expected to cut this and bought only 9s,
because the cost was never repeated reads — it was the reads themselves. The
first implementation went through the WindowsInstaller COM API with
`dynamic`, which binds late: one IDispatch round-trip per cell, over roughly
two thousand file rows per package. Replacing it with
`WixToolset.Dtf.WindowsInstaller` — the WiX toolset's own managed wrapper over
msi.dll — moves a whole table across the boundary in one call.

## Setup.Dorc.Monitors — what is actually in it

Measured from the shipped package of run 30905477688 with `msiinfo`:

| Package | File rows | Uncompressed | On disk |
| --- | --- | --- | --- |
| Setup.Dorc.Monitors | 1932 | 572.1 MB | 83.0 MB |
| Setup.Dorc.Cli | 2276 | 631.0 MB | 36.0 MB |
| Setup.Dorc.Api | 445 | 185.3 MB | 54.4 MB |
| Setup.Acceptance | — | — | 25.8 MB |
| Setup.Dorc.Web | 250 | 7.9 MB | 5.0 MB |

Every row lands at a distinct target path, but only ~470 distinct file names
are involved: each of the four payload directories (`Dorc.Monitor`,
`Dorc.Runner`, `Dorc.NetFramework.Runner`, `Dorc.TerraformRunner`) is
harvested twice, once into the Prod tree and once into NonProd.

**That duplication is the design, not waste.** `Product.wxs` has a single
`Feature` at `Level="1"` holding both the `Prod*` and `NonProd*` component
groups, and separate `DeployMonitorServiceProd.exe` /
`DeployMonitorServiceNonProd.exe` service components. Both trees are installed
on every server, side by side. Removing a copy would remove a service.

So the 572 MB is real payload, and the only lever is how it is compressed.

## Parallel cabbing was tried and reverted

`<Media Id="1">` is a single cabinet, and a cabinet is compressed by one
thread. That is why the tail of `Build solution` is one core working while the
other four idle: the other packages finish around 97s and Monitors runs on
alone to 139.6s. So `Setup.Dorc.Monitors` was changed to
`<MediaTemplate MaximumUncompressedMediaSize="64">` with
`CabinetCreationThreadCount=4`, splitting the payload across roughly nine
cabinets compressed concurrently.

The prediction on record was that this would cost little in size, because
MSZIP resets its dictionary every 32 KB and the duplicate Prod/NonProd copies
are hundreds of megabytes apart in the stream, so they were never compressing
against each other.

**That was wrong.** Run 30909238019:

| | One cabinet | Nine cabinets |
| --- | --- | --- |
| Setup.Dorc.Monitors.msi | 83.0 MB | **166.0 MB** |
| Whole build artifact | 198.1 MB | 280.8 MB |
| Build solution | 141.5s | 134s |

Exactly double, which is exactly the Prod/NonProd duplication factor. WiX
stores a given source file once per cabinet and points both `File` rows at
that one entry; the second copy was nearly free, and splitting the media
forced it into a different cabinet where it paid full price. 83 MB of artifact
for 7 seconds of build is not a trade worth making, so this is reverted.

Two things follow. Applying the same change to `Setup.Dorc.Cli` would be worse
still — 2276 file rows over 378 distinct names is a higher duplication factor
than Monitors. And the 572 MB figure below overstates the compression work:
WiX is deduplicating within the cabinet, so the real input is closer to the
distinct content.

**Parallel cabbing is a dead end for this solution.** The packages that are
slow to cab are slow precisely because they carry many copies of the same
files, and that is the property a single cabinet exploits.

## What is left

Lowering `CompressionLevel` was considered and rejected. WiX already defaults
to MSZIP, which is the fast setting; the only faster value is `none`, which
would take the package from 83 MB to roughly 572 MB.

With parallel cabbing ruled out, `Build solution` is close to its floor for
this shape of build. What remains is elsewhere in the job:

- **The NuGet cache save has been dropped from pull requests.** The key is
  `hashFiles('**/packages.lock.json', '**/*.csproj')`, so any csproj edit minted
  a new key: the run restored an older entry through `restore-keys` — which
  counts as a miss — and then re-saved the whole cache in the post-job step.
  Measured across three runs, restore costs 1m 28s-1m 38s and the save
  1m 38s-5m 11s, to make `dotnet restore` take ~16s instead of ~2m 04s.
  Excluding `.nupkg` archives cut the save from 5m 11s to about 1m 38s, which
  helped but did not turn it positive. The restore half does pay for itself, so
  only the save is gone, and only where it could never help: a cache written by
  a pull request is scoped to that pull request and no other branch can read it.
  Saves now happen on `main` and `develop`, whose caches everything else can
  read. Expected to remove 1m 38s-3m 06s from every pull-request run.
- **A larger runner.** Cabbing is CPU-bound and `windows-latest` is 4 vCPUs.
- **Skipping MSI authoring on pull requests is not available** — the packages
  are used to test pull requests before merge.
