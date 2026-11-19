use crate::blocklist::Blocklist;
use crate::models::{ProcessItem, RiskLevel, ServiceItem, StartupItem, TweakItem};
use crate::power::ProcessController;
use crate::profile::{ProfileData, ProfileService};
use crate::scanner::SystemScanner;
use crate::service::ServiceManager;
use crate::startup::StartupManager;
use crate::tweaks::RegistryTweaks;

use std::os::windows::ffi::OsStrExt;
use std::ptr::null_mut;
use windows::core::*;
use windows::Win32::Foundation::*;
use windows::Win32::Graphics::Gdi::*;
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::UI::Controls::*;
use windows::Win32::UI::Input::KeyboardAndMouse::{EnableWindow, SetActiveWindow, SetFocus};
use windows::Win32::UI::WindowsAndMessaging::*;

const ID_TAB: usize = 100;
const ID_PROC_LIST: usize = 200;
const ID_SVC_LIST: usize = 201;
const ID_STARTUP_LIST: usize = 202;
const ID_TWEAK_LIST: usize = 203;
const ID_PROFILE_LIST: usize = 204;
const ID_STATUS: usize = 300;

const CMD_PROC_REFRESH: usize = 1000;
const CMD_PROC_KILL: usize = 1001;
const CMD_PROC_KILL_BLOCK: usize = 1002;
const CMD_PROC_BLOCK: usize = 1003;
const CMD_PROC_UNBLOCK: usize = 1004;
const CMD_PROC_TRIM: usize = 1005;

const CMD_SVC_REFRESH: usize = 1010;
const CMD_SVC_DISABLE: usize = 1011;
const CMD_SVC_ENABLE: usize = 1012;
const CMD_SVC_MANUAL: usize = 1013;
const CMD_SVC_STOP: usize = 1014;
const CMD_SVC_START: usize = 1015;

const CMD_STARTUP_REFRESH: usize = 1020;
const CMD_STARTUP_DISABLE: usize = 1021;
const CMD_STARTUP_ENABLE: usize = 1022;

const CMD_TWEAK_REFRESH: usize = 1030;
const CMD_TWEAK_APPLY: usize = 1031;
const CMD_TWEAK_REVERT: usize = 1032;
const CMD_TWEAK_TRIM: usize = 1033;
const CMD_TWEAK_JUNK: usize = 1034;
const CMD_TWEAK_DNS: usize = 1035;
const CMD_TWEAK_BALANCED: usize = 1036;

const CMD_PROFILE_REFRESH: usize = 1040;
const CMD_PROFILE_SAVE: usize = 1041;
const CMD_PROFILE_APPLY: usize = 1042;
const CMD_PROFILE_DELETE: usize = 1043;
const CMD_PROFILE_DEFAULT: usize = 1044;

const TIMER_ENFORCE: usize = 1;
const WM_LOAD: u32 = WM_APP + 1;
const ENFORCE_INTERVAL_MS: u32 = 2000;
/// Header notifications (HDN_ITEMCLICKA/W) sent by the header child window.
const HDN_ITEMCLICKA_CODE: u32 = 0xFFFF_FED2; // -300 - 2
const HDN_ITEMCLICKW_CODE: u32 = 0xFFFF_FEBE; // -300 - 22
/// LVN_COLUMNCLICK = LVN_FIRST (-100) - 48, exposed as u32 by NMHDR.
const LVN_COLUMNCLICK_CODE: u32 = 0xFFFF_FF6C;
const HDF_SORTUP_BIT: u32 = 0x0400;
const HDF_SORTDOWN_BIT: u32 = 0x0800;

struct Page {
    list: HWND,
    buttons: Vec<(HWND, i32)>,
}

pub struct App {
    hwnd: HWND,
    tabs: HWND,
    status: HWND,
    pages: Vec<Page>,
    current: usize,
    processes: Vec<ProcessItem>,
    services: Vec<ServiceItem>,
    startups: Vec<StartupItem>,
    tweaks: Vec<TweakItem>,
    profiles: Vec<ProfileData>,
    /// User-chosen sort per list: (column index, ascending).
    proc_sort: SortState,
    svc_sort: SortState,
    startup_sort: SortState,
    tweak_sort: SortState,
    profile_sort: SortState,
}

type SortState = Option<(usize, bool)>;

fn ordered(cmp: std::cmp::Ordering, ascending: bool) -> std::cmp::Ordering {
    if ascending { cmp } else { cmp.reverse() }
}

fn risk_rank(risk: &RiskLevel) -> u8 {
    match risk {
        RiskLevel::Protected => 0,
        RiskLevel::Warning => 1,
        RiskLevel::Recommended => 2,
        RiskLevel::Optional => 3,
        RiskLevel::Info => 4,
    }
}

/// MB at 1 MB and above, KB below that, and B below 1 KB.
pub(crate) fn format_memory(bytes: u64) -> String {
    const KB: u64 = 1024;
    const MB: u64 = 1024 * 1024;
    if bytes >= MB {
        format!("{:.1} MB", bytes as f64 / MB as f64)
    } else if bytes >= KB {
        format!("{:.1} KB", bytes as f64 / KB as f64)
    } else if bytes > 0 {
        format!("{} B", bytes)
    } else {
        "0 B".to_string()
    }
}

fn proc_state_rank(p: &ProcessItem) -> u8 {
    if p.is_blocked {
        0
    } else if p.is_protected {
        2
    } else {
        1
    }
}

static mut APP: Option<App> = None;
static mut PROMPT_EDIT: HWND = HWND(null_mut());
static mut PROMPT_VALUE: Option<String> = None;
static mut PROMPT_CLASS_READY: bool = false;

unsafe fn app_mut() -> Option<&'static mut App> {
    let ptr = std::ptr::addr_of_mut!(APP);
    (*ptr).as_mut()
}

fn wide(value: &str) -> Vec<u16> {
    std::ffi::OsStr::new(value)
        .encode_wide()
        .chain(std::iter::once(0))
        .collect()
}

unsafe fn set_font(control: HWND) {
    if control.0.is_null() {
        return;
    }
    let font = GetStockObject(DEFAULT_GUI_FONT);
    SendMessageW(control, WM_SETFONT, WPARAM(font.0 as usize), LPARAM(1));
}

unsafe fn make_button(parent: HWND, id: usize, text: &str, width: i32) -> HWND {
    let label = wide(text);
    let hwnd = CreateWindowExW(
        WINDOW_EX_STYLE::default(),
        w!("BUTTON"),
        PCWSTR(label.as_ptr()),
        WS_CHILD | WS_VISIBLE | WS_TABSTOP | WINDOW_STYLE(BS_PUSHBUTTON as u32),
        0,
        0,
        width,
        30,
        parent,
        HMENU(id as *mut _),
        None,
        None,
    )
    .unwrap_or_default();
    if !hwnd.0.is_null() {
        set_font(hwnd);
    }
    hwnd
}

