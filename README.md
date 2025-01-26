# Unity Nvim Support

Script "csproj_references_for_wsl_fix.cs" simply translates paths references to binaries in .csproj from windows path to wsl/linux path.

Script "sln_csproj_generator.cs" auto generate .csproj files after scripts compilation.

Script "open_in_tmux_nvim.sh" - is a bash script that make Unity to open .cs scripts in my nvim environment - its not ready to go solution for everybody, so use its as reference to write your own

# Requirements:
Unity Package `com.unity.ide.visualstudio`

# How to?
1. Get those 2 .cs scripts in to your project, "sln_csproj_generator.cs" will generate up-to-date .csproj after each script compilation - `[InitializeOnLoad]`
2. "csproj_references_for_wsl_fix.cs" will translate paths in .csproj from window to linux, so the references resolve correctly for nvim lsp.

