# DOS 3.3 Reference

## Overview

DOS 3.3 (Disk Operating System) is Apple's disk operating system, providing file management and disk I/O for the Apple II. DOS 3.3 loads into memory at $9600-$BFFF and hooks into BASIC to add disk commands.

**Memory Requirements:** ~10KB ($9600-$BFFF)
**HIMEM with DOS:** $9600 (from $BFFF)

---

## Memory Map

```
$9600-$9CFF   DOS buffers and work area
$9D00-$A8FF   File Manager (RWTS wrapper, catalog, file ops)
$A900-$AAFF   File Manager extended
$AB00-$ACFF   Additional buffers
$AD00-$B3FF   RWTS (Read/Write Track/Sector)
$B400-$B5FF   Disk arm movement
$B600-$B7FF   Boot code and low-level disk access
$B800-$BFFF   DOS command interpreter and main routines
```

---

## Zero Page Locations

DOS 3.3 uses very few zero page locations to preserve compatibility:

| Address | Name     | Description |
|---------|----------|-------------|
| $00-$01 | Reserved | Used by BASIC USR |
| $40-$47 | DOS work | Temporary workspace (shared) |
| $48     | (P save) | RWTS uses this byte. It is also where the monitor saves the status register. Set it to `$00` after each RWTS call. Beneath Apple DOS, ch. 6. |

---

## Page 3 Vectors

DOS keeps this table at fixed addresses because the routines it points at move when DOS is relocated. Beneath Apple DOS, chapter 5, is the source for what each entry is. The absolute targets in the last column are the 48K map in chapter 8. A slave diskette or a smaller machine changes those targets. The page-3 addresses do not.

| Address | Kind | Description | 48K target |
|---------|------|-------------|------------|
| $03D0 | `JMP` | Warm start. Re-enters DOS and keeps the BASIC program, `MAXFILES`, and the other environment. | `$9DBF` (ch. 8, `$9DBF`–`$9DE9`) |
| $03D3 | `JMP` | Cold start. Reinitializes DOS, clears the BASIC program, and resets `HIMEM`. | `$9D84` (ch. 8, `$9D84`–`$9DBE`) |
| $03D6 | `JMP` | File manager. Call with `JSR $03D6` after the parameter list is filled. `X = 0` allocates a file that is not found. Any other `X` does not. | `$AAFD` (ch. 8: "File manager external entry point (from `$3D6`)") |
| $03D9 | `JMP` | RWTS. `Y` is the low byte of the IOB address and `A` is the high byte. | `$B7B5` (ch. 8 calls `$B7B5` the external RWTS entry; ch. 5 says `$03D9` is the `JMP` to RWTS) |
| $03DC | subroutine | Returns the file-manager parameter list. `Y` is the low byte, `A` is the high byte. Not a `JMP`. | DOS's own list; ch. 6 |
| $03E3 | subroutine | Returns the RWTS IOB the same way (`Y` low, `A` high). Not a `JMP`. | DOS's IOB at `$B7E8` on the 48K map (ch. 8) |
| $03EA | `JMP` | Reconnects the DOS keyboard and screen intercepts. | ch. 5 does not print the target |
| $03EF | `JMP` | `BRK` handler. Autostart ROM only. Normally the monitor's register display. | monitor ROM, unless changed |
| $03F2 | address | Reset vector, low byte then high byte. DOS stores its restart here. | user-replaceable |
| $03F4 | byte | Power-up byte. Exclusive-or the `$03F2` address with `$A5` and store it here, or the Autostart ROM treats the next reset as power-up and tries to boot. | — |
| $03F5 | `JMP` | Applesoft `&` entry. | user routine |
| $03F8 | `JMP` | Monitor Ctrl-Y entry. | user routine |
| $03FB | `JMP` | Non-maskable interrupt. | user routine |
| $03FE | address | Maskable-interrupt vector, low byte then high byte. | user routine |

`$03DC` runs through the byte before `$03E3`. Reset is the address at `$03F2`. The Applesoft `&` vector is `$03F5`.

---

## File Manager Entry ($AAFD)

The file manager is the high-level disk interface. On a 48K DOS its external entry is `$AAFD`, reached by `JSR $03D6` (Beneath Apple DOS, ch. 8 and ch. 6). `$A764` is the routine that completes DOS's own parameter list (ch. 8). Callers from outside DOS enter at `$AAFD`.

