'use strict';
const { app, BrowserWindow, ipcMain, nativeTheme } = require('electron');
const path = require('path');
const fs = require('fs');

const launchT0 = Date.now();
const benchArg = process.argv.find(a => a.startsWith('--bench-out='));
const benchOut = benchArg ? benchArg.substring('--bench-out='.length) : null;

nativeTheme.themeSource = 'dark';

// Keep rendering when the window is launched without focus (e.g. during benchmarks).
app.commandLine.appendSwitch('disable-renderer-backgrounding');
app.commandLine.appendSwitch('disable-backgrounding-occluded-windows');

function overlayFor(dark) {
  return dark
    ? { color: '#00000000', symbolColor: '#F2F6FA', height: 48 }
    : { color: '#00000000', symbolColor: '#0B0F14', height: 48 };
}

function createWindow() {
  const win = new BrowserWindow({
    width: 1707,
    height: 1067,
    title: 'HANGAR · Contoso Aero',
    icon: path.join(__dirname, 'src', 'assets', 'AppIcon.ico'),
    backgroundColor: '#00000000',
    backgroundMaterial: 'mica',
    titleBarStyle: 'hidden',
    titleBarOverlay: overlayFor(true),
    show: true,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      backgroundThrottling: false,
    },
  });
  win.setMenu(null);
  if (process.env.HANGAR_DEBUG_LOG) {
    const log = s => { try { fs.appendFileSync(process.env.HANGAR_DEBUG_LOG, s + '\n'); } catch { /* ignore */ } };
    log(`start args=${JSON.stringify(process.argv)}`);
    win.webContents.on('console-message', (e) => log(`[${e.level}] ${e.message} (${e.sourceId}:${e.lineNumber})`));
    win.webContents.on('preload-error', (_e, p, err) => log(`preload-error ${p}: ${err && err.stack}`));
    win.webContents.on('render-process-gone', (_e, d) => log(`render-process-gone ${JSON.stringify(d)}`));
    win.webContents.on('did-finish-load', () => {
      log('did-finish-load');
      setTimeout(() => win.webContents.executeJavaScript(
        'JSON.stringify({host: typeof window.host, hostVal: window.host ? JSON.stringify(window.host) : null, bodyLen: document.body.innerHTML.length, page: window.App && window.App.page, err: window.__lastErr || null})'
      ).then(r => log('probe ' + r)).catch(e => log('probe-fail ' + e)), 1500);
    });
  }
  const argVal = name => {
    const a = process.argv.find(x => x.startsWith(`--${name}=`));
    return a ? a.substring(name.length + 3) : '';
  };
  const query = {
    theme: argVal('theme'),
    page: argVal('page'),
    bench: benchOut ? '1' : '',
    autonav: process.argv.includes('--bench-nav') ? '1' : '',
  };
  win.loadFile(path.join(__dirname, 'src', 'index.html'), { query });

  ipcMain.on('theme', (_e, mode) => {
    nativeTheme.themeSource = mode === 'light' ? 'light' : 'dark';
    try { win.setTitleBarOverlay(overlayFor(mode !== 'light')); } catch { /* not supported */ }
  });

  ipcMain.on('ready', (_e, info) => {
    if (!benchOut) return;
    const payload = { mainStartEpochMs: launchT0, readyEpochMs: Date.now(), renderer: info };
    try { fs.writeFileSync(benchOut, JSON.stringify(payload)); } catch (err) {
      if (process.env.HANGAR_DEBUG_LOG) fs.appendFileSync(process.env.HANGAR_DEBUG_LOG, `ready-write-fail ${err}\n`);
    }
  });

  ipcMain.on('bench-nav', (_e, info) => {
    if (!benchOut) return;
    try { fs.appendFileSync(benchOut + '.nav.jsonl', JSON.stringify(info) + '\n'); } catch { /* ignore */ }
  });
}

app.whenReady().then(createWindow);
app.on('window-all-closed', () => app.quit());
