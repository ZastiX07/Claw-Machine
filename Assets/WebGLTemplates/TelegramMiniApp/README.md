# Telegram Mini App WebGL Template

This template is intended for Unity WebGL builds embedded as a Telegram Mini App.

## What it does
- Loads `https://telegram.org/js/telegram-web-app.js`
- Calls `Telegram.WebApp.ready()` and `Telegram.WebApp.expand()`
- Reacts to Telegram theme changes and viewport updates
- Uses full-screen canvas layout optimized for in-app mobile webview
- Exposes a minimal JS bridge for Unity plugins at `window.UnityTelegramMiniApp`

## Unity setup
1. Open **Project Settings -> Player -> Resolution and Presentation**.
2. Set **WebGL Template** to `TelegramMiniApp`.
3. Build WebGL as usual.

## Notes
- The game must be hosted over HTTPS for Telegram Mini Apps.
- If you need Telegram init data in C#, create a `.jslib` plugin and read `window.UnityTelegramMiniApp.getInitData()`.
