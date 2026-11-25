# ProDOS 8 Reference

## Overview

ProDOS 8 (Professional Disk Operating System) is Apple's advanced disk operating system for 8-bit Apple II computers. It provides a hierarchical file system, clock/calendar support, and a clean machine language interface (MLI).

**Memory Requirements:** ~16KB in language card
**RAM Disk Support:** /RAM on IIe with 128KB

---

## Memory Map

ProDOS uses memory differently than DOS 3.3:

```
$0000-$00FF   Zero page (ProDOS uses $40-$4F)
$0100-$01FF   Stack
$0200-$02FF   Input buffer (shared with Monitor)
$0300-$03FF   System vectors and global page
$0800-$BEFF   User program space (varies)
$BF00-$BFFF   ProDOS global page
$D000-$DFFF   ProDOS in Language Card (bank 1)
$D000-$DFFF   ProDOS in Language Card (bank 2)
$E000-$FFFF   ProDOS in Language Card
```

### BASIC.SYSTEM Loaded

When BASIC.SYSTEM is loaded:

```
$0800-$09FF   BASIC.SYSTEM command handler
$0A00-$0BFF   BASIC.SYSTEM continued
$9A00-$9FFF   BASIC.SYSTEM continued
$2000-$9A00   User program space
```

**HIMEM with ProDOS and BASIC.SYSTEM:** $9A00

---

## ProDOS Global Page ($BF00-$BFFF)