Call `$03DC` once to locate that parameter list (`Y` low, `A` high). Fill it, set `X` as described under the page-3 table, then `JSR $03D6`. Carry is set when the return code is nonzero.

### File Manager Parameter List

General layout from Beneath Apple DOS, chapter 6. Bytes `$02`–`$09` change with the command. Addresses are low byte first.

| Offset | Description |
|--------|-------------|
| $00 | Call type. See the command table. |
| $01 | Sub-call for `READ` and `WRITE`: `$00` none, `$01` one byte, `$02` a range, `$03` position then one byte, `$04` position then a range. |
| $02–$09 | Fields that depend on the call type. |
| $0A | Return code. |
| $0B | Unused. |
| $0C–$0D | Address of a 45-byte file-manager workarea. The caller supplies the buffer. |
| $0E–$0F | Address of a 256-byte track/sector-list buffer. |
| $10–$11 | Address of a 256-byte data-sector buffer. |

Three buffers together occupy 557 bytes, and each open file needs its own set (ch. 6).

Return codes from that chapter: `$00` none, `$02` bad call type, `$03` bad sub-call, `$04` write protected, `$05` end of data, `$06` file not found (allocated when `X` was 0), `$07` volume mismatch, `$08` disk I/O error, `$09` disk full, `$0A` file locked. `$01` is unused. The processor carry flag is set when the code is nonzero.

### File Manager Commands

Call types from Beneath Apple DOS, chapter 6.

| Code | Command | Description |
|------|---------|-------------|
| $01  | OPEN    | Open file |
| $02  | CLOSE   | Close file |
| $03  | READ    | Read from file |
| $04  | WRITE   | Write to file |
| $05  | DELETE  | Delete file |
| $06  | CATALOG | Display catalog |
| $07  | LOCK    | Lock file |
| $08  | UNLOCK  | Unlock file |
| $09  | RENAME  | Rename file |
| $0A  | POSITION| Position in file |
| $0B  | INIT    | Initialize disk |
| $0C  | VERIFY  | Verify file |

### File Types

Types stored for a new file (Beneath Apple DOS, ch. 6). `$08` is relocatable and `$10` is an S-type file.

| Code | Type | Description |
|------|------|-------------|
| $00  | T    | Text file |
| $01  | I    | Integer BASIC program |
| $02  | A    | Applesoft BASIC program |
| $04  | B    | Binary file |
| $08  | R    | Relocatable |
| $10  | S    | S type file |
| $20  | A    | Type A (new A) |
| $40  | B    | Type B (new B) |

---

## RWTS Entry ($B7B5)

RWTS (Read/Write Track/Sector) is the low-level disk driver. Beneath Apple DOS, chapter 8, places the external entry at `$B7B5` on a 48K machine (`$B7B5`–`$B7C1`, disable interrupts and call RWTS). Call it through the page-3 vector `JSR $03D9`. `Y` holds the low byte of the IOB address and `A` holds the high byte (ch. 6). Carry is set when the return code is nonzero.

### Calling RWTS

```assembly
        LDY #<IOB       ; Low byte of IOB address
        LDA #>IOB       ; High byte of IOB address
        JSR $03D9       ; Page-3 JMP; target is $B7B5 on 48K DOS
        BCS ERROR       ; Carry set = error
```

`JSR $03E3` returns DOS's own IOB the same way, if the caller wants that block instead of a private one.

### RWTS Input/Output Block (IOB)

Offsets from Beneath Apple DOS, chapter 6. On the 48K map the same bytes sit at `$B7E8`–`$B7F8` (ch. 8). Addresses are low byte, then high byte.

