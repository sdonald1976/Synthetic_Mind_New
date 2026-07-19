# Test video

Sample clips for the vision pipeline (finding 019) and the batch watcher (finding 028).

| File | Source | Notes |
|---|---|---|
| `earth.gif` | Wikimedia Commons ["Rotating earth (large).gif"](https://commons.wikimedia.org/wiki/File:Rotating_earth_(large).gif) | Public domain (derived from NASA imagery). 44 frames, 400×400. Decoded by our own `GifDecoder`. |
| `bbb.mp4` | *Big Buck Bunny* (Blender Foundation, [CC-BY 3.0](https://peach.blender.org/about/)) via w3schools | 10 s, h264 video + aac audio. A real video-with-sound file to exercise `SyntheticMind.Watch` (OpenCvSharp frames + ffmpeg audio). |

Committed so the experiments run without a download. Regenerate at any time from the sources above.