The global page contains system status and vectors. Layout below is the [ProDOS 8 Technical Reference Manual, §5.2.4](https://prodos8.com/docs/techref/writing-a-prodos-system-program/). The only address a program should call is `ENTRY`.

### System entry points

| Address | Name | Size | Description | Source |
|---------|------|------|-------------|--------|
| $BF00 | ENTRY | 3 | `JMP` to the MLI. Call with `JSR $BF00`, then the command byte, then the parameter-list address. | TechRef §5.2.4 |
| $BF03 | JSPARE | 3 | `JMP` toward cold start, a selector, or `SYS.RTS`. | TechRef §5.2.4 |
| $BF06 | DATETIME | 3 | Clock routine. `$BF06` is `$60` (`RTS`) when no driver is installed, or `$4C` (`JMP`) when one is. The routine address lives at `$BF07`–`$BF08`. | TechRef §5.2.4 and [§6.1](https://prodos8.com/docs/techref/adding-routines-to-prodos/) |
| $BF09 | SYSERR | 3 | `JMP` to the error-reporting hook. | TechRef §5.2.4 |
| $BF0C | SYSDEATH | 3 | `JMP` to the system-failure hook. | TechRef §5.2.4 |
| $BF0F | SERR | 1 | Last error code. `$00` means no error. | TechRef §5.2.4 |

### Machine identification and prefix

| Address | Name | Description | Source |
|---------|------|-------------|--------|
| $BF98 | MACHID | Machine identification byte. Bit layout below. | TechRef §5.2.4; [TN #11](https://prodos8.com/docs/technote/11/) |
| $BF99 | SLTBYT | A set bit means a ROM was found in that slot. | TechRef §5.2.4 |
| $BF9A | PFIXPTR | Nonzero when a prefix is active. | TechRef §5.2.4 |
| $BF9B | MLIACTV | Nonzero while an MLI call is in progress. | TechRef §5.2.4 |

`$BF96`–`$BF97` are `SPARE1`, reserved for the MLI (TechRef §5.2.4). `MACHID` is the byte at `$BF98`. The memory bitmap is `MEMTABL` at `$BF58`–`$BF6F`.

#### MACHID byte ($BF98)

| Bits | Meaning | Source |
|------|---------|--------|
| 7–6 | Bit 3 clear: `00` = II, `01` = II+, `10` = IIe, `11` = III in emulation. Bit 3 set: `10` = IIc. The other bit-3-set combinations are not assigned. | TechRef §5.2.4 |
| 5–4 | Memory: `01` = 48K, `10` = 64K, `11` = 128K. `00` is not assigned. | TechRef §5.2.4 |
| 3 | Modifier for bits 7–6. Set selects the IIc line of the table above. | TechRef §5.2.4 |
| 2 | Reserved. | TechRef §5.2.4 |
| 1 | 80-column card. Always set on an Apple IIc (TechRef §5.3.1.3) and on an Apple IIgs (TN #11). | TechRef §5.2.4, §5.3.1.3; TN #11 |
| 0 | A clock card ProDOS recognized. Always set on an Apple IIgs, because that clock cannot be switched out. | TechRef §5.2.4; TN #11 |

TN #11 states that an Apple IIgs, and an Apple IIe with an 80-column card and a clock card, both read as `$B3`. The TechRef does not publish one IIc constant. `$C3` is not an IIc id under this layout: bit 3 is clear and bits 7–6 are `11`, which is the Apple III emulation encoding.

### Date and time

Four bytes, low byte first. The figure is in [TechRef §4.6.1](https://prodos8.com/docs/techref/calls-to-the-mli/) and [§6.1](https://prodos8.com/docs/techref/adding-routines-to-prodos/). The §5.2.4 listing comments are `BITS 15-9=YR, 8-5=MO, 4-0=DAY` and `BITS 12-8=HR, 5-0=MIN`.

| Address | Name | Contents | Source |
|---------|------|----------|--------|
| $BF90 | DATELO | Day in bits 4–0. Low three bits of the month in bits 7–5. | TechRef §4.6.1, §5.2.4, §6.1 |
| $BF91 | DATEHI | High bit of the month in bit 0 (word bit 8). Year in bits 7–1 (word bits 15–9). | TechRef §4.6.1, §5.2.4, §6.1 |
| $BF92 | TIMELO | Minute in bits 5–0 (0–59). Bits 7–6 unused. | TechRef §4.6.1, §5.2.4, §6.1 |
| $BF93 | TIMEHI | Hour in bits 4–0 (0–23). Bits 7–5 of this byte are unused (word bits 15–13). | TechRef §4.6.1, §5.2.4, §6.1 |

[Technical Note #28](https://prodos8.com/docs/technote/28/) (Dave Lyons, September 1990) defines the seven-bit year. Values 100–127 are invalid. 40–99 means 1940–1999. 0–39 means 2000–2039. The note says Apple II and Apple IIgs system software did not yet apply that rule.

The optional `prodos-clock` motherboard device writes these four bytes, with `year % 100`, when ProDOS left `RTS` at `$BF06`. A clock card that ProDOS installed as `JMP` still owns `DATETIME`.

### Interpreter and kernel version

| Address | Name | Description | Source |
|---------|------|-------------|--------|
| $BFFC | IBAKVER | Reserved. | TechRef §5.2.4 |
| $BFFD | IVERSION | Version of the running interpreter. The interpreter writes it. BASIC.SYSTEM 1.7, shipped with ProDOS 2.4.3, stores `7` here. | TechRef §5.2.4; [ProDOS 2.4.3 release note](https://prodos8.com/releases/prodos-243/) |
| $BFFE | KBAKVER | Reserved. | TechRef §5.2.4 |
| $BFFF | KVERSION | ProDOS kernel release id. ProDOS writes it. | TechRef §5.2.4 |

`KVERSION` values a primary source states:

| Release | `$BFFF` | Source |
|---------|---------|--------|
| 1.1.1-era manual listing | `$02` | TechRef §5.2.4 prints `KVERSION DFB $2`. [TN #23](https://prodos8.com/docs/technote/23/) Table 1 says that manual covers 1.1.1. |
| 1.4 image named by TN #23 | `4` | TN #23 writes `KVERSION = 4` for the 1.4 image on the Apple IIe Workstation Card server. |
| 1.9 | `$08` | TN #23: "KVERSION is still $08." The note does not name the release that first stored `$08`. |
| 2.0.1 | `$21` | TN #23 |
| 2.0.3 | `$23` | [`GLOBALS.S`](https://github.com/markpmlim/ProDOS8/blob/master/MLI.SRC/GLOBALS.S): `kVersion DB $23 ;Represents release 2.03` |
| 2.4 and 2.4.1 | `$24` | [ProDOS 2.4.1 release note](https://prodos8.com/releases/prodos-241/) |
| 2.4.2 and 2.4.3 | `$24` | The `PRODOS` file on the official 2.4.2 and 2.4.3 disk images carries the same global-page tail as 2.4.1, `04 00 00 00 00 24`. The 2.4.1 note identifies the last byte as `KVERSION`. |

TN #23 says ProDOS 1.1 incremented `KVERSION` and does not print the old or new byte. These releases are not given a `$BFFF` value in TN #23, the TechRef listing, `GLOBALS.S`, or the 2.4.x notes checked here: 1.0, 1.0.1, 1.0.2, 1.2, 1.3, 1.5, 1.6, 1.7, 1.8, 2.0.0, and 2.0.2.

---

## Machine Language Interface (MLI)

The MLI is ProDOS's programmatic interface. All operations use a consistent calling convention.

### Calling Convention

```assembly
        JSR $BF00       ; MLI entry point
        DB  CMD         ; Command number
        DW  PARMLIST    ; Pointer to parameter list
        BCS ERROR       ; Carry set on error
        ; Success - A = 0
ERROR:  ; A = error code
```

### MLI Commands

#### Housekeeping Calls

| Code | Name           | Description |
|------|----------------|-------------|
| $40  | ALLOC_INTERRUPT| Install interrupt handler |
| $41  | DEALLOC_INTERRUPT| Remove interrupt handler |
| $65  | QUIT           | Exit to ProDOS |
| $80  | READ_BLOCK     | Read disk block |
| $81  | WRITE_BLOCK    | Write disk block |
| $82  | GET_TIME       | Update time globals |

#### Filing Calls

| Code | Name           | Description |
|------|----------------|-------------|
| $C0  | CREATE         | Create file or directory |
| $C1  | DESTROY        | Delete file or directory |
| $C2  | RENAME         | Rename file or directory |
| $C3  | SET_FILE_INFO  | Set file attributes |
| $C4  | GET_FILE_INFO  | Get file attributes |
| $C5  | ON_LINE        | Get volume names |
| $C6  | SET_PREFIX     | Set current directory |
| $C7  | GET_PREFIX     | Get current directory |
| $C8  | OPEN           | Open file |
| $C9  | NEWLINE        | Set newline mode |
| $CA  | READ           | Read from file |
| $CB  | WRITE          | Write to file |
| $CC  | CLOSE          | Close file |
| $CD  | FLUSH          | Flush file buffer |
| $CE  | SET_MARK       | Set file position |
| $CF  | GET_MARK       | Get file position |
| $D0  | SET_EOF        | Set file size |
| $D1  | GET_EOF        | Get file size |
| $D2  | SET_BUF        | Set file buffer address |
| $D3  | GET_BUF        | Get file buffer address |

---

## MLI Call Details

### CREATE ($C0)

**Purpose:** Create a new file or directory.

**Parameter List (7 bytes):**

| Offset | Size | Name         | Description |
|--------|------|--------------|-------------|
| $00    | 1    | PARAM_COUNT  | $07 |
| $01    | 2    | PATHNAME     | Pointer to pathname |
| $03    | 1    | ACCESS       | Access permissions |
| $04    | 1    | FILE_TYPE    | File type |
| $05    | 2    | AUX_TYPE     | Auxiliary type |
| $07    | 1    | STORAGE_TYPE | Storage type ($0D=dir, $01=file) |

**Example:**
```assembly
CREATE_PARMS:
        DB $07          ; Parameter count
        DW PATHNAME     ; Pointer to pathname
        DB $C3          ; Access: rename/delete/read/write
        DB $06          ; File type: BIN
        DW $2000        ; Aux type: load address
        DB $01          ; Storage: seedling file
PATHNAME:
        DB $0A          ; Length
        ASC "MYFILE.BIN"

        JSR $BF00
        DB  $C0
        DW  CREATE_PARMS
```

### DESTROY ($C1)

**Purpose:** Delete a file or directory (directory must be empty).

**Parameter List (3 bytes):**

| Offset | Size | Name         | Description |
|--------|------|--------------|-------------|
| $00    | 1    | PARAM_COUNT  | $01 |
| $01    | 2    | PATHNAME     | Pointer to pathname |

### RENAME ($C2)

**Purpose:** Rename a file or directory.

**Parameter List (5 bytes):**

| Offset | Size | Name         | Description |
|--------|------|--------------|-------------|
| $00    | 1    | PARAM_COUNT  | $02 |
| $01    | 2    | PATHNAME     | Pointer to old pathname |
| $03    | 2    | NEW_PATHNAME | Pointer to new pathname |

### GET_FILE_INFO ($C4)

**Purpose:** Get file information.

**Parameter List (18 bytes):**

| Offset | Size | Name         | Description |
|--------|------|--------------|-------------|
| $00    | 1    | PARAM_COUNT  | $0A |
| $01    | 2    | PATHNAME     | Pointer to pathname |
| $03    | 1    | ACCESS       | (returned) |
| $04    | 1    | FILE_TYPE    | (returned) |
| $05    | 2    | AUX_TYPE     | (returned) |
| $07    | 1    | STORAGE_TYPE | (returned) |
| $08    | 2    | BLOCKS_USED  | (returned) |
| $0A    | 2    | MOD_DATE     | (returned) |
| $0C    | 2    | MOD_TIME     | (returned) |
| $0E    | 2    | CREATE_DATE  | (returned) |
| $10    | 2    | CREATE_TIME  | (returned) |

### OPEN ($C8)

**Purpose:** Open a file for I/O.

**Parameter List (4 bytes):**

| Offset | Size | Name         | Description |
|--------|------|--------------|-------------|
| $00    | 1    | PARAM_COUNT  | $03 |
| $01    | 2    | PATHNAME     | Pointer to pathname |
| $03    | 2    | IO_BUFFER    | 1024-byte buffer address |
| $05    | 1    | REF_NUM      | (returned) Reference number |

**Example:**
```assembly
OPEN_PARMS:
        DB $03          ; Parameter count
        DW PATHNAME     ; Pathname pointer
        DW $1C00        ; I/O buffer (1024 bytes)
        DB $00          ; Ref num (returned)

        JSR $BF00
        DB  $C8
        DW  OPEN_PARMS
        BCS ERROR
        LDA OPEN_PARMS+5 ; Get ref num
```

### READ ($CA)

**Purpose:** Read data from an open file.

**Parameter List (5 bytes):**

| Offset | Size | Name         | Description |
|--------|------|--------------|-------------|
| $00    | 1    | PARAM_COUNT  | $04 |
| $01    | 1    | REF_NUM      | File reference number |
| $02    | 2    | DATA_BUFFER  | Destination address |
| $04    | 2    | REQUEST_COUNT| Bytes to read |
| $06    | 2    | TRANS_COUNT  | (returned) Bytes actually read |

### WRITE ($CB)

**Purpose:** Write data to an open file.

**Parameter List (5 bytes):**

| Offset | Size | Name         | Description |
|--------|------|--------------|-------------|
| $00    | 1    | PARAM_COUNT  | $04 |
| $01    | 1    | REF_NUM      | File reference number |
| $02    | 2    | DATA_BUFFER  | Source address |
| $04    | 2    | REQUEST_COUNT| Bytes to write |
| $06    | 2    | TRANS_COUNT  | (returned) Bytes actually written |

### CLOSE ($CC)

**Purpose:** Close an open file.

**Parameter List (2 bytes):**

| Offset | Size | Name         | Description |
|--------|------|--------------|-------------|
| $00    | 1    | PARAM_COUNT  | $01 |
| $01    | 1    | REF_NUM      | File reference number (0 = all) |

### SET_MARK ($CE)

**Purpose:** Set file position (seek).

**Parameter List (4 bytes):**

| Offset | Size | Name         | Description |
|--------|------|--------------|-------------|
| $00    | 1    | PARAM_COUNT  | $02 |
| $01    | 1    | REF_NUM      | File reference number |
| $02    | 3    | POSITION     | New position (24-bit) |

### GET_MARK ($CF)

**Purpose:** Get current file position.

**Parameter List (4 bytes):**

| Offset | Size | Name         | Description |
|--------|------|--------------|-------------|
| $00    | 1    | PARAM_COUNT  | $02 |
| $01    | 1    | REF_NUM      | File reference number |
| $02    | 3    | POSITION     | (returned) Current position |

### GET_EOF ($D1)

**Purpose:** Get file size.

**Parameter List (4 bytes):**

| Offset | Size | Name         | Description |
|--------|------|--------------|-------------|
| $00    | 1    | PARAM_COUNT  | $02 |
| $01    | 1    | REF_NUM      | File reference number |
| $02    | 3    | EOF          | (returned) File size |

### SET_PREFIX ($C6)

**Purpose:** Set current directory prefix.

**Parameter List (2 bytes):**

| Offset | Size | Name         | Description |
|--------|------|--------------|-------------|
| $00    | 1    | PARAM_COUNT  | $01 |
| $01    | 2    | PATHNAME     | Pointer to pathname |

**Note:** Pathname must begin with "/" for absolute, or no "/" for relative.

### GET_PREFIX ($C7)

**Purpose:** Get current directory prefix.

**Parameter List (2 bytes):**

| Offset | Size | Name         | Description |
|--------|------|--------------|-------------|
| $00    | 1    | PARAM_COUNT  | $01 |
| $01    | 2    | DATA_BUFFER  | Pointer to 64-byte buffer |

### ON_LINE ($C5)

**Purpose:** Get list of online volumes.

**Parameter List (4 bytes):**

| Offset | Size | Name         | Description |
|--------|------|--------------|-------------|
| $00    | 1    | PARAM_COUNT  | $02 |
| $01    | 1    | UNIT_NUM     | Unit (0 = all) |
| $02    | 2    | DATA_BUFFER  | 256-byte buffer |

### READ_BLOCK ($80)

**Purpose:** Read a 512-byte block directly from device.

**Parameter List (6 bytes):**

| Offset | Size | Name         | Description |
|--------|------|--------------|-------------|
| $00    | 1    | PARAM_COUNT  | $03 |
| $01    | 1    | UNIT_NUM     | Device unit number |
| $02    | 2    | DATA_BUFFER  | 512-byte buffer address |
| $04    | 2    | BLOCK_NUM    | Block number |

### WRITE_BLOCK ($81)

**Purpose:** Write a 512-byte block directly to device.

**Parameter List (6 bytes):**

| Offset | Size | Name         | Description |
|--------|------|--------------|-------------|
| $00    | 1    | PARAM_COUNT  | $03 |
| $01    | 1    | UNIT_NUM     | Device unit number |
| $02    | 2    | DATA_BUFFER  | 512-byte buffer address |
| $04    | 2    | BLOCK_NUM    | Block number |

### QUIT ($65)

**Purpose:** Exit program and return to ProDOS selector.

**Parameter List (5 bytes):**

| Offset | Size | Name         | Description |
|--------|------|--------------|-------------|
| $00    | 1    | PARAM_COUNT  | $04 |
| $01    | 1    | QUIT_TYPE    | $00 = normal |
| $02    | 2    | Unused       | |
| $04    | 1    | Unused       | |
| $05    | 2    | Unused       | |

---

## Interrupt Handlers

### ALLOC_INTERRUPT ($40)

**Purpose:** Install an interrupt handler.

**Parameter List (3 bytes):**

| Offset | Size | Name         | Description |
|--------|------|--------------|-------------|
| $00    | 1    | PARAM_COUNT  | $02 |
| $01    | 1    | INT_NUM      | (returned) Handler number |
| $02    | 2    | INT_CODE     | Pointer to handler code |

**Handler Requirements:**
- Must preserve all registers
- Must CLC and return if interrupt handled
- Must SEC and return if not your interrupt
- Handler must be in non-bank-switched memory

### DEALLOC_INTERRUPT ($41)

**Purpose:** Remove an interrupt handler.

**Parameter List (2 bytes):**

| Offset | Size | Name         | Description |
|--------|------|--------------|-------------|
| $00    | 1    | PARAM_COUNT  | $01 |
| $01    | 1    | INT_NUM      | Handler number to remove |

---

## File Types

| Code | Name | Description |
|------|------|-------------|
| $00  | UNK  | Unknown (typeless) |
| $01  | BAD  | Bad blocks file |
| $04  | TXT  | Text file |
| $06  | BIN  | Binary file |
| $0F  | DIR  | Directory |
| $19  | ADB  | AppleWorks database |
| $1A  | AWP  | AppleWorks word processor |
| $1B  | ASP  | AppleWorks spreadsheet |
| $EF  | PAS  | Pascal area |
| $F0  | CMD  | ProDOS added command |
| $FA  | INT  | Integer BASIC program |
| $FB  | IVR  | Integer BASIC variables |
| $FC  | BAS  | Applesoft BASIC program |
| $FD  | VAR  | Applesoft variables |
| $FE  | REL  | Relocatable code |
| $FF  | SYS  | System file (executable) |

### Auxiliary Type

The auxiliary type provides additional information:

- For BIN files: Load address
- For TXT files: Record length (0 = sequential)
- For SYS files: Load address (usually $2000)

---

## Error Codes

| Code | Name                    | Description |
|------|-------------------------|-------------|
| $00  | (none)                  | No error |
| $01  | BAD_SYSTEM_CALL         | Invalid MLI call number |
| $04  | BAD_PARAMETER           | Invalid parameter count |
| $25  | INTERRUPT_TBL_FULL      | Interrupt table full |
| $27  | I/O_ERROR               | Device I/O error |
| $28  | NO_DEVICE_CONNECTED     | Device not found |
| $2B  | WRITE_PROTECTED         | Disk is write-protected |
| $2E  | SWITCHED_DISK           | Disk switched during operation |
| $40  | INVALID_PATHNAME        | Bad pathname syntax |
| $42  | MAXFILES_EXCEEDED       | Too many files open |
| $43  | INVALID_REFERENCE_NUM   | Bad file reference number |
| $44  | DIRECTORY_NOT_FOUND     | Directory not found |
| $45  | VOLUME_NOT_FOUND        | Volume not found |
| $46  | FILE_NOT_FOUND          | File not found |
| $47  | DUPLICATE_FILENAME      | File already exists |
| $48  | VOLUME_FULL             | Disk full |
| $49  | DIRECTORY_FULL          | Directory full |
| $4A  | INCOMPATIBLE_FORMAT     | Not a ProDOS disk |
| $4B  | UNSUPPORTED_STORAGE     | Unknown storage type |
| $4C  | END_OF_FILE             | Read past end of file |
| $4D  | OUT_OF_RANGE            | Position out of range |
| $4E  | INVALID_ACCESS          | Access denied |
| $50  | FILE_IS_OPEN            | File already open |
| $51  | DIRECTORY_DAMAGED       | Directory structure corrupted |
| $52  | NOT_PRODOS_VOLUME       | Not a ProDOS volume |
| $53  | INVALID_PARAMETER       | Bad parameter value |
| $55  | VOLUME_DIRECTORY_FULL   | Volume directory full |
| $56  | BAD_BUFFER_ADDRESS      | Buffer address invalid |
| $57  | DUPLICATE_VOLUME        | Volume already online |
| $5A  | FILE_STRUCTURE_DAMAGED  | File structure corrupted |

---

## Pathname Format

ProDOS pathnames follow these rules:

- Maximum 64 characters total
- Volume name: 1-15 characters
- Each segment: 1-15 characters
- Valid characters: A-Z, 0-9, period
- Must start with letter
- Case-insensitive
- Stored as length-prefixed Pascal string

### Examples

| Pathname | Description |
|----------|-------------|
| /MYDISK  | Volume root |
| /MYDISK/GAMES | Directory |
| /MYDISK/GAMES/LODERUN | File |
| MYFILE   | Relative to current prefix |

---

## Disk Format

### Physical Format

- 512 bytes per block
- 280 blocks per 5.25" disk (140KB)
- 1600 blocks per 3.5" disk (800KB)

### Key Blocks

| Block | Contents |
|-------|----------|
| 0-1   | Boot code |
| 2     | Volume directory key block |
| 3-5   | Volume directory (continuation) |
| 6     | Volume bitmap start |

### Boot Block (Block 0)

The SmartPort / ProDOS bootstrap loads block 0 to `$0800` and jumps unconditionally to `$0801`.
The ProDOS PBOOT loader therefore begins with:

| Block offset | Byte | Description |
|-------------|------|-------------|
| `$00`       | `$01` | Boot block indicator |
| `$01`       | `$38` | `SEC` — first opcode executed at `$0801` |
| `$02`       | `$B0` | `BCS` opcode |
| `$03`       | `$03` | Branch offset |
| `$04`–`$1FF`| …    | Remainder of the PBOOT loader (block 0) and stage-2 loader (block 1) |

**Bootability signature (offsets 0–3):** `01 38 B0 03`

A freshly initialised or blank volume leaves block 0 as all-`$00` bytes; no valid boot block is
present and the volume is not bootable.

### Volume Directory Format (Block 2)

| Offset | Size | Description |
|--------|------|-------------|
| $00    | 2    | Prev block pointer (0) |
| $02    | 2    | Next block pointer |
| $04    | 1    | Entry type/name length |
| $05    | 15   | Volume name |
| $14    | 8    | Reserved |
| $1C    | 2    | Creation date |
| $1E    | 2    | Creation time |
| $20    | 1    | Version (0) |
| $21    | 1    | Min version (0) |
| $22    | 1    | Access |
| $23    | 1    | Entry length ($27) |
| $24    | 1    | Entries per block ($0D) |
| $25    | 2    | File count |
| $27    | 2    | Bitmap pointer |
| $29    | 2    | Total blocks |

### File Entry Format (39 bytes)

| Offset | Size | Description |
|--------|------|-------------|
| $00    | 1    | Storage type/name length |
| $01    | 15   | Filename |
| $10    | 1    | File type |
| $11    | 2    | Key pointer (first block) |
| $13    | 2    | Blocks used |
| $15    | 3    | EOF (file size) |
| $18    | 2    | Creation date |
| $1A    | 2    | Creation time |
| $1C    | 1    | Version |
| $1D    | 1    | Min version |
| $1E    | 1    | Access |
| $1F    | 2    | Auxiliary type |
| $21    | 2    | Last modified date |
| $23    | 2    | Last modified time |
| $25    | 2    | Header pointer |

### Storage Types (high nibble of byte $00)

| Type | Description |
|------|-------------|
| $0   | Deleted entry |
| $1   | Seedling (1-block file) |
| $2   | Sapling (2-256 blocks) |
| $3   | Tree (large file) |
| $D   | Subdirectory header |
| $E   | Subdirectory entry |
| $F   | Volume directory header |

---

## BASIC.SYSTEM Commands

BASIC.SYSTEM is loaded by ProDOS to provide BASIC commands similar to DOS 3.3.

### File Commands

| Command | Description |
|---------|-------------|
| CATALOG [pathname] | List directory |
| CAT [pathname]     | Abbreviated catalog |
| PREFIX [pathname]  | Set/show current directory |
| LOAD pathname      | Load BASIC program |
| SAVE pathname      | Save BASIC program |
| RUN pathname       | Load and run |
| CHAIN pathname     | Run preserving variables |
| DELETE pathname    | Delete file |
| LOCK pathname      | Lock file |
| UNLOCK pathname    | Unlock file |
| RENAME old,new     | Rename file |
| VERIFY pathname    | Verify file |
| CREATE pathname    | Create directory |

### Binary Commands

| Command | Description |
|---------|-------------|
| BLOAD pathname[,Aaddr][,Ttype]  | Load binary |
| BSAVE pathname,Aaddr,Llen[,Ttype] | Save binary |
| BRUN pathname[,Aaddr][,Ttype]  | Load and run binary |

### Text File Commands

| Command | Description |
|---------|-------------|
| OPEN pathname       | Open text file |
| CLOSE [pathname]    | Close file(s) |
| READ pathname       | Set file for reading |
| WRITE pathname      | Set file for writing |
| APPEND pathname     | Open for appending |
| EXEC pathname       | Execute text file as commands |

### System Commands

| Command | Description |
|---------|-------------|
| -pathname          | Run system file |
| FRE(0)             | Show/collect free memory |
| STORE pathname     | Save hi-res picture |
| RESTORE pathname   | Load hi-res picture |
| MONO               | Monochrome mode |
| COLOR              | Color mode |
| FLASH              | Flash text |
| INVERSE            | Inverse text |
| NORMAL             | Normal text |

---

## IIe vs IIc Differences

| Feature | Apple IIe | Apple IIc |
|---------|-----------|-----------|
| /RAM disk | Requires 128KB | Built-in 128KB |
| Boot slot | Usually slot 6 | Built-in |
| Clock | Clock card | No built-in clock; same card protocol |
| MACHID | Read `$BF98`. An Apple IIe with 80 columns and a clock is `$B3` (TN #11). | Read `$BF98`. Bit 3 set and bits 7–6 = `10` means IIc. |
| Default prefix | /slot6disk | /slot5disk |

### Clock Support

The Apple IIc has no built-in clock. The [Technical Reference Manual preface](https://prodos8.com/docs/techref/preface/) lists the IIc as an Apple IIe with 128K RAM, serial ports in slots 1 and 2, 80-column firmware in slot 3, a mouse in slot 4, and a disk controller in slot 6, and that list has no clock. ProDOS reads the date and time from a clock/calendar card when one is installed ([§1.1](https://prodos8.com/docs/techref/introduction/), [§6.1](https://prodos8.com/docs/techref/adding-routines-to-prodos/)). Bit 0 of `MACHID` reports that card.

For IIe, a clock card (like the ThunderClock) must be installed. ProDOS searches for clock drivers at boot.

The PocketWatch card answers those entries with `JSR $Cn0B` (latch) and `JSR $Cn08` (read). The read writes `mo,da,dt,hr,mn` at `$0200` in high-bit ASCII: digits `$B0`-`$B9` and commas `$AC`. ProDOS 8's built-in Thunderclock driver, `MLI.SRC/TCLOCK.S` (the same routine in 1.x, 2.0.3, and 2.4.3), converts each field with `LDA $0200,Y` / `AND #$07` / times ten / `ADC` the next byte / `SBC #"0"` (`#"0"` is `$B0`). Plain digits decode as garbage. See [TCLOCK.S](https://github.com/markpmlim/ProDOS8/blob/master/MLI.SRC/TCLOCK.S).

### RAM Disk (/RAM)

Both 128KB IIe and IIc support a /RAM volume using auxiliary memory:

- Size: 60KB (approximately)
- Slot 3, Drive 2 (/RAM)
- Contents lost on reboot
- ProDOS reserves space for /RAM bitmap

---

## Example: Reading a File

```assembly
; Complete file reading example
        
        ; Set prefix
        JSR MLI
        DB  $C6          ; SET_PREFIX
        DW  PREFIX_PARMS
        BCS ERROR
        
        ; Open file
        JSR MLI
        DB  $C8          ; OPEN
        DW  OPEN_PARMS
        BCS ERROR
        
        ; Read file
        LDA REF_NUM
        STA READ_PARMS+1
        JSR MLI
        DB  $CA          ; READ
        DW  READ_PARMS
        BCS ERROR
        
        ; Close file
        LDA REF_NUM
        STA CLOSE_PARMS+1
        JSR MLI
        DB  $CC          ; CLOSE
        DW  CLOSE_PARMS
        
        RTS

MLI     EQU $BF00

PREFIX_PARMS:
        DB $01
        DW PREFIX
PREFIX: DB 8
        ASC "/MYDISK"

OPEN_PARMS:
        DB $03
        DW FILENAME
        DW $1C00         ; I/O buffer
REF_NUM:DB $00

READ_PARMS:
        DB $04
        DB $00           ; Ref num (filled in)
        DW $2000         ; Data buffer
        DW $2000         ; Request count
        DW $0000         ; Trans count (returned)

CLOSE_PARMS:
        DB $01
        DB $00           ; Ref num

FILENAME:
        DB 8
        ASC "TESTFILE"

ERROR:  ; Handle error in A
        RTS
```

---

## Document History

| Version | Date       | Changes |
|---------|------------|---------|
| 1.0     | 2025-12-30 | Initial specification |
| 1.1     | 2026-11-24 | Corrected global-page vectors, `MACHID`, date/time bytes, and `KVERSION` / `IVERSION` against the TechRef, TN #11, TN #23, and TN #28. Removed the built-in IIc clock claim. |
| 1.2     | 2026-11-25 | Noted that `prodos-clock` fills `DATE`/`TIME` when `$BF06` is `RTS`. |
