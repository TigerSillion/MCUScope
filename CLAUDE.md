# CLAUDE.md - MCUScope Repository Guide

## Project Overview

**MCUScope** (DTLScope) is a Windows desktop oscilloscope and signal measurement application developed by Desk Top Laboratories Inc. (Japan). It is used for analyzing electronic signals from microcontrollers, with ICSP (In-Circuit Serial Programming) hardware interface support.

- **Application**: DTLScope.exe v1.6.0.2
- **Platform**: Windows only (.NET Framework / WPF)
- **Architecture**: MVVM (Model-View-ViewModel) using MVVM Light framework
- **UI Framework**: WPF with Xceed AvalonDock docking panels and extended toolkit controls

## Repository Structure

This repository contains **precompiled binaries and documentation only** — no source code.

```
MCUScope/
└── 参考文档和软件包汇总/          # "Reference Documentation and Software Package Summary"
    ├── DTLScope.exe               # Main application executable (v1.6.0.2)
    ├── DTLScope.exe.config        # .NET assembly binding configuration
    ├── Plugins/                   # Plugin modules (3 DLLs)
    │   ├── ...Modules.ArrayEditor.dll
    │   ├── ...Modules.CustomControlPanel.dll
    │   └── ...Modules.Timetable.dll
    ├── *.dll                      # 21 dependency DLLs (see Dependencies below)
    ├── *.swidtag                  # SWID license tag (ISO/IEC 19770-2)
    ├── P00301-*.pdf               # ICSP Users Manual (Rev 1.03 EN)
    ├── P30002-*.pdf               # ICSP Library Manual (v3.60, v1.09 EN)
    └── 项目说明.docx               # Project description (Chinese, with screenshots)
```

## Application Modules (DeskTopLab.Scope namespace)

| Assembly | Purpose |
|----------|---------|
| `DeskTopLab.Scope.Common.dll` | Shared utilities |
| `DeskTopLab.Scope.Devices.dll` | Hardware/device interface, library version management |
| `DeskTopLab.Scope.ScopeApp.Common.dll` | Application-level shared code |
| **Plugins/** | |
| `...Modules.ArrayEditor.dll` | Array data editing |
| `...Modules.CustomControlPanel.dll` | Custom control panel UI |
| `...Modules.Timetable.dll` | Timing/waveform analysis |

## Key Dependencies

| Library | Purpose |
|---------|---------|
| OxyPlot (+ Wpf, WindowsForms, Xps) | Signal charting and visualization |
| MathNet.Numerics | Numerical computation and signal processing |
| Jace | Mathematical expression evaluation |
| GalaSoft.MvvmLight (+ Extras, Platform) | MVVM framework |
| Xceed.Wpf.Toolkit | Extended WPF controls |
| Xceed.Wpf.AvalonDock | Docking panel layout |
| Microsoft.Practices.ServiceLocation | Service locator pattern / IoC |
| Microsoft.Expression.Interactions | Blend interaction triggers |
| System.Windows.Interactivity | WPF interactivity behaviors |

## Development Notes

- **No build system**: No .csproj, .sln, Makefile, or CMake files. The source code is not included in this repository.
- **No tests**: No test infrastructure or test files present.
- **No CI/CD**: No GitHub Actions, pipelines, or automated workflows configured.
- **Plugin architecture**: The application supports modular plugins loaded from the `Plugins/` directory.
- **Configuration**: Assembly binding redirects are managed in `DTLScope.exe.config` (XML format).

## Git Workflow

- **Primary branch**: `master`
- **No branch protection or CI gates** are configured.
- Single-commit history (initial commit with all binaries).

## For AI Assistants

- This is a **binary-only distribution** repository. There is no source code to modify or build.
- Documentation files are in Chinese (项目说明.docx) and English (PDF manuals).
- Any contributions would likely involve documentation, tooling, or adding source code if the project transitions to an open-source model.
- The `.exe` and `.dll` files are .NET PE32 assemblies targeting Windows.
