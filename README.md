# Unity Nvim Support

Script `csproj_references_for_wsl_fix.cs` simply translates paths references to binaries in .csproj from windows path to wsl/linux path.

Script `sln_csproj_generator.cs` auto generate .csproj files after scripts compilation.

Script `open_in_tmux_nvim.sh` - is a bash script that make Unity to open .cs scripts in my nvim environment - its not ready to go solution for everybody, so use its as reference to write your own

# Requirements:
Unity Package `com.unity.ide.visualstudio`

# How to?
Place scripts: `sln_csproj_generator.cs` and `csproj_references_for_wsl_fix.cs` in to your project, those are Editor only scripts so its wise to put them under `Assets/Editor` or you will get errors in runtime. 
- `sln_csproj_generator.cs` will generate up-to-date .csproj after each script compilation - in detail - every time the domain is reloaded - its bound to  `[InitializeOnLoad]`.
- `csproj_references_for_wsl_fix.cs` will translate paths in .csproj from window to linux, so the references resolve correctly for nvim lsp - it executes as part of AssetPostprocessor.
- `csproj_references_for_wsl_fix.cs` have hardcoded enable `bool enable = true;`, so be aware of it and apply changes if its required.

Simplified events order:
Scripts Compilation -> Domain Reload -> `[InitializeOnLoad]` -> Assets Import -> Assets Postprocessing.

# Future development
- [ ] Wrap this repo in to Unity Package

# lsp support for newly created files .cs from nvim/terminal
add line `<Compile Include="Assets\**\*.cs" />`,  in Assembly-CSharp.csproj, ofc in ItemGroup where other files are included 
![image](https://github.com/user-attachments/assets/d8efb9f1-0591-4b0c-af8c-c8dae1aae3f9)

# How To Set Up Roslyn lsp
My setup:
```
wsl with Unbuntu22.04
NVIM v0.10.2
I use Mason for external tools for lsp, but Roslyn require to be set independently
but works alongside mason
```

## 1. Install .NET 9 SDK

Open your terminal and run:

```bash
sudo apt-get update
sudo apt-get install -y software-properties-common
```

Then install the .NET 9 SDK:

```bash
sudo add-apt-repository ppa:dotnet/backports
sudo apt-get update
sudo apt-get install -y dotnet-sdk-9.0
```

Verify the installation:

```bash
dotnet --list-sdks
```

### Update Your Shell Environment (for zsh)

Add these lines to your `~/.zshrc` (or your shell profile) so that `dotnet` and MSBuild are properly set:

```bash
export DOTNET_ROOT=/usr/lib/dotnet
export MSBuildSDKsPath="${DOTNET_ROOT}/sdk/9.0.103/Sdks"
```

Then reload your shell:

```bash
source ~/.zshrc
```

---

## 2. Download and Unpack Roslyn

### Download Roslyn for Linux

Download the package from:

- **Linux:**  
    [https://dev.azure.com/azure-public/vside/_artifacts/feed/vs-impl/NuGet/Microsoft.CodeAnalysis.LanguageServer.linux-x64/overview/5.0.0-1.25111.6](https://dev.azure.com/azure-public/vside/_artifacts/feed/vs-impl/NuGet/Microsoft.CodeAnalysis.LanguageServer.linux-x64/overview/5.0.0-1.25111.6)

Or navigate to:

- **Azure DevOps Artifact Feed:**  
    Go to `https://dev.azure.com/azure-public/vside/_artifacts/feed/vs-impl` and look for `Microsoft.CodeAnalysis.LanguageServer`, then select the Linux-x64 version and click download.

### Unzip and Install Roslyn

Run these commands in your terminal (in WSL):

```bash
# Create the target directory for Roslyn
mkdir -p ~/.local/share/nvim/roslyn
cd ~/.local/share/nvim/roslyn

# Move your downloaded .nupkg to this folder and rename it (adjust <path_of_downloaded .nupkg> accordingly)
mv <path_of_downloaded .nupkg> roslyn.nupkg

# Unzip the .nupkg file into a temporary folder
unzip roslyn.nupkg -d roslyn_extracted

# Move the contents of the Linux-x64 folder to the current directory
mv roslyn_extracted/content/LanguageServer/linux-x64/* .

# Clean up the extracted folder and the original .nupkg file
rm -r roslyn_extracted roslyn.nupkg
```

Test the installation by running:

```bash
dotnet Microsoft.CodeAnalysis.LanguageServer.dll --version
```

You should see a version number printed.

---

## 3. Extend File-Watching Limits in Ubuntu

For Unity projects, you may need to increase the number of files the OS can watch. Check your current limits:

```bash
cat /proc/sys/fs/inotify/max_user_instances
cat /proc/sys/fs/inotify/max_user_watches
ulimit -n
```

Temporarily increase them:

```bash
sudo sysctl -w fs.inotify.max_user_instances=512
sudo sysctl -w fs.inotify.max_user_watches=524288
```

To make these changes permanent, add the following lines to `/etc/sysctl.conf`:

```bash
echo "fs.inotify.max_user_instances=512" | sudo tee -a /etc/sysctl.conf
echo "fs.inotify.max_user_watches=524288" | sudo tee -a /etc/sysctl.conf
sudo sysctl -p
```

---

## 4. Neovim Plugin Configuration for Roslyn

Place this configuration in your Neovim config (e.g., in your Lua config file):
https://github.com/seblyng/roslyn.nvim
```lua
return {
  "seblyng/roslyn.nvim",
  autostart = true,
  ft = "cs",
  config = {
    settings = {
      ["csharp|background_analysis"] = {
        background_analysis = {
          dotnet_analyzer_diagnostics_scope = "fullSolution",
          dotnet_compiler_diagnostics_scope = "fullSolution",
        }
      }
    }
  },
  exe = {
    "dotnet",
    vim.fs.joinpath(vim.fn.stdpath("data"), "roslyn", "Microsoft.CodeAnalysis.LanguageServer.dll"),
  },
  filewatching = true,
  capabilities = function()
    local capabilities = require("cmp_nvim_lsp").default_capabilities()

    capabilities = vim.tbl_deep_extend("force", capabilities, {
      textDocument = {
        foldingRange = {
          dynamicRegistration = true,
          lineFoldingOnly = true,
        },
        completion = {
          completionItem = {
            snippetSupport = true,
          },
        },
      },
      workspace = {
        didChangeWatchedFiles = {
          dynamicRegistration = true,
        },
      },
    })
    return capabilities
  end,
  root_dir = require("lspconfig").util.root_pattern("package.json", "yarn.lock", "tsconfig.json", ".git"),
}
```

Add this line somewhere in your nvim config to prevent bloating LspLog.
```lua
vim.lsp.set_log_level("error")
```

---

## 5. .csproj Workaround for LSP Support
To ensure that your LSP picks up new C# files automatically, add the following to your `Assembly-CSharp.csproj`:

(This will be add to auto generate in .csproj on compile in future)
```xml
# Look for item group that includes Compile elements, then add it here
  <Compile Include="Assets\**\*.cs">
```

## Summary:
Roslyn should start in 10 sec for unity project, then with this set up it will work for any newly created file via terminal in this project.
