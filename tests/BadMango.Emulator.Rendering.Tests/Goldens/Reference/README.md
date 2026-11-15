# Video reference goldens

These frozen BGRA reference frames are the `main` renderer baseline for the video-pipeline epic. They were produced from the output of `Pocket2VideoRenderer.RenderFrame` as of commit `38668da` (or the PR base commit for the branch used to land this step), and are intentionally kept frozen while the scanner/output/provider refactor is implemented.

Do not regenerate these files after the refactor starts. The outputs here are a permanent compatibility record for text-mode parity and intentional mode differences discussed in the video pipeline PRD.

`Pocket2VideoRenderer` has no double lo-res or double hi-res path. `DoubleLoRes`, `DoubleLoResMixed`, `DoubleHiRes`, and `DoubleHiResMixed` fall back to Text40. The `Double*` goldens record that fallback on purpose. Issue #406 asks for the output the renderer produces today, and those cells stay in the matrix.

Set `BPE_UPDATE_REFERENCE_GOLDENS=1` to refresh the reference set when the baseline renderer is intentionally changed and the new output is reviewed by the team. The fixture's one-time setup then rewrites every golden, and the comparisons are skipped. Without that environment variable, the test fixture compares the current renderer output to the stored golden files bit for bit and does not write files.
