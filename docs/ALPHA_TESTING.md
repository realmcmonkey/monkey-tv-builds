# Alpha test checklist

Use a copy of the test build and non-critical streaming accounts. Back up `Data` before changing builds.

## Startup and shell

- [ ] Splash appears before Home and no service opens automatically.
- [ ] Home, Services, and Settings work without creating another normal taskbar window.
- [ ] Resize, maximize, and F11 retain the top navigation and visible fullscreen exit control.
- [ ] System, Light, and Dark themes are readable; keyboard focus is visible.

## Services

- [ ] Favorite/unfavorite persists across restart.
- [ ] Hidden services stay in Services and do not appear on Home.
- [ ] Home artwork opens the chosen service.
- [ ] Service A → Home → Service A resumes normally.
- [ ] Service A → Home → Service B reuses the same browser and loads B.
- [ ] Default Experience, TV Experience, and any configured custom user agent remain per-service.
- [ ] Add, edit, reorder, remove, and restore services work without losing unrelated settings.

## Browser behavior

- [ ] Back, reload, Home, F11, and Escape behave correctly.
- [ ] Login, popup/auth flow, downloads, and provider fullscreen work where the provider permits them.
- [ ] Protected playback is tested manually with the user’s own account.
- [ ] Restart preserves the intended shared browser profile and does not reopen a service automatically.

## VPN Location

- [ ] Startup displays Direct and does not connect automatically.
- [ ] Direct, USA, Canada, and United Kingdom selection display clear connecting/success/failure states.
- [ ] A service is not reloaded until the chosen route is verified.
- [ ] Direct → location → Direct and country switching work repeatedly.
- [ ] Connection loss fails safely rather than silently using an unintended route.
- [ ] Other Windows applications retain their normal connection.

## Data and recovery

- [ ] `Data` stays beside the EXE and ordinary settings persist after restart.
- [ ] Logs remain sanitized and bounded.
- [ ] Backup/export does not include browser profiles, credentials, cookies, or provider sign-ins.
- [ ] A clean shutdown leaves no Monkey TV process running.
