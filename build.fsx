open System
open System.Diagnostics
open System.IO
open System.Net.Http
open System.IO.Compression

let httpClient = new HttpClient()

let filenameOf (path: string) =
    let uri = Uri path
    Path.GetFileName uri.LocalPath

let download (path: string) = task {
    let filename = filenameOf path

    let! r = httpClient.GetAsync path
    
    use outfile = File.OpenWrite filename
    do! r.Content.CopyToAsync outfile
}

let run processName args =
    let p = new ProcessStartInfo (
        FileName = processName,
        Arguments = args,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    )
    let proc = Process.Start p
    let stdout = proc.StandardOutput.ReadToEnd ()
    let stderr = proc.StandardError.ReadToEnd ()
    proc.WaitForExit ()

    if proc.ExitCode <> 0 then
        printfn $"Error running {processName}:\n{stdout}\n{stderr}"
        exit proc.ExitCode

let un7z (path: string) (outpath: string) =
    // use reader = new ArchiveReader(path)
    // let progress = new Progress<Report>(fun f -> printfn $"{f}%%")
    // reader.Save(Path.GetDirectoryName path, progress);
    run "7z" $"x -y -spe -o{outpath} {path} "

let rm path =
    if File.Exists path then
        File.Delete path
    else if Directory.Exists path then
        Directory.Delete path
    else
        printfn $"{path} does not exist"

let args = System.Environment.GetCommandLineArgs ()

let fetchFFMPEG () = task {
    let! res_ver = httpClient.GetAsync "https://www.gyan.dev/ffmpeg/builds/release-version"
    let! releaseVer = res_ver.Content.ReadAsStringAsync()

    let releaseURL = $"https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-{releaseVer}-full_build.7z"

    let! res_sha = httpClient.GetAsync $"{releaseURL}.sha256"
    let! sha = res_sha.Content.ReadAsStringAsync()

    let zipFileName = filenameOf releaseURL

    if File.Exists zipFileName then
        printfn $"   Found existing release at {zipFileName}."
    else
        printfn $"   Downloading release {releaseVer}..."
        do! download releaseURL
        printfn "   Downloaded release!"
    
    // checksum
    let sha_downloaded =
        //run "sha256sum" zipFileName
        let sha256 = System.Security.Cryptography.SHA256.Create()
        let hash = sha256.ComputeHash(File.ReadAllBytes(zipFileName))
        let hash_str = BitConverter.ToString(hash)
        hash_str.Replace("-", "").ToLower()

    if sha <> sha_downloaded then
        printfn $"   SHA mismatch:\nClaimed: {sha}\nActual: {sha_downloaded}"
        exit 1
    else
        printfn "   SHA match"

    let destination = "FFMPEG"
    if Directory.Exists destination then
        printfn "   Already unpacked."
    else
        printfn "   Unpacking release..."
    
        un7z zipFileName "."

        printfn "   Unpacked release."

        // rename to folder without version
        Directory.Move($"ffmpeg-{releaseVer}-full_build", "FFMPEG")
    return releaseVer
}

printfn "Fetching latest release:"
let version = fetchFFMPEG() |> Async.AwaitTask |> Async.RunSynchronously

let heat s = run "heat" s

let candle s = run "candle" s

let light s = run "light" s

let startTime = DateTime.Now

let genComponents = [
    "FFMPEGbin", "FFMPEG\\bin"
    "FFMPEGlicense", "FFMPEG\\LICENSE"
    "FFMPEGreadme", "FFMPEG\\README.txt"
    "FFMPEGdoc", "FFMPEG\\doc"
    "FFMPEGpresets", "FFMPEG\\presets"
]
printfn "Generating components..."

for componentId, path in genComponents do
    let type', maybe_srd, sourceDirName =
        if File.Exists path then
            printfn $"   file: .\\{path} -> {componentId}"
            "file", "-srd", path.Split('\\')[0] // -srd
        else
            printfn $"   directory: .\\{path}\\* -> {componentId}"
            "dir", "", path
    // nologo: this is automated
    // srd: for files, do not nest the FFMPEG\ directory into INSTALLDIR
    // sreg: do not harvest registry info from the EXE's
    // sfrag: no need for separate fragments in these components
    // gg: i dont care what the GUID's are (at least, at the moment)
    // dr: put these in the FFMPEG\ directory
    heat $"{type'} {path} -nologo {maybe_srd} -sreg -sfrag -gg -dr INSTALLDIR -cg {componentId} -out {componentId.ToLower()}.g.wxs"

    // work around for correcting the Source path for files
    // just a find and replace for 'SourceDir'

    // maybe using \ instead of / in genComponenets fixed this! no..

    let newText = (File.ReadAllText $"{componentId.ToLower()}.g.wxs").Replace("SourceDir", $"""SourceDir\{sourceDirName}""")
    
    use f = File.CreateText $"{componentId.ToLower()}.g.wxs"
    f.Write newText


let product = "FFmpeg"

printfn "Candle..."



let genScripts =
    Directory.EnumerateFiles "."
    |> Seq.filter (fun s -> s.EndsWith ".g.wxs")
    |> List.ofSeq

let genScriptsStr = String.Join(" ", genScripts)

// let newMainWxs = (File.ReadAllText $"{product}.wxs").Replace("$VERSION", version)
// File.WriteAllText($"{product}.g.wxs", newMainWxs)

// exit 1

candle $"""-arch x64 {product}.wxs {genScriptsStr}""" // {product}.wxs 

let elapsedCandle = DateTime.Now - startTime

printfn $"Candle completed in {elapsedCandle.TotalSeconds:N2} seconds"

let genObjs = 
    Directory.EnumerateFiles "."
    |> Seq.filter (fun s -> s.EndsWith ".wixobj")
    |> List.ofSeq

let genObjsStr = String.Join(" ", genObjs)

printfn "Light..."

// genObjsStr includes the product.wixobj
//      -b FFMPEG -b FFMPEG/bin -b FFMPEG/presets -b FFMPEG/doc
light $"-ext WixUIExtension -reusecab -cc cabinets -cultures:en-us -loc en-us.wxl -o {product}.msi {genObjsStr} "

// remove generated files
if not (Array.contains "--keep-generated" args) then
    List.append genScripts genObjs
    |> List.map rm
    |> ignore

let elapsedLight = DateTime.Now - startTime - elapsedCandle

printfn $"Done. Elapsed time: {(elapsedCandle + elapsedLight).TotalSeconds:N2} seconds"