unsafe fn make_list(parent: HWND, id: usize) -> HWND {
    let hwnd = CreateWindowExW(
        WS_EX_CLIENTEDGE,
        WC_LISTVIEWW,
        PCWSTR::null(),
        WS_CHILD | WS_VISIBLE | WS_TABSTOP | WINDOW_STYLE(LVS_REPORT | LVS_SHOWSELALWAYS),
        0,
        0,
        100,
        100,
        parent,
        HMENU(id as *mut _),
        None,
        None,
    )
    .unwrap_or_default();

    if !hwnd.0.is_null() {
        set_font(hwnd);
        SendMessageW(
            hwnd,
            LVM_SETEXTENDEDLISTVIEWSTYLE,
            WPARAM(0),
            LPARAM(
                (LVS_EX_FULLROWSELECT
                    | LVS_EX_CHECKBOXES
                    | LVS_EX_GRIDLINES
                    | LVS_EX_DOUBLEBUFFER) as isize,
            ),
        );
    }
    hwnd
}

/// Shows the Win32 sort arrow on the active column (all others cleared).
unsafe fn mark_sort_arrow(list: HWND, column: usize, ascending: bool) {
    let header = HWND(SendMessageW(list, LVM_GETHEADER, WPARAM(0), LPARAM(0)).0 as *mut _);
    if header.0.is_null() {
        return;
    }
    let count = SendMessageW(header, HDM_GETITEMCOUNT, WPARAM(0), LPARAM(0)).0 as usize;
    for index in 0..count {
        let mut item = HDITEMW {
            mask: HDI_FORMAT,
            ..Default::default()
        };
        let ok = SendMessageW(
            header,
            HDM_GETITEMW,
            WPARAM(index),
            LPARAM(&mut item as *mut _ as isize),
        )
        .0;
        if ok == 0 {
            continue;
        }
        let mut fmt = item.fmt.0 & !((HDF_SORTUP_BIT | HDF_SORTDOWN_BIT) as i32);
        if index == column {
            fmt |= if ascending {
                HDF_SORTUP_BIT as i32
            } else {
                HDF_SORTDOWN_BIT as i32
            };
        }
        item.fmt = HEADER_CONTROL_FORMAT_FLAGS(fmt);
        SendMessageW(
            header,
            HDM_SETITEMW,
            WPARAM(index),
            LPARAM(&mut item as *mut _ as isize),
        );
    }
}

unsafe fn add_column(list: HWND, index: i32, width: i32, title: &str) {
    let mut text = wide(title);
    let mut column = LVCOLUMNW {
        mask: LVCF_FMT | LVCF_WIDTH | LVCF_TEXT | LVCF_SUBITEM,
        fmt: LVCFMT_LEFT,
        cx: width,
        pszText: PWSTR(text.as_mut_ptr()),
        cchTextMax: text.len() as i32,
        iSubItem: index,
        ..Default::default()
    };
    SendMessageW(
        list,
        LVM_INSERTCOLUMNW,
        WPARAM(index as usize),
        LPARAM(&mut column as *mut _ as isize),
    );
}

unsafe fn list_clear(list: HWND) {
    SendMessageW(list, LVM_DELETEALLITEMS, WPARAM(0), LPARAM(0));
}

unsafe fn list_add(list: HWND, cells: &[String], checked: bool) {
    if cells.is_empty() {
        return;
    }
    let count = SendMessageW(list, LVM_GETITEMCOUNT, WPARAM(0), LPARAM(0)).0 as i32;
    let mut buffers: Vec<Vec<u16>> = cells.iter().map(|c| wide(c)).collect();
    let mut item = LVITEMW {
        mask: LVIF_TEXT,
        iItem: count,
        iSubItem: 0,
        pszText: PWSTR(buffers[0].as_mut_ptr()),
        cchTextMax: buffers[0].len() as i32,
        ..Default::default()
    };
    let index = SendMessageW(
        list,
        LVM_INSERTITEMW,
        WPARAM(0),
        LPARAM(&mut item as *mut _ as isize),
    )
    .0 as i32;

    for (sub, buffer) in buffers.iter_mut().enumerate().skip(1) {
        let mut sub_item = LVITEMW {
            iSubItem: sub as i32,
            pszText: PWSTR(buffer.as_mut_ptr()),
            cchTextMax: buffer.len() as i32,
            ..Default::default()
        };
        SendMessageW(
            list,
            LVM_SETITEMTEXTW,
            WPARAM(index as usize),
            LPARAM(&mut sub_item as *mut _ as isize),
        );
    }

    if checked {
        set_checked(list, index, true);
    }
}

unsafe fn set_checked(list: HWND, index: i32, checked: bool) {
    let state = if checked { 0x2000u32 } else { 0x1000u32 };
    let mut item = LVITEMW {
        stateMask: LIST_VIEW_ITEM_STATE_FLAGS(LVIS_STATEIMAGEMASK.0),
        state: LIST_VIEW_ITEM_STATE_FLAGS(state),
        ..Default::default()
    };
    SendMessageW(
        list,
        LVM_SETITEMSTATE,
        WPARAM(index as usize),
        LPARAM(&mut item as *mut _ as isize),
    );
}

unsafe fn is_checked(list: HWND, index: usize) -> bool {
    let state = SendMessageW(
        list,
        LVM_GETITEMSTATE,
        WPARAM(index),
        LPARAM(LVIS_STATEIMAGEMASK.0 as isize),
    )
    .0 as u32;
    ((state >> 12) & 0x0F) == 2
}

unsafe fn checked_rows(list: HWND) -> Vec<usize> {
    let count = SendMessageW(list, LVM_GETITEMCOUNT, WPARAM(0), LPARAM(0)).0 as usize;
    (0..count).filter(|index| is_checked(list, *index)).collect()
}

unsafe fn selected_row(list: HWND) -> Option<usize> {
    let index = SendMessageW(
        list,
        LVM_GETNEXTITEM,
        WPARAM(usize::MAX),
        LPARAM(LVNI_SELECTED as isize),
    )
    .0;
    if index < 0 {
        None
    } else {
        Some(index as usize)
    }
}

unsafe fn target_rows(list: HWND, count: usize) -> Vec<usize> {
    let mut rows = checked_rows(list);
    if rows.is_empty() {
        if let Some(row) = selected_row(list) {
            rows.push(row);
        }
    }
    rows.retain(|row| *row < count);
    rows
}

unsafe fn set_status(text: &str) {
    if let Some(app) = app_mut() {
        let message = wide(text);
        SendMessageW(
            app.status,
            SB_SETTEXTW,
            WPARAM(0),
            LPARAM(message.as_ptr() as isize),
        );
    }
}

unsafe fn confirm(text: &str, title: &str) -> bool {
    let app = app_mut();
    let parent = app.map(|a| a.hwnd).unwrap_or_default();
    let text = wide(text);
    let title = wide(title);
    MessageBoxW(
        parent,
        PCWSTR(text.as_ptr()),
        PCWSTR(title.as_ptr()),
        MB_YESNO | MB_ICONWARNING,
    )
    .0
        == IDYES.0
}

unsafe fn notify(text: &str, title: &str) {
    let app = app_mut();
    let parent = app.map(|a| a.hwnd).unwrap_or_default();
    let text = wide(text);
    let title = wide(title);
    MessageBoxW(
        parent,
        PCWSTR(text.as_ptr()),
        PCWSTR(title.as_ptr()),
        MB_OK | MB_ICONINFORMATION,
    );
}

