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

## Next lever, not yet taken

`Setup.Dorc.Monitors`'s 116.7s of cabbing is the largest single item left in
the build, and it is on the critical path by definition. Each package declares
one `<Media Id="1" Cabinet="media1.cab" EmbedCab="yes"/>`, which is a single
cabinet built by a single thread. `<MediaTemplate>` splits the payload across
several cabinets that WiX can build concurrently, and `CompressionLevel`
trades MSI size for time directly.

Neither is free: more cabinets and lower compression both enlarge the
artifact, which is copied to a network share on every build. That trade is a
judgement call about the release artifact, not a build-script change, so it is
recorded here rather than made.
