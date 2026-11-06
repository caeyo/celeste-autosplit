use core::mem::offset_of;

use bytemuck::{Pod, Zeroable};

pub const MAGIC_BYTES: [u8; 20] = *b"EVERESTAUTOSPLIT\xf0\xf1\xf2\xf3";
pub const CURRENT_VERSION: u8 = 3;
pub const UPDATING_VERSION: u8 = 0xff;

#[derive(Clone, Copy, Pod, Zeroable)]
#[repr(C)]
pub struct CoreAutoSplitterInfo {
    pub magic: [u8; 20],
    pub celeste_version_major: u8,
    pub celeste_version_minor: u8,
    pub celeste_version_build: u8,
    pub info_version: u8,
    pub everest_version_str_ptr: u64,
    pub level_set_str_ptr: u64,
    pub chapter_sid_str_ptr: u64,
    pub chapter_id: i32,
    pub chapter_mode: i32,
    pub room_name_str_ptr: u64,
    pub chapter_time: i64,
    pub chapter_strawberries: i32,
    pub chapter_flags: u32,
    pub file_time: i64,
    pub file_strawberries: i32,
    pub file_golden_strawberries: i32,
    pub file_cassettes: i32,
    pub file_hearts: i32,
    pub file_flags: u32,
    pub padding: [u8; 4],
}

pub const INFO_VERSION: u64 = offset_of!(CoreAutoSplitterInfo, info_version) as u64;

const _: () = {
    assert!(size_of::<CoreAutoSplitterInfo>() == 0x70);
    assert!(offset_of!(CoreAutoSplitterInfo, magic) == 0x00);
    assert!(offset_of!(CoreAutoSplitterInfo, celeste_version_major) == 0x14);
    assert!(offset_of!(CoreAutoSplitterInfo, celeste_version_minor) == 0x15);
    assert!(offset_of!(CoreAutoSplitterInfo, celeste_version_build) == 0x16);
    assert!(offset_of!(CoreAutoSplitterInfo, info_version) == 0x17);
    assert!(offset_of!(CoreAutoSplitterInfo, everest_version_str_ptr) == 0x18);
    assert!(offset_of!(CoreAutoSplitterInfo, level_set_str_ptr) == 0x20);
    assert!(offset_of!(CoreAutoSplitterInfo, chapter_sid_str_ptr) == 0x28);
    assert!(offset_of!(CoreAutoSplitterInfo, chapter_id) == 0x30);
    assert!(offset_of!(CoreAutoSplitterInfo, chapter_mode) == 0x34);
    assert!(offset_of!(CoreAutoSplitterInfo, room_name_str_ptr) == 0x38);
    assert!(offset_of!(CoreAutoSplitterInfo, chapter_time) == 0x40);
    assert!(offset_of!(CoreAutoSplitterInfo, chapter_strawberries) == 0x48);
    assert!(offset_of!(CoreAutoSplitterInfo, chapter_flags) == 0x4c);
    assert!(offset_of!(CoreAutoSplitterInfo, file_time) == 0x50);
    assert!(offset_of!(CoreAutoSplitterInfo, file_strawberries) == 0x58);
    assert!(offset_of!(CoreAutoSplitterInfo, file_golden_strawberries) == 0x5c);
    assert!(offset_of!(CoreAutoSplitterInfo, file_cassettes) == 0x60);
    assert!(offset_of!(CoreAutoSplitterInfo, file_hearts) == 0x64);
    assert!(offset_of!(CoreAutoSplitterInfo, file_flags) == 0x68);
    assert!(offset_of!(CoreAutoSplitterInfo, padding) == 0x6c);
};

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