pub unsafe fn install(hwnd: HWND) -> bool {
    let mut icc = INITCOMMONCONTROLSEX {
        dwSize: std::mem::size_of::<INITCOMMONCONTROLSEX>() as u32,
        dwICC: ICC_LISTVIEW_CLASSES | ICC_TAB_CLASSES | ICC_BAR_CLASSES,
    };
    let _ = InitCommonControlsEx(&mut icc);

    let tabs = CreateWindowExW(
        WINDOW_EX_STYLE::default(),
        WC_TABCONTROLW,
        PCWSTR::null(),
        WS_CHILD | WS_VISIBLE | WS_TABSTOP,
        8,
        8,
        800,
        600,
        hwnd,
        HMENU(ID_TAB as *mut _),
        None,
        None,
    )
    .unwrap_or_default();
    if tabs.0.is_null() {
        return false;
    }
    set_font(tabs);

    let page_defs: [(&str, &[(usize, &str, i32)], usize, &[(i32, &str)]); 5] = [
        (
            "Processes",
            &[
                (CMD_PROC_REFRESH, "Refresh", 90),
                (CMD_PROC_KILL, "Terminate", 110),
                (CMD_PROC_KILL_BLOCK, "Kill + Block", 120),
                (CMD_PROC_BLOCK, "Block Only", 110),
                (CMD_PROC_UNBLOCK, "Unblock", 100),
                (CMD_PROC_TRIM, "Trim RAM", 100),
            ],
            ID_PROC_LIST,
            &[
                (70, "PID"),
                (210, "Process"),
                (100, "Memory"),
                (110, "State"),
                (420, "Executable Path"),
            ],
        ),
        (
            "Services",
            &[
                (CMD_SVC_REFRESH, "Refresh", 90),
                (CMD_SVC_DISABLE, "Disable", 100),
                (CMD_SVC_ENABLE, "Enable (Auto)", 120),
                (CMD_SVC_MANUAL, "Set Manual", 110),
                (CMD_SVC_STOP, "Stop", 80),
                (CMD_SVC_START, "Start", 80),
            ],
            ID_SVC_LIST,
            &[
                (180, "Service Name"),
                (280, "Display Name"),
                (100, "Startup"),
                (100, "Status"),
                (130, "Risk"),
            ],
        ),
        (
            "Startup",
            &[
                (CMD_STARTUP_REFRESH, "Refresh", 90),
                (CMD_STARTUP_DISABLE, "Disable (Quarantine)", 170),
                (CMD_STARTUP_ENABLE, "Enable (Restore)", 150),
            ],
            ID_STARTUP_LIST,
            &[
                (200, "Name"),
                (240, "Location"),
                (430, "Command"),
                (110, "State"),
            ],
        ),
        (
            "Tweaks",
            &[
                (CMD_TWEAK_REFRESH, "Refresh", 90),
                (CMD_TWEAK_APPLY, "Apply Selected", 130),
                (CMD_TWEAK_REVERT, "Revert Selected", 130),
                (CMD_TWEAK_TRIM, "Trim RAM", 100),
                (CMD_TWEAK_JUNK, "Clean Junk", 110),
                (CMD_TWEAK_DNS, "Flush DNS", 100),
                (CMD_TWEAK_BALANCED, "Balanced Plan", 120),
            ],
            ID_TWEAK_LIST,
            &[
                (300, "Tweak"),
                (90, "Category"),
                (110, "State"),
                (470, "Description"),
            ],
        ),
        (
            "Profiles",
            &[
                (CMD_PROFILE_REFRESH, "Refresh", 90),
                (CMD_PROFILE_SAVE, "Save Current As...", 160),
                (CMD_PROFILE_APPLY, "Apply Selected", 130),
                (CMD_PROFILE_DELETE, "Delete Selected", 130),
                (CMD_PROFILE_DEFAULT, "Restore Default", 140),
            ],
            ID_PROFILE_LIST,
            &[
                (220, "Profile"),
                (190, "Created"),
                (130, "Disabled Services"),
                (100, "Tweaks On"),
                (90, "Blocked"),
            ],
        ),
    ];

    let mut pages = Vec::with_capacity(5);
    for (name, buttons, list_id, columns) in page_defs {
        let mut name_buffer = wide(name);
        let mut tab_item = TCITEMW {
            mask: TCIF_TEXT,
            pszText: PWSTR(name_buffer.as_mut_ptr()),
            cchTextMax: name_buffer.len() as i32,
            ..Default::default()
        };
        SendMessageW(
            tabs,
            TCM_INSERTITEMW,
            WPARAM(pages.len()),
            LPARAM(&mut tab_item as *mut _ as isize),
        );
        drop(name_buffer);

        let button_handles = buttons
            .iter()
            .map(|(id, text, width)| (make_button(hwnd, *id, text, *width), *width))
            .collect();

        let list = make_list(hwnd, list_id);
        for (index, (width, title)) in columns.iter().enumerate() {
            add_column(list, index as i32, *width, title);
        }

        pages.push(Page {
            list,
            buttons: button_handles,
        });
    }

    let status = CreateWindowExW(
        WINDOW_EX_STYLE::default(),
        STATUSCLASSNAMEW,
        PCWSTR::null(),
        WS_CHILD | WS_VISIBLE | WINDOW_STYLE(SBARS_SIZEGRIP),
        0,
        0,
        100,
        24,
        hwnd,
        HMENU(ID_STATUS as *mut _),
        None,
        None,
    )
    .unwrap_or_default();
    set_font(status);

    let app = App {
        hwnd,
        tabs,
        status,
        pages,
        current: 0,
        processes: Vec::new(),
        services: Vec::new(),
        startups: Vec::new(),
        tweaks: Vec::new(),
        profiles: Vec::new(),
        proc_sort: None,
        svc_sort: None,
        startup_sort: None,
        tweak_sort: None,
        profile_sort: None,
    };

    let ptr = std::ptr::addr_of_mut!(APP);
    *ptr = Some(app);

    layout();
    show_page(0);
    set_status("Ready — running with full Administrator privileges.");

    let _ = PostMessageW(hwnd, WM_LOAD, WPARAM(0), LPARAM(0));
    true
}

unsafe fn layout() {
    let Some(app) = app_mut() else { return };
    let mut client = RECT::default();
    let _ = GetClientRect(app.hwnd, &mut client);

    let width = (client.right - client.left).max(400);
    let height = (client.bottom - client.top).max(300);
    let status_height = 26;
    let tab_height = (height - status_height - 8).max(120);

    let _ = MoveWindow(app.tabs, 8, 8, width - 16, tab_height, true);
    let _ = MoveWindow(app.status, 0, height - status_height, width, status_height, true);

    let mut item_rect = RECT::default();
    SendMessageW(
        app.tabs,
        TCM_GETITEMRECT,
        WPARAM(0),
        LPARAM(&mut item_rect as *mut _ as isize),
    );
    let header = (item_rect.bottom + 6).max(32);

    let content_x = 10;
    let content_y = 8 + header;
    let content_width = (width - 20).max(200);
    let content_height = (tab_height - header - 6).max(100);

    for page in &app.pages {
        let mut x = content_x;
        for (button, button_width) in &page.buttons {
            let _ = MoveWindow(*button, x, content_y, *button_width, 30, true);
            x += *button_width + 6;
        }
        let _ = MoveWindow(
            page.list,
            content_x,
            content_y + 36,
            content_width,
            (content_height - 36).max(60),
            true,
        );
    }
}

