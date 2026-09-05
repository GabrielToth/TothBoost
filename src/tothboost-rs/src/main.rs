#![windows_subsystem = "windows"]

mod elevation;
mod models;
mod power;
mod profile;
mod scanner;
mod service;
mod tweaks;

#[cfg(test)]
mod tests;

use elevation::ElevationManager;
use power::ProcessController;
use profile::{ProfileData, ProfileService};
use scanner::SystemScanner;
use service::{ServiceManager, ServiceStartKind};
use tweaks::RegistryTweaks;

use std::os::windows::ffi::OsStrExt;
use std::ptr::null_mut;

use windows::core::*;
use windows::Win32::Foundation::*;
use windows::Win32::Graphics::Gdi::*;
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::UI::WindowsAndMessaging::*;

const ID_BTN_KILL_PROC: usize = 101;
const ID_BTN_REFRESH_PROCS: usize = 102;
const ID_BTN_DISABLE_SVC: usize = 103;
const ID_BTN_ENABLE_SVC: usize = 104;
const ID_BTN_APPLY_TWEAK: usize = 105;
const ID_BTN_SAVE_PROFILE: usize = 106;

static mut HWND_STATUS: HWND = HWND(null_mut());

fn main() -> Result<()> {
    // 1. Mandatory Administrator enforcement upon startup (mirrors C# app requirement)
    ElevationManager::enforce_administrator();

    unsafe {
        let instance = GetModuleHandleW(PCWSTR::null())?;
        let class_name = w!("TothBoostClass");

        let wc = WNDCLASSW {
            style: CS_HREDRAW | CS_VREDRAW,
            lpfnWndProc: Some(wnd_proc),
            hInstance: instance.into(),
            lpszClassName: class_name,
            hbrBackground: HBRUSH((COLOR_WINDOW.0 + 1) as *mut std::ffi::c_void),
            hCursor: LoadCursorW(HMODULE::default(), IDC_ARROW)?,
            ..Default::default()
        };

        if RegisterClassW(&wc) == 0 {
            let err = GetLastError();
            if err.0 != 1410 {
                return Err(Error::from_win32());
            }
        }

        let title_str = "TothBoost v1.0 — Windows 10/11 Gaming Performance Optimizer [ADMINISTRATOR]";
        let title_wide = to_wide_null(title_str);

        let hwnd = CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            class_name,
            PCWSTR(title_wide.as_ptr()),
            WS_OVERLAPPEDWINDOW | WS_VISIBLE,
            CW_USEDEFAULT,
            CW_USEDEFAULT,
            800,
            600,
            HWND::default(),
            HMENU::default(),
            instance,
            None,
        )?;

        if hwnd.0.is_null() {
            return Err(Error::from_win32());
        }

        let btn_style = WS_VISIBLE | WS_CHILD | WINDOW_STYLE(BS_PUSHBUTTON as u32);

        // Control Panel
        CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            w!("BUTTON"),
            w!("Refresh Processes"),
            btn_style,
            20, 20, 170, 36,
            hwnd,
            HMENU(ID_BTN_REFRESH_PROCS as *mut _),
            instance,
            None,
        )?;

        CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            w!("BUTTON"),
            w!("Terminate Process"),
            btn_style,
            200, 20, 170, 36,
            hwnd,
            HMENU(ID_BTN_KILL_PROC as *mut _),
            instance,
            None,
        )?;

        CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            w!("BUTTON"),
            w!("Disable Selected Service"),
            btn_style,
            380, 20, 190, 36,
            hwnd,
            HMENU(ID_BTN_DISABLE_SVC as *mut _),
            instance,
            None,
        )?;

        CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            w!("BUTTON"),
            w!("Enable Selected Service"),
            btn_style,
            580, 20, 190, 36,
            hwnd,
            HMENU(ID_BTN_ENABLE_SVC as *mut _),
            instance,
            None,
        )?;

        HWND_STATUS = CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            w!("STATIC"),
            w!("Ready. Running with full Administrator privileges."),
            WS_VISIBLE | WS_CHILD,
            20, 70, 750, 470,
            hwnd,
            HMENU::default(),
            instance,
            None,
        )?;

        ShowWindow(hwnd, SW_SHOW);
        let _ = UpdateWindow(hwnd);

        let mut msg = MSG::default();
        while GetMessageW(&mut msg, HWND::default(), 0, 0).as_bool() {
            let _ = TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }
    Ok(())
}

fn to_wide_null(s: &str) -> Vec<u16> {
    std::ffi::OsStr::new(s).encode_wide().chain(std::iter::once(0)).collect()
}

fn set_status_text(text: &str) {
    unsafe {
        if !HWND_STATUS.0.is_null() {
            let wide = to_wide_null(text);
            let _ = SetWindowTextW(HWND_STATUS, PCWSTR(wide.as_ptr()));
        }
    }
}

unsafe extern "system" fn wnd_proc(
    hwnd: HWND,
    msg: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    match msg {
        WM_COMMAND => {
            let id = wparam.0 & 0xffff;
            match id {
                ID_BTN_REFRESH_PROCS => {
                    let procs = SystemScanner::scan_processes();
                    let svcs = SystemScanner::scan_services();
                    let tweaks = RegistryTweaks::get_available_tweaks();
                    let text = format!(
                        "=== TOTHBOOST SYSTEM SCAN SUMMARY ===\n\n- Running User & System Processes: {}\n- Registered Windows Services: {}\n- Available Tweaks in Catalog: {}\n\nTop Memory Process: {} ({} MB)\n",
                        procs.len(),
                        svcs.len(),
                        tweaks.len(),
                        procs.first().map(|p| p.name.as_str()).unwrap_or("N/A"),
                        procs.first().map(|p| p.memory_mb).unwrap_or(0)
                    );
                    set_status_text(&text);
                }
                ID_BTN_DISABLE_SVC => {
                    match ServiceManager::disable("SysMain") {
                        Ok(()) => set_status_text("Disabled SysMain service in HKLM registry."),
                        Err(e) => set_status_text(&format!("Error disabling SysMain: {}", e)),
                    }
                }
                ID_BTN_ENABLE_SVC => {
                    match ServiceManager::enable_auto("SysMain") {
                        Ok(()) => set_status_text("Enabled SysMain service (Automatic) in HKLM registry."),
                        Err(e) => set_status_text(&format!("Error enabling SysMain: {}", e)),
                    }
                }
                _ => {}
            }
            LRESULT(0)
        }
        WM_DESTROY => {
            PostQuitMessage(0);
            LRESULT(0)
        }
        _ => DefWindowProcW(hwnd, msg, wparam, lparam),
    }
}
