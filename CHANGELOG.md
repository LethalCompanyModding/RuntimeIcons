# Runtime Icons

## 0.3.0
- full rework of the rendering system to render items asynchronously:
  - enqueue items to compute their positions for rendering off the main thread
  - render items during the normal game rendering loop to avoid overhead
  - use a compute shader to process textures entirely on the GPU

## 0.2.0
- new rendering system w/o custom-passes
- mod is now correctly categorized as client-only
- new file-based override system
- add default icon for bodies

## 0.1.6
- fix broken package

## 0.1.5
- Initial beta release