unsafe fn show_page(index: usize) {
    let Some(app) = app_mut() else { return };
    app.current = index.min(app.pages.len().saturating_sub(1));
    for (page_index, page) in app.pages.iter().enumerate() {
        let show = page_index == app.current;
        for (button, _) in &page.buttons {
            let _ = ShowWindow(*button, if show { SW_SHOW } else { SW_HIDE });
        }
        let _ = ShowWindow(page.list, if show { SW_SHOW } else { SW_HIDE });
    }
}

pub unsafe fn load_initial() {
    let Some(app) = app_mut() else { return };
    let _ = ProfileService::ensure_default();
    app.reload_all();
    sync_enforcement();
}

unsafe fn sync_enforcement() {
    let Some(app) = app_mut() else { return };
    let blocked = Blocklist::load();
    if blocked.is_empty() {
        let _ = KillTimer(app.hwnd, TIMER_ENFORCE);
    } else {
        let _ = SetTimer(app.hwnd, TIMER_ENFORCE, ENFORCE_INTERVAL_MS, None);
    }
}

impl App {
    unsafe fn sort_by_column(&mut self, page: usize, column: usize) {
        let current = match page {
            0 => self.proc_sort,
            1 => self.svc_sort,
            2 => self.startup_sort,
            3 => self.tweak_sort,
            _ => self.profile_sort,
        };
        let next = match current {
            Some((active, ascending)) if active == column => Some((column, !ascending)),
            _ => Some((column, true)),
        };

        match page {
            0 => {
                self.proc_sort = next;
                self.reload_processes();
            }
            1 => {
                self.svc_sort = next;
                self.reload_services();
            }
            2 => {
                self.startup_sort = next;
                self.reload_startup();
            }
            3 => {
                self.tweak_sort = next;
                self.reload_tweaks();
            }
            _ => {
                self.profile_sort = next;
                self.reload_profiles();
            }
        }

        let ascending = next.map(|(_, asc)| asc).unwrap_or(true);
        mark_sort_arrow(self.pages[page].list, column, ascending);
        set_status(&format!(
            "Sorted by column {} ({}).",
            column + 1,
            if ascending { "ascending" } else { "descending" }
        ));
    }

    unsafe fn reload_all(&mut self) {
        self.reload_processes();
        self.reload_services();
        self.reload_startup();
        self.reload_tweaks();
        self.reload_profiles();
        set_status(&format!(
            "Ready — {} processes, {} services, {} startup items, {} blocked process(es).",
            self.processes.len(),
            self.services.len(),
            self.startups.len(),
            Blocklist::load().len()
        ));
    }

    unsafe fn reload_processes(&mut self) {
        self.processes = SystemScanner::scan_processes();
        if let Some((column, ascending)) = self.proc_sort {
            self.processes.sort_by(|a, b| {
                let cmp = match column {
                    0 => a.pid.cmp(&b.pid),
                    2 => a.memory_bytes.cmp(&b.memory_bytes),
                    3 => proc_state_rank(a).cmp(&proc_state_rank(b)),
                    4 => a.path.to_lowercase().cmp(&b.path.to_lowercase()),
                    _ => a.name.to_lowercase().cmp(&b.name.to_lowercase()),
                };
                ordered(cmp, ascending)
            });
        }
        let list = self.pages[0].list;
        list_clear(list);
        for process in &self.processes {
            let state = if process.is_blocked {
                "BLOCKED"
            } else if process.is_protected {
                "Protected"
            } else {
                "Running"
            };
            list_add(
                list,
                &[
                    process.pid.to_string(),
                    process.name.clone(),
                    format_memory(process.memory_bytes),
                    state.to_string(),
                    process.path.clone(),
                ],
                false,
            );
        }
    }

    unsafe fn reload_services(&mut self) {
        self.services = SystemScanner::scan_services();
        if let Some((column, ascending)) = self.svc_sort {
            self.services.sort_by(|a, b| {
                let cmp = match column {
                    1 => a.display_name.to_lowercase().cmp(&b.display_name.to_lowercase()),
                    2 => a.start_label().cmp(b.start_label()),
                    3 => a.status.to_lowercase().cmp(&b.status.to_lowercase()),
                    4 => risk_rank(&a.risk).cmp(&risk_rank(&b.risk)),
                    _ => a.name.to_lowercase().cmp(&b.name.to_lowercase()),
                };
                ordered(cmp, ascending)
            });
        }
        let list = self.pages[1].list;
        list_clear(list);
        for service in &self.services {
            list_add(
                list,
                &[
                    service.name.clone(),
                    service.display_name.clone(),
                    service.start_label().to_string(),
                    service.status.clone(),
                    service.risk.label().to_string(),
                ],
                false,
            );
        }
    }

    unsafe fn reload_startup(&mut self) {
        self.startups = SystemScanner::scan_startup();
        if let Some((column, ascending)) = self.startup_sort {
            self.startups.sort_by(|a, b| {
                let cmp = match column {
                    1 => a.location.to_lowercase().cmp(&b.location.to_lowercase()),
                    2 => a.command.to_lowercase().cmp(&b.command.to_lowercase()),
                    3 => a.is_enabled.cmp(&b.is_enabled),
                    _ => a.name.to_lowercase().cmp(&b.name.to_lowercase()),
                };
                ordered(cmp, ascending)
            });
        }
        let list = self.pages[2].list;
        list_clear(list);
        for item in &self.startups {
            list_add(
                list,
                &[
                    item.name.clone(),
                    item.location.clone(),
                    item.command.clone(),
                    if item.is_enabled {
                        "Enabled".into()
                    } else {
                        "Quarantined".into()
                    },
                ],
                false,
            );
        }
    }

    unsafe fn reload_tweaks(&mut self) {
        self.tweaks = RegistryTweaks::get_available_tweaks();
        if let Some((column, ascending)) = self.tweak_sort {
            self.tweaks.sort_by(|a, b| {
                let cmp = match column {
                    1 => a.category.to_lowercase().cmp(&b.category.to_lowercase()),
                    2 => a.is_applied.cmp(&b.is_applied),
                    3 => a.description.to_lowercase().cmp(&b.description.to_lowercase()),
                    _ => a.name.to_lowercase().cmp(&b.name.to_lowercase()),
                };
                ordered(cmp, ascending)
            });
        }
        let list = self.pages[3].list;
        list_clear(list);
        for tweak in &self.tweaks {
            list_add(
                list,
                &[
                    tweak.name.clone(),
                    tweak.category.clone(),
                    if tweak.is_applied {
                        "Applied".into()
                    } else {
                        "Not applied".into()
                    },
                    tweak.description.clone(),
                ],
                false,
            );
        }
    }

