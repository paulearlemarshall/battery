# BatteryTray Performance Checklist

Use this checklist before release:

- [ ] Tray menu opens quickly (<250ms perceived delay)
- [ ] No `Thread.Sleep` in menu-open path
- [ ] WMI-backed fields use TTL caching
- [ ] Public IP fetch is async and cached (>=5 min)
- [ ] Top CPU process uses sampled deltas (no per-process sleep)
- [ ] Rebuilding context menu disposes old menu
- [ ] Tray icon old instance is disposed when replaced
- [ ] BATTERYTRAY_DEBUG=1 logging works and does not crash app
