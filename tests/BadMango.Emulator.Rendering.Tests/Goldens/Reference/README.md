# Video reference goldens

These frozen BGRA reference frames are the `main` renderer baseline for the video-pipeline epic. They were produced from the output of `Pocket2VideoRenderer.RenderFrame` as of commit `38668da` (or the PR base commit for the branch used to land this step), and are intentionally kept frozen while the scanner/output/provider refactor is implemented.

Do not regenerate these files after the refactor starts. The outputs here are a permanent compatibility record for text-mode parity and intentional mode differences discussed in the video pipeline PRD.

Set `BPE_UPDATE_REFERENCE_GOLDENS=1` to refresh the reference set when the baseline renderer is intentionally changed and the new output is reviewed by the team. Without that environment variable, the test fixture compares the current renderer output to the stored golden files bit for bit.
