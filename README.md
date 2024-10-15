# UnityNvimSupport
The script "postProcessCSProj.cs" simply translates paths references to binaries in .csproj from windows path to wsl/linux path.
Which allows Nvim Lsp to find references to required binaries.

#How to?
1. Create new C# script in Unity, replace the content of it to the the content of "postProcessCSProj.cs", save.
2. Remove all of .csporj files in main dir of project.
3. Unity->Preferences->External tools
4. 
![image](https://github.com/user-attachments/assets/fb5b6de2-c5e5-4b7c-a133-5c320358b0cf)
Pick VisualStudio, then regenerate files.
It will regenerate .csproj files it main dir of project.
Keep the list of generated files short or your nvim lsp will be very slow.

Now it's working!

![image](https://github.com/user-attachments/assets/6e70d0d7-371f-41f1-bf29-c770a1eb1cc8)