| Offset | 48K address | Description |
|--------|-------------|-------------|
| $00 | $B7E8 | Table type. Must be `$01`. |
| $01 | $B7E9 | Slot number times 16. Slot 6 is `$60`. |
| $02 | $B7EA | Drive, `$01` or `$02`. |
| $03 | $B7EB | Volume expected. `$00` matches any volume. |
| $04 | $B7EC | Track, `$00` through `$22`. |
| $05 | $B7ED | Sector, `$00` through `$0F`. |
| $06–$07 | $B7EE | Address of the device characteristics table. |
| $08–$09 | $B7F0 | Address of the 256-byte data buffer. |
| $0A | $B7F2 | Unused. |
| $0B | $B7F3 | Byte count for a partial sector. `$00` means 256 bytes. |
| $0C | $B7F4 | Command. See below. |
| $0D | $B7F5 | Return code. Meaningful when carry is set. |
| $0E | $B7F6 | Volume of the last access. Must be initialized. |
| $0F | $B7F7 | Slot times 16 of the last access. Must be initialized. |
| $10 | $B7F8 | Drive of the last access. Must be initialized. |

### Device characteristics table

| Offset | Description |
|--------|-------------|
| $00 | Device type. `$00` for Disk II. |
| $01 | Phases per track. `$01` for Disk II. |
| $02–$03 | Motor-on time count. Chapter 6 gives the value `$EFD8`. Chapter 8 lists the two stored bytes as `$EF`, `$D8`. The byte order is unresolved between those two sentences. |

On a 48K DOS the table DOS itself uses is at `$B7FB`–`$B7FE` (ch. 8).

### RWTS commands (IOB offset `$0C`)

| Value | Command | Description |
|-------|---------|-------------|
| $00 | SEEK | Move to the track |
| $01 | READ | Read a sector |
| $02 | WRITE | Write a sector |
| $04 | FORMAT | Format the track. This does not write a VTOC, a catalog, or a HELLO program. |

### RWTS return codes (IOB offset `$0D`)

Chapter 6:

| Code | Description |
|------|-------------|
| $00 | No error |
| $08 | Error during initialization |
| $10 | Write protected |
| $20 | Volume mismatch |
| $40 | Drive error |
| $80 | Read error (chapter 6 marks this code obsolete) |

Chapter 8's note on `$B7F5` lists `$10`, `$20`, `$40`, and `$80` and does not mention `$08`.

### RWTS usage example

```assembly
; Read track $11 sector 0 through the page-3 vector.
IOB:    DB $01          ; Table type
        DB $60          ; Slot 6
        DB $01          ; Drive 1
        DB $00          ; Any volume
        DB $11          ; Track 17
        DB $00          ; Sector 0
        DW DCTBL        ; DCT pointer
        DW BUFFER       ; 256-byte buffer
        DB $00          ; Unused
        DB $00          ; Byte count: $00 = 256
        DB $01          ; READ
        DB $00          ; Return code
        DB $00          ; Last volume
        DB $60          ; Last slot
        DB $01          ; Last drive

        LDY #<IOB
        LDA #>IOB
        JSR $03D9
        BCS ERROR
```

---

## DOS Commands

DOS 3.3 adds commands to BASIC by intercepting the input line:

### File Commands

| Command | Description |
|---------|-------------|
| CATALOG [,Dn][,Sn][,Vn] | Display disk catalog |
| LOAD filename[,Dn][,Sn][,Vn] | Load BASIC program |
| SAVE filename[,Dn][,Sn][,Vn] | Save BASIC program |
| RUN filename[,Dn][,Sn][,Vn] | Load and run program |
| CHAIN filename[,Dn][,Sn][,Vn] | Run preserving variables |
| DELETE filename | Delete file |
| LOCK filename | Lock file (prevent deletion) |
| UNLOCK filename | Unlock file |
| RENAME oldname,newname | Rename file |
| VERIFY filename | Verify file is readable |

### File I/O Commands

| Command | Description |
|---------|-------------|
| OPEN filename[,Ln][,Dn][,Sn] | Open file for I/O |
| CLOSE [filename] | Close file(s) |
| READ filename | Set file for reading |
| WRITE filename | Set file for writing |
| APPEND filename | Open for appending |
| POSITION filename,Rn | Position to record |

### Binary File Commands

| Command | Description |
|---------|-------------|
| BLOAD filename[,Aadr][,Dn][,Sn] | Load binary file |
| BSAVE filename,Aadr,Llen[,Dn][,Sn] | Save binary file |
| BRUN filename[,Aadr][,Dn][,Sn] | Load and execute binary |

### Disk Commands

