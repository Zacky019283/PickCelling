# Pick-celling

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![Platform](https://img.shields.io/badge/platform-Windows-lightgrey.svg)
![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)

**Pick-celling** is a standalone, desktop-based pixel-art editor designed to make pixel-art creation faster, flexible, and more enjoyable. Built with **.NET 10** and **WinForms**, it provides precise control over every pixel to help you create sprites, icons, and game assets efficiently.

Originally ported from a Unity Editor tool, Pick-celling has evolved into a fully independent Windows application with expanded features, a sleek dark-mode interface, and no external engine dependencies.

## ✨ Features

### 🛠️ Core Tools
- **Paint, Erase, Fill, Line, Pick, & Blur**: A complete set of drawing tools. The Blur tool is specifically designed to only affect textured pixels, preserving transparency.
- **Alpha Transparency**: Full control over brush opacity (0-255).
- **Dithering Patterns**: Includes *Checkerboard 50%* and *Grid 25%* modes for retro-style color blending.
- **Shift-Constraint**: Hold **Shift** to draw perfectly straight lines or 45-degree angles.
- **Symmetry Mode**: Draw with Horizontal, Vertical, or Both mirroring active simultaneously.
- **Transform**: Instantly Flip Horizontal, Flip Vertical, or Rotate 90 degrees.

### 🎨 Canvas & Grid
- **Multi-Canvas Grid**: Work with multiple sub-canvases (up to 16x16 grid) in a single project.
- **Ghost Overlay**: See faint outlines of drawings from other canvases to perfectly align your artwork.
- **Smart Navigation**: Smooth panning (Middle-mouse), precise zooming (up to 512x), and auto-adjusting H/V scrollbars.
- **Smart Scrolling**: Scroll wheels naturally navigate the sidebar panel without accidentally changing numerical values, unless focused.

### ✨ Smart Effects & Auto-Shading
- **Auto-Shade**: Automatically generate 3D-like lighting, shadows, reflections, and edge shadows. Includes per-canvas custom centering points.
- **Stone Pits**: Add rough, rocky textures with customizable depth and chance.
- **Striped Surface & Plank Lines**: Create wood or striped materials instantly.

### 📁 File Management
- **Project Files (.pxb)**: Save and load your entire grid, layers, and settings in a single JSON-based file.
- **Import/Export PNG**: Standard image import and composite grid export.
- **Merge Tools**: Merge all canvases into a single PNG or merge two external PNG files together via alpha-overlay.
- **Undo/Redo**: 50 levels of history to keep your workflow safe.

## 🚀 Installation

### For Users (Ready-to-Run)
1. Go to the [Releases](https://github.com/Zacky019283/Pick-celling/releases) page.
2. Download the latest **Pick-celling.exe** (Self-contained — no .NET installation required).
3. Run the executable.

*Note: Because the application is not digitally signed, Windows SmartScreen may warn you on the first run. Click "More info" and "Run anyway".*

### For Developers (Build from Source)
1. Clone this repository.
2. Open **Pick-celling.sln** in Visual Studio 2022 (or newer).
3. Ensure you have the **.NET 10 SDK** installed.
4. Build and run the project (**F5**).

Alternatively, via CLI:
[di sini, bash]

To build a self-contained single-file executable:
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish

## ⌨️ Keyboard Shortcuts

| Key | Action |
| :--- | :--- |
| **B** / **E** / **G** / **L** / **I** / **R** | Paint / Erase / Fill / Line / Pick / Blur |
| **[** / **]** | Decrease / Increase brush size |
| **Ctrl+Z** / **Ctrl+Y** | Undo / Redo |
| **Ctrl+S** / **Ctrl+O** / **Ctrl+N** | Save / Open / New Project |
| **Ctrl+H** / **Ctrl+Shift+H** / **Ctrl+R** | Flip H / Flip V / Rotate 90° |
| **Ctrl+Wheel** | Zoom in / out |
| **Shift+Wheel** | Scroll horizontally |
| **Alt+Click** | Pick color under cursor |
| **Middle-Mouse Drag** | Pan around the canvas |

## ⚙️ Configuration

Application settings (window size, recent files) and exported textures are saved automatically in your local AppData folder:
**%APPDATA%\Pick-celling**

## 🤝 Contributing

Contributions, issues, and feature requests are welcome! 
If you have ideas for new features, bug fixes, or UX improvements, feel free to open an issue or submit a pull request.

## 📄 License

This project is licensed under the **MIT License**. See the [LICENSE](LICENSE) file for details.