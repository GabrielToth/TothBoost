#![windows_subsystem = "windows"]

mod blocklist;
mod elevation;
mod models;
mod power;
mod profile;
mod scanner;
mod service;
mod startup;
mod tweaks;
mod ui;

#[cfg(test)]
mod tests;

use elevation::ElevationManager;
use std::os::windows::ffi::OsStrExt;
use windows::core::*;
use windows::Win32::Foundation::*;
use windows::Win32::Graphics::Gdi::{COLOR_WINDOW, HBRUSH, UpdateWindow};
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::System::Threading::CreateMutexW;
use windows::Win32::UI::WindowsAndMessaging::*;

const WINDOW_TITLE: &str = "TothBoost — Windows 10/11 Performance Optimizer [ADMINISTRATOR]";

fn main() -> Result<()> {
    // Mandatory Administrator enforcement before anything else.
    ElevationManager::enforce_administrator();

    let self_test = std::env::args().any(|arg| arg == "--self-test");

    unsafe {
        // Single instance: two copies would fight over service/tweak changes.
        let _mutex = CreateMutexW(None, TRUE, w!("Global\\TothBoost_SingleInstance"));
        if GetLastError() == ERROR_ALREADY_EXISTS {
            let text = to_wide_null("TothBoost is already running.");
            let title = to_wide_null("TothBoost");
            MessageBoxW(
                HWND::default(),
                PCWSTR(text.as_ptr()),
                PCWSTR(title.as_ptr()),
                MB_OK | MB_ICONINFORMATION,
            );
            return Ok(());
        }

        let instance = GetModuleHandleW(PCWSTR::null())?;
        let class_name = w!("TothBoostClass");

        let wc = WNDCLASSW {
            style: CS_HREDRAW | CS_VREDRAW,
            lpfnWndProc: Some(ui::wnd_proc),
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

        let title = to_wide_null(WINDOW_TITLE);
        let (x, y) = if self_test {
            (-32000, -32000)
        } else {
            (CW_USEDEFAULT, CW_USEDEFAULT)
        };

        let hwnd = CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            class_name,
            PCWSTR(title.as_ptr()),
            WS_OVERLAPPEDWINDOW | WS_CLIPCHILDREN,
            x,
            y,
            1160,
            760,
            HWND::default(),
            HMENU::default(),
            instance,
            None,
        )?;

        if hwnd.0.is_null() {
            return Err(Error::from_win32());
        }

        if self_test {
            return run_self_test(hwnd);
        }

        let _ = ShowWindow(hwnd, SW_SHOW);
        let _ = UpdateWindow(hwnd);

        let mut msg = MSG::default();
        while GetMessageW(&mut msg, HWND::default(), 0, 0).as_bool() {
            let _ = TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }
    Ok(())
}

/// Verifies that the process is really elevated and that the window, tabs and
/// tables were created. Writes evidence to `%TEMP%\tothboost-rust-selftest.log`
/// and exits non-zero on failure. Performs no system modifications.
unsafe fn run_self_test(hwnd: HWND) -> Result<()> {
    let mut ok = true;
    let mut lines: Vec<String> = vec![format!(
        "TOTHBOOST NATIVE SELF-TEST {}",
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map(|d| d.as_secs())
            .unwrap_or(0)
    )];

    let mut check = |name: &str, condition: bool, detail: &str| {
        if !condition {
            ok = false;
        }
        lines.push(format!(
            "{}={}{}",
            name,
            if condition { "OK" } else { "FAIL" },
            if detail.is_empty() {
                String::new()
            } else {
                format!("  {}", detail)
            }
        ));
    };

    check(
        "ELEVATED",
        ElevationManager::is_elevated(),
        &format!("pid={}", std::process::id()),
    );

    let _ = ShowWindow(hwnd, SW_SHOW);
    // The first ShowWindow call can be overridden by STARTUPINFO (SW_HIDE),
    // so call it twice to guarantee the window is really visible.
    let _ = ShowWindow(hwnd, SW_SHOW);
    let _ = UpdateWindow(hwnd);

    let mut title_buf = [0u16; 256];
    let title_len = GetWindowTextW(hwnd, &mut title_buf);
    let title = String::from_utf16_lossy(&title_buf[..title_len as usize]);

    check("WINDOW_HANDLE", !hwnd.0.is_null(), "");

    let style = GetWindowLongW(hwnd, GWL_STYLE);
    let style_visible = (style as u32 & WS_VISIBLE.0) != 0;
    let mut session_id = 0u32;
    let _ = windows::Win32::System::RemoteDesktop::ProcessIdToSessionId(
        std::process::id(),
        &mut session_id,
    );
    check(
        "WINDOW_VISIBLE",
        IsWindowVisible(hwnd).as_bool() || style_visible,
        &format!(
            "IsWindowVisible={} WS_VISIBLE={} session={}",
            IsWindowVisible(hwnd).as_bool(),
            style_visible,
            session_id
        ),
    );
    check("WINDOW_TITLE", !title.trim().is_empty(), &title);
    check("UI_READY", ui::is_window_ready(), "");

    let loaded_processes = ui::self_test_load();
    let d = ui::diagnostics();
    check("TAB_COUNT", d.pages == 5, &format!("{} tabs", d.pages));
    check(
        "PROCESS_ROWS",
        d.rows[0] > 0 && d.rows[0] == d.model_rows[0] && loaded_processes == d.rows[0],
        &format!("{} rendered / {} scanned", d.rows[0], d.model_rows[0]),
    );
    check(
        "SERVICE_ROWS",
        d.rows[1] > 0 && d.rows[1] == d.model_rows[1],
        &format!("{} rendered / {} scanned", d.rows[1], d.model_rows[1]),
    );
    check(
        "STARTUP_ROWS",
        d.rows[2] == d.model_rows[2],
        &format!("{} rendered / {} scanned", d.rows[2], d.model_rows[2]),
    );
    check(
        "TWEAK_ROWS",
        d.rows[3] > 0 && d.rows[3] == d.model_rows[3],
        &format!("{} rendered / {} scanned", d.rows[3], d.model_rows[3]),
    );
    check(
        "PROFILE_ROWS",
        d.rows[4] == d.model_rows[4],
        &format!("{} rendered / {} scanned", d.rows[4], d.model_rows[4]),
    );
    check(
        "TABLE_COLUMNS",
        d.columns == [5, 5, 4, 4, 5],
        &format!("{:?}", d.columns),
    );
    check(
        "CHECKBOXES",
        d.checkboxes.iter().all(|enabled| *enabled),
        &format!("{:?}", d.checkboxes),
    );
    check("STATUS_BAR", d.status_ready, "");
    check("COLUMN_SORT", ui::self_test_sort(), "ascending and descending order verified on rendered list");

    let log_path = std::env::temp_dir().join("tothboost-rust-selftest.log");
    lines.push(format!("RESULT={}", if ok { "PASS" } else { "FAIL" }));
    let _ = std::fs::write(&log_path, lines.join("\n") + "\n");

    let _ = DestroyWindow(hwnd);

    if ok {
        Ok(())
    } else {
        std::process::exit(1);
    }
}

fn to_wide_null(value: &str) -> Vec<u16> {
    std::ffi::OsStr::new(value)
        .encode_wide()
        .chain(std::iter::once(0))
        .collect()
}
