# Runtime Icons

## 0.3.2
- Fixed lights on some moons disappearing based on camera angle after items have been rendered

## 0.3.1
- Disabled some camera post-processing to hide TZP effects, etc.
- Fixed item icons failing to render sometimes on slow GPUs

## 0.3.0
- Reworked the rendering system to render items asynchronously:
  - Items are enqueued to compute the positioning and framing off the main thread
  - Rendering is done during the normal game rendering loop to avoid extra overhead
  - Textures are post-processed using an async compute shader

## 0.2.0
- Reworked the transparent rendering system to avoid custom passes
- Categorized the mod as client-only in LobbyCompatibility
- Added a new file-based override system
- Added default icon for bodies

## 0.1.6
- Fixed broken package

## 0.1.5
- Initial beta release