    unsafe fn reload_profiles(&mut self) {
        let names = ProfileService::list_profiles();
        self.profiles = names
            .iter()
            .filter_map(|name| ProfileService::load_profile(name).ok())
            .collect();

        if let Some((column, ascending)) = self.profile_sort {
            self.profiles.sort_by(|a, b| {
                let cmp = match column {
                    1 => a.created_utc.cmp(&b.created_utc),
                    2 => a.services_disabled().cmp(&b.services_disabled()),
                    3 => a.tweaks_applied().cmp(&b.tweaks_applied()),
                    4 => a.blocked_processes.len().cmp(&b.blocked_processes.len()),
                    _ => a.name.to_lowercase().cmp(&b.name.to_lowercase()),
                };
                ordered(cmp, ascending)
            });
        }

        let list = self.pages[4].list;
        list_clear(list);
        for profile in &self.profiles {
            list_add(
                list,
                &[
                    profile.name.clone(),
                    profile.created_utc.clone(),
                    profile.services_disabled().to_string(),
                    profile.tweaks_applied().to_string(),
                    profile.blocked_processes.len().to_string(),
                ],
                false,
            );
        }
    }

    unsafe fn handle_command(&mut self, id: usize) {
        match id {
            CMD_PROC_REFRESH => {
                self.reload_processes();
                set_status(&format!("{} processes loaded.", self.processes.len()));
            }
            CMD_PROC_KILL => self.kill_selected(false),
            CMD_PROC_KILL_BLOCK => self.kill_selected(true),
            CMD_PROC_BLOCK => self.block_selected(),
            CMD_PROC_UNBLOCK => self.unblock_selected(),
            CMD_PROC_TRIM | CMD_TWEAK_TRIM => {
                let (trimmed, freed) = ProcessController::trim_working_sets();
                self.reload_processes();
                set_status(&format!(
                    "RAM trimmed: {} process(es), {:.1} MB released.",
                    trimmed,
                    freed as f64 / (1024.0 * 1024.0)
                ));
            }

            CMD_SVC_REFRESH => {
                self.reload_services();
                set_status(&format!("{} services loaded.", self.services.len()));
            }
            CMD_SVC_DISABLE => self.service_action(ServiceAction::Disable),
            CMD_SVC_ENABLE => self.service_action(ServiceAction::EnableAuto),
            CMD_SVC_MANUAL => self.service_action(ServiceAction::Manual),
            CMD_SVC_STOP => self.service_action(ServiceAction::Stop),
            CMD_SVC_START => self.service_action(ServiceAction::Start),

            CMD_STARTUP_REFRESH => {
                self.reload_startup();
                set_status(&format!("{} startup entries loaded.", self.startups.len()));
            }
            CMD_STARTUP_DISABLE => self.startup_action(true),
            CMD_STARTUP_ENABLE => self.startup_action(false),

            CMD_TWEAK_REFRESH => {
                self.reload_tweaks();
                set_status("Tweaks refreshed.");
            }
            CMD_TWEAK_APPLY => self.tweak_action(true),
            CMD_TWEAK_REVERT => self.tweak_action(false),
            CMD_TWEAK_JUNK => {
                if !confirm(
                    "Delete temporary files, prefetch data and update caches?\n\nLocked files are skipped automatically.",
                    "Clean Junk",
                ) {
                    return;
                }
                let (files, bytes, errors) = crate::power::clean_junk();
                set_status(&format!(
                    "Cleanup: {} file(s), {:.1} MB freed{}.",
                    files,
                    bytes as f64 / (1024.0 * 1024.0),
                    if errors.is_empty() {
                        String::new()
                    } else {
                        format!(", {} skipped", errors.len())
                    }
                ));
            }
            CMD_TWEAK_DNS => match crate::power::flush_dns() {
                Ok(()) => set_status("DNS resolver cache flushed."),
                Err(e) => set_status(&format!("DNS flush failed: {}", e)),
            },
            CMD_TWEAK_BALANCED => match RegistryTweaks::set_balanced_plan() {
                Ok(()) => {
                    self.reload_tweaks();
                    set_status("Balanced power plan activated.");
                }
                Err(e) => set_status(&format!("Power plan change failed: {}", e)),
            },

            CMD_PROFILE_REFRESH => {
                self.reload_profiles();
                set_status(&format!("{} profile(s) available.", self.profiles.len()));
            }
            CMD_PROFILE_SAVE => self.save_profile_dialog(),
            CMD_PROFILE_APPLY => self.apply_selected_profile(),
            CMD_PROFILE_DELETE => self.delete_selected_profile(),
            CMD_PROFILE_DEFAULT => self.apply_default_profile(),

            _ => {}
        }
    }

    unsafe fn kill_selected(&mut self, block: bool) {
        let rows = target_rows(self.pages[0].list, self.processes.len());
        if rows.is_empty() {
            set_status("Select or check at least one process first.");
            return;
        }

        let mut names = Vec::new();
        let mut skipped = 0usize;
        for row in rows {
            let process = &self.processes[row];
            if process.is_protected {
                skipped += 1;
                continue;
            }
            names.push(process.name.clone());
        }
        names.sort();
        names.dedup();

        if names.is_empty() {
            set_status("Only protected processes were selected — nothing was terminated.");
            return;
        }

        let question = if block {
            format!(
                "Terminate and PERMANENTLY BLOCK {} process(es)?\n\nTothBoost will kill them now and every 2 seconds while it runs, and will quarantine matching startup entries so they cannot return after a reboot.\n\nProcesses: {}",
                names.len(),
                names.join(", ")
            )
        } else {
            format!(
                "Terminate {} process(es)?\n\n{}",
                names.len(),
                names.join(", ")
            )
        };
        if !confirm(&question, if block { "Kill + Block" } else { "Terminate" }) {
            return;
        }

        let mut killed = 0usize;
        for name in &names {
            killed += ProcessController::terminate_by_name(name).unwrap_or(0);
        }

        let mut startup_disabled = 0usize;
        if block {
            if let Err(e) = Blocklist::add_many(&names) {
                notify(&format!("Could not save the blocklist: {}", e), "Blocklist");
            }
            for name in &names {
                startup_disabled += StartupManager::disable_matching(name);
            }
            sync_enforcement();
        }

        self.reload_processes();
        self.reload_startup();
        set_status(&format!(
            "Killed {} process(es){}; {} skipped as protected{}{}.",
            killed,
            if block { " and added to the persistent blocklist" } else { "" },
            skipped,
            if block {
                format!(", {} startup entrie(s) quarantined", startup_disabled)
            } else {
                String::new()
            },
            if block { ". Enforcement is active." } else { "." }
        ));
    }

    unsafe fn block_selected(&mut self) {
        let rows = target_rows(self.pages[0].list, self.processes.len());
        if rows.is_empty() {
            set_status("Select at least one process to block.");
            return;
        }
        let names: Vec<String> = rows
            .iter()
            .filter(|row| !self.processes[**row].is_protected)
            .map(|row| self.processes[*row].name.clone())
            .collect();
        if names.is_empty() {
            set_status("Protected processes cannot be blocked.");
            return;
        }
        match Blocklist::add_many(&names) {
            Ok(list) => {
                sync_enforcement();
                self.reload_processes();
                set_status(&format!(
                    "{} process name(s) blocked ({} total). They will be terminated on sight.",
                    names.len(),
                    list.len()
                ));
            }
            Err(e) => set_status(&format!("Could not save blocklist: {}", e)),
        }
    }

