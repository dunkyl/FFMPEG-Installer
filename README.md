# FFMPEG-Installer

Windows installer to add [FFmpeg](https://ffmpeg.org/) and a path entry to your system.

Note: FFmpeg is licensed separately from this source code. I am not the owner or maintainer of FFmpeg, credit for that goes to the open source contributors to the FFmpeg project, which is licensed separately from this project.


## Build it yourself

*temporary wip*

dotnet 8+

```sh
dotnet tool install --global wix
wix extension add WixToolset.UI.wixext
dotnet fsi build.fsx
```