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
| 30847083209 (pre-split) | — | warm | 2m 31s | 35s | ~8m 03s |
| 30889337127 (post-split) | `4462786` | warm | 2m 20s | 1m 58s | ~9m 10s |
| 30905477688 | `f7fb35b` | **cold** | 2m 22s | 1m 49s | 10m 51s |

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

## What was changed

`<Media Id="1">` is a single cabinet, and a cabinet is compressed by one
thread. That is why the tail of `Build solution` is one core working while the
other four idle: the other packages finish around 97s and Monitors runs on
alone to 139.6s.

`Setup.Dorc.Monitors` now declares `<MediaTemplate MaximumUncompressedMediaSize="64">`
and `CabinetCreationThreadCount=4`, so the payload splits across roughly nine
cabinets compressed concurrently.

The obvious worry is that splitting the media costs compression ratio, since
the duplicate Prod/NonProd copies would no longer be in the same cabinet. It
should not: MSZIP resets its dictionary every 32 KB, so a copy hundreds of
megabytes away in the stream was never compressing against the first one. The
6.9:1 ratio is within-file compression of .NET assemblies. **This is a
prediction, and the run that lands this change is the test of it** — if
`Setup.Dorc.Monitors.msi` grows materially past 83 MB, the change is not worth
its size and should be reverted.

Lowering `CompressionLevel` was considered and rejected. WiX already defaults
to MSZIP, which is the fast setting; the only faster value is `none`, which
would take the package from 83 MB to roughly 572 MB and the build artifact
from 207 MB to about 1 GB, copied to a network share on every build.

Not yet applied to `Setup.Dorc.Api`, `Setup.Dorc.Cli` or `Setup.Acceptance`.
The same one-line change fits all three, and doing all of them is what would
actually move `Build solution` — the arithmetic says roughly 330 CPU-seconds
of compression over 4 vCPUs, so a floor near 110s against today's 141.5s. They
are held back only so this run measures one variable.
