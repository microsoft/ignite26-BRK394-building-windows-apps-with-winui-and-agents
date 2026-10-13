'use strict';
const { contextBridge, ipcRenderer } = require('electron');

const q = new URLSearchParams(window.location.search);
const bench = q.get('bench') === '1';
const autoNav = q.get('autonav') === '1';
const initialTheme = q.get('theme') || null;
const initialPage = q.get('page') || null;

contextBridge.exposeInMainWorld('host', {
  bench,
  autoNav,
  initialTheme,
  initialPage,
  setTheme: mode => ipcRenderer.send('theme', mode),
  ready: info => ipcRenderer.send('ready', info),
  navTiming: info => ipcRenderer.send('bench-nav', info),
});