    unsafe fn unblock_selected(&mut self) {
        let rows = target_rows(self.pages[0].list, self.processes.len());
        if rows.is_empty() {
            set_status("Select a blocked process to unblock.");
            return;
        }
        let names: Vec<String> = rows
            .iter()
            .map(|row| self.processes[*row].name.clone())
            .collect();
        match Blocklist::remove_many(&names) {
            Ok(list) => {
                sync_enforcement();
                self.reload_processes();
                set_status(&format!(
                    "Removed from blocklist. {} name(s) remain blocked.",
                    list.len()
                ));
            }
            Err(e) => set_status(&format!("Could not update blocklist: {}", e)),
        }
    }

    unsafe fn service_action(&mut self, action: ServiceAction) {
        let rows = target_rows(self.pages[1].list, self.services.len());
        if rows.is_empty() {
            set_status("Select or check at least one service first.");
            return;
        }

        let mut protected_rows = Vec::new();
        for row in &rows {
            if self.services[*row].is_protected {
                protected_rows.push(row.clone());
            }
        }

        if !protected_rows.is_empty() {
            let names: Vec<&str> = protected_rows
                .iter()
                .map(|row| self.services[*row].name.as_str())
                .collect();
            let question = format!(
                "WARNING: {} selected service(s) are classified as PROTECTED:\n\n{}\n\nForcing changes to protected services can destabilize Windows, break networking, audio or security, and may require Safe Mode to recover.\n\nContinue anyway?",
                names.len(),
                names.join(", ")
            );
            if !confirm(&question, "Force protected service change") {
                return;
            }
        }

        let verb = match action {
            ServiceAction::Disable => "disable",
            ServiceAction::EnableAuto => "enable",
            ServiceAction::Manual => "set to manual",
            ServiceAction::Stop => "stop",
            ServiceAction::Start => "start",
        };
        if matches!(action, ServiceAction::Disable) {
            let question = format!(
                "{} {} service(s)?\n\nDisabled services will NOT start again after a reboot until re-enabled here.",
                action.verb_capitalized(),
                rows.len()
            );
            if !confirm(&question, "Disable services") {
                return;
            }
        }

        let mut ok = 0usize;
        let mut failures: Vec<String> = Vec::new();
        for row in &rows {
            let service = &self.services[*row];
            let result = match action {
                ServiceAction::Disable => ServiceManager::disable(&service.name),
                ServiceAction::EnableAuto => ServiceManager::enable_auto(&service.name),
                ServiceAction::Manual => ServiceManager::enable_manual(&service.name),
                ServiceAction::Stop => ServiceManager::stop(&service.name),
                ServiceAction::Start => ServiceManager::start(&service.name),
            };
            match result {
                Ok(()) => ok += 1,
                Err(e) => failures.push(format!("{}: {}", service.name, e)),
            }
        }

        self.reload_services();
        self.reload_profiles();
        set_status(&format!(
            "{} service(s) {}d successfully, {} failed.",
            ok,
            verb,
            failures.len()
        ));
        if !failures.is_empty() {
            notify(
                &format!(
                    "Some services could not be changed:\n\n{}",
                    failures.iter().take(15).cloned().collect::<Vec<_>>().join("\n")
                ),
                "Service errors",
            );
        }
    }

    unsafe fn startup_action(&mut self, disable: bool) {
        let rows = target_rows(self.pages[2].list, self.startups.len());
        if rows.is_empty() {
            set_status("Select at least one startup entry.");
            return;
        }

        let mut changed = 0usize;
        let mut failures = Vec::new();
        for row in rows {
            let item = &self.startups[row];
            if item.is_enabled == !disable {
                continue;
            }
            let result = if disable {
                StartupManager::disable(item)
            } else {
                StartupManager::enable(item)
            };
            match result {
                Ok(()) => changed += 1,
                Err(e) => failures.push(format!("{}: {}", item.name, e)),
            }
        }

        self.reload_startup();
        self.reload_profiles();
        set_status(&format!(
            "{} startup entrie(s) {}.",
            changed,
            if disable { "quarantined" } else { "restored" }
        ));
        if !failures.is_empty() {
            notify(&failures.join("\n"), "Startup errors");
        }
    }

    unsafe fn tweak_action(&mut self, apply: bool) {
        let rows = target_rows(self.pages[3].list, self.tweaks.len());
        if rows.is_empty() {
            set_status("Select or check at least one tweak.");
            return;
        }

        let mut ok = 0usize;
        let mut failures = Vec::new();
        for row in rows {
            let tweak = &self.tweaks[row];
            let result = if apply {
                RegistryTweaks::apply_tweak(&tweak.id)
            } else {
                RegistryTweaks::revert_tweak(&tweak.id)
            };
            match result {
                Ok(_) => ok += 1,
                Err(e) => failures.push(format!("{}: {}", tweak.name, e)),
            }
        }

        self.reload_tweaks();
        self.reload_profiles();
        set_status(&format!(
            "{} tweak(s) {}, {} failed.",
            ok,
            if apply { "applied" } else { "reverted" },
            failures.len()
        ));
        if !failures.is_empty() {
            notify(&failures.join("\n"), "Tweak errors");
        }
    }

    unsafe fn save_profile_dialog(&mut self) {
        let Some(name) = prompt("Save current state as profile", "Default") else {
            return;
        };
        let trimmed = name.trim();
        if trimmed.is_empty() {
            return;
        }

        if trimmed.eq_ignore_ascii_case("Default") {
            if !confirm(
                "Overwrite the Default baseline with the CURRENT system state?\n\nThe original baseline will be lost.",
                "Overwrite Default",
            ) {
                return;
            }
        }

        let data = ProfileService::capture(trimmed);
        match ProfileService::save_profile(&data) {
            Ok(()) => {
                self.reload_profiles();
                set_status(&format!(
                    "Profile '{}' saved ({} services, {} tweaks, {} blocked process names).",
                    data.name,
                    data.services.len(),
                    data.tweaks.len(),
                    data.blocked_processes.len()
                ));
            }
            Err(e) => notify(&format!("Could not save profile: {}", e), "Profiles"),
        }
    }

    unsafe fn apply_selected_profile(&mut self) {
        let Some(row) = selected_profile_row(self) else {
            set_status("Select a profile in the list first.");
            return;
        };
        self.apply_profile_at(row);
    }

    unsafe fn apply_default_profile(&mut self) {
        let _ = ProfileService::ensure_default();
        if let Some(row) = self
            .profiles
            .iter()
            .position(|profile| profile.name.eq_ignore_ascii_case("Default"))
        {
            self.apply_profile_at(row);
        } else {
            set_status("Default baseline not found.");
        }
    }

    unsafe fn apply_profile_at(&mut self, row: usize) {
        let Some(profile) = self.profiles.get(row).cloned() else {
            return;
        };
        if !confirm(
            &format!(
                "Apply profile '{}'?\n\nThis will change service startup types, tweaks, startup entries and the persistent process blocklist.",
                profile.name
            ),
            "Apply profile",
        ) {
            return;
        }

        let report = ProfileService::apply(&profile);
        sync_enforcement();
        self.reload_all();

        set_status(&format!("Profile '{}' applied — {}.", profile.name, report.summary()));
        if report.error_count > 0 {
            notify(
                &format!(
                    "{}\n\n{}",
                    report.summary(),
                    report.failures().iter().take(15).cloned().collect::<Vec<_>>().join("\n")
                ),
                "Profile applied with errors",
            );
        }
    }

