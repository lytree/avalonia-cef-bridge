// Tarui Blazor bridge: lets Blazor JS interop reuse Tarui's CEF invoke channel when a Blazor app
// wants to drive the front-end (for example when a Razor component calls JS into a feature that
// then needs to call back into Tarui over the IPC pipe rather than via ITaruiIpc).
//
// In a Tarui desktop window the page runs inside a CEF instance whose JavaScript context already
// exposes `window.invokeCSharpAction` for the standard IPC dispatch. This bridge simply proxies to
// it so JS code can call `window.tarui.invoke('core:os|info', {})` instead of poking at the raw
// message channel. The contract is intentionally tiny: returning a JSON string with `{success,
// payload, error}` matching Tarui's InvokeResponse shape.

(function () {
    'use strict';

    if (window.tarui && window.tarui.invoke) {
        return;
    }

    async function invoke(command, payload) {
        if (typeof command !== 'string' || command.length === 0) {
            throw new Error('Tarui.invoke: command must be a non-empty string.');
        }

        const request = JSON.stringify({
            protocol: 1,
            id: cryptoRandomId(),
            command: command,
            payload: payload ?? {},
            windowLabel: 'main',
            webViewLabel: 'main',
        });

        // CefGlue Next delivers Tarui invocations through window.invokeCSharpAction.
        if (typeof window.invokeCSharpAction !== 'function') {
            throw new Error('Tarui.invoke: window.invokeCSharpAction is not available; ' +
                'the page is not running inside a Tarui WebView.');
        }

        const responseText = await window.invokeCSharpAction(request);
        const response = JSON.parse(responseText);

        if (!response.success) {
            const error = response.error || { code: 'UNKNOWN', message: 'Tarui command failed.' };
            const failure = new Error(error.message);
            failure.code = error.code;
            failure.command = command;
            throw failure;
        }

        return response.payload ?? null;
    }

    function cryptoRandomId() {
        if (window.crypto && typeof window.crypto.randomUUID === 'function') {
            return window.crypto.randomUUID().replace(/-/g, '');
        }
        return Math.floor(Math.random() * 0xFFFFFFFF).toString(16).padStart(8, '0');
    }

    window.tarui = Object.freeze({
        invoke: invoke,
    });
})();