| Command | Description |
|---------|-------------|
| INIT filename[,Vn] | Initialize disk |
| FP | Enter BASIC (clear DOS hooks) |
| INT | Enter Integer BASIC |
| MON[,C][,I][,O] | Enable I/O monitoring |
| NOMON[,C][,I][,O] | Disable I/O monitoring |
| MAXFILES n | Set max open files (1-16) |

### Parameter Suffixes

| Suffix | Description |
|--------|-------------|
| ,Dn    | Drive number (1 or 2) |
| ,Sn    | Slot number (1-7) |
| ,Vn    | Volume number (1-254) |
| ,Ln    | Record length (text files) |
| ,Rn    | Record number |
| ,Aadr  | Load/execute address |
| ,Llen  | Length in bytes |
| ,Bn    | Byte offset in record |

---

## Disk Format

### Physical Format

- 35 tracks (0-34)
- 16 sectors per track (0-15)
- 256 bytes per sector
- Total: 140KB per disk

### Logical Structure

| Track | Sector | Contents |
|-------|--------|----------|
| 0     | 0      | Boot sector 0 (loads more boot code) |
| 0     | 1-9    | DOS boot code |
| 17    | 0      | VTOC (Volume Table of Contents) |
| 17    | 1-15   | Catalog sectors (linked list) |
| Other | Other  | File data |

### Boot Sector 0 (Track 0, Sector 0)

The Disk II controller ROM reads T0/S0 to `$0800` and jumps unconditionally to `$0801`. The DOS 3.3
boot sector therefore begins with:

| Block offset | Byte | Description |
|-------------|------|-------------|
| `$00`       | `$01` | Sector-chain count — number of additional sectors to load before transferring control to DOS proper |
| `$01`       | `$A5` | First opcode executed at `$0801` (`LDA $27`, zero page) |
| `$02`       | `$27` | Operand of `LDA $27` |
| `$03`       | `$C9` | `CMP #$09` opcode |
| `$04`       | `$09` | Operand of `CMP #$09` |
| `$05`–`$FF` | …    | Remainder of the Boot 1 loader |

**Bootability signature (offsets 0–4):** `01 A5 27 C9 09`

A freshly initialised or blank disk leaves T0/S0 as all-`$00` bytes; no valid boot sector is
present and the disk is not bootable.

### Volume Table of Contents (Track 17, Sector 0)

| Offset | Description |
|--------|-------------|
| $00    | Unused |
| $01    | Catalog track |
| $02    | Catalog sector |
| $03    | DOS version ($03 = DOS 3.3) |
| $04-$05| Unused |
| $06    | Volume number |
| $27    | Max T/S pairs per T/S list |
| $30    | Last track allocated |
| $31    | Allocation direction (+1 or -1) |
| $34    | Tracks per disk (35) |
| $35    | Sectors per track (16) |
| $36-$37| Bytes per sector (256) |
| $38-$C3| Free sector bitmap (4 bytes per track) |

### Catalog Entry Format

| Offset | Description |
|--------|-------------|
| $00    | Track of first T/S list (0 = deleted) |
| $01    | Sector of first T/S list |
| $02    | File type + locked flag |
| $03-$20| Filename (30 bytes, high bit set) |
| $21-$22| File length in sectors |

### File Type Byte ($02)

| Bit | Meaning |
|-----|---------|
| 7   | Locked flag (1 = locked) |
| 6-0 | File type |

### Track/Sector List

Each T/S list sector contains pointers to data sectors:

| Offset | Description |
|--------|-------------|
| $00    | Unused |
| $01    | Track of next T/S list (0 = none) |
| $02    | Sector of next T/S list |
| $05-$06| Sector offset of first entry |
| $0C-$0D| Track/sector pair 0 |
| $0E-$0F| Track/sector pair 1 |
| ...    | (up to 122 pairs) |

---

## Important RAM Locations

48K addresses from Beneath Apple DOS, chapter 8, except `$9600`, which this document's memory map uses as `HIMEM` with the default buffers. Chapter 5 describes those buffers as the region below the DOS code and does not print `$9600` itself.