    unsafe fn delete_selected_profile(&mut self) {
        let Some(row) = selected_profile_row(self) else {
            set_status("Select a profile to delete.");
            return;
        };
        let name = self.profiles[row].name.clone();
        if !confirm(
            &format!("Delete profile '{}'? This cannot be undone.", name),
            "Delete profile",
        ) {
            return;
        }
        match ProfileService::delete_profile(&name) {
            Ok(()) => {
                self.reload_profiles();
                set_status(&format!("Profile '{}' deleted.", name));
            }
            Err(e) => notify(&e, "Profiles"),
        }
    }
}

unsafe fn selected_profile_row(app: &App) -> Option<usize> {
    let mut rows = checked_rows(app.pages[4].list);
    if rows.is_empty() {
        if let Some(row) = selected_row(app.pages[4].list) {
            rows.push(row);
        }
    }
    rows.into_iter().next().filter(|row| *row < app.profiles.len())
}

#[derive(Clone, Copy)]
enum ServiceAction {
    Disable,
    EnableAuto,
    Manual,
    Stop,
    Start,
}

impl ServiceAction {
    fn verb_capitalized(&self) -> &'static str {
        match self {
            Self::Disable => "Disable",
            Self::EnableAuto => "Enable",
            Self::Manual => "Set to manual",
            Self::Stop => "Stop",
            Self::Start => "Start",
        }
    }
}

// ---------------- Modal text prompt (profile name) ----------------

unsafe extern "system" fn prompt_proc(
    hwnd: HWND,
    msg: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    match msg {
        WM_COMMAND => {
            let id = (wparam.0 & 0xFFFF) as i32;
            if id == 1 {
                let edit = PROMPT_EDIT;
                let length = GetWindowTextLengthW(edit).max(0) as usize;
                let mut buffer = vec![0u16; length + 1];
                let copied = GetWindowTextW(edit, &mut buffer);
                let value = String::from_utf16_lossy(&buffer[..copied as usize]);
                let ptr = std::ptr::addr_of_mut!(PROMPT_VALUE);
                *ptr = Some(value);
                let _ = DestroyWindow(hwnd);
            } else if id == 2 {
                let ptr = std::ptr::addr_of_mut!(PROMPT_VALUE);
                *ptr = None;
                let _ = DestroyWindow(hwnd);
            }
            LRESULT(0)
        }
        WM_CLOSE => {
            let _ = DestroyWindow(hwnd);
            LRESULT(0)
        }
        WM_DESTROY => {
            PostQuitMessage(0);
            LRESULT(0)
        }
        _ => DefWindowProcW(hwnd, msg, wparam, lparam),
    }
}

