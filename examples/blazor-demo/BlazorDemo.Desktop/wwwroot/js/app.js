(function () {
    'use strict';

    // Restore the persisted theme before Blazor boots so the first paint has no flash.
    var storedTheme = null;
    try { storedTheme = localStorage.getItem('devbox-theme'); } catch (e) { /* ignore */ }
    if (storedTheme) { document.documentElement.dataset.theme = storedTheme; }

    window.devbox = {
        getTheme: function () {
            try { return localStorage.getItem('devbox-theme') || 'light'; } catch (e) { return 'light'; }
        },
        setTheme: function (theme) {
            try { localStorage.setItem('devbox-theme', theme); } catch (e) { /* ignore */ }
            document.documentElement.dataset.theme = theme;
        },
        clipboard: {
            copy: function (text) {
                if (navigator.clipboard && navigator.clipboard.writeText) {
                    return navigator.clipboard.writeText(String(text));
                }
                var area = document.createElement('textarea');
                area.value = String(text);
                area.style.position = 'fixed';
                area.style.opacity = '0';
                document.body.appendChild(area);
                area.select();
                try { document.execCommand('copy'); } catch (e) { /* ignore */ }
                document.body.removeChild(area);
                return Promise.resolve();
            }
        }
    };
})();
