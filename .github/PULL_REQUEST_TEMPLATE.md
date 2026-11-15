## Summary
<!-- One or two sentences: what this PR does and why. -->

Fixes #
<!-- Use "Relates to #" instead if this PR doesn't fully close the issue. -->

## Changes
<!-- Key changes, grouped by area. -->

- 

## Acceptance criteria
<!-- Copy each criterion from the issue and point to the code and test that cover it. -->

- [ ] Criterion: covered by `TestName`

## Tests
<!-- Bug fixes are test-first: list each test that failed before the fix and passes after. -->

**Failing before the fix:**
- 

**New or changed tests:**
- 

**Results:** `dotnet test BackPocketEmulator.slnx -c Release`: ___ passed, ___ failed, ___ skipped

## Deferred / follow-ups
<!-- Anything in the issue's scope that this PR leaves out, with the reason. Accepted tech debt needs a follow-up issue in the same epic. Write "None" if nothing. -->

- 

## Checklist
- [ ] `dotnet build BackPocketEmulator.slnx -c Release -warnaserror` is clean
- [ ] All tests pass
- [ ] Changes stay within the issue's scope, or the extras are explained above
- [ ] Locks use `System.Threading.Lock`; no static mutable state; no empty catch blocks
- [ ] No ROM images added (`pocket2-charset.rom` is the only allowed exception)
- [ ] Docs, PRDs or XML comments updated where behavior or contracts changed
