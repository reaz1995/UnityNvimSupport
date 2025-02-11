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


