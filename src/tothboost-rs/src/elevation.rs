use std::os::windows::ffi::OsStrExt;
use windows::core::*;
use windows::Win32::Foundation::*;
use windows::Win32::Security::*;
use windows::Win32::System::Threading::*;
use windows::Win32::UI::Shell::*;
use windows::Win32::UI::WindowsAndMessaging::*;

pub struct ElevationManager;

impl ElevationManager {
    pub fn is_elevated() -> bool {
        unsafe {
            let mut token = HANDLE::default();
            if OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &mut token).is_ok() {
                let mut elevation = TOKEN_ELEVATION::default();
                let mut return_length = 0u32;
                let res = GetTokenInformation(
                    token,
                    TokenElevation,
                    Some(&mut elevation as *mut _ as *mut _),
                    std::mem::size_of::<TOKEN_ELEVATION>() as u32,
                    &mut return_length,
                );
                let _ = CloseHandle(token);
                if res.is_ok() {
                    return elevation.TokenIsElevated != 0;
                }
            }
        }
        false
    }

    /// Enforces Administrator rights. Relaunches via UAC if not admin, or exits if declined.
    pub fn enforce_administrator() {
        if !Self::is_elevated() {
            unsafe {
                let exe_path = match std::env::current_exe() {
                    Ok(p) => p,
                    Err(_) => std::process::exit(1),
                };
                let exe_wide: Vec<u16> = exe_path.as_os_str().encode_wide().chain(std::iter::once(0)).collect();

                let res = ShellExecuteW(
                    HWND::default(),
                    w!("runas"),
                    PCWSTR(exe_wide.as_ptr()),
                    PCWSTR::null(),
                    PCWSTR::null(),
                    SW_SHOWNORMAL,
                );

                // If launched or UAC shown, exit the non-elevated process
                if res.0 as usize > 32 {
                    std::process::exit(0);
                } else {
                    // User canceled UAC prompt
                    MessageBoxW(
                        HWND::default(),
                        w!("TothBoost requires Administrator privileges to manage Windows services, processes, and performance tweaks."),
                        w!("Administrator Rights Required"),
                        MB_OK | MB_ICONERROR,
                    );
                    std::process::exit(1);
                }
            }
        }
    }
}
