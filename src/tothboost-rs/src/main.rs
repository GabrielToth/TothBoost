#![windows_subsystem = "windows"]

mod models;
mod scanner;
mod tweaks;

#[cfg(test)]
mod tests;

use scanner::{SystemScanner, Win32Ops};
use tweaks::RegistryTweaks;
use std::os::windows::ffi::OsStrExt;
use std::ptr::null_mut;

use windows::core::*;
use windows::Win32::Foundation::*;
use windows::Win32::Graphics::Gdi::*;
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::UI::WindowsAndMessaging::*;

const ID_BTN_BOOST: usize = 101;
const ID_BTN_TRIM_RAM: usize = 102;
const ID_BTN_CLEAN_JUNK: usize = 103;
const ID_BTN_SCAN_SERVICES: usize = 104;
const ID_BTN_APPLY_TWEAKS: usize = 105;

static mut HWND_STATUS: HWND = HWND(null_mut());

fn main() -> Result<()> {
    unsafe {
        let instance = GetModuleHandleW(PCWSTR::null())?;
        let class_name = w!("TothBoostClass");

        let wc = WNDCLASSW {
            lpfnWndProc: Some(wnd_proc),
            hInstance: instance.into(),
            lpszClassName: class_name,
            hbrBackground: HBRUSH((COLOR_WINDOW.0 + 1) as *mut std::ffi::c_void),
            ..Default::default()
        };

        RegisterClassW(&wc);

        let hwnd = CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            class_name,
            w!("TothBoost - Native Win32 Optimizer v1.0"),
            WS_OVERLAPPEDWINDOW | WS_VISIBLE,
            CW_USEDEFAULT,
            CW_USEDEFAULT,
            700,
            450,
            HWND::default(),
            HMENU::default(),
            instance,
            None,
        )?;

        let btn_style = WS_VISIBLE | WS_CHILD | WINDOW_STYLE(BS_PUSHBUTTON as u32);

        // Controls
        CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            w!("BUTTON"),
            w!("🚀 ONE-CLICK BOOST"),
            btn_style,
            20, 20, 180, 40,
            hwnd,
            HMENU(ID_BTN_BOOST as *mut _),
            instance,
            None,
        )?;

        CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            w!("BUTTON"),
            w!("🧹 Clean Junk & Temp"),
            btn_style,
            210, 20, 180, 40,
            hwnd,
            HMENU(ID_BTN_CLEAN_JUNK as *mut _),
            instance,
            None,
        )?;

        CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            w!("BUTTON"),
            w!("⚡ Trim RAM"),
            btn_style,
            400, 20, 180, 40,
            hwnd,
            HMENU(ID_BTN_TRIM_RAM as *mut _),
            instance,
            None,
        )?;

        CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            w!("BUTTON"),
            w!("🔍 Scan Services"),
            btn_style,
            20, 75, 180, 40,
            hwnd,
            HMENU(ID_BTN_SCAN_SERVICES as *mut _),
            instance,
            None,
        )?;

        CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            w!("BUTTON"),
            w!("⚙️ Apply Registry Tweaks"),
            btn_style,
            210, 75, 220, 40,
            hwnd,
            HMENU(ID_BTN_APPLY_TWEAKS as *mut _),
            instance,
            None,
        )?;

        HWND_STATUS = CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            w!("STATIC"),
            w!("Ready. Select an action above."),
            WS_VISIBLE | WS_CHILD,
            20, 130, 640, 260,
            hwnd,
            HMENU::default(),
            instance,
            None,
        )?;

        let mut msg = MSG::default();
        while GetMessageW(&mut msg, HWND::default(), 0, 0).into() {
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
        if HWND_STATUS.0 != null_mut() {
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
                ID_BTN_BOOST => {
                    let (trimmed, freed) = Win32Ops::trim_memory();
                    let (junk_files, junk_bytes) = Win32Ops::clean_junk();
                    let report = RegistryTweaks::apply_all();
                    let text = format!(
                        "=== BOOST COMPLETE ===\n\n- Trimmed Working Sets: {} processes ({:.2} MB freed)\n- Cleaned Junk: {} files ({:.2} MB freed)\n- Registry Tweaks: {}/{} applied successfully\n",
                        trimmed,
                        freed as f64 / (1024.0 * 1024.0),
                        junk_files,
                        junk_bytes as f64 / (1024.0 * 1024.0),
                        report.success_count,
                        report.total_actions
                    );
                    set_status_text(&text);
                }
                ID_BTN_TRIM_RAM => {
                    let (trimmed, freed) = Win32Ops::trim_memory();
                    let text = format!(
                        "RAM Trim Completed:\nSuccessfully emptied working sets for {} processes.\nFreed: {:.2} MB",
                        trimmed,
                        freed as f64 / (1024.0 * 1024.0)
                    );
                    set_status_text(&text);
                }
                ID_BTN_CLEAN_JUNK => {
                    let (files, bytes) = Win32Ops::clean_junk();
                    let text = format!(
                        "Junk Cleaner Completed:\nRemoved {} temp files.\nFreed: {:.2} MB",
                        files,
                        bytes as f64 / (1024.0 * 1024.0)
                    );
                    set_status_text(&text);
                }
                ID_BTN_SCAN_SERVICES => {
                    let services = SystemScanner::scan_services();
                    let procs = SystemScanner::scan_processes();
                    let text = format!(
                        "System Scanner Result:\nTotal Running Services: {}\nTotal Running Processes: {}\nTop Memory Process: {} ({} MB)",
                        services.len(),
                        procs.len(),
                        procs.first().map(|p| p.name.as_str()).unwrap_or("N/A"),
                        procs.first().map(|p| p.memory_mb).unwrap_or(0)
                    );
                    set_status_text(&text);
                }
                ID_BTN_APPLY_TWEAKS => {
                    let report = RegistryTweaks::apply_all();
                    let mut text = format!("Registry Tweaks Applied ({}/{}):\n\n", report.success_count, report.total_actions);
                    for msg in report.messages {
                        text.push_str(&format!("• {}\n", msg));
                    }
                    set_status_text(&text);
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
