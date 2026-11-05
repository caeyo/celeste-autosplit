pub const MAGIC_BYTES: [u8; 20] = *b"EVERESTAUTOSPLIT\xf0\xf1\xf2\xf3";
pub const CURRENT_VERSION: u8 = 3;
pub const UPDATING_VERSION: u8 = 0xff;

pub const CELESTE_VERSION_MAJOR: u64 = 0x14;
pub const CELESTE_VERSION_MINOR: u64 = 0x15;
pub const CELESTE_VERSION_BUILD: u64 = 0x16;
pub const INFO_VERSION: u64 = 0x17;
pub const EVEREST_VERSION_STR_PTR: u64 = 0x18;
pub const LEVEL_SET_STR_PTR: u64 = 0x20;
pub const CHAPTER_SID_STR_PTR: u64 = 0x28;
pub const CHAPTER_ID: u64 = 0x30;
pub const CHAPTER_MODE: u64 = 0x34;
pub const ROOM_NAME_STR_PTR: u64 = 0x38;
pub const CHAPTER_TIME: u64 = 0x40;
pub const CHAPTER_STRAWBERRIES: u64 = 0x48;
pub const CHAPTER_FLAGS: u64 = 0x4c;
pub const FILE_TIME: u64 = 0x50;
pub const FILE_STRAWBERRIES: u64 = 0x58;
pub const FILE_GOLDEN_STRAWBERRIES: u64 = 0x5c;
pub const FILE_CASSETTES: u64 = 0x60;
pub const FILE_HEARTS: u64 = 0x64;
pub const FILE_FLAGS: u64 = 0x68;

pub const CHAPTER_STARTED: u32 = 1 << 0;
pub const CHAPTER_COMPLETE: u32 = 1 << 1;
pub const CHAPTER_CASSETTE: u32 = 1 << 2;
pub const CHAPTER_HEART: u32 = 1 << 3;
pub const GRABBED_GOLDEN: u32 = 1 << 4;
pub const TIMER_ACTIVE: u32 = 1 << 31;

pub const IS_DEBUG: u32 = 1 << 0;
pub const ASSIST_MODE: u32 = 1 << 1;
pub const VARIANTS_MODE: u32 = 1 << 2;
pub const STARTING_NEW_FILE: u32 = 1 << 30;
pub const FILE_ACTIVE: u32 = 1 << 31;
