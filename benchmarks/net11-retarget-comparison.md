# net10.0 vs net11.0 benchmark comparison

Matched harness: BenchmarkDotNet 0.16.0-preview.2, Default job, `MemoryDiagnoser`, one VM, back to back.

- net10.0 is unmodified `main` at `718a125`. The only local change was the BenchmarkDotNet package version, so the harness matches the net11 run. Emulator source was not edited.
- net11.0 is this branch (`TargetFramework` net11.0, Microsoft.Extensions and Serilog host packages 11.0.0).
- Ratio is net11 mean / net10 mean. Below 1 is faster on net11.
- Counted MHz is `cycles per instruction / nanoseconds per instruction * 1000`, the formula already documented on the workload benchmarks. Rows without a cycle model leave those columns blank.
- Geometric mean of all 58 ratios: 1.004.

An earlier net10 run used the repo's previous BenchmarkDotNet 0.15.8. That build throws `GetRuntimeVersion not implemented for NotRecognized` on a net11 host, so it cannot be the other side of this table. The 0.15.8 and 0.16 net10 means for the same workloads also moved (LINQ 60.69 ns vs 94.88 ns, load-store loop 22.50 ns vs 19.24 ns), which is why the table uses one harness version.

## Results

| Benchmark | Method | net10 mean | net10 SD | net10 alloc | net11 mean | net11 SD | net11 alloc | Ratio | net10 MHz | net11 MHz |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| DevicePageLoop | Loop | 135.92 ns | 2.59 ns | 0 B | 120.08 ns | 0.71 ns | 0 B | 0.883 | 28.8 | 32.6 |
| PageRemap | SelectSwapVariant | 63.68 ns | 0.52 ns | 0 B | 74.28 ns | 0.20 ns | 0 B | 1.166 | — | — |
| WorkloadMhzDispatch | Mix | 17.43 ns | 0.12 ns | 0 B | 17.82 ns | 0.13 ns | 0 B | 1.022 | 259.3 | 253.6 |
| WorkloadMhzDispatch | Ram | 15.27 ns | 0.04 ns | 0 B | 15.84 ns | 0.26 ns | 0 B | 1.037 | 196.5 | 189.4 |
| WorkloadMhzDispatch | FlagsTransfer | 12.82 ns | 0.12 ns | 0 B | 12.73 ns | 0.19 ns | 0 B | 0.993 | 156.7 | 157.9 |
| WorkloadMhzDispatch | LoadStore | 19.24 ns | 0.10 ns | 0 B | 19.49 ns | 0.30 ns | 0 B | 1.013 | 206.2 | 203.5 |
| HotPathLoopVsLinq | HandWrittenLoop | 17.23 ns | 0.26 ns | 0 B | 17.36 ns | 0.16 ns | 0 B | 1.008 | — | — |
| HotPathLoopVsLinq | LinqPipeline | 94.88 ns | 1.58 ns | 48 B | 62.12 ns | 2.28 ns | 48 B | 0.655 | — | — |
| FlagsTransferDispatch | Execute [CLC] | 11.21 ns | 0.07 ns | 0 B | 12.33 ns | 0.30 ns | 0 B | 1.100 | — | — |
| FlagsTransferDispatch | Execute [CLD] | 11.30 ns | 0.20 ns | 0 B | 12.58 ns | 0.17 ns | 0 B | 1.113 | — | — |
| FlagsTransferDispatch | Execute [CLI] | 11.09 ns | 0.10 ns | 0 B | 12.18 ns | 0.32 ns | 0 B | 1.098 | — | — |
| FlagsTransferDispatch | Execute [CLV] | 12.01 ns | 0.14 ns | 0 B | 11.50 ns | 0.13 ns | 0 B | 0.958 | — | — |
| FlagsTransferDispatch | Execute [SEC] | 12.03 ns | 0.17 ns | 0 B | 11.80 ns | 0.51 ns | 0 B | 0.981 | — | — |
| FlagsTransferDispatch | Execute [SED] | 11.00 ns | 0.03 ns | 0 B | 12.11 ns | 0.16 ns | 0 B | 1.101 | — | — |
| FlagsTransferDispatch | Execute [SEI] | 11.26 ns | 0.04 ns | 0 B | 12.20 ns | 0.34 ns | 0 B | 1.083 | — | — |
| FlagsTransferDispatch | Execute [TAX] | 13.03 ns | 0.09 ns | 0 B | 13.68 ns | 0.10 ns | 0 B | 1.050 | — | — |
| FlagsTransferDispatch | Execute [TAY] | 13.18 ns | 0.05 ns | 0 B | 13.65 ns | 0.20 ns | 0 B | 1.036 | — | — |
| FlagsTransferDispatch | Execute [TSX] | 12.49 ns | 0.18 ns | 0 B | 13.68 ns | 0.19 ns | 0 B | 1.095 | — | — |
| FlagsTransferDispatch | Execute [TXA] | 12.84 ns | 0.16 ns | 0 B | 13.84 ns | 0.16 ns | 0 B | 1.078 | — | — |
| FlagsTransferDispatch | Execute [TXS] | 11.61 ns | 0.11 ns | 0 B | 12.08 ns | 0.30 ns | 0 B | 1.040 | — | — |
| FlagsTransferDispatch | Execute [TYA] | 12.74 ns | 0.05 ns | 0 B | 14.15 ns | 0.34 ns | 0 B | 1.111 | — | — |
| LoadStoreDispatch | Execute [LDA #] | 16.00 ns | 0.10 ns | 0 B | 16.08 ns | 0.08 ns | 0 B | 1.005 | — | — |
| LoadStoreDispatch | Execute [LDA (zp)] | 20.45 ns | 0.07 ns | 0 B | 20.76 ns | 0.12 ns | 0 B | 1.015 | — | — |
| LoadStoreDispatch | Execute [LDA (zp),Y] | 21.80 ns | 0.09 ns | 0 B | 21.65 ns | 0.20 ns | 0 B | 0.993 | — | — |
| LoadStoreDispatch | Execute [LDA (zp,X)] | 20.58 ns | 0.06 ns | 0 B | 20.58 ns | 0.08 ns | 0 B | 1.000 | — | — |
| LoadStoreDispatch | Execute [LDA abs] | 18.77 ns | 0.14 ns | 0 B | 18.72 ns | 0.10 ns | 0 B | 0.997 | — | — |
| LoadStoreDispatch | Execute [LDA abs,X] | 19.50 ns | 0.05 ns | 0 B | 19.61 ns | 0.08 ns | 0 B | 1.006 | — | — |
| LoadStoreDispatch | Execute [LDA abs,Y] | 19.61 ns | 0.17 ns | 0 B | 19.46 ns | 0.10 ns | 0 B | 0.992 | — | — |
| LoadStoreDispatch | Execute [LDA zp] | 18.69 ns | 0.31 ns | 0 B | 17.34 ns | 0.03 ns | 0 B | 0.928 | — | — |
| LoadStoreDispatch | Execute [LDA zp,X] | 17.53 ns | 0.09 ns | 0 B | 17.35 ns | 0.07 ns | 0 B | 0.990 | — | — |
| LoadStoreDispatch | Execute [LDX #] | 16.39 ns | 0.33 ns | 0 B | 16.24 ns | 0.12 ns | 0 B | 0.991 | — | — |
| LoadStoreDispatch | Execute [LDX abs] | 18.73 ns | 0.18 ns | 0 B | 18.68 ns | 0.04 ns | 0 B | 0.997 | — | — |
| LoadStoreDispatch | Execute [LDX abs,Y] | 23.12 ns | 0.37 ns | 0 B | 19.85 ns | 0.10 ns | 0 B | 0.859 | — | — |
| LoadStoreDispatch | Execute [LDX zp] | 17.89 ns | 0.14 ns | 0 B | 17.35 ns | 0.05 ns | 0 B | 0.970 | — | — |
| LoadStoreDispatch | Execute [LDX zp,Y] | 17.66 ns | 0.21 ns | 0 B | 17.26 ns | 0.05 ns | 0 B | 0.977 | — | — |
| LoadStoreDispatch | Execute [LDY #] | 16.06 ns | 0.11 ns | 0 B | 16.40 ns | 0.29 ns | 0 B | 1.021 | — | — |
| LoadStoreDispatch | Execute [LDY abs] | 18.79 ns | 0.26 ns | 0 B | 18.83 ns | 0.16 ns | 0 B | 1.002 | — | — |
| LoadStoreDispatch | Execute [LDY abs,X] | 19.64 ns | 0.25 ns | 0 B | 19.43 ns | 0.12 ns | 0 B | 0.989 | — | — |
| LoadStoreDispatch | Execute [LDY zp] | 17.43 ns | 0.12 ns | 0 B | 17.39 ns | 0.07 ns | 0 B | 0.998 | — | — |
| LoadStoreDispatch | Execute [LDY zp,X] | 22.50 ns | 0.17 ns | 0 B | 17.36 ns | 0.07 ns | 0 B | 0.772 | — | — |
| LoadStoreDispatch | Execute [STA (zp)] | 19.52 ns | 0.16 ns | 0 B | 19.16 ns | 0.09 ns | 0 B | 0.982 | — | — |
| LoadStoreDispatch | Execute [STA (zp),Y] | 19.31 ns | 0.11 ns | 0 B | 20.33 ns | 0.32 ns | 0 B | 1.053 | — | — |
| LoadStoreDispatch | Execute [STA (zp,X)] | 19.74 ns | 0.23 ns | 0 B | 19.62 ns | 0.15 ns | 0 B | 0.994 | — | — |
| LoadStoreDispatch | Execute [STA abs] | 17.02 ns | 0.08 ns | 0 B | 16.67 ns | 0.08 ns | 0 B | 0.979 | — | — |
| LoadStoreDispatch | Execute [STA abs,X] | 16.69 ns | 0.09 ns | 0 B | 17.18 ns | 0.15 ns | 0 B | 1.029 | — | — |
| LoadStoreDispatch | Execute [STA abs,Y] | 16.77 ns | 0.05 ns | 0 B | 17.27 ns | 0.17 ns | 0 B | 1.030 | — | — |
| LoadStoreDispatch | Execute [STA zp] | 15.64 ns | 0.14 ns | 0 B | 16.01 ns | 0.31 ns | 0 B | 1.024 | — | — |
| LoadStoreDispatch | Execute [STA zp,X] | 16.35 ns | 0.75 ns | 0 B | 16.35 ns | 0.20 ns | 0 B | 1.000 | — | — |
| LoadStoreDispatch | Execute [STX abs] | 16.64 ns | 0.04 ns | 0 B | 17.37 ns | 0.17 ns | 0 B | 1.044 | — | — |
| LoadStoreDispatch | Execute [STX zp] | 15.86 ns | 0.04 ns | 0 B | 16.20 ns | 0.07 ns | 0 B | 1.021 | — | — |
| LoadStoreDispatch | Execute [STX zp,Y] | 15.76 ns | 0.05 ns | 0 B | 16.21 ns | 0.09 ns | 0 B | 1.029 | — | — |
| LoadStoreDispatch | Execute [STY abs] | 16.72 ns | 0.12 ns | 0 B | 16.73 ns | 0.18 ns | 0 B | 1.001 | — | — |
| LoadStoreDispatch | Execute [STY zp] | 15.48 ns | 0.08 ns | 0 B | 15.83 ns | 0.09 ns | 0 B | 1.023 | — | — |
| LoadStoreDispatch | Execute [STY zp,X] | 15.83 ns | 0.13 ns | 0 B | 16.87 ns | 0.09 ns | 0 B | 1.066 | — | — |
| LoadStoreDispatch | Execute [STZ abs] | 16.20 ns | 0.19 ns | 0 B | 16.10 ns | 0.08 ns | 0 B | 0.994 | — | — |
| LoadStoreDispatch | Execute [STZ abs,X] | 16.07 ns | 0.06 ns | 0 B | 16.18 ns | 0.19 ns | 0 B | 1.007 | — | — |
| LoadStoreDispatch | Execute [STZ zp] | 15.15 ns | 0.11 ns | 0 B | 15.84 ns | 0.10 ns | 0 B | 1.046 | — | — |
| LoadStoreDispatch | Execute [STZ zp,X] | 15.20 ns | 0.14 ns | 0 B | 15.13 ns | 0.07 ns | 0 B | 0.995 | — | — |

## Noise

The machine is a 4-vCPU KVM guest (`Intel Xeon Processor`, model 207, x86-64-v4). BenchmarkDotNet could not raise process priority (`Permission denied`). Runs were sequential, not one BenchmarkDotNet session, because the product projects are a single target framework and the net10 build still compiles the C# 15 polyfill.

Most dispatch rows have a coefficient of variation under 2%, and their 99.9% error bars are a few tenths of a nanosecond. Treat movement inside about 3% as VM noise. These stand out from that band:

- Device-page loop: 135.92 ns to 120.08 ns (ratio 0.883). The net11 error bar is 0.86 ns and the net10 error bar is 2.64 ns, so the intervals do not overlap. The earlier 0.15.8 net10 mean was 130.58 ns, in the same direction.
- `SelectSwapVariant`: 63.68 ns to 74.28 ns (ratio 1.166). Both net10 harnesses agreed (63.13 ns on 0.15.8). Allocations stayed 0 B. This is the clearest regression in the set.
- `LinqPipeline`: 94.88 ns to 62.12 ns on this pair, but the 0.15.8 net10 run measured 60.69 ns with the same 48 B allocation. The matched net10 sample is the outlier. Do not read this as a LINQ speedup.
- `LDY zp,X` (22.50 ns to 17.36 ns) and `LDX abs,Y` (23.12 ns to 19.85 ns) moved more than neighboring addressing modes. The net10 samples were stable inside the run, and the gaps are larger than the error bars, but they are two opcodes out of 37.
- Flag and transfer opcodes are mostly 4% to 11% slower per opcode. The mixed flags-transfer workload that actually loops those opcodes is flat (12.82 ns vs 12.73 ns).
- `STA zp,X` on net10 has SD 0.75 ns on a 16.35 ns mean (the noisiest load/store row). Its ratio is 1.000.
- `SEC` on net11 has SD 0.51 ns on an 11.80 ns mean.

Workload MHz for the four CPU loops stays inside a few percent: mix 259.3 to 253.6, RAM 196.5 to 189.4, flags-transfer 156.7 to 157.9, load-store 206.2 to 203.5. RAM's error bars do not overlap; mix's barely do not; flags-transfer and load-store do.
