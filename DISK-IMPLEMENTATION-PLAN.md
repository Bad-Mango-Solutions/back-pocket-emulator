# Disk Emulation Implementation Plan

Status: Retired
Retired: 2026-11-17, by the normative revision of [`DISK-EMULATION-PRD.md`](./DISK-EMULATION-PRD.md) (issue #440).

The May 2026 plan is not a source of truth. It sequenced WOZ ahead of a finished block layer, listed SCSI and network volumes as out of scope, and told implementers to trap an illegal opcode the way `GameIOController` does. None of that matches the code or the current priority order.

Use the PRD for:

- delivery order (block devices first, then network boot, then WOZ)
- the call-trap contract (`TrapHandler`, `TrapResult`, PC match inside `Cpu65C02.Step`)
- profile `drives[]` and mount-at-build
- the proposed sub-issue breakdown in the PRD's Appendix A

What has already landed (storage assembly, `DiskIIController`, `disk` commands, config plumbing) is described as current behavior in the PRD, not as future phases of this plan.
