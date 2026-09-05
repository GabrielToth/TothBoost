#![windows_subsystem = "windows"]

mod elevation;
mod models;
mod power;
mod scanner;
mod service;
mod tweaks;

#[cfg(test)]
mod tests;

use elevation::ElevationManager;
use power::{NetworkManager, PowerManager};
use scanner::{SystemScanner, Win32Ops};
use service::ServiceController;
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
const ID_BTN_ELEVATE: usize = 106;
const ID_BTN_POWER_PLAN: usize = 107;
const ID_BTN_GAMING_SERVICES: usize = 108;
const ID_BTN_FLUSH_DNS: usize = 109;

static mut HWND_STATUS: HWND = HWND(null_mut());

fn main() -> Result<()> {
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
            // 1410 = ERROR_CLASS_ALREADY_EXISTS, which is okay if re-registered
            if err.0 != 1410 {
                return Err(Error::from_win32());
            }
        }

        let is_admin = ElevationManager::is_elevated();
        let title_str = format!(
            "TothBoost - Native Win32 Optimizer v1.0 [{}]",
            if is_admin { "ADMINISTRATOR" } else { "LIMITED USER" }
        );
        let title_wide = to_wide_null(&title_str);

        let hwnd = CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            class_name,
            PCWSTR(title_wide.as_ptr()),
            WS_OVERLAPPEDWINDOW | WS_VISIBLE,
            CW_USEDEFAULT,
            CW_USEDEFAULT,
            740,
            500,
            HWND::default(),
            HMENU::default(),
            instance,
            None,
        )?;

        if hwnd.0.is_null() {
            return Err(Error::from_win32());
        }

        let btn_style = WS_VISIBLE | WS_CHILD | WINDOW_STYLE(BS_PUSHBUTTON as u32);

        // Row 1
        CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            w!("BUTTON"),
            w!("🚀 ONE-CLICK BOOST"),
            btn_style,
            20, 20, 210, 40,
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
            245, 20, 210, 40,
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
            470, 20, 220, 40,
            hwnd,
            HMENU(ID_BTN_TRIM_RAM as *mut _),
            instance,
            None,
        )?;

        // Row 2
        CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            w!("BUTTON"),
            w!("🔍 Scan Services"),
            btn_style,
            20, 70, 210, 40,
            hwnd,
            HMENU(ID_BTN_SCAN_SERVICES as *mut _),
            instance,
            None,
        )?;

        CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            w!("BUTTON"),
            w!("⚙️ Registry Tweaks"),
            btn_style,
            245, 70, 210, 40,
            hwnd,
            HMENU(ID_BTN_APPLY_TWEAKS as *mut _),
            instance,
            None,
        )?;

        CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            w!("BUTTON"),
            w!("🎮 Gaming Services"),
            btn_style,
            470, 70, 220, 40,
            hwnd,
            HMENU(ID_BTN_GAMING_SERVICES as *mut _),
            instance,
            None,
        )?;

        // Row 3
        CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            w!("BUTTON"),
            w!("⚡ High Power Plan"),
            btn_style,
            20, 120, 210, 40,
            hwnd,
            HMENU(ID_BTN_POWER_PLAN as *mut _),
            instance,
            None,
        )?;

        CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            w!("BUTTON"),
            w!("🌐 Flush DNS"),
            btn_style,
            245, 120, 210, 40,
            hwnd,
            HMENU(ID_BTN_FLUSH_DNS as *mut _),
            instance,
            None,
        )?;

        if !is_admin {
            CreateWindowExW(
                WINDOW_EX_STYLE::default(),
                w!("BUTTON"),
                w!("🛡️ Relaunch as Admin"),
                btn_style,
                470, 120, 220, 40,
                hwnd,
                HMENU(ID_BTN_ELEVATE as *mut _),
                instance,
                None,
            )?;
        }

        HWND_STATUS = CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            w!("STATIC"),
            w!("Ready. Select an action above."),
            WS_VISIBLE | WS_CHILD,
            20, 180, 670, 260,
            hwnd,
            HMENU::default(),
            instance,
            None,
        )?;

        if !is_admin {
            set_status_text("⚠️ WARNING: Running as standard user. Some tweaks (Registry, Services, Power Plan) require Administrator elevation.\nClick '🛡️ Relaunch as Admin' to grant full performance capabilities.");
        }

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
                ID_BTN_BOOST => {
                    let (trimmed, freed) = Win32Ops::trim_memory();
                    let (junk_files, junk_bytes) = Win32Ops::clean_junk();
                    let report = RegistryTweaks::apply_all();
                    let power_res = PowerManager::set_high_performance();
                    let dns_res = NetworkManager::flush_dns();
                    let service_results = ServiceController::optimize_for_gaming();
                    let svc_ok = service_results.iter().filter(|(_, ok)| *ok).count();

                    let text = format!(
                        "=== ULTIMATE BOOST COMPLETE ===\n\n- Trimmed Working Sets: {} processes ({:.2} MB freed)\n- Cleaned Junk: {} files ({:.2} MB freed)\n- Registry Tweaks: {}/{} applied\n- Gaming Services: {}/{} optimized\n- Power Plan: {}\n- DNS Flush: {}\n",
                        trimmed,
                        freed as f64 / (1024.0 * 1024.0),
                        junk_files,
                        junk_bytes as f64 / (1024.0 * 1024.0),
                        report.success_count,
                        report.total_actions,
                        svc_ok,
                        service_results.len(),
                        if power_res.is_ok() { "High Performance Activated" } else { "Failed (Admin Required)" },
                        if dns_res.is_ok() { "DNS Cache Cleared" } else { "Failed" }
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
                ID_BTN_GAMING_SERVICES => {
                    let results = ServiceController::optimize_for_gaming();
                    let mut text = format!("Gaming Services Optimization Results:\n\n");
                    for (svc, ok) in results {
                        text.push_str(&format!("• {}: {}\n", svc, if ok { "Stopped & Disabled" } else { "Failed / Access Denied" }));
                    }
                    set_status_text(&text);
                }
                ID_BTN_POWER_PLAN => {
                    match PowerManager::set_high_performance() {
                        Ok(()) => set_status_text("⚡ Success: Windows Power Scheme set to High Performance!"),
                        Err(e) => set_status_text(&format!("❌ Failed to change power plan: {}", e)),
                    }
                }
                ID_BTN_FLUSH_DNS => {
                    match NetworkManager::flush_dns() {
                        Ok(msg) => set_status_text(&format!("🌐 DNS Flush Result:\n{}", msg)),
                        Err(e) => set_status_text(&format!("❌ DNS Flush failed: {}", e)),
                    }
                }
                ID_BTN_ELEVATE => {
                    if let Err(e) = ElevationManager::relaunch_as_admin() {
                        set_status_text(&format!("Elevation failed: {}", e));
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
