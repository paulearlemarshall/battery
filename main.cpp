#include <windows.h>
#include <shellapi.h>
#include <string>
#include <tchar.h>

#define WM_TRAYICON (WM_USER + 1)
#define ID_TRAY_EXIT 1001
#define ID_TIMER_UPDATE 1002

NOTIFYICONDATA nid = { 0 };
HWND g_hWnd = NULL;

// Function to create an icon with text (battery percentage)
HICON CreateBatteryIcon(int percentage, bool isCharging) {
    int size = GetSystemMetrics(SM_CXSMICON);
    HDC hdc = GetDC(NULL);
    
    // 1. Create the Color Bitmap (XOR mask)
    HDC hColorDC = CreateCompatibleDC(hdc);
    HBITMAP hColorBitmap = CreateCompatibleBitmap(hdc, size, size);
    HBITMAP hOldColorBitmap = (HBITMAP)SelectObject(hColorDC, hColorBitmap);

    // Background is black (will be transparent where mask is white)
    RECT rect = { 0, 0, size, size };
    FillRect(hColorDC, &rect, (HBRUSH)GetStockObject(BLACK_BRUSH));

    // Determine color
    COLORREF color;
    if (isCharging) color = RGB(0, 255, 255); // Cyan
    else if (percentage > 50) color = RGB(0, 255, 0); // Green
    else if (percentage > 20) color = RGB(255, 255, 0); // Yellow
    else color = RGB(255, 0, 0); // Red

    // Setup font - adjust size slightly to fit 3 digits if needed
    int fontSize = (percentage == 100) ? size - 6 : size - 2;
    HFONT hFont = CreateFont(-fontSize, 0, 0, 0, FW_BOLD, FALSE, FALSE, FALSE, ANSI_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, ANTIALIASED_QUALITY, DEFAULT_PITCH | FF_SWISS, _T("Segoe UI"));
    HFONT hOldFont = (HFONT)SelectObject(hColorDC, hFont);

    SetTextColor(hColorDC, color);
    SetBkMode(hColorDC, TRANSPARENT);

    std::wstring text = std::to_wstring(percentage);
    DrawText(hColorDC, text.c_str(), -1, &rect, DT_CENTER | DT_VCENTER | DT_SINGLELINE);

    // 2. Create the Mask Bitmap (AND mask)
    // We want the text area to be black (0) and background to be white (1)
    HDC hMaskDC = CreateCompatibleDC(hdc);
    HBITMAP hMaskBitmap = CreateBitmap(size, size, 1, 1, NULL); // Monochrome
    HBITMAP hOldMaskBitmap = (HBITMAP)SelectObject(hMaskDC, hMaskBitmap);

    // Fill mask with white (transparent area)
    FillRect(hMaskDC, &rect, (HBRUSH)GetStockObject(WHITE_BRUSH));

    // Draw text in black on the mask (opaque area)
    SelectObject(hMaskDC, hFont);
    SetTextColor(hMaskDC, RGB(0, 0, 0));
    SetBkMode(hMaskDC, TRANSPARENT);
    DrawText(hMaskDC, text.c_str(), -1, &rect, DT_CENTER | DT_VCENTER | DT_SINGLELINE);

    // Cleanup DCs and select old objects
    SelectObject(hColorDC, hOldFont);
    SelectObject(hMaskDC, hOldFont); // Note: we used the same hFont
    SelectObject(hColorDC, hOldColorBitmap);
    SelectObject(hMaskDC, hOldMaskBitmap);

    ICONINFO ii = { 0 };
    ii.fIcon = TRUE;
    ii.hbmMask = hMaskBitmap;
    ii.hbmColor = hColorBitmap;
    HICON hIcon = CreateIconIndirect(&ii);

    // Final cleanup
    DeleteObject(hFont);
    DeleteObject(hColorBitmap);
    DeleteObject(hMaskBitmap);
    DeleteDC(hColorDC);
    DeleteDC(hMaskDC);
    ReleaseDC(NULL, hdc);

    return hIcon;
}