| Address | Description |
|---------|-------------|
| $9600 | Low end of the DOS image used as `HIMEM` in the memory map above. Not re-derived from chapter 8. |
| $9D84 | Cold-start entry (`$9D84`–`$9DBE`). |
| $9DBF | Warm-start entry (`$9DBF`–`$9DE9`). |
| $AA72 | Parsed "address" keyword, two bytes. The filename buffer starts at `$AA75`. |
| $AA75–$AA92 | Primary filename buffer. |
| $AA93–$AAB0 | Secondary filename buffer, used by `RENAME`. |
| $AAB6 | Active BASIC flag: `$00` Integer, `$40` Applesoft ROM, `$80` Applesoft RAM. |
| $AAC1 | Address of the RWTS parameter list (`$B7E8`). |
| $AAFD | File-manager external entry, the target of `$03D6`. |
| $B7B5 | RWTS external entry, the target of `$03D9`. |
| $B7E8–$B7F8 | RWTS parameter list. Fields are in the IOB table above. |

`$B3D2`, `$B3D3`, and `$B3F4` are not in that chapter's map. They are left out here instead of being given a name the book does not use.

---

## Common Routines

### DOS cold start ($9D84)

**Purpose:** Initialize DOS from a cold start. Beneath Apple DOS, chapter 8, `$9D84`–`$9DBE`. The page-3 `JMP` is `$03D3`.

**Called:** After boot, or by `JMP $03D3` / `JMP $9D84` on a 48K machine.

### DOS warm start ($9DBF)

**Purpose:** Re-enter DOS without clearing the BASIC program. Chapter 8, `$9DBF`–`$9DE9`. The page-3 `JMP` is `$03D0`.

**Called:** After BASIC reset, through `$03D0`.

### CATALOG ($A56E)

**Purpose:** Display disk catalog.

**Input:**
- Slot/Drive set from defaults or command

### OPEN ($A2A3)

**Purpose:** Open a file.

**Input:**
- Filename in buffer
- File type and parameters

### CLOSE ($A2EA)

**Purpose:** Close file(s).

### READ ($A318)

**Purpose:** Read from open file.

### WRITE ($A350)

**Purpose:** Write to open file.

---

## Error Messages

| Code | Message |
|------|---------|
| 1    | LANGUAGE NOT AVAILABLE |
| 2    | RANGE ERROR |
| 4    | WRITE PROTECTED |
| 5    | END OF DATA |
| 6    | FILE NOT FOUND |
| 7    | VOLUME MISMATCH |
| 8    | I/O ERROR |
| 9    | DISK FULL |
| 10   | FILE LOCKED |
| 11   | SYNTAX ERROR |
| 12   | NO BUFFERS AVAILABLE |
| 13   | FILE TYPE MISMATCH |
| 14   | PROGRAM TOO LARGE |
| 15   | NOT DIRECT COMMAND |

---

## BASIC Integration

DOS patches BASIC's input routine to intercept commands. When you type a DOS command, DOS:

1. Checks if line starts with a DOS keyword
2. If yes, parses and executes the DOS command
3. If no, passes line to BASIC

### I/O Hooks

DOS intercepts character I/O for file operations:

| Vector | Default | With File Open |
|--------|---------|----------------|
| CSW ($36) | $FDF0 | DOS output hook |
| KSW ($38) | $FD1B | DOS input hook |

When a file is open for output, PRINTed data goes to the file instead of screen.

---

## IIe vs IIc Differences

| Feature | Apple IIe | Apple IIc |
|---------|-----------|-----------|
| Boot slot | Slot 6 card | Built-in slot 6 |
| Second drive | Slot 6 drive 2 | External port |
| Memory | $9600-$BFFF | $9600-$BFFF |

No functional differences in DOS 3.3 between IIe and IIc.

---

## Compatibility Notes

### ProDOS Conversion

DOS 3.3 disks are not directly readable by ProDOS. Files must be converted using utilities like COPY II PLUS or Apple's conversion programs.

### File Limitations

- Maximum files per disk: ~105 (catalog space)
- Maximum file size: ~126KB (limited by T/S list)
- Filename length: 30 characters

### Memory Conflicts

Programs using memory above $9600 will conflict with DOS. Use HIMEM: $9600 or MAXFILES 1 to minimize DOS footprint.

---

## Document History

| Version | Date       | Changes |
|---------|------------|---------|
| 1.0     | 2025-12-30 | Initial specification |
| 1.1     | 2026-11-24 | Corrected page-3 vectors, the file-manager entry (`$AAFD`), and the RWTS IOB against Beneath Apple DOS, chapters 5, 6, and 8. |
