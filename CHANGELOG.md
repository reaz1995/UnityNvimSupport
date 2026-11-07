# Changelog

## [1.0.0] - 2026-11-07

### Added
- Unity package (`com.sebastianlangner.nvim-unity`), installable from git URL or disk.
- Neovim registered as an External Script Editor: "Neovim" (native) and "Neovim (WSL)".
- Opens files at line/column in a running Neovim over msgpack-RPC (`nvim --listen 127.0.0.1:PORT`), one port per project.
- Launch modes: Windows Terminal / platform terminal, tmux session, custom command.
- WSL launch script runs nvim through the user's login + interactive shell (keeps `DOTNET_ROOT`, `PATH`, …).
- Incremental .sln/.csproj generation through com.unity.ide.visualstudio (legacy or SDK-style), with its package filters in Preferences.
- Per-assembly wildcard `Compile` items so files created outside Unity get LSP support (respects asmdef/asmref, Editor, Plugins folders).
- Settings UI in Preferences > External Tools, menu items under Tools > Neovim.
- Windows Terminal window is brought to the front when a file is opened in a running instance (without switching tabs).
- Launch script explains how to fix it when `nvim` is not found in the selected WSL distribution.

### Changed
- WSL path conversion now handles every absolute path in the project (HintPath, Analyzer, None, …), `C:/` style paths, `\\wsl$\` / `\\wsl.localhost\` UNC paths and a configurable mount root.
- Path conversion only runs while "Neovim (WSL)" is selected, so switching back to Visual Studio/Rider produces normal project files.

### Removed
- The loose scripts `sln_csproj_generator.cs`, `csproj_references_for_wsl_fix.cs` and `open_in_tmux_nvim.sh` (replaced by the package).

### Fixed
- Crash on paths shorter than 3 characters in the WSL path converter.
- `None` items after the first one without an `Include` attribute were skipped.
