# Roslyn LSP setup for Neovim (WSL / Linux)

Tested setup: WSL Ubuntu 22.04+, Neovim with [seblyng/roslyn.nvim](https://github.com/seblyng/roslyn.nvim).
Check the roslyn.nvim README for the current minimum Neovim version — recent versions require Neovim 0.12+.

## 1. .NET SDK

```bash
sudo add-apt-repository ppa:dotnet/backports
sudo apt-get update
sudo apt-get install -y dotnet-sdk-9.0
dotnet --list-sdks
```

Add to `~/.zshrc` / `~/.bashrc` (the package launches nvim through your login + interactive shell, so these are picked up):

```bash
export DOTNET_ROOT=/usr/lib/dotnet
```

## 2. Roslyn language server

**Mason (easiest)** — roslyn.nvim recommends adding the Crashdummyy registry, which ships the same server version VS Code uses:

```lua
require("mason").setup({
  registries = {
    "github:mason-org/mason-registry",
    "github:Crashdummyy/mason-registry",
  },
})
```

then `:MasonInstall roslyn`.

**dotnet tool:**

```bash
dotnet tool install -g roslyn-language-server --prerelease \
  --source https://pkgs.dev.azure.com/azure-public/vside/_packaging/vs-impl/nuget/v3/index.json
```

**Manual (old way):** download `Microsoft.CodeAnalysis.LanguageServer.linux-x64` from the
[vs-impl Azure feed](https://dev.azure.com/azure-public/vside/_artifacts/feed/vs-impl), then

```bash
mkdir -p ~/.local/share/nvim/roslyn && cd ~/.local/share/nvim/roslyn
unzip <downloaded>.nupkg -d extracted
mv extracted/content/LanguageServer/linux-x64/* . && rm -r extracted
dotnet Microsoft.CodeAnalysis.LanguageServer.dll --version
```

## 3. roslyn.nvim

```lua
-- lazy.nvim
return {
  "seblyng/roslyn.nvim",
  ft = "cs",
  opts = {
    filewatching = "roslyn", -- let the server watch files (new files from nvim are picked up via the wildcard Compile items)
  },
  config = function(_, opts)
    vim.lsp.config("roslyn", {
      settings = {
        ["csharp|background_analysis"] = {
          dotnet_analyzer_diagnostics_scope = "openFiles",
          dotnet_compiler_diagnostics_scope = "fullSolution",
        },
      },
    })
    require("roslyn").setup(opts)
  end,
}
```

roslyn.nvim finds the `.sln` Unity generates in the project root. If you have several, use `:Roslyn target`.

Keep the LSP log small:

```lua
vim.lsp.log.set_level("error") -- older Neovim: vim.lsp.set_log_level("error")
```

## 4. File watching limits (Linux / WSL)

Unity projects have many files:

```bash
echo "fs.inotify.max_user_instances=512"   | sudo tee -a /etc/sysctl.conf
echo "fs.inotify.max_user_watches=524288"  | sudo tee -a /etc/sysctl.conf
sudo sysctl -p
```

## 5. Unity side

Select **Neovim (WSL)** in *Preferences → External Tools* and press **Regenerate project files**.
The generated `.csproj` files then contain WSL paths and wildcard `Compile` items — no manual editing needed anymore.

Roslyn usually needs ~10 s to load a Unity solution the first time.