void UpdateTrayIcon(HWND hWnd) {
    SYSTEM_POWER_STATUS sps;
    if (GetSystemPowerStatus(&sps)) {
        int percent = sps.BatteryLifePercent;
        if (percent > 100) percent = 100;
        bool isCharging = (sps.ACLineStatus == 1);

        HICON hIcon = CreateBatteryIcon(percent, isCharging);
        if (nid.hIcon) DestroyIcon(nid.hIcon);
        nid.hIcon = hIcon;

        std::wstring tooltip = L"Battery: " + std::to_wstring(percent) + L"%";
        if (sps.BatteryLifeTime != (DWORD)-1) {
            int mins = sps.BatteryLifeTime / 60;
            tooltip += L" (" + std::to_wstring(mins / 60) + L"h " + std::to_wstring(mins % 60) + L"m remaining)";
        } else if (isCharging) {
            tooltip += L" (Charging)";
        }
        
        _tcsncpy_s(nid.szTip, tooltip.c_str(), _TRUNCATE);
        Shell_NotifyIcon(NIM_MODIFY, &nid);
    }
}

LRESULT CALLBACK WndProc(HWND hWnd, UINT message, WPARAM wParam, LPARAM lParam) {
    switch (message) {
    case WM_TRAYICON:
        if (lParam == WM_RBUTTONUP) {
            POINT curPoint;
            GetCursorPos(&curPoint);
            HMENU hMenu = CreatePopupMenu();
            AppendMenu(hMenu, MF_STRING, ID_TRAY_EXIT, _T("Exit"));
            SetForegroundWindow(hWnd);
            TrackPopupMenu(hMenu, TPM_BOTTOMALIGN | TPM_LEFTALIGN, curPoint.x, curPoint.y, 0, hWnd, NULL);
            DestroyMenu(hMenu);
        }
        break;
    case WM_COMMAND:
        if (LOWORD(wParam) == ID_TRAY_EXIT) {
            Shell_NotifyIcon(NIM_DELETE, &nid);
            PostQuitMessage(0);
        }
        break;
    case WM_TIMER:
        if (wParam == ID_TIMER_UPDATE) {
            UpdateTrayIcon(hWnd);
        }
        break;
    case WM_DESTROY:
        Shell_NotifyIcon(NIM_DELETE, &nid);
        PostQuitMessage(0);
        break;
    default:
        return DefWindowProc(hWnd, message, wParam, lParam);
    }
    return 0;
}

int WINAPI WinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPSTR lpCmdLine, int nCmdShow) {
    const wchar_t CLASS_NAME[] = L"BatteryTrayClass";
    WNDCLASS wc = { 0 };
    wc.lpfnWndProc = WndProc;
    wc.hInstance = hInstance;
    wc.lpszClassName = CLASS_NAME;
    RegisterClass(&wc);

    g_hWnd = CreateWindowEx(0, CLASS_NAME, L"Battery Tray", 0, 0, 0, 0, 0, HWND_MESSAGE, NULL, hInstance, NULL);

    nid.cbSize = sizeof(NOTIFYICONDATA);
    nid.hWnd = g_hWnd;
    nid.uID = 1;
    nid.uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP;
    nid.uCallbackMessage = WM_TRAYICON;
    nid.hIcon = CreateBatteryIcon(0, false);
    _tcsncpy_s(nid.szTip, L"Battery Monitor", _TRUNCATE);

    Shell_NotifyIcon(NIM_ADD, &nid);
    UpdateTrayIcon(g_hWnd);

    SetTimer(g_hWnd, ID_TIMER_UPDATE, 10000, NULL); // Update every 10 seconds

    MSG msg;
    while (GetMessage(&msg, NULL, 0, 0)) {
        TranslateMessage(&msg);
        DispatchMessage(&msg);
    }

    return 0;
}
