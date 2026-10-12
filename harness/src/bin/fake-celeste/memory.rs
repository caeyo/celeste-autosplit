//! The fake's own memory: mappings where the auto splitter looks for the game, and an arena for
//! the game's objects.

use std::ptr;

use anyhow::{Context, Result, bail};

/// Maps `size` bytes, read-write, at the first of the addresses given that's free, or anywhere
/// if none is given. Each mapping starts a memory region of its own.
pub fn map(size: usize, at: &[u64]) -> Result<u64> {
    if at.is_empty() {
        return sys::map(size, None).with_context(|| format!("couldn't map {size} bytes"));
    }
    for &address in at {
        if let Some(mapped) = sys::map(size, Some(address)) {
            return Ok(mapped);
        }
        eprintln!("fake-celeste: 0x{address:x} isn't free, trying the next address");
    }
    bail!("couldn't map {size} bytes at any of {at:x?}");
}

/// Makes a mapping read-only and executable, once its code is written.
pub fn make_executable(address: u64, size: usize) -> Result<()> {
    if !sys::make_executable(address, size) {
        bail!("couldn't make 0x{address:x} executable");
    }
    Ok(())
}

pub fn write(address: u64, bytes: &[u8]) {
    // SAFETY: Only ever called on the fake's own mappings, which are read-write until made
    // executable, and nothing else uses them.
    unsafe { ptr::copy_nonoverlapping(bytes.as_ptr(), address as *mut u8, bytes.len()) }
}

pub fn read<const N: usize>(address: u64) -> [u8; N] {
    // SAFETY: As for `write`.
    unsafe { ptr::read_unaligned(address as *const [u8; N]) }
}

/// Objects, allocated in order from the start of a mapping.
pub struct Arena {
    next: u64,
    end: u64,
}

impl Arena {
    pub fn map(size: usize, at: &[u64]) -> Result<Self> {
        let start = map(size, at)?;
        Ok(Self {
            next: start,
            end: start + size as u64,
        })
    }

    /// Zeroed, and 8-byte aligned.
    pub fn alloc(&mut self, size: u64) -> Result<u64> {
        let address = self.next;
        if size > self.end - address {
            bail!("out of memory for objects");
        }
        self.next = (address + size).next_multiple_of(8).min(self.end);
        Ok(address)
    }
}

#[cfg(unix)]
mod sys {
    use std::ptr;

    use libc::{
        MAP_ANON, MAP_FAILED, MAP_PRIVATE, PROT_EXEC, PROT_NONE, PROT_READ, PROT_WRITE, c_void,
    };

    fn page_size() -> usize {
        // SAFETY: Just a query.
        unsafe { libc::sysconf(libc::_SC_PAGESIZE) as usize }
    }

    pub fn map(size: usize, at: Option<u64>) -> Option<u64> {
        let page = page_size();
        let size = size.next_multiple_of(page);
        // A page with no access on each side, so that the mapping can't merge with a neighbour
        // that has the same protection: the auto splitter must see where it starts.
        let total = size + 2 * page;
        let hint = at.map_or(ptr::null_mut(), |address| {
            (address as usize - page) as *mut c_void
        });
        // SAFETY: A new mapping, which only replaces nothing, as there's no MAP_FIXED. The kernel
        // takes the address as a hint, and uses it if it's free.
        let base = unsafe { libc::mmap(hint, total, PROT_NONE, MAP_PRIVATE | MAP_ANON, -1, 0) };
        if base == MAP_FAILED {
            return None;
        }
        // SAFETY: The mapping just made, which nothing uses yet.
        unsafe {
            if !hint.is_null() && base != hint {
                libc::munmap(base, total);
                return None;
            }
            let start = base.byte_add(page);
            if libc::mprotect(start, size, PROT_READ | PROT_WRITE) != 0 {
                libc::munmap(base, total);
                return None;
            }
            Some(start as u64)
        }
    }

    pub fn make_executable(address: u64, size: usize) -> bool {
        let size = size.next_multiple_of(page_size());
        // SAFETY: Only called on the fake's own mappings, once it's done writing them.
        unsafe { libc::mprotect(address as *mut c_void, size, PROT_READ | PROT_EXEC) == 0 }
    }
}

#[cfg(windows)]
mod sys {
    use std::{ffi::c_void, ptr};

    use windows_sys::Win32::System::Memory::{
        MEM_COMMIT, MEM_RESERVE, PAGE_EXECUTE_READ, PAGE_READWRITE, VirtualAlloc, VirtualProtect,
    };

    // Each allocation is a region of its own, so no guard pages are needed.
    pub fn map(size: usize, at: Option<u64>) -> Option<u64> {
        let address = at.map_or(ptr::null(), |address| address as *const c_void);
        // SAFETY: A new allocation. At a given address, it fails unless that's free.
        let mapped =
            unsafe { VirtualAlloc(address, size, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE) };
        (!mapped.is_null()).then_some(mapped as u64)
    }

    pub fn make_executable(address: u64, size: usize) -> bool {
        let mut old = 0;
        // SAFETY: Only called on the fake's own allocations, once it's done writing them.
        unsafe { VirtualProtect(address as *const c_void, size, PAGE_EXECUTE_READ, &mut old) != 0 }
    }
}
