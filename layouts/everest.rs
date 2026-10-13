// Everest's own auto splitter info, the struct CoreAutoSplitterInfo, transcribed by hand from
// https://github.com/EverestAPI/Everest/blob/47d6a61fa918c17b8085b93b61ee59a7465968f8/Celeste.Mod.mm/Mod/Everest/AutoSplitter.cs
// in Everest stable-1.6623.0. To update it, transcribe the struct again at a newer commit, and
// update the link.
//
// Everest puts the struct at the start of a memory mapping of its own. Each field's offset is from
// the struct's start, with its type in a comment. Everest runs on 64-bit .NET, so a pointer takes
// 8 bytes. A string is a pointer to NUL-terminated UTF-8, with its length in bytes in the u16
// before it, or 0 for no string.

/// `MagicBytes`, which the struct starts with.
pub const MAGIC_BYTES: [u8; 20] = *b"EVERESTAUTOSPLIT\xf0\xf1\xf2\xf3";
/// `CurrentVersion`, which `InfoVersion` holds once Everest has written the struct.
pub const CURRENT_VERSION: u8 = 3;
/// What `InfoVersion` holds while Everest updates the struct.
pub const UPDATING_VERSION: u8 = 0xff;

pub const CELESTE_VERSION_MAJOR: u64 = 0x14; // u8
pub const CELESTE_VERSION_MINOR: u64 = 0x15; // u8
pub const CELESTE_VERSION_BUILD: u64 = 0x16; // u8
pub const INFO_VERSION: u64 = 0x17; // u8
pub const EVEREST_VERSION_STR_PTR: u64 = 0x18; // string
pub const LEVEL_SET_STR_PTR: u64 = 0x20; // string
pub const CHAPTER_SID_STR_PTR: u64 = 0x28; // string
pub const CHAPTER_ID: u64 = 0x30; // i32
pub const CHAPTER_MODE: u64 = 0x34; // i32
pub const ROOM_NAME_STR_PTR: u64 = 0x38; // string
pub const CHAPTER_TIME: u64 = 0x40; // i64
pub const CHAPTER_STRAWBERRIES: u64 = 0x48; // i32
pub const CHAPTER_FLAGS: u64 = 0x4c; // u32, AutoSplitterChapterFlags
pub const FILE_TIME: u64 = 0x50; // i64
pub const FILE_STRAWBERRIES: u64 = 0x58; // i32
pub const FILE_GOLDEN_STRAWBERRIES: u64 = 0x5c; // i32
pub const FILE_CASSETTES: u64 = 0x60; // i32
pub const FILE_HEARTS: u64 = 0x64; // i32
pub const FILE_FLAGS: u64 = 0x68; // u32, AutoSplitterFileFlags

// AutoSplitterChapterFlags
pub const CHAPTER_STARTED: u32 = 1 << 0;
pub const CHAPTER_COMPLETE: u32 = 1 << 1;
pub const CHAPTER_CASSETTE: u32 = 1 << 2;
pub const CHAPTER_HEART: u32 = 1 << 3;
pub const GRABBED_GOLDEN: u32 = 1 << 4;
pub const TIMER_ACTIVE: u32 = 1 << 31;

// AutoSplitterFileFlags
pub const IS_DEBUG: u32 = 1 << 0;
pub const ASSIST_MODE: u32 = 1 << 1;
pub const VARIANTS_MODE: u32 = 1 << 2;
pub const STARTING_NEW_FILE: u32 = 1 << 30;
pub const FILE_ACTIVE: u32 = 1 << 31;
