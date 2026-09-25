// DevBox — Blazor Hybrid 启动脚本：主题持久化。
// index.html 在 Blazor 挂载前加载本文件：先把持久化主题写到 <html data-theme>，
// 避免暗色用户首帧白屏；再暴露 devbox.getTheme / devbox.setTheme 供组件读写。
(function () {
  'use strict';

  var STORAGE_KEY = 'devbox.theme';

  function readTheme() {
    try {
      return window.localStorage.getItem(STORAGE_KEY) === 'dark' ? 'dark' : 'light';
    } catch (error) {
      return 'light';
    }
  }

  function persistTheme(theme) {
    try {
      window.localStorage.setItem(STORAGE_KEY, theme);
    } catch (error) {
      // localStorage 不可用时主题不持久化，功能不受影响。
    }
  }

  document.documentElement.setAttribute('data-theme', readTheme());

  window.devbox = {
    getTheme: readTheme,
    setTheme: function (theme) {
      var normalized = theme === 'dark' ? 'dark' : 'light';
      persistTheme(normalized);
      document.documentElement.setAttribute('data-theme', normalized);
    }
  };
})();
