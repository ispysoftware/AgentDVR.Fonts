# Agent DVR fork of SixLabors.Fonts

Branch `agent`, based on upstream tag **v1.0.1** (2023-09-15), the last release under the
Apache License 2.0. Upstream relicensed to the Six Labors Split License on 2023-08-24
(commit 8c06da29); nothing from after that point is merged here, and fixes for bugs reported
upstream since then are written independently from the bug description and the OpenType spec.

Modified by iSpyConnect / The Playful Group for Agent DVR. Changes from v1.0.1:

- Build: self-contained net10.0 project. The `shared-infrastructure` submodule and upstream's
  global build props (StyleCop, strong naming, artifacts output, extra restore feeds) are
  removed; the seven SharedInfrastructure sources are copied into
  `src/SixLabors.Fonts/SharedInfrastructure`.
- `TextLayout.cs`: line gap no longer counted in the content area when centring the baseline
  in the 1em line box (fonts with a large line gap, e.g. Tekton Pro, rendered too high).