unsafe fn prompt(title: &str, default_value: &str) -> Option<String> {
    let Some(app) = app_mut() else {
        return None;
    };
    let parent = app.hwnd;

    if !PROMPT_CLASS_READY {
        let class_name = w!("TothBoostPrompt");
        let wc = WNDCLASSW {
            lpfnWndProc: Some(prompt_proc),
            hInstance: GetModuleHandleW(PCWSTR::null()).unwrap_or_default().into(),
            lpszClassName: class_name,
            hbrBackground: HBRUSH((COLOR_WINDOW.0 + 1) as *mut std::ffi::c_void),
            hCursor: LoadCursorW(HMODULE::default(), IDC_ARROW).unwrap_or_default(),
            ..Default::default()
        };
        let _ = RegisterClassW(&wc);
        let ptr = std::ptr::addr_of_mut!(PROMPT_CLASS_READY);
        *ptr = true;
    }

    let mut parent_rect = RECT::default();
    let _ = GetWindowRect(parent, &mut parent_rect);
    let width = 470;
    let height = 170;
    let x = parent_rect.left + ((parent_rect.right - parent_rect.left) - width) / 2;
    let y = parent_rect.top + ((parent_rect.bottom - parent_rect.top) - height) / 2;

    let title_wide = wide(title);
    let dialog = CreateWindowExW(
        WS_EX_DLGMODALFRAME,
        w!("TothBoostPrompt"),
        PCWSTR(title_wide.as_ptr()),
        WS_POPUP | WS_CAPTION | WS_SYSMENU,
        x,
        y,
        width,
        height,
        parent,
        HMENU::default(),
        None,
        None,
    )
    .unwrap_or_default();
    if dialog.0.is_null() {
        return None;
    }

    let label_text = wide("Profile name:");
    let label = CreateWindowExW(
        WINDOW_EX_STYLE::default(),
        w!("STATIC"),
        PCWSTR(label_text.as_ptr()),
        WS_CHILD | WS_VISIBLE,
        16,
        14,
        300,
        20,
        dialog,
        HMENU::default(),
        None,
        None,
    )
    .unwrap_or_default();

    let default_wide = wide(default_value);
    let edit = CreateWindowExW(
        WS_EX_CLIENTEDGE,
        w!("EDIT"),
        PCWSTR(default_wide.as_ptr()),
        WS_CHILD | WS_VISIBLE | WS_TABSTOP | WINDOW_STYLE(ES_AUTOHSCROLL as u32),
        16,
        38,
        width - 48,
        26,
        dialog,
        HMENU(10 as *mut _),
        None,
        None,
    )
    .unwrap_or_default();

    let ok_text = wide("Save");
    let ok_button = CreateWindowExW(
        WINDOW_EX_STYLE::default(),
        w!("BUTTON"),
        PCWSTR(ok_text.as_ptr()),
        WS_CHILD | WS_VISIBLE | WS_TABSTOP | WINDOW_STYLE(BS_DEFPUSHBUTTON as u32),
        width - 200,
        92,
        80,
        30,
        dialog,
        HMENU(1 as *mut _),
        None,
        None,
    )
    .unwrap_or_default();

    let cancel_text = wide("Cancel");
    let cancel_button = CreateWindowExW(
        WINDOW_EX_STYLE::default(),
        w!("BUTTON"),
        PCWSTR(cancel_text.as_ptr()),
        WS_CHILD | WS_VISIBLE | WS_TABSTOP | WINDOW_STYLE(BS_PUSHBUTTON as u32),
        width - 110,
        92,
        80,
        30,
        dialog,
        HMENU(2 as *mut _),
        None,
        None,
    )
    .unwrap_or_default();

    for control in [label, edit, ok_button, cancel_button] {
        set_font(control);
    }

    let ptr = std::ptr::addr_of_mut!(PROMPT_EDIT);
    *ptr = edit;
    let value_ptr = std::ptr::addr_of_mut!(PROMPT_VALUE);
    *value_ptr = None;

    let _ = ShowWindow(dialog, SW_SHOW);
    let _ = SetFocus(edit);
    SendMessageW(edit, EM_SETSEL, WPARAM(0), LPARAM(-1));
    let _ = EnableWindow(parent, false);

    let mut msg = MSG::default();
    while GetMessageW(&mut msg, HWND::default(), 0, 0).as_bool() {
        if !IsDialogMessageW(dialog, &msg).as_bool() {
            let _ = TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }

    let _ = EnableWindow(parent, true);
    let _ = SetActiveWindow(parent);

    let value_ptr = std::ptr::addr_of_mut!(PROMPT_VALUE);
    (*value_ptr).take()
}

// ---------------- Window procedure ----------------

pub unsafe extern "system" fn wnd_proc(
    hwnd: HWND,
    msg: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    match msg {
        WM_CREATE => {
            if install(hwnd) {
                LRESULT(0)
            } else {
                LRESULT(-1)
            }
        }
        WM_SIZE => {
            layout();
            LRESULT(0)
        }
        WM_NOTIFY => {
            let header = &*(lparam.0 as *const NMHDR);
            if header.idFrom == ID_TAB && header.code == TCN_SELCHANGE {
                // TCM_GETCURSEL belongs to the tab control. Sending it to the
                // main window always returns the first page, so Services never appeared.
                let index = SendMessageW(header.hwndFrom, TCM_GETCURSEL, WPARAM(0), LPARAM(0))
                    .0 as usize;
                show_page(index);
            } else if header.code == LVN_COLUMNCLICK_CODE {
                let info = &*(lparam.0 as *const NMLISTVIEW);
                let page = if header.idFrom == ID_PROC_LIST {
                    Some(0)
                } else if header.idFrom == ID_SVC_LIST {
                    Some(1)
                } else if header.idFrom == ID_STARTUP_LIST {
                    Some(2)
                } else if header.idFrom == ID_TWEAK_LIST {
                    Some(3)
                } else if header.idFrom == ID_PROFILE_LIST {
                    Some(4)
                } else {
                    None
                };
                if let (Some(page), Some(app)) = (page, app_mut()) {
                    app.sort_by_column(page, info.iSubItem.max(0) as usize);
                }
            } else if header.code == HDN_ITEMCLICKA_CODE || header.code == HDN_ITEMCLICKW_CODE {
                // Header control sends HDN_ITEMCLICK when user clicks a header column.
                // NMHEADERW layout starts with NMHDR, followed by iItem (the column index).
                #[repr(C)]
                struct NmHeaderItem {
                    hdr: NMHDR,
                    i_item: i32,
                    i_button: i32,
                }
                let info = &*(lparam.0 as *const NmHeaderItem);
                if let Some(app) = app_mut() {
                    let page = app.pages.iter().position(|p| {
                        let h = HWND(SendMessageW(p.list, LVM_GETHEADER, WPARAM(0), LPARAM(0)).0 as *mut _);
                        h == header.hwndFrom
                    });
                    if let Some(page) = page {
                        app.sort_by_column(page, info.i_item.max(0) as usize);
                    }
                }
            }
            LRESULT(0)
        }
        WM_COMMAND => {
            let id = (wparam.0 & 0xFFFF) as usize;
            if let Some(app) = app_mut() {
                app.handle_command(id);
            }
            LRESULT(0)
        }
        WM_LOAD => {
            load_initial();
            LRESULT(0)
        }
        WM_TIMER => {
            if wparam.0 == TIMER_ENFORCE {
                let killed = Blocklist::enforce();
                if killed > 0 {
                    if let Some(app) = app_mut() {
                        if app.current == 0 {
                            app.reload_processes();
                        }
                        sync_enforcement();
                    }
                    set_status(&format!(
                        "Blocklist enforcement: {} process(es) terminated.",
                        killed
                    ));
                }
            }
            LRESULT(0)
        }
        WM_CLOSE => {
            let _ = DestroyWindow(hwnd);
            LRESULT(0)
        }
        WM_DESTROY => {
            PostQuitMessage(0);
            LRESULT(0)
        }
        _ => DefWindowProcW(hwnd, msg, wparam, lparam),
    }
}

/// Diagnostic counters used by `--self-test`.
pub struct Diagnostics {
    pub pages: usize,
    /// Item rows actually present in each list view.
    pub rows: [usize; 5],
    /// Items held by the corresponding model vector.
    pub model_rows: [usize; 5],
    pub columns: [usize; 5],
    pub checkboxes: [bool; 5],
    pub status_ready: bool,
}

pub unsafe fn diagnostics() -> Diagnostics {
    let mut result = Diagnostics {
        pages: 0,
        rows: [0; 5],
        model_rows: [0; 5],
        columns: [0; 5],
        checkboxes: [false; 5],
        status_ready: false,
    };
    let Some(app) = app_mut() else {
        return result;
    };

    result.pages = app.pages.len();
    result.model_rows = [
        app.processes.len(),
        app.services.len(),
        app.startups.len(),
        app.tweaks.len(),
        app.profiles.len(),
    ];
    result.status_ready = !app.status.0.is_null();

    for (index, page) in app.pages.iter().enumerate().take(5) {
        result.rows[index] =
            SendMessageW(page.list, LVM_GETITEMCOUNT, WPARAM(0), LPARAM(0)).0 as usize;

        let header = HWND(
            SendMessageW(page.list, LVM_GETHEADER, WPARAM(0), LPARAM(0)).0
                as *mut std::ffi::c_void,
        );
        if !header.0.is_null() {
            result.columns[index] =
                SendMessageW(header, HDM_GETITEMCOUNT, WPARAM(0), LPARAM(0)).0 as usize;
        }

        let style =
            SendMessageW(page.list, LVM_GETEXTENDEDLISTVIEWSTYLE, WPARAM(0), LPARAM(0)).0 as u32;
        result.checkboxes[index] = style & LVS_EX_CHECKBOXES != 0;
    }

    result
}

pub unsafe fn is_window_ready() -> bool {
    app_mut().is_some()
}

/// Loads every table in read-only mode. Used by `--self-test` so the check
/// proves the UI renders real data without changing services or the registry.
pub unsafe fn self_test_load() -> usize {
    let Some(app) = app_mut() else {
        return 0;
    };
    app.reload_processes();
    app.reload_services();
    app.reload_startup();
    app.reload_tweaks();
    app.reload_profiles();
    app.processes.len()
}

/// Dispatches a column click on the requested page and verifies list order.
pub unsafe fn self_test_sort() -> bool {
    let Some(app) = app_mut() else { return false };
    if app.processes.len() < 2 {
        return true;
    }
    // Sort page 0 (Processes) by column 0 (PID) ascending
    app.sort_by_column(0, 0);
    let list = app.pages[0].list;
    let count = SendMessageW(list, LVM_GETITEMCOUNT, WPARAM(0), LPARAM(0)).0 as usize;
    if count < 2 {
        return true;
    }

    let read_pid = |row: usize| -> Option<u32> {
        let mut buf = [0u16; 32];
        let mut it = LVITEMW {
            iSubItem: 0,
            pszText: PWSTR(buf.as_mut_ptr()),
            cchTextMax: 32,
            ..Default::default()
        };
        let len = SendMessageW(list, LVM_GETITEMTEXTW, WPARAM(row), LPARAM(&mut it as *mut _ as isize)).0 as usize;
        let text = String::from_utf16_lossy(&buf[..len]);
        text.parse::<u32>().ok()
    };

    let p0 = read_pid(0);
    let p1 = read_pid(1);
    let Some((p0, p1)) = p0.zip(p1) else { return false };
    if p0 > p1 {
        return false;
    }

    // Now sort descending by column 0 (PID)
    app.sort_by_column(0, 0);
    let p0_desc = read_pid(0);
    let p1_desc = read_pid(1);
    let Some((p0_desc, p1_desc)) = p0_desc.zip(p1_desc) else { return false };
    p0_desc >= p1_desc
}
